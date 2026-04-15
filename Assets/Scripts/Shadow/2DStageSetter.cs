using System.Collections.Generic;
using UnityEngine;

public class PlaneStageSetter : MonoBehaviour
{
    [SerializeField] private ShadowColliderBuilder[] builders;
    [SerializeField] private ShadowProjectionContext context;
    [SerializeField] private bool autoDiscoverBuilders = true;
    [SerializeField] private bool buildBuildersOnStart = true;

    public bool IsShadow = true;

    // Inspector 未設定の参照を自動で埋める。
    private void Reset()
    {
        AutoAssignMissingReferences();
    }

    // 起動時にも参照を揃えておく。
    private void Awake()
    {
        AutoAssignMissingReferences();
    }

    // モード切替の購読はやめ、常時有効の設定だけを維持する。
    private void OnEnable()
    {
        AutoAssignMissingReferences();
    }

    // 起動直後に一度だけ影出力を構築する。
    private void Start()
    {
        BuildShadowOutputsIfNeeded(forceRebuild: false, ignoreBuildBuildersOnStart: false);
        ApplyShadowState();
    }

    // エディタ上で変更したときも、実行中なら即反映する。
    private void OnValidate()
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
        // この構成では影は常時有効として扱う。
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

        // 必要なビルダーだけを順に再構築する。
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
