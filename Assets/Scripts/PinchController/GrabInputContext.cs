using UnityEngine;

public enum GrabInputSource
{
  Hand,
  Pointer,
}

public readonly struct GrabInputContext
{
  public readonly GrabInteractor GrabInteractor;
  public readonly OutlineController OutlineController;
  public readonly GrabInputCoordinator InputCoordinator;
  public readonly Camera FallbackWorldCamera;
  public readonly LayerMask GrabbableLayerMask;
  public readonly float DragPlaneY;
  public readonly float XRotationDegrees;
  public readonly float YRotationDegrees;

  public GrabInputContext(
    GrabInteractor grabInteractor,
    OutlineController outlineController,
    GrabInputCoordinator inputCoordinator,
    Camera fallbackWorldCamera,
    LayerMask grabbableLayerMask,
    float dragPlaneY,
    float xRotationDegrees,
    float yRotationDegrees)
  {
    GrabInteractor = grabInteractor;
    OutlineController = outlineController;
    InputCoordinator = inputCoordinator;
    FallbackWorldCamera = fallbackWorldCamera;
    GrabbableLayerMask = grabbableLayerMask;
    DragPlaneY = dragPlaneY;
    XRotationDegrees = xRotationDegrees;
    YRotationDegrees = yRotationDegrees;
  }
}

public sealed class GrabInputCoordinator
{
  private GrabInputSource _activeSource;
  private bool _hasActiveSource;

  public bool IsBlocked(GrabInputSource source)
  {
    return _hasActiveSource && _activeSource != source;
  }

  public bool TryClaim(GrabInputSource source)
  {
    if (IsBlocked(source))
    {
      return false;
    }

    _activeSource = source;
    _hasActiveSource = true;
    return true;
  }

  public void Release(GrabInputSource source)
  {
    if (_hasActiveSource && _activeSource == source)
    {
      _hasActiveSource = false;
    }
  }
}
