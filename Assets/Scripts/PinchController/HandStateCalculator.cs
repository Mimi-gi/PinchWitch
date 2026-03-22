using Mediapipe.Unity;
using UnityEngine;

public struct HandState
{
  public bool detected;
  public float x;
  public float y;
  public float depth;
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

  private bool _handDepthCalibrated;
  private int _handDepthCalibFrames;
  private float _handDepthCalibMin = float.PositiveInfinity;
  private float _handDepthCalibMax = float.NegativeInfinity;
  private float _handDepthRangeMin = 0.05f;
  private float _handDepthRangeMax = 0.14f;
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
      depth = 0.5f,
      pinch = false,
    };
  }

  // 手未検知時の状態へ戻す。
  public void SetUndetected(ref HandState handState)
  {
    handState.detected = false;
    handState.pinch = false;
  }

  // ランドマークから手状態を更新する。
  public void UpdateHandState(Vector3[] landmarks, ref HandState handState)
  {
    handState.detected = true;

    var indexTip = landmarks[IndexTipIndex];
    var thumbTip = landmarks[ThumbTipIndex];

    handState.x = Mathf.Lerp(handState.x, ToSceneNormalizedX(indexTip.x), 0.55f);
    handState.y = Mathf.Lerp(handState.y, indexTip.y, 0.55f);

    var palmScale = GetPalmScale(landmarks);
    UpdateHandDepthFromPalmScale(palmScale, ref handState);

    var pinchDistance = Vector3.Distance(indexTip, thumbTip);
    const float pinchOnThreshold = 0.05f;
    const float pinchOffThreshold = 0.075f;
    handState.pinch = handState.pinch
      ? pinchDistance < pinchOffThreshold
      : pinchDistance < pinchOnThreshold;
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

  // 手のひらサイズから depth の正規化値を更新する。
  private void UpdateHandDepthFromPalmScale(float palmScale, ref HandState handState)
  {
    if (!_handDepthCalibrated)
    {
      _handDepthCalibMin = Mathf.Min(_handDepthCalibMin, palmScale);
      _handDepthCalibMax = Mathf.Max(_handDepthCalibMax, palmScale);
      _handDepthCalibFrames++;

      if (_handDepthCalibFrames >= 12)
      {
        var initialSpan = Mathf.Max(_handDepthCalibMax - _handDepthCalibMin, 0.02f);
        _handDepthRangeMin = _handDepthCalibMin - initialSpan * 0.45f;
        _handDepthRangeMax = _handDepthCalibMax + initialSpan * 0.45f;
        _handDepthCalibrated = true;
      }
      else
      {
        return;
      }
    }
    else
    {
      const float edgeMargin = 0.004f;
      if (palmScale < _handDepthRangeMin + edgeMargin)
      {
        _handDepthRangeMin = Mathf.Lerp(_handDepthRangeMin, palmScale - edgeMargin, 0.22f);
      }
      if (palmScale > _handDepthRangeMax - edgeMargin)
      {
        _handDepthRangeMax = Mathf.Lerp(_handDepthRangeMax, palmScale + edgeMargin, 0.22f);
      }
      if (_handDepthRangeMax - _handDepthRangeMin < 0.07f)
      {
        _handDepthRangeMax = _handDepthRangeMin + 0.07f;
      }
    }

    var normalizedDepth = Mathf.Clamp01((palmScale - _handDepthRangeMin) / (_handDepthRangeMax - _handDepthRangeMin));
    normalizedDepth = Mathf.Clamp01((normalizedDepth - 0.5f) * 0.95f + 0.5f);

    const float deadZone = 0.04f;
    if (Mathf.Abs(normalizedDepth - handState.depth) < deadZone)
    {
      normalizedDepth = handState.depth;
    }

    handState.depth = Mathf.Lerp(handState.depth, normalizedDepth, 0.16f);
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
