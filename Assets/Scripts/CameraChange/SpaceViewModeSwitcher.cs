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

  public event Action<SpaceViewMode> ModeChanged;
  public SpaceViewMode CurrentMode { get; private set; } = SpaceViewMode.World3D;
  public bool IsRealTimeMode => isRealTimeMode;
  public bool IsRealTimeGameplayStarted => !IsRealTimeStartGestureRequired() || _realTimeGameplayStarted;
  public bool ShouldEnablePlayerGameplay => isRealTimeMode
    ? IsRealTimeGameplayStarted
    : CurrentMode == SpaceViewMode.Wall2D;
  public int ResetOrder => -100;

  private bool _realTimeGameplayStarted;
  private float _goodGestureHeldSeconds;

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

    UpdateRealTimeStartGesture();

    if (Input.GetKeyDown(startGameplayKey))
    {
      StartRealTimeGameplay();
    }
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
  }

  private bool IsRealTimeStartGestureRequired()
  {
    return isRealTimeMode && requireGoodGestureToStartRealTime;
  }

  private void ResetRealTimeStartGate()
  {
    _realTimeGameplayStarted = !IsRealTimeStartGestureRequired();
    _goodGestureHeldSeconds = 0f;
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

    StartRealTimeGameplay();
  }

  private void StartRealTimeGameplay()
  {
    if (_realTimeGameplayStarted || !isRealTimeMode)
    {
      return;
    }

    _realTimeGameplayStarted = true;
    _goodGestureHeldSeconds = 0f;
    UpdatePlayerMovementState();
  }
}
