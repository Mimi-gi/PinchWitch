using Mediapipe.Unity;
using UnityEngine;

public struct HandState
{
  // 手が現在検知されているかどうか。
  public bool detected;
  // 手の横方向位置を 0..1 で表した値。
  public float x;
  // 手の縦方向位置を 0..1 で表した値。
  public float y;
  // 0 = 画面奥側、1 = 画面手前側。カメラ基準の見た目 depth。
  public float depth;
  // depth 用キャリブレーションが完了しているかどうか。
  public bool depthReady;
  // 親指と人差し指で pinch 中かどうか。
  public bool pinch;
}

public sealed class HandStateCalculator
{
  // 手首ランドマークのインデックス。
  private const int WristIndex = 0;
  // 親指先ランドマークのインデックス。
  private const int ThumbTipIndex = 4;
  // 人差し指先ランドマークのインデックス。
  private const int IndexTipIndex = 8;
  // 人差し指付け根ランドマークのインデックス。
  private const int IndexMcpIndex = 5;
  // 中指付け根ランドマークのインデックス。
  private const int MiddleMcpIndex = 9;
  // 小指付け根ランドマークのインデックス。
  private const int PinkyMcpIndex = 17;
  // 手前基準サイズから奥側基準サイズを作る倍率。
  private const float RelativeBackPalmScaleRatioFromFront = 1.27f;
  // 初期 2 秒の平均 palm size を割り当てる depth 値。
  private const float CapturedAverageDepth = 0.8f;
  // 初期 2 秒の平均 palm size から、手前端サイズを作る縮小率。
  private const float RelativeFrontPalmScaleRatioFromAverage = 0.9f;
  // 手前基準サイズの下限値。
  private const float MinimumFrontPalmScale = 0.0001f;
  // palm size の瞬間値を平滑化する lerp 率。
  private const float PalmScaleStabilizeLerp = 0.2f;
  // depth を追従させる lerp 率。
  private const float DepthResponseLerp = 0.16f;
  // 再認識時に手前基準を集める時間。
  private const float FrontCaptureDelaySeconds = 2f;

  // 手前基準サイズが確定済みかどうか。
  private bool _hasFrontPalmScale;
  // 初期 2 秒で得た平均 palm size。
  private float _frontPalmScale;
  // 現在の再認識区間が始まった時刻。
  private float _frontCaptureVisibleSince = float.NegativeInfinity;
  // 再認識後の 2 秒間で観測した palm size の合計値。
  private float _frontCapturePalmScaleSum;
  // 再認識後の 2 秒間で観測した palm size のサンプル数。
  private int _frontCaptureSampleCount;
  // palm size の平滑化値が初期化済みかどうか。
  private bool _hasStabilizedPalmScale;
  // palm size の平滑化済み値。
  private float _stabilizedPalmScale;
  // X 軸反転の向きが解決済みかどうか。
  private bool _xMirrorResolved;
  // 画像の X を左右反転して使うべきかどうか。
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
    handState.pinch = false;
    handState.depthReady = false;
    handState.depth = 1f;
    _hasStabilizedPalmScale = false;
    _hasFrontPalmScale = false;
    _frontPalmScale = 0f;
    ResetFrontCaptureTimer();
  }

  // ランドマークから手状態を更新する。
  public void UpdateHandState(Vector3[] landmarks, ref HandState handState)
  {
    handState.detected = true;

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

  // 手が見えてから 2 秒のあいだの palm size 平均を、
  // 前後変換の基準アンカーとして一度だけ確定する。
  private bool TryCaptureFrontPalmScale(float palmScale)
  {
    if (_hasFrontPalmScale)
    {
      return false;
    }

    if (float.IsNegativeInfinity(_frontCaptureVisibleSince))
    {
      _frontCaptureVisibleSince = Time.unscaledTime;
      _frontCapturePalmScaleSum = palmScale;
      _frontCaptureSampleCount = 1;
      return false;
    }

    _frontCapturePalmScaleSum += palmScale;
    _frontCaptureSampleCount++;

    if (Time.unscaledTime - _frontCaptureVisibleSince < FrontCaptureDelaySeconds)
    {
      return false;
    }

    var averagePalmScale = _frontCaptureSampleCount > 0
      ? _frontCapturePalmScaleSum / _frontCaptureSampleCount
      : MinimumFrontPalmScale;
    _frontPalmScale = Mathf.Max(averagePalmScale, MinimumFrontPalmScale);
    _hasFrontPalmScale = true;
    ResetFrontCaptureTimer();
    Debug.Log("認識しました。");
    return true;
  }

  // 次回の手前基準確定待ち時間を初期化する。
  private void ResetFrontCaptureTimer()
  {
    _frontCaptureVisibleSince = float.NegativeInfinity;
    _frontCapturePalmScaleSum = 0f;
    _frontCaptureSampleCount = 0;
  }

  // 小さい手サイズほど手前、大きい手サイズほど奥になるように
  // 前後全域を線形に使って depth を変換する。
  // 初期 2 秒の平均サイズは depth=0.8 の手前寄り位置になる。
  private void UpdateHandDepthFromPalmScale(float palmScale, bool depthReadyThisFrame, ref HandState handState)
  {
    if (!_hasFrontPalmScale)
    {
      handState.depth = 1f;
      return;
    }

    var visualFrontPalmScale = Mathf.Max(_frontPalmScale * RelativeFrontPalmScaleRatioFromAverage, MinimumFrontPalmScale);
    var visualBackPalmScale = Mathf.Max(
      ResolveBackPalmScaleForCapturedAverage(_frontPalmScale, visualFrontPalmScale),
      visualFrontPalmScale * RelativeBackPalmScaleRatioFromFront);
    var normalizedDepth = Mathf.InverseLerp(visualBackPalmScale, visualFrontPalmScale, palmScale);

    if (depthReadyThisFrame)
    {
      handState.depth = normalizedDepth;
      return;
    }

    handState.depth = Mathf.Lerp(handState.depth, normalizedDepth, DepthResponseLerp);
  }

  // 初期 2 秒の平均サイズが指定 depth に来るよう、奥端の palm size を逆算する。
  private static float ResolveBackPalmScaleForCapturedAverage(float capturedAveragePalmScale, float frontPalmScale)
  {
    var clampedDepth = Mathf.Clamp(CapturedAverageDepth, 0.01f, 0.99f);
    var denominator = Mathf.Max(1f - clampedDepth, 0.0001f);
    var backPalmScale = (capturedAveragePalmScale - (clampedDepth * frontPalmScale)) / denominator;
    return Mathf.Max(backPalmScale, frontPalmScale + MinimumFrontPalmScale);
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
