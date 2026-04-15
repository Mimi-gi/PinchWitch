using System.Collections.Generic;
using UnityEngine;

public class PlaneStageSetter : MonoBehaviour
{
    [SerializeField] ShadowColliderBuilder[] builders;
    [SerializeField] ShadowProjectionContext context;
    [SerializeField] bool autoDiscoverBuilders = true;
    [SerializeField] bool buildBuildersOnStart = true;

    public bool IsShadow = true;

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
    }

    void Start()
    {
        BuildShadowOutputsIfNeeded(forceRebuild: false, ignoreBuildBuildersOnStart: false);
        ApplyShadowState();
    }

    void OnValidate()
    {
        AutoAssignMissingReferences();

        if (!Application.isPlaying)
        {
            return;
        }

        ApplyShadowState();
    }

    [ContextMenu("Apply Shadow State")]
    public void ApplyShadowState()
    {
        IsShadow = true;

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
    }
}
