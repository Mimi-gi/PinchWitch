using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// 1つのライトが2D面(壁)に実際に到達した光領域の保持・描画・点内包判定を行う。
// コライダーの穴(LightHole2D)と全く同じ四角形群からメッシュを作るため、見た目とコライダーが必ず一致する。
[DisallowMultipleComponent]
public sealed class LightRegionOutput : MonoBehaviour
{
    [SerializeField] private MeshFilter meshFilter;
    [SerializeField] private MeshRenderer meshRenderer;
    [SerializeField] private Material material;
    [SerializeField] private Color fallbackColor = new Color(1f, 0.95f, 0.6f, 0.35f);
    [SerializeField] private float wallNormalOffset = 0.02f;

    private Mesh _mesh;
    private readonly List<LightLitQuad> _quads = new List<LightLitQuad>();
    private readonly List<Vector3> _vertexBuffer = new List<Vector3>();
    private readonly List<int> _triangleBuffer = new List<int>();

    private void Awake()
    {
        EnsureSetup();
    }

    private void OnEnable()
    {
        EnsureSetup();
        LightRegionRegistry.Register(this);
    }

    private void OnDisable()
    {
        LightRegionRegistry.Unregister(this);
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
            _mesh = new Mesh { name = "LightRegionMesh", hideFlags = HideFlags.DontSave };
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

    // worldQuadCorners は4個ひと組(A,B,C,D)のワールド座標。wallNormalWorld はZファイティング回避用のオフセット方向。
    public void SetQuads(IReadOnlyList<Vector3> worldQuadCorners, Vector3 wallNormalWorld)
    {
        EnsureSetup();

        _quads.Clear();
        _vertexBuffer.Clear();
        _triangleBuffer.Clear();

        var offset = wallNormalWorld.sqrMagnitude > 0f ? wallNormalWorld.normalized * wallNormalOffset : Vector3.zero;

        for (var i = 0; i + 3 < worldQuadCorners.Count; i += 4)
        {
            var a = worldQuadCorners[i];
            var b = worldQuadCorners[i + 1];
            var c = worldQuadCorners[i + 2];
            var d = worldQuadCorners[i + 3];

            _quads.Add(new LightLitQuad(a, b, c, d));

            var baseIndex = _vertexBuffer.Count;
            _vertexBuffer.Add(transform.InverseTransformPoint(a + offset));
            _vertexBuffer.Add(transform.InverseTransformPoint(b + offset));
            _vertexBuffer.Add(transform.InverseTransformPoint(c + offset));
            _vertexBuffer.Add(transform.InverseTransformPoint(d + offset));

            _triangleBuffer.Add(baseIndex); _triangleBuffer.Add(baseIndex + 1); _triangleBuffer.Add(baseIndex + 2);
            _triangleBuffer.Add(baseIndex); _triangleBuffer.Add(baseIndex + 2); _triangleBuffer.Add(baseIndex + 3);
        }

        _mesh.Clear();
        _mesh.SetVertices(_vertexBuffer);
        _mesh.SetTriangles(_triangleBuffer, 0);
        _mesh.RecalculateBounds();
        _mesh.RecalculateNormals();

        LightRegionRegistry.NotifyChanged();
    }

    public bool ContainsWorldPoint(Vector2 worldPoint)
    {
        for (var i = 0; i < _quads.Count; i++)
        {
            if (_quads[i].ContainsWorldPoint(worldPoint))
            {
                return true;
            }
        }

        return false;
    }

    public void AppendQuads(List<Vector2[]> buffer)
    {
        for (var i = 0; i < _quads.Count; i++)
        {
            buffer.Add(_quads[i].ToArray());
        }
    }
}
