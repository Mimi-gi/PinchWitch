using UnityEngine;

public sealed class HandInteractionProcessor
{
  public struct Config
  {
    // 掴みを離した直後に再掴みを禁止する時間。
    public float grabReacquireCooldownSeconds;
    // 再掴みを許可する前に、指を開いたまま確認する時間。
    public float grabReopenConfirmSeconds;
  }

  // 手カーソルの表示と手ターゲット計算を担当する依存。
  private HandCursorController _handCursorController;
  // 掴み・解放・回転を担当する依存。
  private GrabInteractor _grabInteractor;
  // 回転ジェスチャー判定を担当する依存。
  private RotationGestureDetector _rotationGestureDetector;
  // アウトラインの表示制御を担当する依存。
  private OutlineController _outlineController;
  
  // 掴み再開クールダウンなどの操作設定。
  private Config _config;
  // 前フレームで pinch 中だったかどうか。
  private bool _prevPinch;
  // 次に grab を開始してよい時刻。
  private float _nextGrabAllowedTime;
  // 再掴み前に、指を開き直した確認が必要かどうか。
  private bool _requiresGrabRearm;
  // 指を開いた状態が始まった時刻。
  private float _grabReopenStartedAt = float.PositiveInfinity;
  // 回転完了後に release する保留状態かどうか。
  private bool _releasePendingAfterRotation;

  // 操作実行に使う各責務クラスを受け取って初期化する。
  public HandInteractionProcessor(HandCursorController handCursorController, GrabInteractor grabInteractor, RotationGestureDetector rotationGestureDetector, OutlineController outlineController, Config config)
  {
    UpdateDependencies(handCursorController, grabInteractor, rotationGestureDetector, outlineController);
    ApplyConfiguration(config);
  }

  // 操作実行に使う依存オブジェクトを更新する。
  public void UpdateDependencies(HandCursorController handCursorController, GrabInteractor grabInteractor, RotationGestureDetector rotationGestureDetector, OutlineController outlineController)
  {
    _handCursorController = handCursorController;
    _grabInteractor = grabInteractor;
    _rotationGestureDetector = rotationGestureDetector;
    _outlineController = outlineController;
  }

  // 掴み再開クールダウンなどの設定を更新する。
  public void ApplyConfiguration(Config config)
  {
    _config = config;
  }

  // 移動担当の手でカーソル表示と grab の開始・追従・解放を制御する。
  public bool TryProcessMovementInteraction(HandState movementHandState)
  {
    if (_handCursorController == null || _grabInteractor == null)
    {
      ResetInteractionState();
      return false;
    }

    if (!movementHandState.detected
        || !_handCursorController.TryUpdateCursor(movementHandState, out var handTarget))
    {
      HandleUnavailableMovementInteraction();
      return false;
    }

    // depth キャリブレーション完了までは grab 判定に使う pinch を無効化する。
    var interactionPinch = movementHandState.depthReady && movementHandState.pinch;
    UpdateGrabRearmState(interactionPinch);
    UpdateDeferredRelease(interactionPinch);
    var pinchStarted = interactionPinch && !_prevPinch;
    var pinchEnded = !interactionPinch && _prevPinch;
    var canBeginGrab = !_requiresGrabRearm && Time.unscaledTime >= _nextGrabAllowedTime;
    _prevPinch = interactionPinch;

    if (pinchStarted && canBeginGrab && !_grabInteractor.HasGrabbedBody)
    {
      _grabInteractor.TryBeginGrab(handTarget);
    }

    if (_grabInteractor.HasGrabbedBody && interactionPinch)
    {
      _grabInteractor.UpdateGrabbedObject(handTarget);
    }

    if (pinchEnded)
    {
      _rotationGestureDetector?.Reset();
      if (_grabInteractor.HasGrabbedBody)
      {
        if (_grabInteractor.IsGrabbedObjectRotating)
        {
          _releasePendingAfterRotation = true;
        }
        else
        {
          ReleaseGrabbedBody();
        }
      }
    }

    UpdateOutlineFeedback(handTarget);

    return true;
  }

  // 回転担当の手で grab 中オブジェクトの回転入力を処理する。
  public void ProcessRotationInteraction(MediaPipeReceiver mediaPipeReceiver, HandState rotationHandState, HandRoleAssignment handRoles)
  {
    if (_rotationGestureDetector == null
        || _grabInteractor == null
        || _releasePendingAfterRotation
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
    _outlineController?.ClearCurrentOutline();
    _prevPinch = false;
    _nextGrabAllowedTime = 0f;
    _requiresGrabRearm = false;
    _grabReopenStartedAt = float.PositiveInfinity;
    _releasePendingAfterRotation = false;
  }

  // hand が一時的に取れなくても、回転完了待ちの release は維持する。
  private void HandleUnavailableMovementInteraction()
  {
    _handCursorController?.HideCursor();
    _rotationGestureDetector?.Reset();
    _prevPinch = false;

    if (_releasePendingAfterRotation)
    {
      UpdateDeferredRelease(false);
      UpdateGrabbedOutlineFeedback();
      return;
    }

    ResetInteractionState();
  }

  // 回転完了待ちの release を更新する。
  private void UpdateDeferredRelease(bool interactionPinch)
  {
    if (!_releasePendingAfterRotation)
    {
      return;
    }

    if (interactionPinch)
    {
      _releasePendingAfterRotation = false;
      return;
    }

    if (_grabInteractor == null || !_grabInteractor.HasGrabbedBody)
    {
      _releasePendingAfterRotation = false;
      return;
    }

    if (_grabInteractor.IsGrabbedObjectRotating)
    {
      return;
    }

    ReleaseGrabbedBody();
  }

  // grab 中オブジェクトを解放し、再 grab クールダウンを開始する。
  private void ReleaseGrabbedBody()
  {
    if (_grabInteractor == null)
    {
      _releasePendingAfterRotation = false;
      return;
    }

    var hadGrabbedBody = _grabInteractor.HasGrabbedBody;
    _releasePendingAfterRotation = false;
    _grabInteractor.ReleaseGrabbedObject();
    _rotationGestureDetector?.Reset();
    if (!hadGrabbedBody)
    {
      return;
    }

    _nextGrabAllowedTime = Time.unscaledTime + _config.grabReacquireCooldownSeconds;
    _requiresGrabRearm = true;
    _grabReopenStartedAt = Time.unscaledTime;
  }

  // 再掴み前に「指をしっかり開いた」ことを確認する状態を更新する。
  private void UpdateGrabRearmState(bool interactionPinch)
  {
    if (!_requiresGrabRearm)
    {
      return;
    }

    if (interactionPinch)
    {
      _grabReopenStartedAt = float.PositiveInfinity;
      return;
    }

    if (float.IsPositiveInfinity(_grabReopenStartedAt))
    {
      _grabReopenStartedAt = Time.unscaledTime;
    }

    if (Time.unscaledTime - _grabReopenStartedAt >= _config.grabReopenConfirmSeconds)
    {
      _requiresGrabRearm = false;
      _grabReopenStartedAt = float.PositiveInfinity;
    }
  }

  // 状態に合わせて対象オブジェクトのアウトライン表示を更新する。
  private void UpdateOutlineFeedback(Vector3 handTarget)
  {
    if (_outlineController == null || _grabInteractor == null)
    {
      return;
    }

    if (_grabInteractor.HasGrabbedBody)
    {
      UpdateGrabbedOutlineFeedback();
    }
    else
    {
      // 掴める位置にあるオブジェクトならホバー表示
      if (_grabInteractor.TryGetNearestGrabbableBody(handTarget, out var nearestBody))
      {
        _outlineController.ShowHover(nearestBody);
      }
      else
      {
        _outlineController.ClearCurrentOutline();
      }
    }
  }

  // grab 中オブジェクトのアウトラインだけを更新する。
  private void UpdateGrabbedOutlineFeedback()
  {
    if (_outlineController == null || _grabInteractor == null)
    {
      return;
    }

    if (_grabInteractor.HasGrabbedBody)
    {
      if (_grabInteractor.IsGrabbedObjectOverlapping)
      {
        _outlineController.ShowError(_grabInteractor.GrabbedBody);
      }
      else
      {
        _outlineController.ShowGrabbed(_grabInteractor.GrabbedBody);
      }
      return;
    }

    _outlineController.ClearCurrentOutline();
  }
}
