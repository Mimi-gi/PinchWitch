using UnityEngine;

public sealed class TrackedHandStateStore
{
  private readonly Vector3[] _leftFrameLandmarks = new Vector3[MediaPipeReceiver.HandLandmarkCount];
  private readonly Vector3[] _rightFrameLandmarks = new Vector3[MediaPipeReceiver.HandLandmarkCount];

  private readonly HandStateCalculator _leftHandStateCalculator;
  private readonly HandStateCalculator _rightHandStateCalculator;

  private HandState _leftHandState;
  private HandState _rightHandState;

  // 左右の手状態ストアを初期化する。
  public TrackedHandStateStore()
  {
    _leftHandStateCalculator = new HandStateCalculator();
    _rightHandStateCalculator = new HandStateCalculator();
    _leftHandState = _leftHandStateCalculator.CreateInitialState();
    _rightHandState = _rightHandStateCalculator.CreateInitialState();
  }

  // MediaPipe の最新受信内容で左右の手状態を更新する。
  public void Update(MediaPipeReceiver mediaPipeReceiver)
  {
    UpdateTrackedHandState(mediaPipeReceiver, MediaPipeReceiver.TrackedHandType.Left, _leftFrameLandmarks);
    UpdateTrackedHandState(mediaPipeReceiver, MediaPipeReceiver.TrackedHandType.Right, _rightFrameLandmarks);
  }

  // 指定した手の最新状態を返す。
  public HandState GetHandState(MediaPipeReceiver.TrackedHandType handType)
  {
    switch (handType)
    {
      case MediaPipeReceiver.TrackedHandType.Left:
        return _leftHandState;
      case MediaPipeReceiver.TrackedHandType.Right:
        return _rightHandState;
      default:
        return default;
    }
  }

  // 指定した手を未検知状態へ戻す。
  public void SetUndetected(MediaPipeReceiver.TrackedHandType handType)
  {
    switch (handType)
    {
      case MediaPipeReceiver.TrackedHandType.Left:
        _leftHandStateCalculator.SetUndetected(ref _leftHandState);
        return;
      case MediaPipeReceiver.TrackedHandType.Right:
        _rightHandStateCalculator.SetUndetected(ref _rightHandState);
        return;
      default:
        return;
    }
  }

  // 左右すべての手状態を未検知へ戻す。
  public void ResetAll()
  {
    _leftHandStateCalculator.SetUndetected(ref _leftHandState);
    _rightHandStateCalculator.SetUndetected(ref _rightHandState);
  }

  // 指定した手の最新フレームを手状態へ反映する。
  private void UpdateTrackedHandState(MediaPipeReceiver mediaPipeReceiver, MediaPipeReceiver.TrackedHandType handType, Vector3[] frameLandmarks)
  {
    switch (handType)
    {
      case MediaPipeReceiver.TrackedHandType.Left:
        UpdateTrackedHandState(mediaPipeReceiver, _leftHandStateCalculator, ref _leftHandState, handType, frameLandmarks);
        return;
      case MediaPipeReceiver.TrackedHandType.Right:
        UpdateTrackedHandState(mediaPipeReceiver, _rightHandStateCalculator, ref _rightHandState, handType, frameLandmarks);
        return;
      default:
        return;
    }
  }

  // 受信結果に応じて手状態を更新し、未検知ならリセットする。
  private static void UpdateTrackedHandState(MediaPipeReceiver mediaPipeReceiver, HandStateCalculator calculator, ref HandState handState, MediaPipeReceiver.TrackedHandType handType, Vector3[] frameLandmarks)
  {
    if (mediaPipeReceiver == null || !mediaPipeReceiver.TryGetLatestLandmarks(handType, frameLandmarks))
    {
      calculator.SetUndetected(ref handState);
      return;
    }

    calculator.UpdateHandState(frameLandmarks, ref handState);
  }
}
