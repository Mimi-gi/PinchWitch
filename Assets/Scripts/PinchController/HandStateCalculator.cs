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
  // 手前基準サイズの下限値。
  private const float MinimumFrontPalmScale = 0.0001f;
  // palm size の瞬間値を平滑化する lerp 率。
  private const float PalmScaleStabilizeLerp = 0.2f;
  // depth の微小な揺れを無視する幅。
  private const float DepthDeadZone = 0.04f;
  // depth を追従させる lerp 率。
  private const float DepthResponseLerp = 0.16f;
  // 再認識時に手前基準を集める時間。
  private const float FrontCaptureDelaySeconds = 2f;

  // 手前基準サイズが確定済みかどうか。
  private bool _hasFrontPalmScale;
  // 初期 2 秒で得た手前判定用の最大 palm size。
  private float _frontPalmScale;
  // 現在の再認識区間が始まった時刻。
  private float _frontCaptureVisibleSince = float.NegativeInfinity;
  // 再認識後の 2 秒間で観測した最大 palm size。
  private float _frontCaptureMaxPalmScale;
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

  // 手が見えてから 2 秒のあいだで最も大きかった手サイズを、
  // 「手前として扱うサイズ帯の上限」として一度だけ確定する。
  // これにより、最初の 2 秒で見えていた手サイズはすべて手前側に収まる。
  private bool TryCaptureFrontPalmScale(float palmScale)
  {
    if (_hasFrontPalmScale)
    {
      return false;
    }

    if (float.IsNegativeInfinity(_frontCaptureVisibleSince))
    {
      _frontCaptureVisibleSince = Time.unscaledTime;
      _frontCaptureMaxPalmScale = palmScale;
      return false;
    }

    _frontCaptureMaxPalmScale = Mathf.Max(_frontCaptureMaxPalmScale, palmScale);

    if (Time.unscaledTime - _frontCaptureVisibleSince < FrontCaptureDelaySeconds)
    {
      return false;
    }

    _frontPalmScale = Mathf.Max(_frontCaptureMaxPalmScale, MinimumFrontPalmScale);
    _hasFrontPalmScale = true;
    ResetFrontCaptureTimer();
    Debug.Log("認識しました。");
    return true;
  }

  // 次回の手前基準確定待ち時間を初期化する。
  private void ResetFrontCaptureTimer()
  {
    _frontCaptureVisibleSince = float.NegativeInfinity;
    _frontCaptureMaxPalmScale = 0f;
  }

  // 小さい手サイズほど手前、大きい手サイズほど奥になるように depth を変換する。
  // 初期 2 秒で見えていた最大サイズまでは手前扱いに固定し、
  // それよりさらに大きくなったときだけ奥方向へ送る。
  private void UpdateHandDepthFromPalmScale(float palmScale, bool depthReadyThisFrame, ref HandState handState)
  {
    if (!_hasFrontPalmScale)
    {
      handState.depth = 1f;
      return;
    }

    var visualBackPalmScale = Mathf.Max(_frontPalmScale * RelativeBackPalmScaleRatioFromFront, MinimumFrontPalmScale);
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
