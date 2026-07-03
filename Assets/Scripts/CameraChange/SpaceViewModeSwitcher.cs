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

  [Header("Realtime Start")]
  [SerializeField] private bool requireInputToStartRealTime = true;

  public event Action<SpaceViewMode> ModeChanged;
  public SpaceViewMode CurrentMode { get; private set; } = SpaceViewMode.World3D;
  public bool IsRealTimeMode => isRealTimeMode;
  public bool IsRealTimeGameplayStarted => !IsRealTimeStartRequired() || _realTimeGameplayStarted;
  public bool ShouldEnablePlayerGameplay => ResolveBasePlayerGameplayEnabled() && !_isPlayerGameplayLockedExternally;
  public int ResetOrder => -100;

  private bool _realTimeGameplayStarted;
  private bool _isStartReactionPlaying;
  private float _startReactionEndTime;
  private bool _hasPlayedGameplayStartSE;
  private bool _isPlayerGameplayLockedExternally;
  private bool _unlockPlayerGameplayWhenRealtimeStarts;
  private bool _waitForStartInputRelease;

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
      _waitForStartInputRelease = Input.GetKey(startGameplayKey);
      if (_waitForStartInputRelease)
      {
        return;
      }
    }

    UpdateStartGameplayKey();
  }

  private void UpdateStartGameplayKey()
  {
    if (!Input.GetKeyDown(startGameplayKey))
    {
      return;
    }

    HandleStartGameplayKeyDown();
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

    if (IsRealTimeStartRequired())
    {
      _realTimeGameplayStarted = false;
    }

    _unlockPlayerGameplayWhenRealtimeStarts = true;
    _waitForStartInputRelease = Input.GetKey(startGameplayKey);

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
  }

  private bool IsRealTimeStartRequired()
  {
    return isRealTimeMode && requireInputToStartRealTime;
  }

  private bool ResolveBasePlayerGameplayEnabled()
  {
    return isRealTimeMode
      ? IsRealTimeGameplayStarted && !_isStartReactionPlaying
      : CurrentMode == SpaceViewMode.Wall2D;
  }

  private void ResetRealTimeStartGate()
  {
    _realTimeGameplayStarted = !IsRealTimeStartRequired();
    _isStartReactionPlaying = false;
    _startReactionEndTime = 0f;
    _hasPlayedGameplayStartSE = false;
    _isPlayerGameplayLockedExternally = false;
    _unlockPlayerGameplayWhenRealtimeStarts = false;
    _waitForStartInputRelease = false;
  }

  private void StartRealTimeGameplay(bool playGoodReaction)
  {
    if (_realTimeGameplayStarted || !isRealTimeMode)
    {
      return;
    }

    _realTimeGameplayStarted = true;
    _isStartReactionPlaying = false;
    _startReactionEndTime = 0f;
    _waitForStartInputRelease = false;

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

  private void HandleStartGameplayKeyDown()
  {
    if (!IsRealTimeGameplayStarted)
    {
      StartRealTimeGameplay(playGoodReaction: true);
      return;
    }

    if (!CanUsePlayerMovementToggle())
    {
      return;
    }

    TogglePlayerMovement();
  }

  private bool CanUsePlayerMovementToggle()
  {
    return isRealTimeMode
      && IsRealTimeGameplayStarted
      && !_isStartReactionPlaying
      && !_isPlayerGameplayLockedExternally;
  }

  private void TogglePlayerMovement()
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
