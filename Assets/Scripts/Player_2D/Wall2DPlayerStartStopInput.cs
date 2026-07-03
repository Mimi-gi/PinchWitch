using UnityEngine;

[DisallowMultipleComponent]
public sealed class Wall2DPlayerStartStopInput : MonoBehaviour
{
  [Header("Target")]
  [SerializeField] private Wall2DPlayerController playerController;
  [SerializeField] private Wall2DPlayerRespawner playerRespawner;

  [Header("Input")]
  [SerializeField] private KeyCode toggleKey = KeyCode.Space;
  [SerializeField] private bool startStopped = true;
  [SerializeField] private bool disablePhysicsWhileStopped = true;

  private Wall2DPlayerController _cachedPlayerController;
  private Rigidbody2D _playerRigidbody2D;
  private Wall2DPlayerCrushDetector _playerCrushDetector;
  private Wall2DPlayerAnimator _playerAnimator;
  private bool _isMovementActive;
  private bool _hasAppliedStateToCurrentPlayer;
  private bool _isTransitionReactionPlaying;
  private bool _pendingMovementActive;
  private float _transitionReactionEndTime;

  private void Reset()
  {
    AutoAssignMissingReferences();
  }

  private void Awake()
  {
    AutoAssignMissingReferences();
    _isMovementActive = !startStopped;
  }

  private void Start()
  {
    ApplyCurrentStateToPlayerIfAvailable();
  }

  private void Update()
  {
    AutoAssignMissingReferences();
    if (IsPlayerLockedByRespawner())
    {
      ResetPendingInputState();
      _hasAppliedStateToCurrentPlayer = false;
      return;
    }

    ApplyCurrentStateToPlayerIfAvailable();

    if (UpdateTransitionReaction())
    {
      return;
    }

    UpdateStartStopInput();
  }

  private void AutoAssignMissingReferences()
  {
    if (playerController == null)
    {
      playerController = FindFirstObjectByType<Wall2DPlayerController>(FindObjectsInactive.Include);
    }

    if (playerRespawner == null)
    {
      playerRespawner = FindFirstObjectByType<Wall2DPlayerRespawner>(FindObjectsInactive.Include);
    }

    RefreshCachedPlayerComponents();
  }

  private void RefreshCachedPlayerComponents()
  {
    if (_cachedPlayerController == playerController)
    {
      return;
    }

    _cachedPlayerController = playerController;
    _hasAppliedStateToCurrentPlayer = false;
    _playerRigidbody2D = playerController != null ? playerController.GetComponent<Rigidbody2D>() : null;
    _playerCrushDetector = playerController != null ? playerController.GetComponent<Wall2DPlayerCrushDetector>() : null;
    _playerAnimator = playerController != null ? playerController.GetComponent<Wall2DPlayerAnimator>() : null;
  }

  private void ApplyCurrentStateToPlayerIfAvailable()
  {
    if (playerController == null)
    {
      return;
    }

    if (IsPlayerLockedByRespawner())
    {
      _hasAppliedStateToCurrentPlayer = false;
      return;
    }

    if (_hasAppliedStateToCurrentPlayer && !ShouldReapplyCurrentState())
    {
      return;
    }

    ApplyMovementState(_isMovementActive);
    _hasAppliedStateToCurrentPlayer = true;
  }

  private bool ShouldReapplyCurrentState()
  {
    if (playerController == null)
    {
      return false;
    }

    if (IsPlayerLockedByRespawner())
    {
      return false;
    }

    if (!_isMovementActive)
    {
      return _playerRigidbody2D != null
             && _playerRigidbody2D.simulated
             && !playerController.IsStoppedByStartStopInput;
    }

    return !playerController.enabled
           || playerController.IsStoppedByStartStopInput
           || (_playerRigidbody2D != null && !_playerRigidbody2D.simulated);
  }

  private void UpdateStartStopInput()
  {
    if (!Input.GetKeyDown(toggleKey))
    {
      return;
    }

    RequestMovementToggle();
  }

  private bool IsPlayerLockedByRespawner()
  {
    return playerRespawner != null && playerRespawner.IsShadowCrushPausedFor(playerController);
  }

  private void ResetPendingInputState()
  {
    _isTransitionReactionPlaying = false;
    _transitionReactionEndTime = 0f;
  }

  private void RequestMovementToggle()
  {
    BeginMovementTransition(!_isMovementActive);
  }

  private void BeginMovementTransition(bool targetMovementActive)
  {
    _pendingMovementActive = targetMovementActive;

    if (!targetMovementActive)
    {
      ApplyMovementState(false);
    }

    if (TryBeginTransitionReaction())
    {
      return;
    }

    CompleteMovementTransition();
  }

  private bool TryBeginTransitionReaction()
  {
    if (_playerAnimator == null && playerController != null)
    {
      _playerAnimator = playerController.GetComponent<Wall2DPlayerAnimator>();
    }

    if (_playerAnimator == null)
    {
      return false;
    }

    _playerAnimator.PlayGoodReaction();

    var reactionDuration = Mathf.Max(0f, _playerAnimator.GoodReactionDuration);
    if (reactionDuration <= Mathf.Epsilon)
    {
      return false;
    }

    _isTransitionReactionPlaying = true;
    _transitionReactionEndTime = Time.unscaledTime + reactionDuration;
    return true;
  }

  private bool UpdateTransitionReaction()
  {
    if (!_isTransitionReactionPlaying)
    {
      return false;
    }

    if (Time.unscaledTime < _transitionReactionEndTime)
    {
      return true;
    }

    CompleteMovementTransition();
    return true;
  }

  private void CompleteMovementTransition()
  {
    _isTransitionReactionPlaying = false;
    _transitionReactionEndTime = 0f;

    if (_isMovementActive == _pendingMovementActive)
    {
      return;
    }

    ApplyMovementState(_pendingMovementActive);
  }

  private void ApplyMovementState(bool isActive)
  {
    _isMovementActive = isActive;

    if (playerController == null)
    {
      _hasAppliedStateToCurrentPlayer = false;
      return;
    }

    if (isActive)
    {
      StartPlayerMovement();
    }
    else
    {
      StopPlayerMovement();
    }
  }

  private void StartPlayerMovement()
  {
    if (_playerRigidbody2D != null)
    {
      _playerRigidbody2D.simulated = true;
    }

    if (_playerCrushDetector != null)
    {
      _playerCrushDetector.enabled = true;
    }

    if (playerController == null)
    {
      return;
    }

    if (!playerController.enabled)
    {
      playerController.enabled = true;
    }

    playerController.StartFromStartStopInput();
  }

  private void StopPlayerMovement()
  {
    if (playerController != null)
    {
      playerController.StopFromStartStopInput();
    }

    if (_playerCrushDetector != null)
    {
      _playerCrushDetector.enabled = false;
    }

    if (!disablePhysicsWhileStopped || _playerRigidbody2D == null)
    {
      return;
    }

    _playerRigidbody2D.linearVelocity = Vector2.zero;
    _playerRigidbody2D.angularVelocity = 0f;
    _playerRigidbody2D.simulated = false;
  }
}
