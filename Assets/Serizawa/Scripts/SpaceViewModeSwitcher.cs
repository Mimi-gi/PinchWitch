using UnityEngine;
using System;

public enum SpaceViewMode
{
  World3D,
  Wall2D,
}

[DisallowMultipleComponent]
public sealed class SpaceViewModeSwitcher : MonoBehaviour
{
  [Header("Input")]
  [SerializeField] private KeyCode toggleKey = KeyCode.Space;

  [Header("Cameras")]
  [SerializeField] private Camera world3DCamera;
  [SerializeField] private Camera wall2DCamera;

  [Header("Startup")]
  [SerializeField] private SpaceViewMode initialMode = SpaceViewMode.World3D;
  [SerializeField] private bool switchAudioListeners = true;

  public event Action<SpaceViewMode> ModeChanged;
  public SpaceViewMode CurrentMode { get; private set; } = SpaceViewMode.World3D;

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

    if (modeChanged)
    {
      ModeChanged?.Invoke(CurrentMode);
    }
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
