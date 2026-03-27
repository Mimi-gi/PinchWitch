using UnityEngine;

public sealed class TrackedHandStateStore
{
  // 左手の最新ランドマーク作業バッファ。
  private readonly Vector3[] _leftFrameLandmarks = new Vector3[MediaPipeReceiver.HandLandmarkCount];
  // 右手の最新ランドマーク作業バッファ。
  private readonly Vector3[] _rightFrameLandmarks = new Vector3[MediaPipeReceiver.HandLandmarkCount];

  // 左手の HandState を計算する計算機。
  private readonly HandStateCalculator _leftHandStateCalculator;
  // 右手の HandState を計算する計算機。
  private readonly HandStateCalculator _rightHandStateCalculator;
  // 左手の生入力を安定化するフィルタ。
  private readonly HandInputStabilizer _leftHandInputStabilizer;
  // 右手の生入力を安定化するフィルタ。
  private readonly HandInputStabilizer _rightHandInputStabilizer;

  // 左手の最新 HandState。
  private HandState _leftHandState;
  // 右手の最新 HandState。
  private HandState _rightHandState;

  // 左右の手状態ストアを初期化する。
  public TrackedHandStateStore()
  {
    _leftHandStateCalculator = new HandStateCalculator();
    _rightHandStateCalculator = new HandStateCalculator();
    _leftHandInputStabilizer = new HandInputStabilizer();
    _rightHandInputStabilizer = new HandInputStabilizer();
    _leftHandState = _leftHandStateCalculator.CreateInitialState();
    _rightHandState = _rightHandStateCalculator.CreateInitialState();
  }

  // MediaPipe の最新受信内容から、
  // 生の手状態の計算と入力安定化を順に適用して左右の手状態を更新する。
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
        _leftHandInputStabilizer.Reset();
        return;
      case MediaPipeReceiver.TrackedHandType.Right:
        _rightHandStateCalculator.SetUndetected(ref _rightHandState);
        _rightHandInputStabilizer.Reset();
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
    _leftHandInputStabilizer.Reset();
    _rightHandInputStabilizer.Reset();
  }

  // 指定した手の最新フレームを手状態へ反映する。
  private void UpdateTrackedHandState(MediaPipeReceiver mediaPipeReceiver, MediaPipeReceiver.TrackedHandType handType, Vector3[] frameLandmarks)
  {
    switch (handType)
    {
      case MediaPipeReceiver.TrackedHandType.Left:
        UpdateTrackedHandState(mediaPipeReceiver, _leftHandStateCalculator, _leftHandInputStabilizer, ref _leftHandState, handType, frameLandmarks);
        return;
      case MediaPipeReceiver.TrackedHandType.Right:
        UpdateTrackedHandState(mediaPipeReceiver, _rightHandStateCalculator, _rightHandInputStabilizer, ref _rightHandState, handType, frameLandmarks);
        return;
      default:
        return;
    }
  }

  // 受信結果に応じて生の手状態を計算し、
  // その後に安定化済みの手状態へ変換する。
  private static void UpdateTrackedHandState(MediaPipeReceiver mediaPipeReceiver, HandStateCalculator calculator, HandInputStabilizer stabilizer, ref HandState handState, MediaPipeReceiver.TrackedHandType handType, Vector3[] frameLandmarks)
  {
    if (mediaPipeReceiver == null || !mediaPipeReceiver.TryGetLatestLandmarks(handType, frameLandmarks))
    {
      calculator.SetUndetected(ref handState);
      stabilizer.Reset();
      return;
    }

    calculator.UpdateHandState(frameLandmarks, ref handState);
    stabilizer.Stabilize(ref handState);
  }
}
