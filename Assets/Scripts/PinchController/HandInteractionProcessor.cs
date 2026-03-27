public sealed class HandInteractionProcessor
{
  // 手カーソルの表示と手ターゲット計算を担当する依存。
  private HandCursorController _handCursorController;
  // 掴み・解放・回転を担当する依存。
  private GrabInteractor _grabInteractor;
  // 回転ジェスチャー判定を担当する依存。
  private RotationGestureDetector _rotationGestureDetector;
  // 前フレームで pinch 中だったかどうか。
  private bool _prevPinch;

  // 操作実行に使う各責務クラスを受け取って初期化する。
  public HandInteractionProcessor(HandCursorController handCursorController, GrabInteractor grabInteractor, RotationGestureDetector rotationGestureDetector)
  {
    UpdateDependencies(handCursorController, grabInteractor, rotationGestureDetector);
  }

  // 操作実行に使う依存オブジェクトを更新する。
  public void UpdateDependencies(HandCursorController handCursorController, GrabInteractor grabInteractor, RotationGestureDetector rotationGestureDetector)
  {
    _handCursorController = handCursorController;
    _grabInteractor = grabInteractor;
    _rotationGestureDetector = rotationGestureDetector;
  }

  // 移動担当の手でカーソル表示と grab の開始・追従・解放を制御する。
  public bool TryProcessMovementInteraction(HandState movementHandState)
  {
    if (_handCursorController == null
        || _grabInteractor == null
        || !movementHandState.detected
        || !_handCursorController.TryUpdateCursor(movementHandState, out var handTarget))
    {
      ResetInteractionState();
      return false;
    }

    // depth キャリブレーション完了までは grab 判定に使う pinch を無効化する。
    var interactionPinch = movementHandState.depthReady && movementHandState.pinch;
    var pinchStarted = interactionPinch && !_prevPinch;
    var pinchEnded = !interactionPinch && _prevPinch;
    _prevPinch = interactionPinch;

    if (pinchStarted && !_grabInteractor.HasGrabbedBody)
    {
      _grabInteractor.TryBeginGrab(handTarget);
    }

    if (_grabInteractor.HasGrabbedBody && interactionPinch)
    {
      _grabInteractor.UpdateGrabbedObject(handTarget);
    }

    if (pinchEnded)
    {
      _grabInteractor.ReleaseGrabbedObject();
      _rotationGestureDetector?.Reset();
    }

    return true;
  }

  // 回転担当の手で grab 中オブジェクトの回転入力を処理する。
  public void ProcessRotationInteraction(MediaPipeReceiver mediaPipeReceiver, HandState rotationHandState, HandRoleAssignment handRoles)
  {
    if (_rotationGestureDetector == null
        || _grabInteractor == null
        || mediaPipeReceiver == null
        || !_grabInteractor.HasGrabbedBody
        || !mediaPipeReceiver.HasFreshHand(handRoles.MovementHandType)
        || !mediaPipeReceiver.HasFreshHand(handRoles.RotationHandType))
    {
      _rotationGestureDetector?.Reset();
      return;
    }

    if (!rotationHandState.pinch)
    {
      _rotationGestureDetector.Reset();
      return;
    }

    if (_rotationGestureDetector.TryDetectSwipe(rotationHandState, out var rotationCommand))
    {
      _grabInteractor.RotateGrabbedObject(rotationCommand.worldAxis, rotationCommand.angleDegrees);
    }
  }

  // カーソル・grab・回転まわりの一時状態をまとめてリセットする。
  public void ResetInteractionState()
  {
    _handCursorController?.HideCursor();
    _grabInteractor?.ReleaseGrabbedObject();
    _rotationGestureDetector?.Reset();
    _prevPinch = false;
  }
}
