using UnityEngine;
using UnityEngine.Serialization;
using System;

public enum SpaceViewMode
{
  World3D,
  Wall2D,
}

[DisallowMultipleComponent]
public sealed class SpaceViewModeSwitcher : MonoBehaviour, IRuntimeResettable
{
  [Header("Input")]
  [FormerlySerializedAs("toggleKey")]
  [SerializeField] private KeyCode startGameplayKey = KeyCode.Space;

  [Header("Cameras")]
  [SerializeField] private Camera world3DCamera;
  [SerializeField] private Camera wall2DCamera;

  [Header("Startup")]
  [SerializeField] private SpaceViewMode initialMode = SpaceViewMode.World3D;
  [SerializeField] private bool switchAudioListeners = true;
  [SerializeField] private bool isRealTimeMode = true;

  [Header("Realtime Start Gesture")]
  [SerializeField] private bool requireGoodGestureToStartRealTime = true;
  [SerializeField] private HandPinchController handPinchController;
  [SerializeField] private float goodGestureHoldSeconds = 2f;
  [SerializeField] private float goodGestureMovementToggleHoldSeconds = 0.35f;

  public event Action<SpaceViewMode> ModeChanged;
  public SpaceViewMode CurrentMode { get; private set; } = SpaceViewMode.World3D;
  public bool IsRealTimeMode => isRealTimeMode;
  public bool IsRealTimeGameplayStarted => !IsRealTimeStartGestureRequired() || _realTimeGameplayStarted;
  public bool ShouldEnablePlayerGameplay => ResolveBasePlayerGameplayEnabled() && !_isPlayerGameplayLockedExternally;
  public int ResetOrder => -100;

  private bool _realTimeGameplayStarted;
  private float _goodGestureHeldSeconds;
  private bool _isStartReactionPlaying;
  private float _startReactionEndTime;
  private bool _hasPlayedGameplayStartSE;
  private bool _isPlayerGameplayLockedExternally;
  private bool _unlockPlayerGameplayWhenRealtimeStarts;
  private bool _waitForStartInputRelease;
  private float _goodGestureMovementToggleHeldSeconds;
  private bool _waitForGoodGestureMovementToggleRelease;

  private void Reset()
  {
    AutoAssignMissingReferences();
  }

  private void Awake()
  {
    AutoAssignMissingReferences();
    ResetRealTimeStartGate();
    SetMode(initialMode);
  }

  private void OnValidate()
  {
    AutoAssignMissingReferences();
    ClampSerializedValues();

    if (!Application.isPlaying)
    {
      return;
    }

    SetMode(CurrentMode);
  }

  private void Update()
  {
    if (!Application.isPlaying)
    {
      return;
    }

    UpdateStartReactionPlayback();

    if (_waitForStartInputRelease)
    {
      _waitForStartInputRelease = IsStartGameplayInputPressed();
      if (_waitForStartInputRelease)
      {
        return;
      }
    }

    UpdateRealTimeStartGesture();
    UpdateGoodGestureMovementToggle();
    UpdateStartGameplayKey();
  }

  private void UpdateStartGameplayKey()
  {
    if (!Input.GetKeyDown(startGameplayKey))
    {
      return;
    }

    if (CanUseGoodGestureMovementToggle())
    {
      TogglePlayerMovementFromGoodGesture();
      _goodGestureMovementToggleHeldSeconds = 0f;
      return;
    }

    StartRealTimeGameplay(playGoodReaction: true);
  }

  public void ToggleView()
  {
    if (CurrentMode == SpaceViewMode.World3D)
    {
      SetMode(SpaceViewMode.Wall2D);
    }
    else
    {
      SetMode(SpaceViewMode.World3D);
    }
  }

  public void SetMode(SpaceViewMode mode)
  {
    if (!HasRequiredReferences())
    {
      return;
    }

    var modeChanged = CurrentMode != mode;
    CurrentMode = mode;
    var isWorld3D = mode == SpaceViewMode.World3D;
    ApplyCameraState(world3DCamera, isWorld3D);
    ApplyCameraState(wall2DCamera, !isWorld3D);

    // 視点切り替えに合わせてプレイヤーの有効/無効を更新
    UpdatePlayerMovementState();

    if (modeChanged)
    {
      ModeChanged?.Invoke(CurrentMode);
    }
  }

  /// <summary>
  /// プレイヤーが操作中であれば、現在のカメラモードと isRealTimeMode 設定に基づいて
  /// 物理挙動のオンオフを再適用する。
  /// </summary>
  public void UpdatePlayerMovementState()
  {
    var respawner = FindFirstObjectByType<Wall2DPlayerRespawner>();
    if (respawner == null)
    {
      return;
    }

    respawner.SetPlayerGameplayEnabled(ShouldEnablePlayerGameplay);
  }

  public void SetExternalPlayerGameplayLock(bool isLocked)
  {
    _unlockPlayerGameplayWhenRealtimeStarts = false;

    if (_isPlayerGameplayLockedExternally == isLocked)
    {
      return;
    }

    _isPlayerGameplayLockedExternally = isLocked;
    UpdatePlayerMovementState();
  }

  public void WaitForRealTimeGameplayStart()
  {
    if (!isRealTimeMode)
    {
      _unlockPlayerGameplayWhenRealtimeStarts = false;
      SetExternalPlayerGameplayLock(false);
      return;
    }

    if (IsRealTimeStartGestureRequired())
    {
      _realTimeGameplayStarted = false;
    }

    _unlockPlayerGameplayWhenRealtimeStarts = true;
    _goodGestureHeldSeconds = 0f;
    _waitForStartInputRelease = IsStartGameplayInputPressed();

    if (_isPlayerGameplayLockedExternally)
    {
      return;
    }

    _isPlayerGameplayLockedExternally = true;
    UpdatePlayerMovementState();
  }

  public void EnsureRealTimeGameplayStarted(bool playGoodReaction = false)
  {
    if (!isRealTimeMode)
    {
      UpdatePlayerMovementState();
      return;
    }

    StartRealTimeGameplay(playGoodReaction);
  }

  /// <summary>
  /// 視点切り替えを起動時のモードへ戻す。
  /// </summary>
  public void ResetRuntimeState()
  {
    ResetRealTimeStartGate();
    SetMode(initialMode);
  }

  private void AutoAssignMissingReferences()
  {
    if (world3DCamera == null)
    {
      world3DCamera = Camera.main;
    }

    if (wall2DCamera == null)
    {
      var cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
      foreach (var candidate in cameras)
      {
        if (candidate != null && candidate != world3DCamera)
        {
          wall2DCamera = candidate;
          break;
        }
      }
    }

    if (handPinchController == null)
    {
      handPinchController = FindFirstObjectByType<HandPinchController>(FindObjectsInactive.Include);
    }
  }

  private bool HasRequiredReferences()
  {
    if (world3DCamera != null && wall2DCamera != null)
    {
      return true;
    }

    Debug.LogWarning("SpaceViewModeSwitcher requires both world3DCamera and wall2DCamera to be assigned.");
    return false;
  }

  private void ApplyCameraState(Camera target, bool isActive)
  {
    if (target == null)
    {
      return;
    }

    target.enabled = isActive;

    if (!switchAudioListeners)
    {
      return;
    }

    var audioListener = target.GetComponent<AudioListener>();
    if (audioListener != null)
    {
      audioListener.enabled = isActive;
    }
  }

  private void ClampSerializedValues()
  {
    if (world3DCamera != null && wall2DCamera == world3DCamera)
    {
      wall2DCamera = null;
    }

    goodGestureHoldSeconds = Mathf.Max(0f, goodGestureHoldSeconds);
    goodGestureMovementToggleHoldSeconds = Mathf.Max(0f, goodGestureMovementToggleHoldSeconds);
  }

  private bool IsRealTimeStartGestureRequired()
  {
    return isRealTimeMode && requireGoodGestureToStartRealTime;
  }

  private bool ResolveBasePlayerGameplayEnabled()
  {
    return isRealTimeMode
      ? IsRealTimeGameplayStarted && !_isStartReactionPlaying
      : CurrentMode == SpaceViewMode.Wall2D;
  }

  private void ResetRealTimeStartGate()
  {
    _realTimeGameplayStarted = !IsRealTimeStartGestureRequired();
    _goodGestureHeldSeconds = 0f;
    _isStartReactionPlaying = false;
    _startReactionEndTime = 0f;
    _hasPlayedGameplayStartSE = false;
    _isPlayerGameplayLockedExternally = false;
    _unlockPlayerGameplayWhenRealtimeStarts = false;
    _waitForStartInputRelease = false;
    _goodGestureMovementToggleHeldSeconds = 0f;
    _waitForGoodGestureMovementToggleRelease = false;
  }

  private void UpdateRealTimeStartGesture()
  {
    if (IsRealTimeGameplayStarted || !IsRealTimeStartGestureRequired())
    {
      return;
    }

    if (handPinchController == null)
    {
      handPinchController = FindFirstObjectByType<HandPinchController>(FindObjectsInactive.Include);
    }

    if (handPinchController != null && handPinchController.IsKachinkoGestureDetected())
    {
      StartRealTimeGameplay(playGoodReaction: true);
      return;
    }

    if (handPinchController != null && handPinchController.IsLeftGoodGestureDetected())
    {
      _goodGestureHeldSeconds += Time.unscaledDeltaTime;
    }
    else
    {
      _goodGestureHeldSeconds = 0f;
    }

    if (_goodGestureHeldSeconds < goodGestureHoldSeconds)
    {
      return;
    }

    StartRealTimeGameplay(playGoodReaction: true);
  }

  private void StartRealTimeGameplay(bool playGoodReaction)
  {
    if (_realTimeGameplayStarted || !isRealTimeMode)
    {
      return;
    }

    _realTimeGameplayStarted = true;
    _goodGestureHeldSeconds = 0f;
    _isStartReactionPlaying = false;
    _startReactionEndTime = 0f;
    _waitForStartInputRelease = false;
    _goodGestureMovementToggleHeldSeconds = 0f;
    _waitForGoodGestureMovementToggleRelease = IsMovementToggleGesturePressed();

    if (playGoodReaction)
    {
      BeginStartReactionIfPossible();
    }

    if (_unlockPlayerGameplayWhenRealtimeStarts)
    {
      _unlockPlayerGameplayWhenRealtimeStarts = false;
      _isPlayerGameplayLockedExternally = false;
    }

    if (!_isStartReactionPlaying)
    {
      PlayGameplayStartSE();
    }

    UpdatePlayerMovementState();
  }

  private bool IsStartGameplayInputPressed()
  {
    if (Input.GetKey(startGameplayKey))
    {
      return true;
    }

    if (handPinchController == null)
    {
      handPinchController = FindFirstObjectByType<HandPinchController>(FindObjectsInactive.Include);
    }

    return handPinchController != null && handPinchController.IsRealTimeStartGestureDetected();
  }

  private void UpdateGoodGestureMovementToggle()
  {
    if (!CanUseGoodGestureMovementToggle())
    {
      _goodGestureMovementToggleHeldSeconds = 0f;
      return;
    }

    var isMovementToggleGesturePressed = IsMovementToggleGesturePressed();
    if (_waitForGoodGestureMovementToggleRelease)
    {
      if (!isMovementToggleGesturePressed)
      {
        _waitForGoodGestureMovementToggleRelease = false;
      }

      _goodGestureMovementToggleHeldSeconds = 0f;
      return;
    }

    if (handPinchController != null && handPinchController.IsKachinkoGestureDetected())
    {
      TogglePlayerMovementFromGoodGesture();
      _goodGestureMovementToggleHeldSeconds = 0f;
      _waitForGoodGestureMovementToggleRelease = true;
      return;
    }

    if (handPinchController == null || !handPinchController.IsLeftGoodGestureDetected())
    {
      _goodGestureMovementToggleHeldSeconds = 0f;
      return;
    }

    _goodGestureMovementToggleHeldSeconds += Time.unscaledDeltaTime;
    if (_goodGestureMovementToggleHeldSeconds < goodGestureMovementToggleHoldSeconds)
    {
      return;
    }

    TogglePlayerMovementFromGoodGesture();
    _goodGestureMovementToggleHeldSeconds = 0f;
    _waitForGoodGestureMovementToggleRelease = true;
  }

  private bool CanUseGoodGestureMovementToggle()
  {
    return isRealTimeMode
      && IsRealTimeGameplayStarted
      && !_isStartReactionPlaying
      && !_isPlayerGameplayLockedExternally;
  }

  private bool IsMovementToggleGesturePressed()
  {
    if (handPinchController == null)
    {
      handPinchController = FindFirstObjectByType<HandPinchController>(FindObjectsInactive.Include);
    }

    return handPinchController != null && handPinchController.IsRealTimeStartGestureDetected();
  }

  private void TogglePlayerMovementFromGoodGesture()
  {
    var playerController = FindFirstObjectByType<Wall2DPlayerController>(FindObjectsInactive.Include);
    if (playerController == null)
    {
      return;
    }

    if (!playerController.isActiveAndEnabled)
    {
      return;
    }

    if (playerController.IsPausedByGoodGesture || playerController.HasPendingGoodGesturePause)
    {
      playerController.ResumeFromGoodGesturePause();
      return;
    }

    if (!playerController.IsMoving && !playerController.IsFalling && !playerController.IsClimbing)
    {
      return;
    }

    playerController.RequestGoodGesturePause();
  }

  private void BeginStartReactionIfPossible()
  {
    var playerAnimator = FindFirstObjectByType<Wall2DPlayerAnimator>(FindObjectsInactive.Include);
    if (playerAnimator == null)
    {
      return;
    }

    playerAnimator.PlayGoodReaction();

    if (playerAnimator.GoodReactionDuration <= 0f)
    {
      return;
    }

    _isStartReactionPlaying = true;
    _startReactionEndTime = Time.unscaledTime + playerAnimator.GoodReactionDuration;
  }

  private void UpdateStartReactionPlayback()
  {
    if (!_isStartReactionPlaying)
    {
      return;
    }

    if (Time.unscaledTime < _startReactionEndTime)
    {
      return;
    }

    _isStartReactionPlaying = false;
    _startReactionEndTime = 0f;
    PlayGameplayStartSE();
    UpdatePlayerMovementState();
  }

  private void PlayGameplayStartSE()
  {
    if (_hasPlayedGameplayStartSE)
    {
      return;
    }

    _hasPlayedGameplayStartSE = true;
    AudioManager.Instance.PlaySE(AudioManager.Instance.SE_Start);
  }
}
