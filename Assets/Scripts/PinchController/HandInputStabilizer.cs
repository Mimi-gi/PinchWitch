using UnityEngine;

public sealed class HandInputStabilizer
{
  // フィルタ計算で極端に小さい deltaTime を避ける下限値。
  private const float MinimumDeltaTime = 0.0002f;
  // X/Y の静止時に使うベースのカットオフ周波数。
  private const float PositionMinCutoff = 0.95f;
  // X/Y を素早く動かしたときに追従性を上げる係数。
  private const float PositionBeta = 0.5f;
  // X/Y の速度推定を平滑化するカットオフ周波数。
  private const float PositionDerivativeCutoff = 1.2f;
  // depth の静止時に使うベースのカットオフ周波数。
  private const float DepthMinCutoff = 1.2f;
  // depth を素早く動かしたときに追従性を上げる係数。
  private const float DepthBeta = 0.35f;
  // depth の速度推定を平滑化するカットオフ周波数。
  private const float DepthDerivativeCutoff = 1.2f;

  // 1 つの float 値に対して単純な低域通過フィルタを適用する。
  private sealed class LowPassFilterFloat
  {
    // フィルタが初期値を持っているかどうか。
    private bool _hasValue;
    // 最新の平滑化済み値。
    private float _value;

    // フィルタが有効な値を持っているかを返す。
    public bool HasValue => _hasValue;
    // 最新の平滑化済み値を返す。
    public float Value => _value;

    // 指定した値でフィルタ状態を初期化する。
    public void Reset(float value)
    {
      _value = value;
      _hasValue = true;
    }

    // 指定した alpha で入力値を平滑化する。
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
    // 値そのものを平滑化するフィルタ。
    private readonly LowPassFilterFloat _valueFilter = new LowPassFilterFloat();
    // 速度推定値を平滑化するフィルタ。
    private readonly LowPassFilterFloat _derivativeFilter = new LowPassFilterFloat();

    // One Euro Filter の係数を受け取って初期化する。
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
      var dt = Mathf.Max(deltaTime, MinimumDeltaTime);
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

  // X 用の入力安定化フィルタ。
  private readonly OneEuroFilterFloat _xFilter =
    new OneEuroFilterFloat(PositionMinCutoff, PositionBeta, PositionDerivativeCutoff);
  // Y 用の入力安定化フィルタ。
  private readonly OneEuroFilterFloat _yFilter =
    new OneEuroFilterFloat(PositionMinCutoff, PositionBeta, PositionDerivativeCutoff);
  // depth 用の入力安定化フィルタ。
  private readonly OneEuroFilterFloat _depthFilter =
    new OneEuroFilterFloat(DepthMinCutoff, DepthBeta, DepthDerivativeCutoff);

  // フィルタが現在の検知セッションで初期化済みかどうか。
  private bool _hasTrackedState;
  // 前フレームで depthReady だったかどうか。
  private bool _wasDepthReady;

  // 手を見失ったときや再認識時に、入力安定化の内部状態を破棄する。
  public void Reset()
  {
    _hasTrackedState = false;
    _wasDepthReady = false;
  }

  // 生の手入力に対して安定化を適用する。
  public void Stabilize(ref HandState handState)
  {
    if (!handState.detected)
    {
      Reset();
      return;
    }

    if (!_hasTrackedState)
    {
      ResetFilters(handState);
      return;
    }

    var deltaTime = Mathf.Max(Time.unscaledDeltaTime, MinimumDeltaTime);
    handState.x = Mathf.Clamp01(_xFilter.Filter(handState.x, deltaTime));
    handState.y = Mathf.Clamp01(_yFilter.Filter(handState.y, deltaTime));

    if (!handState.depthReady)
    {
      _depthFilter.Reset(handState.depth);
      _wasDepthReady = false;
      return;
    }

    if (!_wasDepthReady)
    {
      _depthFilter.Reset(handState.depth);
      _wasDepthReady = true;
      handState.depth = Mathf.Clamp01(handState.depth);
      return;
    }

    handState.depth = Mathf.Clamp01(_depthFilter.Filter(handState.depth, deltaTime));
  }

  // フィルタ群を現在の手状態で初期化し、次フレーム以降の安定化に備える。
  private void ResetFilters(HandState handState)
  {
    _xFilter.Reset(handState.x);
    _yFilter.Reset(handState.y);
    _depthFilter.Reset(handState.depth);
    _hasTrackedState = true;
    _wasDepthReady = handState.depthReady;
  }
}
