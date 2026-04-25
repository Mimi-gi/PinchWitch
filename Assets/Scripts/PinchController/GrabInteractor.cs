using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class GrabInteractor
{
  private sealed class VerticalDropState
  {
    public Rigidbody body;
    public Vector3 lockedPosition;
    public RigidbodyConstraints originalConstraints;
    public bool originalIsKinematic;
    public bool originalUseGravity;
    public RigidbodyInterpolation originalInterpolation;
    public float lastY;
    public float stillnessTimer;
  }

  private const float VerticalDropStillnessVelocityThreshold = 0.05f;
  private const float VerticalDropStillnessPositionThreshold = 0.001f;
  private const float VerticalDropStillnessDuration = 0.08f;

  public struct Config
  {
    // 掴める対象を検索するレイヤーマスク。
    public LayerMask grabbableLayerMask;
    // 掴み判定に使う半径。
    public float reach;
    // 掴み対象の X 可動範囲。
    public Vector2 xRange;
    // 掴み対象の Y 可動範囲。
    public Vector2 yRange;
    // 掴み対象の Z 可動範囲。
    public Vector2 zRange;
    // 回転目標へ追従する速度。
    public float rotationDegreesPerSecond;
    // 解放時に元の isKinematic を戻すかどうか。
    public bool restoreIsKinematicOnRelease;
    // 解放時に元の useGravity を戻すかどうか。
    public bool restoreUseGravityOnRelease;
  }

  // OverlapCapsuleNonAlloc の結果を受ける使い回しバッファ。
  private readonly Collider[] _overlapColliders = new Collider[64];
  // 真下に落とす途中の Rigidbody を保持するリスト。
  private readonly List<VerticalDropState> _verticalDropStates = new List<VerticalDropState>(4);

  // 掴み挙動全体の設定。
  private Config _config;
  // 現在掴んでいる Rigidbody。
  private Rigidbody _grabbedBody;
  // 掴み開始時の手位置との差分。
  private Vector3 _grabOffset;
  // 掴み前の isKinematic 設定。
  private bool _grabbedOriginalIsKinematic;
  // 掴み前の useGravity 設定。
  private bool _grabbedOriginalUseGravity;
  // 掴み前の Rigidbody constraints。
  private RigidbodyConstraints _grabbedOriginalConstraints;
  // 掴み前の Rigidbody interpolation 設定。
  private RigidbodyInterpolation _grabbedOriginalInterpolation;
  // 掴み前の各 Collider の isTrigger 設定。
  private Collider[] _grabbedColliders;
  private bool[] _grabbedOriginalIsTrigger;
  // 掴み開始時の位置（配置不可時の復帰先）。
  private Vector3 _grabbedOriginalPosition;
  // 掴み中の重なり検知コンポーネント。
  private ColliderDetection _colliderDetection;
  // 目標としている回転値。
  private Quaternion _targetRotation;
  // 最新フレームで要求された掴み位置。
  private Vector3 _desiredGrabPosition;
  // 掴み位置が初期化済みかどうか。
  private bool _hasDesiredGrabPosition;

  public event Action<Rigidbody> GrabStarted;
  public event Action<Rigidbody> GrabReleased;

  // grab 操作用の設定でインタラクタを生成する。
  public GrabInteractor(Config config)
  {
    ApplyConfiguration(config);
  }

  // 現在 grab 中の Rigidbody を保持しているかを返す。
  public bool HasGrabbedBody => _grabbedBody != null;

  // 現在 grab 中の Rigidbody。
  public Rigidbody GrabbedBody => _grabbedBody;

  // 現在掴んでいるオブジェクトが、他のコライダーと重なっている（置けない状態）かどうか。
  public bool IsGrabbedObjectOverlapping => _colliderDetection != null && _colliderDetection.IsOverlapping;

  // grab 中オブジェクトが回転目標へ追従中かどうか。
  public bool IsGrabbedObjectRotating => _grabbedBody != null && Quaternion.Angle(_grabbedBody.rotation, _targetRotation) > 0.01f;

  // grab と回転に使う設定を更新する。
  public void ApplyConfiguration(Config config)
  {
    _config = config;
  }

  // 指定した手元の XZ 位置に最も近い、掴める Rigidbody を検索して取得する。
  public bool TryGetNearestGrabbableBody(Vector3 handTarget, out Rigidbody nearestBody)
  {
    var capsuleBottom = new Vector3(handTarget.x, -1000f, handTarget.z);
    var capsuleTop = new Vector3(handTarget.x, 1000f, handTarget.z);
    var hitCount = Physics.OverlapCapsuleNonAlloc(capsuleBottom, capsuleTop, _config.reach, _overlapColliders, _config.grabbableLayerMask, QueryTriggerInteraction.Ignore);

    nearestBody = null;
    var nearestHorizontalDistanceSq = float.MaxValue;
    var maxHorizontalDistanceSq = _config.reach * _config.reach;

    for (var i = 0; i < hitCount; i++)
    {
      var collider = _overlapColliders[i];
      if (collider == null)
      {
        continue;
      }

      var body = collider.attachedRigidbody;
      if (body == null)
      {
        continue;
      }
      if ((_config.grabbableLayerMask.value & (1 << body.gameObject.layer)) == 0)
      {
        continue;
      }
      if (IsInVerticalDrop(body))
      {
        continue;
      }

      var closestPoint = collider.ClosestPoint(handTarget);
      var horizontalDistanceX = closestPoint.x - handTarget.x;
      var horizontalDistanceZ = closestPoint.z - handTarget.z;
      var horizontalDistanceSq = horizontalDistanceX * horizontalDistanceX + horizontalDistanceZ * horizontalDistanceZ;
      if (horizontalDistanceSq > maxHorizontalDistanceSq || horizontalDistanceSq >= nearestHorizontalDistanceSq)
      {
        continue;
      }

      nearestHorizontalDistanceSq = horizontalDistanceSq;
      nearestBody = body;
    }

    return nearestBody != null;
  }

  // 手元の XZ 位置に最も近い掴める Rigidbody を見つけて grab を開始する。
  public void TryBeginGrab(Vector3 handTarget)
  {
    if (_grabbedBody != null)
    {
      return;
    }

    if (!TryGetNearestGrabbableBody(handTarget, out var nearestBody))
    {
      return;
    }

    BeginGrab(nearestBody, handTarget);
  }

  // 指定した Rigidbody を指定位置基準で grab する。
  public bool TryBeginGrab(Rigidbody targetBody, Vector3 grabTarget)
  {
    if (targetBody == null || _grabbedBody != null)
    {
      return false;
    }

    if ((_config.grabbableLayerMask.value & (1 << targetBody.gameObject.layer)) == 0)
    {
      return false;
    }
    if (IsInVerticalDrop(targetBody))
    {
      return false;
    }

    BeginGrab(targetBody, grabTarget);
    return true;
  }

  // 指定した Rigidbody の grab 開始処理を実行する。
  private void BeginGrab(Rigidbody targetBody, Vector3 grabTarget)
  {
    CancelVerticalDrop(targetBody);
    _grabbedBody = targetBody;
    _grabOffset = _grabbedBody.position - grabTarget;
    _grabOffset.y = 0f;
    _grabbedOriginalIsKinematic = _grabbedBody.isKinematic;
    _grabbedOriginalUseGravity = _grabbedBody.useGravity;
    _grabbedOriginalConstraints = _grabbedBody.constraints;
    _grabbedOriginalInterpolation = _grabbedBody.interpolation;

    _grabbedBody.linearVelocity = Vector3.zero;
    _grabbedBody.angularVelocity = Vector3.zero;
    _grabbedBody.isKinematic = true;
    _grabbedBody.useGravity = false;
    _grabbedBody.interpolation = RigidbodyInterpolation.Interpolate;
    _grabbedBody.constraints = _grabbedOriginalConstraints & ~RigidbodyConstraints.FreezeRotation;

    // 掴み中は衝突による予期せぬ回転を防ぐため、全コライダーをトリガーに切り替える。
    _grabbedColliders = _grabbedBody.GetComponentsInChildren<Collider>();
    _grabbedOriginalIsTrigger = new bool[_grabbedColliders.Length];
    for (var i = 0; i < _grabbedColliders.Length; i++)
    {
      _grabbedOriginalIsTrigger[i] = _grabbedColliders[i].isTrigger;
      _grabbedColliders[i].isTrigger = true;
    }

    // 元の位置を保存し、重なり検知を開始する。
    _grabbedOriginalPosition = _grabbedBody.position;
    _colliderDetection = _grabbedBody.gameObject.AddComponent<ColliderDetection>();
    _colliderDetection.Initialize(_config.grabbableLayerMask);

    _targetRotation = _grabbedBody.rotation;
    _desiredGrabPosition = _grabbedBody.position;
    _hasDesiredGrabPosition = true;
    GrabStarted?.Invoke(_grabbedBody);
  }

  // grab 中オブジェクトの目標位置を更新する。
  public void UpdateGrabbedObject(Vector3 handTarget)
  {
    UpdateGrabbedObjectInternal(handTarget, true);
  }

  // grab 中オブジェクトの目標位置を、Y 制限なしで更新する。
  public void UpdateGrabbedObjectUnclampedY(Vector3 grabTarget)
  {
    UpdateGrabbedObjectInternal(grabTarget, false);
  }

  // grab 中オブジェクトの目標位置更新を実行する。
  private void UpdateGrabbedObjectInternal(Vector3 grabTarget, bool clampY)
  {
    if (_grabbedBody == null)
    {
      return;
    }

    var tx = Mathf.Clamp(grabTarget.x + _grabOffset.x, _config.xRange.x, _config.xRange.y);
    var rawY = grabTarget.y + _grabOffset.y;
    var ty = clampY ? Mathf.Clamp(rawY, _config.yRange.x, _config.yRange.y) : rawY;
    var tz = Mathf.Clamp(grabTarget.z + _grabOffset.z, _config.zRange.x, _config.zRange.y);
    _desiredGrabPosition = new Vector3(tx, ty, tz);
    _hasDesiredGrabPosition = true;
  }

  // FixedUpdate で掴み中オブジェクトの位置と回転を物理更新する。
  public void FixedTick(float fixedDeltaTime)
  {
    if (_grabbedBody != null && _hasDesiredGrabPosition)
    {
      _grabbedBody.MovePosition(_desiredGrabPosition);
      _grabbedBody.MoveRotation(ResolveNextRotation(fixedDeltaTime));
      _grabbedBody.linearVelocity = Vector3.zero;
      _grabbedBody.angularVelocity = Vector3.zero;
    }

    UpdateVerticalDropStates(fixedDeltaTime);
  }

  // grab 中オブジェクトの目標回転を積み増す。
  public void RotateGrabbedObject(Vector3 worldAxis, float angleDegrees)
  {
    if (_grabbedBody == null || worldAxis.sqrMagnitude <= Mathf.Epsilon || Mathf.Approximately(angleDegrees, 0f))
    {
      return;
    }

    _targetRotation = Quaternion.AngleAxis(angleDegrees, worldAxis.normalized) * _targetRotation;
  }

  // grab 中オブジェクトを解放して元の物理設定へ戻す。
  public void ReleaseGrabbedObject()
  {
    if (_grabbedBody == null)
    {
      return;
    }

    var releasedBody = _grabbedBody;

    // 他の掴み対象と重なっている場合は元の位置に戻す。
    if (_colliderDetection != null && _colliderDetection.IsOverlapping)
    {
      _grabbedBody.position = _grabbedOriginalPosition;
    }

    // 重なり検知コンポーネントを除去する。
    if (_colliderDetection != null)
    {
      UnityEngine.Object.Destroy(_colliderDetection);
      _colliderDetection = null;
    }

    var lockedDropPosition = ResolveLockedDropPosition(_grabbedBody.position);
    _grabbedBody.linearVelocity = Vector3.zero;
    _grabbedBody.angularVelocity = Vector3.zero;
    _grabbedBody.position = lockedDropPosition;
    _grabbedBody.constraints = ResolveVerticalDropConstraints(_grabbedOriginalConstraints);
    _grabbedBody.isKinematic = false;
    _grabbedBody.useGravity = true;
    _grabbedBody.interpolation = RigidbodyInterpolation.Interpolate;

    // 掴み前の isTrigger 設定を復元する。
    if (_grabbedColliders != null)
    {
      for (var i = 0; i < _grabbedColliders.Length; i++)
      {
        if (_grabbedColliders[i] != null)
        {
          _grabbedColliders[i].isTrigger = _grabbedOriginalIsTrigger[i];
        }
      }

      _grabbedColliders = null;
      _grabbedOriginalIsTrigger = null;
    }

    StartVerticalDrop(
      releasedBody,
      lockedDropPosition,
      _grabbedOriginalConstraints,
      _grabbedOriginalIsKinematic,
      _grabbedOriginalUseGravity,
      _grabbedOriginalInterpolation);

    _grabbedBody = null;
    _hasDesiredGrabPosition = false;
    GrabReleased?.Invoke(releasedBody);
  }

  // 真下に落とす途中の Rigidbody を更新する。
  private void UpdateVerticalDropStates(float fixedDeltaTime)
  {
    for (var i = _verticalDropStates.Count - 1; i >= 0; i--)
    {
      var dropState = _verticalDropStates[i];
      if (dropState.body == null)
      {
        _verticalDropStates.RemoveAt(i);
        continue;
      }

      ConstrainVerticalDrop(dropState);
      if (!ShouldFinishVerticalDrop(dropState, fixedDeltaTime))
      {
        continue;
      }

      FinishVerticalDrop(dropState);
      _verticalDropStates.RemoveAt(i);
    }
  }

  // 解放された Rigidbody の真下落下フェーズを開始する。
  private void StartVerticalDrop(
    Rigidbody releasedBody,
    Vector3 lockedDropPosition,
    RigidbodyConstraints originalConstraints,
    bool originalIsKinematic,
    bool originalUseGravity,
    RigidbodyInterpolation originalInterpolation)
  {
    if (releasedBody == null)
    {
      return;
    }

    CancelVerticalDrop(releasedBody);
    _verticalDropStates.Add(new VerticalDropState
    {
      body = releasedBody,
      lockedPosition = lockedDropPosition,
      originalConstraints = originalConstraints,
      originalIsKinematic = originalIsKinematic,
      originalUseGravity = originalUseGravity,
      originalInterpolation = originalInterpolation,
      lastY = releasedBody.position.y,
      stillnessTimer = 0f,
    });
  }

  // 真下落下フェーズ中の Rigidbody を再 grab する前に通常状態へ戻す。
  private void CancelVerticalDrop(Rigidbody targetBody)
  {
    if (targetBody == null)
    {
      return;
    }

    for (var i = _verticalDropStates.Count - 1; i >= 0; i--)
    {
      var dropState = _verticalDropStates[i];
      if (dropState.body != targetBody)
      {
        continue;
      }

      FinishVerticalDrop(dropState);
      _verticalDropStates.RemoveAt(i);
      return;
    }
  }

  // 真下落下フェーズ中の Rigidbody かどうかを返す。
  private bool IsInVerticalDrop(Rigidbody targetBody)
  {
    if (targetBody == null)
    {
      return false;
    }

    for (var i = 0; i < _verticalDropStates.Count; i++)
    {
      if (_verticalDropStates[i].body == targetBody)
      {
        return true;
      }
    }

    return false;
  }

  // 真下落下中は X/Z を固定し、横方向の速度を打ち消す。
  private void ConstrainVerticalDrop(VerticalDropState dropState)
  {
    if (dropState.body == null)
    {
      return;
    }

    var velocity = dropState.body.linearVelocity;
    if (!Mathf.Approximately(velocity.x, 0f) || !Mathf.Approximately(velocity.z, 0f))
    {
      velocity.x = 0f;
      velocity.z = 0f;
      dropState.body.linearVelocity = velocity;
    }
  }

  // 着地して静止したと判断できたら真下落下フェーズを終了する。
  private bool ShouldFinishVerticalDrop(VerticalDropState dropState, float fixedDeltaTime)
  {
    if (dropState.body == null)
    {
      return true;
    }

    if (dropState.body.IsSleeping())
    {
      return true;
    }

    var currentY = dropState.body.position.y;
    var verticalDelta = Mathf.Abs(currentY - dropState.lastY);
    var verticalVelocity = Mathf.Abs(dropState.body.linearVelocity.y);
    if (verticalDelta <= VerticalDropStillnessPositionThreshold
        && verticalVelocity <= VerticalDropStillnessVelocityThreshold)
    {
      dropState.stillnessTimer += fixedDeltaTime;
    }
    else
    {
      dropState.stillnessTimer = 0f;
    }

    dropState.lastY = currentY;
    return dropState.stillnessTimer >= VerticalDropStillnessDuration;
  }

  // 真下落下フェーズを終え、元の物理設定へ戻す。
  private void FinishVerticalDrop(VerticalDropState dropState)
  {
    if (dropState.body == null)
    {
      return;
    }

    var settledPosition = ResolveLockedDropPosition(dropState.body.position);
    dropState.body.position = settledPosition;
    dropState.body.linearVelocity = Vector3.zero;
    dropState.body.angularVelocity = Vector3.zero;
    dropState.body.constraints = dropState.originalConstraints;

    if (_config.restoreIsKinematicOnRelease)
    {
      dropState.body.isKinematic = dropState.originalIsKinematic;
    }
    else
    {
      dropState.body.isKinematic = false;
    }

    if (_config.restoreUseGravityOnRelease)
    {
      dropState.body.useGravity = dropState.originalUseGravity;
    }
    else
    {
      dropState.body.useGravity = true;
    }

    dropState.body.interpolation = dropState.originalInterpolation;
  }

  // release 後に落とし始める位置を X/Z 範囲へ収める。
  private Vector3 ResolveLockedDropPosition(Vector3 currentPosition)
  {
    return new Vector3(
      Mathf.Clamp(currentPosition.x, _config.xRange.x, _config.xRange.y),
      currentPosition.y,
      Mathf.Clamp(currentPosition.z, _config.zRange.x, _config.zRange.y));
  }

  // 真下落下中だけ X/Z を固定する拘束設定を作る。
  private static RigidbodyConstraints ResolveVerticalDropConstraints(RigidbodyConstraints originalConstraints)
  {
    return (originalConstraints & ~RigidbodyConstraints.FreezePositionY)
           | RigidbodyConstraints.FreezePositionX
           | RigidbodyConstraints.FreezePositionZ;
  }

  // 現在の回転から次フレームで到達させる回転を計算する。
  private Quaternion ResolveNextRotation(float deltaTime)
  {
    if (_grabbedBody == null)
    {
      return Quaternion.identity;
    }

    var maxStep = Mathf.Max(0f, _config.rotationDegreesPerSecond) * deltaTime;
    if (maxStep <= 0f)
    {
      return _targetRotation;
    }

    return Quaternion.RotateTowards(_grabbedBody.rotation, _targetRotation, maxStep);
  }
}
