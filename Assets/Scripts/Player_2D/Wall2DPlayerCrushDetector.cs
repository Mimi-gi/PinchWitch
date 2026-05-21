using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 2Dプレイヤーが障害物に挟まれたかを監視するスクリプト。
/// 左右または上下の両側から塞がれた瞬間に Crushed イベントを送る。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public sealed class Wall2DPlayerCrushDetector : MonoBehaviour
{
  private static readonly List<Wall2DPlayerCrushDetector> ActiveDetectors = new List<Wall2DPlayerCrushDetector>();
  private static readonly List<Wall2DPlayerCrushDetector> DetectorSnapshot = new List<Wall2DPlayerCrushDetector>();

  public event Action Crushed;
  public event Action ReleasedFromCrush;

  [Header("Shadow Crush Check")]
  [SerializeField] private LayerMask crushLayers = ~0;
  [SerializeField] private float crushCheckDistance = 0.04f;
  [SerializeField] private float minimumCrushContactAlignment = 0.65f;
  [SerializeField, Range(0f, 1f)] private float fullCoverSampleRatio = 0.9f;
  [SerializeField] private int fullCoverSampleColumns = 3;
  [SerializeField] private int fullCoverSampleRows = 5;
  [SerializeField] private float fullCoverSampleInset = 0.02f;

  private readonly RaycastHit2D[] _crushHits = new RaycastHit2D[8];
  private readonly Collider2D[] _overlapHits = new Collider2D[32];

  private Collider2D _collider2D;
  private bool _isCrushed;

  public bool IsCrushed => _isCrushed;

  public bool IsShadowBlockingDirection(Vector2 direction)
  {
    if (_collider2D == null || direction.sqrMagnitude <= Mathf.Epsilon)
    {
      return false;
    }

    var contactFilter = Wall2DContactCastUtility.CreateContactFilter(crushLayers);
    return HasShadowBlockingHit(direction.normalized, contactFilter);
  }

  private void Reset()
  {
    ClampSerializedValues();
  }

  private void Awake()
  {
    // 毎フレーム使うコライダー参照を最初に確保する。
    _collider2D = GetComponent<Collider2D>();
    ClampSerializedValues();
  }

  private void OnEnable()
  {
    if (!ActiveDetectors.Contains(this))
    {
      ActiveDetectors.Add(this);
    }

    _isCrushed = false;
  }

  private void OnDisable()
  {
    ActiveDetectors.Remove(this);
    _isCrushed = false;
  }

  private void OnValidate()
  {
    ClampSerializedValues();
  }

  private void FixedUpdate()
  {
    // 物理判定なので FixedUpdate で監視する。
    UpdateCrushState();
  }

  private void UpdateCrushState()
  {
    if (_collider2D == null)
    {
      _isCrushed = false;
      return;
    }

    // 左右両方、または上下両方に影がある時に「影に挟まれた」とみなす。
    var contactFilter = Wall2DContactCastUtility.CreateContactFilter(crushLayers);
    var crushedHorizontally = HasShadowBlockingHit(Vector2.left, contactFilter)
      && HasShadowBlockingHit(Vector2.right, contactFilter);

    var crushedVertically = HasShadowBlockingHit(Vector2.up, contactFilter)
      && HasShadowBlockingHit(Vector2.down, contactFilter);

    var fullyCoveredByShadow = IsFullyCoveredByShadow(contactFilter);
    var isCrushedNow = crushedHorizontally || crushedVertically || fullyCoveredByShadow;
    if (isCrushedNow && !_isCrushed)
    {
      // 挟まれ始めた瞬間だけイベントを送る。
      _isCrushed = true;
      Crushed?.Invoke();
      return;
    }

    if (!isCrushedNow && _isCrushed)
    {
      _isCrushed = false;
      ReleasedFromCrush?.Invoke();
      return;
    }

    _isCrushed = isCrushedNow;
  }

  private bool HasShadowBlockingHit(Vector2 direction, ContactFilter2D contactFilter)
  {
    if (_collider2D == null)
    {
      return false;
    }

    if (HasShadowCastHit(direction, contactFilter))
    {
      return true;
    }

    return HasShadowOverlapNearSide(direction, contactFilter);
  }

  private bool HasShadowCastHit(Vector2 direction, ContactFilter2D contactFilter)
  {
    var expectedNormal = -direction.normalized;
    var hitCount = _collider2D.Cast(direction, contactFilter, _crushHits, crushCheckDistance);
    for (var i = 0; i < hitCount; i++)
    {
      var hit = _crushHits[i];
      if (hit.collider == null || !IsShadowCollider(hit.collider))
      {
        continue;
      }

      if (Vector2.Dot(hit.normal, expectedNormal) >= minimumCrushContactAlignment)
      {
        return true;
      }
    }

    return false;
  }

  private bool HasShadowOverlapNearSide(Vector2 direction, ContactFilter2D contactFilter)
  {
    var bounds = _collider2D.bounds;
    var normalizedDirection = direction.normalized;
    var probeThickness = Mathf.Max(0.01f, crushCheckDistance * 2f);
    Vector2 probeCenter;
    Vector2 probeSize;

    if (Mathf.Abs(normalizedDirection.x) >= Mathf.Abs(normalizedDirection.y))
    {
      var x = normalizedDirection.x < 0f
        ? bounds.min.x - (probeThickness * 0.5f)
        : bounds.max.x + (probeThickness * 0.5f);
      probeCenter = new Vector2(x, bounds.center.y);
      probeSize = new Vector2(probeThickness, Mathf.Max(0.01f, bounds.size.y - (fullCoverSampleInset * 2f)));
    }
    else
    {
      var y = normalizedDirection.y < 0f
        ? bounds.min.y - (probeThickness * 0.5f)
        : bounds.max.y + (probeThickness * 0.5f);
      probeCenter = new Vector2(bounds.center.x, y);
      probeSize = new Vector2(Mathf.Max(0.01f, bounds.size.x - (fullCoverSampleInset * 2f)), probeThickness);
    }

    var overlapCount = Physics2D.OverlapBox(probeCenter, probeSize, 0f, contactFilter, _overlapHits);
    for (var i = 0; i < overlapCount; i++)
    {
      if (IsShadowCollider(_overlapHits[i]))
      {
        return true;
      }
    }

    return false;
  }

  private bool IsFullyCoveredByShadow(ContactFilter2D contactFilter)
  {
    if (_collider2D == null)
    {
      return false;
    }

    var bounds = _collider2D.bounds;
    var minX = bounds.min.x + Mathf.Min(fullCoverSampleInset, bounds.extents.x);
    var maxX = bounds.max.x - Mathf.Min(fullCoverSampleInset, bounds.extents.x);
    var minY = bounds.min.y + Mathf.Min(fullCoverSampleInset, bounds.extents.y);
    var maxY = bounds.max.y - Mathf.Min(fullCoverSampleInset, bounds.extents.y);

    var sampleCount = 0;
    var coveredCount = 0;
    for (var row = 0; row < fullCoverSampleRows; row++)
    {
      var y = fullCoverSampleRows <= 1
        ? bounds.center.y
        : Mathf.Lerp(minY, maxY, row / (float)(fullCoverSampleRows - 1));

      for (var column = 0; column < fullCoverSampleColumns; column++)
      {
        var x = fullCoverSampleColumns <= 1
          ? bounds.center.x
          : Mathf.Lerp(minX, maxX, column / (float)(fullCoverSampleColumns - 1));
        var samplePoint = new Vector2(x, y);

        if (!_collider2D.OverlapPoint(samplePoint))
        {
          continue;
        }

        sampleCount++;
        if (IsPointCoveredByShadow(samplePoint, contactFilter))
        {
          coveredCount++;
        }
      }
    }

    if (sampleCount == 0)
    {
      return false;
    }

    return coveredCount / (float)sampleCount >= fullCoverSampleRatio;
  }

  private bool IsPointCoveredByShadow(Vector2 point, ContactFilter2D contactFilter)
  {
    var overlapCount = Physics2D.OverlapPoint(point, contactFilter, _overlapHits);
    for (var i = 0; i < overlapCount; i++)
    {
      if (IsShadowCollider(_overlapHits[i]))
      {
        return true;
      }
    }

    return false;
  }

  public void EvaluateCrushImmediately()
  {
    Physics2D.SyncTransforms();
    UpdateCrushState();
  }

  public static void EvaluateAllActiveDetectorsImmediately()
  {
    DetectorSnapshot.Clear();
    DetectorSnapshot.AddRange(ActiveDetectors);

    for (var i = 0; i < DetectorSnapshot.Count; i++)
    {
      var detector = DetectorSnapshot[i];
      if (detector == null || !detector.isActiveAndEnabled)
      {
        continue;
      }

      detector.EvaluateCrushImmediately();
    }

    DetectorSnapshot.Clear();
  }

  public static bool IsShadowCollider(Collider2D collider2D)
  {
    return collider2D != null
      && (collider2D.GetComponent<ShadowColliderOutput>() != null
          || collider2D.GetComponentInParent<ShadowColliderOutput>() != null);
  }

  private void ClampSerializedValues()
  {
    // 判定距離や閾値が極端な値にならないように補正する。
    crushCheckDistance = Mathf.Max(0.01f, crushCheckDistance);
    minimumCrushContactAlignment = Mathf.Clamp(minimumCrushContactAlignment, 0f, 1f);
    fullCoverSampleRatio = Mathf.Clamp01(fullCoverSampleRatio);
    fullCoverSampleColumns = Mathf.Max(1, fullCoverSampleColumns);
    fullCoverSampleRows = Mathf.Max(1, fullCoverSampleRows);
    fullCoverSampleInset = Mathf.Max(0f, fullCoverSampleInset);
  }
}
