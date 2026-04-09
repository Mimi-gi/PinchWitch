using System.Collections.Generic;
using UnityEngine;

public class PlaneStageSetter : MonoBehaviour
{
    [SerializeField] ShadowColliderBuilder[] builders;
    [SerializeField] ShadowProjectionContext context;
    [SerializeField] SpaceViewModeSwitcher viewModeSwitcher;
    [SerializeField] bool autoDiscoverBuilders = true;
    [SerializeField] bool syncWithViewMode = true;
    [SerializeField] bool keepShadowInWorld3D = true;
    [SerializeField] bool buildBuildersOnStart = true;
    [SerializeField] bool rebuildBuildersOnWall2DEnterIfNeeded = true;

    public bool IsShadow;

    void Reset()
    {
        AutoAssignMissingReferences();
    }

    void Awake()
    {
        AutoAssignMissingReferences();
    }

    void OnEnable()
    {
        AutoAssignMissingReferences();
        SubscribeToViewMode();
    }

    void Start()
    {
        SyncShadowStateWithCurrentViewMode();
        BuildShadowOutputsIfNeeded(forceRebuild: false, ignoreBuildBuildersOnStart: false);
        ApplyShadowState();
    }

    void OnDisable()
    {
        UnsubscribeFromViewMode();
    }

    void OnValidate()
    {
        AutoAssignMissingReferences();

        if (!Application.isPlaying)
        {
            return;
        }

        SyncShadowStateWithCurrentViewMode();
        ApplyShadowState();
    }

    [ContextMenu("Apply Shadow State")]
    public void ApplyShadowState()
    {
        if (context != null)
        {
            context.SetShadowEnabled(IsShadow);
        }

        foreach (var builder in EnumerateBuilders())
        {
            if (builder == null)
            {
                continue;
            }

            builder.SetShadowActive(IsShadow);
        }

        if (Application.isPlaying)
        {
            Physics2D.SyncTransforms();
        }
    }

    [ContextMenu("Build Shadow Outputs If Needed")]
    public void BuildInitialShadowOutputs()
    {
        BuildShadowOutputsIfNeeded(forceRebuild: false, ignoreBuildBuildersOnStart: false);
    }

    private void BuildShadowOutputsIfNeeded(bool forceRebuild, bool ignoreBuildBuildersOnStart)
    {
        if (!ignoreBuildBuildersOnStart && !buildBuildersOnStart)
        {
            return;
        }

        foreach (var builder in EnumerateBuilders())
        {
            if (builder == null)
            {
                continue;
            }

            if (!forceRebuild && !builder.NeedsRebuild())
            {
                continue;
            }

            builder.BuildNow();
            builder.SetOutputEnabled(IsShadow);
        }

        if (Application.isPlaying)
        {
            Physics2D.SyncTransforms();
        }
    }

    private IEnumerable<ShadowColliderBuilder> EnumerateBuilders()
    {
        var seenBuilders = new HashSet<ShadowColliderBuilder>();

        if (builders != null)
        {
            foreach (var builder in builders)
            {
                if (builder == null || !seenBuilders.Add(builder))
                {
                    continue;
                }

                yield return builder;
            }
        }

        if (!autoDiscoverBuilders)
        {
            yield break;
        }

        var discoveredBuilders = FindObjectsByType<ShadowColliderBuilder>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var builder in discoveredBuilders)
        {
            if (builder == null || !seenBuilders.Add(builder))
            {
                continue;
            }

            yield return builder;
        }
    }

    private void AutoAssignMissingReferences()
    {
        if (context == null)
        {
            context = FindFirstObjectByType<ShadowProjectionContext>(FindObjectsInactive.Include);
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
        if (!syncWithViewMode)
        {
            return;
        }

        IsShadow = ShouldEnableShadowForMode(mode);

        if (IsShadow && rebuildBuildersOnWall2DEnterIfNeeded)
        {
            BuildShadowOutputsIfNeeded(forceRebuild: false, ignoreBuildBuildersOnStart: true);
        }

        ApplyShadowState();
    }

    private void SyncShadowStateWithCurrentViewMode()
    {
        if (!syncWithViewMode || viewModeSwitcher == null)
        {
            return;
        }

        IsShadow = ShouldEnableShadowForMode(viewModeSwitcher.CurrentMode);
    }

    private bool ShouldEnableShadowForMode(SpaceViewMode mode)
    {
        if (mode == SpaceViewMode.Wall2D)
        {
            return true;
        }

        return keepShadowInWorld3D;
    }
}
