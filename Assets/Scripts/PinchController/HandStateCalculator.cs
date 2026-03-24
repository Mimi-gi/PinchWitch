using Mediapipe.Unity;
using UnityEngine;

public struct HandState
{
  public bool detected;
  public float x;
  public float y;
  // 0 = 画面奥側、1 = 画面手前側。MediaPipe の実距離ではなくゲーム内の見た目基準。
  public float depth;
  public bool depthReady;
  public bool pinch;
}

public sealed class HandStateCalculator
{
  private const int WristIndex = 0;
  private const int ThumbTipIndex = 4;
  private const int IndexTipIndex = 8;
  private const int IndexMcpIndex = 5;
  private const int MiddleMcpIndex = 9;
  private const int PinkyMcpIndex = 17;
  private const float RelativeNearPalmScaleRatioFromFront = 1.27f;
  private const float MinimumFrontPalmScale = 0.0001f;
  private const float PalmScaleStabilizeLerp = 0.2f;
  private const float DepthDeadZone = 0.04f;
  private const float DepthResponseLerp = 0.16f;
  private const float FrontCaptureDelaySeconds = 2f;
  private const float FrontRetentionSeconds = 1.5f;

  private bool _hasFrontPalmScale;
  private float _frontPalmScale;
  private float _frontCaptureVisibleSince = float.NegativeInfinity;
  private float _frontCaptureMinPalmScale;
  private float _lastDetectedTime = float.NegativeInfinity;
  private bool _hasStabilizedPalmScale;
  private float _stabilizedPalmScale;
  private bool _xMirrorResolved;
  private bool _xShouldMirror;

  // 初期状態の手データを生成する。
  public HandState CreateInitialState()
  {
    return new HandState
    {
      detected = false,
      x = 0.5f,
      y = 0.5f,
      depth = 1f,
      depthReady = false,
      pinch = false,
    };
  }

  // 手未検知時の状態へ戻す。
  public void SetUndetected(ref HandState handState)
  {
    handState.detected = false;
    handState.depthReady = _hasFrontPalmScale;
    handState.pinch = false;
    _hasStabilizedPalmScale = false;
    ResetFrontCaptureTimer();

    if (_hasFrontPalmScale && Time.unscaledTime - _lastDetectedTime > FrontRetentionSeconds)
    {
      _hasFrontPalmScale = false;
      _frontPalmScale = 0f;
      ResetFrontCaptureTimer();
      handState.depthReady = false;
      handState.depth = 1f;
    }
  }

  // ランドマークから手状態を更新する。
  public void UpdateHandState(Vector3[] landmarks, ref HandState handState)
  {
    handState.detected = true;
    _lastDetectedTime = Time.unscaledTime;

    var indexTip = landmarks[IndexTipIndex];
    var thumbTip = landmarks[ThumbTipIndex];

    handState.x = Mathf.Lerp(handState.x, ToSceneNormalizedX(indexTip.x), 0.55f);
    handState.y = Mathf.Lerp(handState.y, indexTip.y, 0.55f);

    var pinchDistance = Vector3.Distance(indexTip, thumbTip);
    const float pinchOnThreshold = 0.05f;
    const float pinchOffThreshold = 0.075f;
    var isPinching = handState.pinch
      ? pinchDistance < pinchOffThreshold
      : pinchDistance < pinchOnThreshold;

    var palmScale = GetPalmScale(landmarks);
    var stabilizedPalmScale = GetStabilizedPalmScale(palmScale);
    var depthReadyThisFrame = TryCaptureFrontPalmScale(stabilizedPalmScale);
    UpdateHandDepthFromPalmScale(stabilizedPalmScale, depthReadyThisFrame, ref handState);
    handState.depthReady = _hasFrontPalmScale;
    handState.pinch = isPinching;
  }

  // 手のひらサイズから depth 推定用の基準値を作る。
  private float GetPalmScale(Vector3[] landmarks)
  {
    var wrist = landmarks[WristIndex];
    var indexMcp = landmarks[IndexMcpIndex];
    var middleMcp = landmarks[MiddleMcpIndex];
    var pinkyMcp = landmarks[PinkyMcpIndex];

    var tri = (Distance2D(wrist, indexMcp) + Distance2D(wrist, pinkyMcp) + Distance2D(indexMcp, pinkyMcp)) / 3f;
    var spine = Distance2D(wrist, middleMcp);
    return tri * 0.7f + spine * 0.3f;
  }

  // 2 点間の 2D 距離を返す。
  private static float Distance2D(Vector3 a, Vector3 b)
  {
    var dx = a.x - b.x;
    var dy = a.y - b.y;
    return Mathf.Sqrt(dx * dx + dy * dy);
  }

  // 手サイズの瞬間ノイズを抑えた値を返す。
  private float GetStabilizedPalmScale(float palmScale)
  {
    if (!_hasStabilizedPalmScale)
    {
      _stabilizedPalmScale = palmScale;
      _hasStabilizedPalmScale = true;
      return _stabilizedPalmScale;
    }

    _stabilizedPalmScale = Mathf.Lerp(_stabilizedPalmScale, palmScale, PalmScaleStabilizeLerp);
    return _stabilizedPalmScale;
  }

  // 手が見えてから 2 秒のあいだで最も小さかった手サイズを、
  // ゲーム世界の手前基準として一度だけ確定し、確定したフレームなら true を返す。
  private bool TryCaptureFrontPalmScale(float palmScale)
  {
    if (_hasFrontPalmScale)
    {
      return false;
    }

    if (float.IsNegativeInfinity(_frontCaptureVisibleSince))
    {
      _frontCaptureVisibleSince = Time.unscaledTime;
      _frontCaptureMinPalmScale = palmScale;
      return false;
    }

    _frontCaptureMinPalmScale = Mathf.Min(_frontCaptureMinPalmScale, palmScale);

    if (Time.unscaledTime - _frontCaptureVisibleSince < FrontCaptureDelaySeconds)
    {
      return false;
    }

    _frontPalmScale = Mathf.Max(_frontCaptureMinPalmScale, MinimumFrontPalmScale);
    _hasFrontPalmScale = true;
    ResetFrontCaptureTimer();
    Debug.Log("認識しました。");
    return true;
  }

  // 次回の手前基準確定待ち時間を初期化する。
  private void ResetFrontCaptureTimer()
  {
    _frontCaptureVisibleSince = float.NegativeInfinity;
    _frontCaptureMinPalmScale = 0f;
  }

  // カメラから遠い手サイズをゲーム手前として、手サイズを depth に変換する。
  private void UpdateHandDepthFromPalmScale(float palmScale, bool depthReadyThisFrame, ref HandState handState)
  {
    if (!_hasFrontPalmScale)
    {
      handState.depth = 1f;
      return;
    }

    var visualBackPalmScale = Mathf.Max(_frontPalmScale * RelativeNearPalmScaleRatioFromFront, MinimumFrontPalmScale);
    var normalizedDepth = Mathf.InverseLerp(visualBackPalmScale, _frontPalmScale, palmScale);

    if (Mathf.Abs(normalizedDepth - handState.depth) < DepthDeadZone)
    {
      normalizedDepth = handState.depth;
    }

    if (depthReadyThisFrame)
    {
      handState.depth = normalizedDepth;
      return;
    }

    handState.depth = Mathf.Lerp(handState.depth, normalizedDepth, DepthResponseLerp);
  }

  // 画像座標の X をシーン用の左右方向へ補正する。
  private float ToSceneNormalizedX(float rawX)
  {
    var normalizedX = Mathf.Clamp01(rawX);
    var imageSource = ImageSourceProvider.ImageSource;
    if (imageSource == null)
    {
      return normalizedX;
    }

    if (!_xMirrorResolved && imageSource.isPrepared)
    {
      _xShouldMirror = true ^ imageSource.isHorizontallyFlipped ^ imageSource.isFrontFacing;
      _xMirrorResolved = true;
    }

    return _xMirrorResolved && _xShouldMirror ? 1f - normalizedX : normalizedX;
  }
}
