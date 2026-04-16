using UnityEngine;
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
  [SerializeField] private KeyCode toggleKey = KeyCode.Space;

  [Header("Cameras")]
  [SerializeField] private Camera world3DCamera;
  [SerializeField] private Camera wall2DCamera;

  [Header("Startup")]
  [SerializeField] private SpaceViewMode initialMode = SpaceViewMode.World3D;
  [SerializeField] private bool switchAudioListeners = true;
  [SerializeField] private bool isRealTimeMode = true;

  public event Action<SpaceViewMode> ModeChanged;
  public SpaceViewMode CurrentMode { get; private set; } = SpaceViewMode.World3D;
  public bool IsRealTimeMode => isRealTimeMode;
  public int ResetOrder => -100;

  private void Reset()
  {
    AutoAssignMissingReferences();
  }

  private void Awake()
  {
    AutoAssignMissingReferences();
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
    if (!Application.isPlaying || !Input.GetKeyDown(toggleKey))
    {
      return;
    }

    ToggleView();
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

    if (isRealTimeMode)
    {
      // リアルタイムモードなら、どちらの視点でも動き続けられるようにする。
      respawner.SetPlayerGameplayEnabled(true);
    }
    else
    {
      // 切り替えモードなら、Wall2D (2D世界) の間だけ物理を有効にする。
      respawner.SetPlayerGameplayEnabled(CurrentMode == SpaceViewMode.Wall2D);
    }
  }

  /// <summary>
  /// 視点切り替えを起動時のモードへ戻す。
  /// </summary>
  public void ResetRuntimeState()
  {
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
}
