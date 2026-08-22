using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 光源に依存せず、元メッシュを指定平面へ正射影して描画する影。
/// Transform の移動・回転には毎フレーム追従し、SkinnedMeshRenderer も動的に Bake する。
/// </summary>
[DisallowMultipleComponent]
public sealed class PlanarProjectedShadow : MonoBehaviour
{
    private const string DefaultMaterialResourcePath = "PlanarProjectionShadow";
    private const string CheckerboardMaterialName = "CheckerBoard3DPlane_mat";
    private const string ProjectionPlaneLayerName = "Plane";
    private const string ProjectionPlaneObjectName = "Plane";
    private const string LegacyProjectionPlaneObjectName = "Floor";

    private static readonly int ShadowColorId = Shader.PropertyToID("_ShadowColor");
    private static readonly int ProjectionPlaneId = Shader.PropertyToID("_ProjectionPlane");
    private static readonly int ProjectionDirectionId = Shader.PropertyToID("_ProjectionDirection");
    private static readonly int SurfaceOffsetId = Shader.PropertyToID("_SurfaceOffset");
    private static readonly int StencilRefId = Shader.PropertyToID("_StencilRef");
    private static int _nextStencilReference = 1;

    [Header("Source")]
    [SerializeField] private Transform sourceRoot;
    [SerializeField] private bool includeInactiveRenderers;

    [Header("Projection Plane")]
    [Tooltip("未設定時はチェック柄マテリアルを持つ Plane を自動検出します。ローカル Up が平面法線です。")]
    [SerializeField] private Transform projectionPlane;
    [SerializeField, Min(0f)] private float surfaceOffset = 0.003f;

    [Header("Appearance")]
    [SerializeField] private Color shadowColor = new Color(0.04f, 0.025f, 0.06f, 0.32f);
    [SerializeField] private Material shadowMaterialTemplate;

    private readonly List<MeshSource> _meshSources = new List<MeshSource>();
    private Material _runtimeMaterial;
    private MaterialPropertyBlock _propertyBlock;
    private int _stencilReference;

    public Transform ProjectionPlane
    {
        get => projectionPlane;
        set => projectionPlane = value;
    }

    private void Reset()
    {
        sourceRoot = transform;
        ResolveProjectionPlane();
    }

    private void Awake()
    {
        if (sourceRoot == null)
        {
            sourceRoot = transform;
        }

        _stencilReference = AllocateStencilReference();
        _propertyBlock = new MaterialPropertyBlock();
        ResolveProjectionPlane();
        RefreshSourceRenderers();
        CreateRuntimeMaterial();
    }

    private void OnEnable()
    {
        ResolveProjectionPlane();
    }

    private void LateUpdate()
    {
        if (projectionPlane == null)
        {
            ResolveProjectionPlane();
        }

        if (projectionPlane == null || !CreateRuntimeMaterial())
        {
            return;
        }

        var planeNormal = projectionPlane.up.normalized;
        var plane = new Vector4(
            planeNormal.x,
            planeNormal.y,
            planeNormal.z,
            -Vector3.Dot(planeNormal, projectionPlane.position));

        _propertyBlock.Clear();
        _propertyBlock.SetColor(ShadowColorId, shadowColor);
        _propertyBlock.SetVector(ProjectionPlaneId, plane);
        _propertyBlock.SetVector(ProjectionDirectionId, -planeNormal);
        _propertyBlock.SetFloat(SurfaceOffsetId, surfaceOffset);

        for (var i = 0; i < _meshSources.Count; i++)
        {
            DrawMeshSource(_meshSources[i]);
        }
    }

    private void OnTransformChildrenChanged()
    {
        RefreshSourceRenderers();
    }

    private void OnDestroy()
    {
        for (var i = 0; i < _meshSources.Count; i++)
        {
            _meshSources[i].Dispose();
        }

        _meshSources.Clear();

        if (_runtimeMaterial != null)
        {
            Destroy(_runtimeMaterial);
        }
    }

    [ContextMenu("Refresh Source Renderers")]
    public void RefreshSourceRenderers()
    {
        for (var i = 0; i < _meshSources.Count; i++)
        {
            _meshSources[i].Dispose();
        }

        _meshSources.Clear();
        var root = sourceRoot != null ? sourceRoot : transform;
        var includeInactive = includeInactiveRenderers;

        foreach (var meshRenderer in root.GetComponentsInChildren<MeshRenderer>(includeInactive))
        {
            if (meshRenderer == null || meshRenderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly)
            {
                continue;
            }

            // 既存の ShadowCaster2D プロキシなど、別レイヤーの補助メッシュを除外する。
            if (meshRenderer.gameObject.layer != gameObject.layer)
            {
                continue;
            }

            var meshFilter = meshRenderer.GetComponent<MeshFilter>();
            if (meshFilter != null)
            {
                _meshSources.Add(new MeshSource(meshRenderer, meshFilter));
            }
        }

        foreach (var skinnedRenderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive))
        {
            if (skinnedRenderer == null || skinnedRenderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly)
            {
                continue;
            }

            if (skinnedRenderer.gameObject.layer == gameObject.layer)
            {
                _meshSources.Add(new MeshSource(skinnedRenderer));
            }
        }
    }

    private void DrawMeshSource(MeshSource source)
    {
        if (!source.TryGetMesh(out var mesh, out var localToWorld, out var renderer))
        {
            return;
        }

        if (!renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy)
        {
            return;
        }

        for (var subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
        {
            Graphics.DrawMesh(
                mesh,
                localToWorld,
                _runtimeMaterial,
                gameObject.layer,
                null,
                subMeshIndex,
                _propertyBlock,
                ShadowCastingMode.Off,
                false,
                null,
                LightProbeUsage.Off);
        }
    }

    private bool CreateRuntimeMaterial()
    {
        if (_runtimeMaterial != null)
        {
            return true;
        }

        var template = shadowMaterialTemplate != null
            ? shadowMaterialTemplate
            : Resources.Load<Material>(DefaultMaterialResourcePath);

        if (template == null)
        {
            Debug.LogWarning($"{nameof(PlanarProjectedShadow)} could not load material '{DefaultMaterialResourcePath}'.", this);
            enabled = false;
            return false;
        }

        _runtimeMaterial = new Material(template)
        {
            name = $"{template.name} ({name})",
            hideFlags = HideFlags.DontSave,
        };
        _runtimeMaterial.SetInt(StencilRefId, _stencilReference);
        return true;
    }

    private void ResolveProjectionPlane()
    {
        if (projectionPlane != null)
        {
            return;
        }

        projectionPlane = FindCheckerboardProjectionPlane();
        if (projectionPlane != null)
        {
            return;
        }

        // マテリアル名が変更された場合も、Plane レイヤーを優先して探す。
        var planeLayer = LayerMask.NameToLayer(ProjectionPlaneLayerName);
        if (planeLayer >= 0)
        {
            var renderers = FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include);
            foreach (var candidate in renderers)
            {
                if (candidate != null && candidate.gameObject.layer == planeLayer)
                {
                    projectionPlane = candidate.transform;
                    return;
                }
            }
        }

        // 古いシーン構成との互換用フォールバック。
        var planeObject = GameObject.Find(ProjectionPlaneObjectName);
        if (planeObject == null)
        {
            planeObject = GameObject.Find(LegacyProjectionPlaneObjectName);
        }

        if (planeObject != null)
        {
            projectionPlane = planeObject.transform;
        }
    }

    private static Transform FindCheckerboardProjectionPlane()
    {
        var renderers = FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include);
        foreach (var candidate in renderers)
        {
            if (candidate == null)
            {
                continue;
            }

            var materials = candidate.sharedMaterials;
            foreach (var material in materials)
            {
                if (material != null && material.name == CheckerboardMaterialName)
                {
                    return candidate.transform;
                }
            }
        }

        return null;
    }

    private static int AllocateStencilReference()
    {
        var result = _nextStencilReference;
        _nextStencilReference = _nextStencilReference >= 255 ? 1 : _nextStencilReference + 1;
        return result;
    }

    private sealed class MeshSource
    {
        private readonly Renderer _renderer;
        private readonly MeshFilter _meshFilter;
        private readonly SkinnedMeshRenderer _skinnedRenderer;
        private Mesh _bakedMesh;

        public MeshSource(MeshRenderer renderer, MeshFilter meshFilter)
        {
            _renderer = renderer;
            _meshFilter = meshFilter;
        }

        public MeshSource(SkinnedMeshRenderer renderer)
        {
            _renderer = renderer;
            _skinnedRenderer = renderer;
            _bakedMesh = new Mesh { name = $"{renderer.name} Planar Shadow Bake" };
            _bakedMesh.MarkDynamic();
        }

        public bool TryGetMesh(out Mesh mesh, out Matrix4x4 localToWorld, out Renderer renderer)
        {
            renderer = _renderer;
            localToWorld = _renderer != null ? _renderer.localToWorldMatrix : Matrix4x4.identity;

            if (_renderer == null)
            {
                mesh = null;
                return false;
            }

            if (_skinnedRenderer != null)
            {
                if (_skinnedRenderer.sharedMesh == null)
                {
                    mesh = null;
                    return false;
                }

                _skinnedRenderer.BakeMesh(_bakedMesh, false);
                mesh = _bakedMesh;
                return mesh.vertexCount > 0;
            }

            mesh = _meshFilter != null ? _meshFilter.sharedMesh : null;
            return mesh != null;
        }

        public void Dispose()
        {
            if (_bakedMesh != null)
            {
                Object.Destroy(_bakedMesh);
                _bakedMesh = null;
            }
        }
    }
}
