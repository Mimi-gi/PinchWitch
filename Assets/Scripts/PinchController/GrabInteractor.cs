using UnityEngine;

public sealed class GrabInteractor
{
  // One Euro Filter のベースカットオフ周波数。
  private const float PositionFilterMinCutoff = 1f;
  // 速度に応じてカットオフを増やす係数。
  private const float PositionFilterBeta = 0.28f;
  // 速度推定に使う微分値のカットオフ周波数。
  private const float PositionFilterDerivativeCutoff = 1f;

  // 1 つの float 値に対して単純な低域通過フィルタを適用する。
  private sealed class LowPassFilterFloat
  {
    // すでに初期値を持っているかどうか。
    private bool _hasValue;
    // 現在の平滑化済み値。
    private float _value;

    // フィルタが有効な初期値を持っているかを返す。
    public bool HasValue => _hasValue;
    // 最新の平滑化済み値を返す。
    public float Value => _value;

    // フィルタ内部状態を指定値で初期化する。
    public void Reset(float value)
    {
      _value = value;
      _hasValue = true;
    }

    // 指定した補間率で新しい値を平滑化する。
    public float Filter(float value, float alpha)
    {
      if (!_hasValue)
      {
        Reset(value);
        return _value;
      }

      _value = Mathf.Lerp(_value, value, alpha);
      return _value;
    }
  }

  // 1 つの float 値に One Euro Filter を適用する。
  private sealed class OneEuroFilterFloat
  {
    // 静止時に使うベースのカットオフ周波数。
    private readonly float _minCutoff;
    // 速度に応じてカットオフを増やす係数。
    private readonly float _beta;
    // 速度推定値を平滑化するカットオフ周波数。
    private readonly float _derivativeCutoff;
    // 元の値そのものを平滑化する低域通過フィルタ。
    private readonly LowPassFilterFloat _valueFilter = new LowPassFilterFloat();
    // 速度推定値を平滑化する低域通過フィルタ。
    private readonly LowPassFilterFloat _derivativeFilter = new LowPassFilterFloat();

    // One Euro Filter の各係数を受け取って初期化する。
    public OneEuroFilterFloat(float minCutoff, float beta, float derivativeCutoff)
    {
      _minCutoff = minCutoff;
      _beta = beta;
      _derivativeCutoff = derivativeCutoff;
    }

    // 値フィルタと速度フィルタを初期値でリセットする。
    public void Reset(float value)
    {
      _valueFilter.Reset(value);
      _derivativeFilter.Reset(0f);
    }

    // 入力値の速度に応じて平滑化の強さを変えながら値を更新する。
    public float Filter(float value, float deltaTime)
    {
      var dt = Mathf.Max(deltaTime, 0.0001f);
      var derivative = _valueFilter.HasValue ? (value - _valueFilter.Value) / dt : 0f;
      var filteredDerivative = _derivativeFilter.Filter(derivative, ComputeAlpha(dt, _derivativeCutoff));
      var cutoff = _minCutoff + (_beta * Mathf.Abs(filteredDerivative));
      return _valueFilter.Filter(value, ComputeAlpha(dt, cutoff));
    }

    // カットオフ周波数から低域通過フィルタ用の alpha を計算する。
    private static float ComputeAlpha(float deltaTime, float cutoff)
    {
      var safeCutoff = Mathf.Max(cutoff, 0.0001f);
      var tau = 1f / (2f * Mathf.PI * safeCutoff);
      return 1f / (1f + (tau / deltaTime));
    }
  }

  // Vector3 の各軸へ個別に One Euro Filter を適用する。
  private sealed class OneEuroFilterVector3
  {
    // X 軸用のフィルタ。
    private readonly OneEuroFilterFloat _xFilter;
    // Y 軸用のフィルタ。
    private readonly OneEuroFilterFloat _yFilter;
    // Z 軸用のフィルタ。
    private readonly OneEuroFilterFloat _zFilter;

    // 各軸に同じ係数の One Euro Filter を構築する。
    public OneEuroFilterVector3(float minCutoff, float beta, float derivativeCutoff)
    {
      _xFilter = new OneEuroFilterFloat(minCutoff, beta, derivativeCutoff);
      _yFilter = new OneEuroFilterFloat(minCutoff, beta, derivativeCutoff);
      _zFilter = new OneEuroFilterFloat(minCutoff, beta, derivativeCutoff);
    }

    // 全軸のフィルタ状態を指定位置で初期化する。
    public void Reset(Vector3 value)
    {
      _xFilter.Reset(value.x);
      _yFilter.Reset(value.y);
      _zFilter.Reset(value.z);
    }

    // Vector3 の各成分を個別に平滑化した結果を返す。
    public Vector3 Filter(Vector3 value, float deltaTime)
    {
      return new Vector3(
        _xFilter.Filter(value.x, deltaTime),
        _yFilter.Filter(value.y, deltaTime),
        _zFilter.Filter(value.z, deltaTime));
    }
  }

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
    // 追従時の X 補間率。
    public float followLerpX;
    // 追従時の Y 補間率。
    public float followLerpY;
    // 追従時の Z 補間率。
    public float followLerpZ;
    // 回転目標へ追従する速度。
    public float rotationDegreesPerSecond;
    // 解放時に元の isKinematic を戻すかどうか。
    public bool restoreIsKinematicOnRelease;
    // 解放時に元の useGravity を戻すかどうか。
    public bool restoreUseGravityOnRelease;
  }

  // OverlapCapsuleNonAlloc の結果を受ける使い回しバッファ。
  private readonly Collider[] _overlapColliders = new Collider[64];
  // 掴み位置を滑らかにする One Euro Filter。
  private readonly OneEuroFilterVector3 _grabPositionFilter =
    new OneEuroFilterVector3(PositionFilterMinCutoff, PositionFilterBeta, PositionFilterDerivativeCutoff);

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

      var toBody = body.worldCenterOfMass - handTarget;
      var horizontalDistanceSq = toBody.x * toBody.x + toBody.z * toBody.z;
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
    _grabPositionFilter.Reset(_grabbedBody.position);
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

    var filteredPosition = _grabPositionFilter.Filter(_desiredGrabPosition, fixedDeltaTime);
    _grabbedBody.MovePosition(filteredPosition);
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
