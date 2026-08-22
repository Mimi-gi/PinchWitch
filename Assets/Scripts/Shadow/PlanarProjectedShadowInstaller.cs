using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Grabbable / handCursor レイヤーのルートへ平面投影シャドウを自動追加する。
/// シーン配置済み・実行時生成の両方を対象にする。
/// </summary>
public sealed class PlanarProjectedShadowInstaller : MonoBehaviour
{
    private static readonly string[] CasterLayerNames =
    {
        "Grabbable",
        "handCursor",
    };
    private const float RescanInterval = 1f;

    private readonly HashSet<int> _casterLayers = new HashSet<int>();
    private float _nextRescanTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InstallRuntimeScanner()
    {
        var installerObject = new GameObject(nameof(PlanarProjectedShadowInstaller));
        DontDestroyOnLoad(installerObject);
        installerObject.AddComponent<PlanarProjectedShadowInstaller>();
    }

    private void Awake()
    {
        foreach (var layerName in CasterLayerNames)
        {
            var layer = LayerMask.NameToLayer(layerName);
            if (layer >= 0)
            {
                _casterLayers.Add(layer);
            }
            else
            {
                Debug.LogWarning($"Layer '{layerName}' was not found. Objects on that layer will not receive planar projected shadows.", this);
            }
        }

        if (_casterLayers.Count == 0)
        {
            enabled = false;
            return;
        }

        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void Start()
    {
        AttachMissingShadowComponents();
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextRescanTime)
        {
            return;
        }

        _nextRescanTime = Time.unscaledTime + RescanInterval;
        AttachMissingShadowComponents();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _nextRescanTime = 0f;
    }

    private void AttachMissingShadowComponents()
    {
        // handcursor はVR姿勢を受信するまで非表示になるため、非アクティブ中に
        // コンポーネントを仕込んで表示初回フレームから影を描けるようにする。
        var rendererCandidates = FindObjectsByType<Renderer>(FindObjectsInactive.Include);
        var casterRoots = new HashSet<Transform>();

        foreach (var candidate in rendererCandidates)
        {
            if (!IsEligibleSourceRenderer(candidate))
            {
                continue;
            }

            casterRoots.Add(FindCasterRoot(candidate.transform));
        }

        foreach (var casterRoot in casterRoots)
        {
            if (casterRoot != null && casterRoot.GetComponent<PlanarProjectedShadow>() == null)
            {
                casterRoot.gameObject.AddComponent<PlanarProjectedShadow>();
            }
        }
    }

    private bool IsEligibleSourceRenderer(Renderer candidate)
    {
        if (candidate == null
            || !_casterLayers.Contains(candidate.gameObject.layer)
            || candidate.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly)
        {
            return false;
        }

        return candidate is SkinnedMeshRenderer
            || (candidate is MeshRenderer && candidate.GetComponent<MeshFilter>() != null);
    }

    private Transform FindCasterRoot(Transform source)
    {
        var root = source;
        var casterLayer = source.gameObject.layer;
        while (root.parent != null && root.parent.gameObject.layer == casterLayer)
        {
            root = root.parent;
        }

        return root;
    }
}
