using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// 光柱(半透明の楕円柱)の見た目メッシュを生成・更新する。
// Unity の Light は使わず、専用メッシュ+専用マテリアルで描画する。
[DisallowMultipleComponent]
public sealed class LightBeamVisual : MonoBehaviour
{
    [SerializeField] private MeshFilter meshFilter;
    [SerializeField] private MeshRenderer meshRenderer;
    [SerializeField] private Material material;
    [SerializeField] private Color fallbackColor = new Color(1f, 0.95f, 0.6f, 0.25f);

    private Mesh _mesh;
    private readonly List<Vector3> _vertexBuffer = new List<Vector3>();
    private readonly List<int> _triangleBuffer = new List<int>();

    private void Awake()
    {
        EnsureSetup();
    }

    private void OnDestroy()
    {
        if (_mesh != null)
        {
            Object.Destroy(_mesh);
        }
    }

    public void EnsureSetup()
    {
        if (meshFilter == null)
        {
            meshFilter = GetComponent<MeshFilter>();
        }

        if (meshFilter == null)
        {
            meshFilter = gameObject.AddComponent<MeshFilter>();
        }

        if (meshRenderer == null)
        {
            meshRenderer = GetComponent<MeshRenderer>();
        }

        if (meshRenderer == null)
        {
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
        }

        if (_mesh == null)
        {
            _mesh = new Mesh { name = "LightBeamMesh", hideFlags = HideFlags.DontSave };
            meshFilter.sharedMesh = _mesh;
        }

        if (material == null)
        {
            material = LightGimicRenderingUtility.CreateDefaultTransparentMaterial(fallbackColor);
        }

        meshRenderer.sharedMaterial = material;
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
    }

    // nearRingWorld/farRingWorld は同じ並び順のワールド座標の円周点列(断面の周)。
    public void UpdateTube(IReadOnlyList<Vector3> nearRingWorld, IReadOnlyList<Vector3> farRingWorld)
    {
        EnsureSetup();

        _vertexBuffer.Clear();
        _triangleBuffer.Clear();

        var segmentCount = Mathf.Min(nearRingWorld.Count, farRingWorld.Count);
        if (segmentCount < 3)
        {
            _mesh.Clear();
            return;
        }

        for (var i = 0; i < segmentCount; i++)
        {
            _vertexBuffer.Add(transform.InverseTransformPoint(nearRingWorld[i]));
            _vertexBuffer.Add(transform.InverseTransformPoint(farRingWorld[i]));
        }

        for (var i = 0; i < segmentCount; i++)
        {
            var next = (i + 1) % segmentCount;
            var nearA = i * 2;
            var farA = i * 2 + 1;
            var nearB = next * 2;
            var farB = next * 2 + 1;

            // 半透明の薄い筒のため、内側からも見えるよう両面分の三角形を積む。
            _triangleBuffer.Add(nearA); _triangleBuffer.Add(farA); _triangleBuffer.Add(nearB);
            _triangleBuffer.Add(farA); _triangleBuffer.Add(farB); _triangleBuffer.Add(nearB);

            _triangleBuffer.Add(nearB); _triangleBuffer.Add(farA); _triangleBuffer.Add(nearA);
            _triangleBuffer.Add(nearB); _triangleBuffer.Add(farB); _triangleBuffer.Add(farA);
        }

        _mesh.Clear();
        _mesh.SetVertices(_vertexBuffer);
        _mesh.SetTriangles(_triangleBuffer, 0);
        _mesh.RecalculateBounds();
        _mesh.RecalculateNormals();
    }
}
