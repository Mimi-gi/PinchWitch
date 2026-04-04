using System;
using UnityEngine;

public sealed class GrabInteractor
{
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

  // grab と回転に使う設定を更新する。
  public void ApplyConfiguration(Config config)
  {
    _config = config;
  }

  // 手元の XZ 位置に最も近い掴める Rigidbody を見つけて grab を開始する。
  public void TryBeginGrab(Vector3 handTarget)
  {
    var capsuleBottom = new Vector3(handTarget.x, -1000f, handTarget.z);
    var capsuleTop = new Vector3(handTarget.x, 1000f, handTarget.z);
    var hitCount = Physics.OverlapCapsuleNonAlloc(capsuleBottom, capsuleTop, _config.reach, _overlapColliders, _config.grabbableLayerMask, QueryTriggerInteraction.Ignore);

    Rigidbody nearestBody = null;
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

    if (nearestBody == null)
    {
      return;
    }

    _grabbedBody = nearestBody;
    _grabOffset = _grabbedBody.position - handTarget;
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
    _targetRotation = _grabbedBody.rotation;
    _desiredGrabPosition = _grabbedBody.position;
    _hasDesiredGrabPosition = true;
    GrabStarted?.Invoke(_grabbedBody);
  }

  // grab 中オブジェクトの目標位置を更新する。
  public void UpdateGrabbedObject(Vector3 handTarget)
  {
    if (_grabbedBody == null)
    {
      return;
    }

    var tx = Mathf.Clamp(handTarget.x + _grabOffset.x, _config.xRange.x, _config.xRange.y);
    var ty = Mathf.Clamp(handTarget.y + _grabOffset.y, _config.yRange.x, _config.yRange.y);
    var tz = Mathf.Clamp(handTarget.z + _grabOffset.z, _config.zRange.x, _config.zRange.y);
    _desiredGrabPosition = new Vector3(tx, ty, tz);
    _hasDesiredGrabPosition = true;
  }

  // FixedUpdate で掴み中オブジェクトの位置と回転を物理更新する。
  public void FixedTick(float fixedDeltaTime)
  {
    if (_grabbedBody == null || !_hasDesiredGrabPosition)
    {
      return;
    }

    _grabbedBody.MovePosition(_desiredGrabPosition);
    _grabbedBody.MoveRotation(ResolveNextRotation(fixedDeltaTime));
    _grabbedBody.linearVelocity = Vector3.zero;
    _grabbedBody.angularVelocity = Vector3.zero;
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

    _grabbedBody.linearVelocity = Vector3.zero;
    _grabbedBody.angularVelocity = Vector3.zero;
    _grabbedBody.constraints = _grabbedOriginalConstraints;

    if (_config.restoreIsKinematicOnRelease)
    {
      _grabbedBody.isKinematic = _grabbedOriginalIsKinematic;
    }
    else
    {
      _grabbedBody.isKinematic = false;
    }

    if (_config.restoreUseGravityOnRelease)
    {
      _grabbedBody.useGravity = _grabbedOriginalUseGravity;
    }
    else
    {
      _grabbedBody.useGravity = true;
    }

    _grabbedBody.interpolation = _grabbedOriginalInterpolation;
    _grabbedBody = null;
    _hasDesiredGrabPosition = false;
    GrabReleased?.Invoke(releasedBody);
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
