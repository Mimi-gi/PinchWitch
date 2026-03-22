using System;
using UnityEngine;

/// <summary>
/// 2Dプレイヤーが障害物に挟まれたかを監視するスクリプト。
/// 左右または上下の両側から押さえ込まれた瞬間に Crushed イベントを送る。
/// </summary>

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public sealed class Wall2DPlayerCrushDetector : MonoBehaviour
{
  public event Action Crushed;

  [Header("Crush Check")]
  [SerializeField] private LayerMask crushLayers = ~0;
  [SerializeField] private float crushCheckDistance = 0.04f;
  [SerializeField] private float minimumCrushContactAlignment = 0.65f;

  private readonly RaycastHit2D[] _crushHits = new RaycastHit2D[8];

  private Collider2D _collider2D;
  private bool _isCrushed;

  private void Reset()
  {
    ClampSerializedValues();
  }

  private void Awake()
  {
    // 毎フレーム使うコライダー参照を最初に保持する。
    _collider2D = GetComponent<Collider2D>();
    ClampSerializedValues();
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

    // 左右両方、または上下両方に障害物がある時に「挟まれた」とみなす。
    var contactFilter = Wall2DContactCastUtility.CreateContactFilter(crushLayers);
    var crushedHorizontally =
      Wall2DContactCastUtility.HasBlockingHit(
        _collider2D,
        Vector2.left,
        contactFilter,
        _crushHits,
        crushCheckDistance,
        minimumCrushContactAlignment)
      && Wall2DContactCastUtility.HasBlockingHit(
        _collider2D,
        Vector2.right,
        contactFilter,
        _crushHits,
        crushCheckDistance,
        minimumCrushContactAlignment);

    var crushedVertically =
      Wall2DContactCastUtility.HasBlockingHit(
        _collider2D,
        Vector2.up,
        contactFilter,
        _crushHits,
        crushCheckDistance,
        minimumCrushContactAlignment)
      && Wall2DContactCastUtility.HasBlockingHit(
        _collider2D,
        Vector2.down,
        contactFilter,
        _crushHits,
        crushCheckDistance,
        minimumCrushContactAlignment);

    var isCrushedNow = crushedHorizontally || crushedVertically;
    if (isCrushedNow && !_isCrushed)
    {
      // 挟まれ始めた瞬間だけイベントを送る。
      _isCrushed = true;
      Crushed?.Invoke();
      return;
    }

    _isCrushed = isCrushedNow;
  }

  private void ClampSerializedValues()
  {
    // 判定距離や閾値が極端な値にならないように補正する。
    crushCheckDistance = Mathf.Max(0.01f, crushCheckDistance);
    minimumCrushContactAlignment = Mathf.Clamp(minimumCrushContactAlignment, 0f, 1f);
  }
}
