using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class ShadowProjectionContext : MonoBehaviour
{
  [Header("Projection")]
  [SerializeField] private Transform pointLight;
  [SerializeField] private Transform projectionWall;

  [Header("Generated Outputs")]
  [SerializeField] private Transform generatedOutputRoot;
  [SerializeField] private SpaceViewModeSwitcher viewModeSwitcher;
  [SerializeField] private bool rebuildOnWall2DEnter = false;

  [Header("Runtime")]
  [SerializeField] private bool shadowEnabled = true;
  [SerializeField] private string shadowOnlyProxyLayerName = "ShadowCaster2D";

  private readonly HashSet<ShadowColliderBuilder> _builders = new HashSet<ShadowColliderBuilder>();

  public Transform PointLight => pointLight;
  public Transform ProjectionWall => projectionWall;
  public bool ShadowEnabled => shadowEnabled;

  public int ShadowOnlyProxyLayer
  {
    get
    {
      var layer = LayerMask.NameToLayer(shadowOnlyProxyLayerName);
      return layer >= 0 ? layer : 0;
    }
  }

  private void Reset()
  {
    AutoAssignMissingReferences();
  }

  private void Awake()
  {
    AutoAssignMissingReferences();
    EnsureGeneratedOutputRoot();
  }

  private void OnEnable()
  {
    AutoAssignMissingReferences();
    SubscribeToViewMode();
  }

  private void OnDisable()
  {
    UnsubscribeFromViewMode();
  }

  private void OnValidate()
  {
    AutoAssignMissingReferences();
  }

  public void Register(ShadowColliderBuilder builder)
  {
    if (builder == null)
    {
      return;
    }

    _builders.Add(builder);
  }

  public void Unregister(ShadowColliderBuilder builder)
  {
    if (builder == null)
    {
      return;
    }

    _builders.Remove(builder);
  }

  public void SetShadowEnabled(bool enabled, bool rebuildImmediately = false)
  {
    shadowEnabled = enabled;
    if (shadowEnabled && rebuildImmediately)
    {
      RebuildAll();
    }
  }

  public bool TryGetProjectionReferences(out Transform light, out Transform wall)
  {
    AutoAssignMissingReferences();
    light = pointLight;
    wall = projectionWall;
    return light != null && wall != null;
  }

  public Transform EnsureGeneratedOutputRoot()
  {
    if (generatedOutputRoot != null)
    {
      return generatedOutputRoot;
    }

    var existingRoot = transform.Find("Shadow2DGeneratedOutputs");
    if (existingRoot != null)
    {
      generatedOutputRoot = existingRoot;
      return generatedOutputRoot;
    }

    var outputRootObject = new GameObject("Shadow2DGeneratedOutputs");
    generatedOutputRoot = outputRootObject.transform;
    generatedOutputRoot.SetParent(transform, false);
    return generatedOutputRoot;
  }

  public void RebuildAll()
  {
    AutoAssignMissingReferences();

    if (!shadowEnabled)
    {
      return;
    }

    var snapshot = new List<ShadowColliderBuilder>(_builders);
    foreach (var builder in snapshot)
    {
      if (builder == null)
      {
        continue;
      }

      builder.BuildNow();
    }
  }

  private void AutoAssignMissingReferences()
  {
    if (pointLight == null)
    {
      var lights = FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);
      foreach (var candidate in lights)
      {
        if (candidate == null)
        {
          continue;
        }

        if (candidate.type == LightType.Point || candidate.type == LightType.Spot)
        {
          pointLight = candidate.transform;
          break;
        }
      }

      if (pointLight == null && lights.Length > 0 && lights[0] != null)
      {
        pointLight = lights[0].transform;
      }
    }

    if (projectionWall == null)
    {
      var wallObject = GameObject.Find("Wall");
      if (wallObject != null)
      {
        projectionWall = wallObject.transform;
      }
    }

    if (viewModeSwitcher == null)
    {
      viewModeSwitcher = FindFirstObjectByType<SpaceViewModeSwitcher>(FindObjectsInactive.Include);
    }
  }

  private void SubscribeToViewMode()
  {
    if (viewModeSwitcher == null)
    {
      return;
    }

    viewModeSwitcher.ModeChanged -= HandleModeChanged;
    viewModeSwitcher.ModeChanged += HandleModeChanged;
  }

  private void UnsubscribeFromViewMode()
  {
    if (viewModeSwitcher == null)
    {
      return;
    }

    viewModeSwitcher.ModeChanged -= HandleModeChanged;
  }

  private void HandleModeChanged(SpaceViewMode mode)
  {
    shadowEnabled = mode == SpaceViewMode.Wall2D;

    if (mode != SpaceViewMode.Wall2D || !rebuildOnWall2DEnter)
    {
      return;
    }

    if (FindFirstObjectByType<PlaneStageSetter>(FindObjectsInactive.Include) != null)
    {
      return;
    }

    if (FindFirstObjectByType<ShadowDirtyRegistry>(FindObjectsInactive.Include) != null)
    {
      return;
    }

    RebuildAll();
  }
}
