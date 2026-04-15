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
  // 左手の移動入力を安定化するフィルタ。
  private readonly HandInputStabilizer _leftMovementInputStabilizer;
  // 右手の移動入力を安定化するフィルタ。
  private readonly HandInputStabilizer _rightMovementInputStabilizer;
  // 左手の回転ジェスチャー入力を軽めに安定化するフィルタ。
  private readonly HandInputStabilizer _leftRotationInputStabilizer;
  // 右手の回転ジェスチャー入力を軽めに安定化するフィルタ。
  private readonly HandInputStabilizer _rightRotationInputStabilizer;

  // 左手の移動用 HandState。
  private HandState _leftHandState;
  // 右手の移動用 HandState。
  private HandState _rightHandState;
  // 左手の回転用 HandState。
  private HandState _leftRotationHandState;
  // 右手の回転用 HandState。
  private HandState _rightRotationHandState;
  // 左手の補正前の生 HandState。
  private HandState _leftRawHandState;
  // 右手の補正前の生 HandState。
  private HandState _rightRawHandState;

  // 左右の手状態ストアを初期化する。
  public TrackedHandStateStore()
  {
    _leftHandStateCalculator = new HandStateCalculator();
    _rightHandStateCalculator = new HandStateCalculator();
    _leftMovementInputStabilizer = new HandInputStabilizer(HandInputStabilizer.CreateMovementConfig());
    _rightMovementInputStabilizer = new HandInputStabilizer(HandInputStabilizer.CreateMovementConfig());
    _leftRotationInputStabilizer = new HandInputStabilizer(HandInputStabilizer.CreateRotationConfig());
    _rightRotationInputStabilizer = new HandInputStabilizer(HandInputStabilizer.CreateRotationConfig());
    _leftRawHandState = _leftHandStateCalculator.CreateInitialState();
    _rightRawHandState = _rightHandStateCalculator.CreateInitialState();
    _leftHandState = _leftHandStateCalculator.CreateInitialState();
    _rightHandState = _rightHandStateCalculator.CreateInitialState();
    _leftRotationHandState = _leftHandStateCalculator.CreateInitialState();
    _rightRotationHandState = _rightHandStateCalculator.CreateInitialState();
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

  // 指定した手の回転ジェスチャー用状態を返す。
  public HandState GetRotationHandState(MediaPipeReceiver.TrackedHandType handType)
  {
    switch (handType)
    {
      case MediaPipeReceiver.TrackedHandType.Left:
        return _leftRotationHandState;
      case MediaPipeReceiver.TrackedHandType.Right:
        return _rightRotationHandState;
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
        _leftHandStateCalculator.SetUndetected(ref _leftRawHandState);
        _leftMovementInputStabilizer.Reset();
        _leftRotationInputStabilizer.Reset();
        _leftHandState = _leftRawHandState;
        _leftRotationHandState = _leftRawHandState;
        return;
      case MediaPipeReceiver.TrackedHandType.Right:
        _rightHandStateCalculator.SetUndetected(ref _rightRawHandState);
        _rightMovementInputStabilizer.Reset();
        _rightRotationInputStabilizer.Reset();
        _rightHandState = _rightRawHandState;
        _rightRotationHandState = _rightRawHandState;
        return;
      default:
        return;
    }
  }

  // 左右すべての手状態を未検知へ戻す。
  public void ResetAll()
  {
    _leftHandStateCalculator.SetUndetected(ref _leftRawHandState);
    _rightHandStateCalculator.SetUndetected(ref _rightRawHandState);
    _leftMovementInputStabilizer.Reset();
    _rightMovementInputStabilizer.Reset();
    _leftRotationInputStabilizer.Reset();
    _rightRotationInputStabilizer.Reset();
    _leftHandState = _leftRawHandState;
    _rightHandState = _rightRawHandState;
    _leftRotationHandState = _leftRawHandState;
    _rightRotationHandState = _rightRawHandState;
  }

  // 指定した手の最新フレームを手状態へ反映する。
  private void UpdateTrackedHandState(MediaPipeReceiver mediaPipeReceiver, MediaPipeReceiver.TrackedHandType handType, Vector3[] frameLandmarks)
  {
    switch (handType)
    {
      case MediaPipeReceiver.TrackedHandType.Left:
        UpdateTrackedHandState(
          mediaPipeReceiver,
          _leftHandStateCalculator,
          _leftMovementInputStabilizer,
          _leftRotationInputStabilizer,
          ref _leftRawHandState,
          ref _leftHandState,
          ref _leftRotationHandState,
          handType,
          frameLandmarks);
        return;
      case MediaPipeReceiver.TrackedHandType.Right:
        UpdateTrackedHandState(
          mediaPipeReceiver,
          _rightHandStateCalculator,
          _rightMovementInputStabilizer,
          _rightRotationInputStabilizer,
          ref _rightRawHandState,
          ref _rightHandState,
          ref _rightRotationHandState,
          handType,
          frameLandmarks);
        return;
      default:
        return;
    }
  }

  // 受信結果に応じて生の手状態を計算し、
  // その後に移動用と回転用の 2 種類の安定化済み手状態へ変換する。
  private static void UpdateTrackedHandState(
    MediaPipeReceiver mediaPipeReceiver,
    HandStateCalculator calculator,
    HandInputStabilizer movementStabilizer,
    HandInputStabilizer rotationStabilizer,
    ref HandState rawHandState,
    ref HandState movementHandState,
    ref HandState rotationHandState,
    MediaPipeReceiver.TrackedHandType handType,
    Vector3[] frameLandmarks)
  {
    if (mediaPipeReceiver == null || !mediaPipeReceiver.TryGetLatestLandmarks(handType, frameLandmarks))
    {
      calculator.SetUndetected(ref rawHandState);
      movementStabilizer.Reset();
      rotationStabilizer.Reset();
      movementHandState = rawHandState;
      rotationHandState = rawHandState;
      return;
    }

    calculator.UpdateHandState(frameLandmarks, ref rawHandState);
    movementHandState = rawHandState;
    rotationHandState = rawHandState;
    movementStabilizer.Stabilize(ref movementHandState);
    rotationStabilizer.Stabilize(ref rotationHandState);
  }
}
