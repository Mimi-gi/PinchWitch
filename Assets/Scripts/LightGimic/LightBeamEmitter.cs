using System.Collections.Generic;
using UnityEngine;

// 光柱ギミックの中心。断面(楕円)をグリッドサンプリングして3D遮蔽判定を行い、
// 壁に届いた領域を矩形ストリップへまとめて 2D 側の穴あけ(LightRegionOutput経由)と
// 光柱の見た目(LightBeamVisual)を更新する。
[DisallowMultipleComponent]
public sealed class LightBeamEmitter : MonoBehaviour
{
    private const string GlassLayerName = "Glass";

    [Header("Projection")]
    [SerializeField] private ShadowProjectionContext context;
    [SerializeField] private Transform projectionWallOverride;

    [Header("Sampling")]
    [SerializeField, Range(4, 64)] private int samplesPerAxis = 24;
    [SerializeField] private LayerMask occluderMask = ~0;
    [Tooltip("有効なら occluderMask の設定に関わらず Glass レイヤーを遮蔽物から除外する(ガラスは光を通す)。")]
    [SerializeField] private bool glassIsTransparent = true;
    [SerializeField] private float originForwardOffset = 0.05f;
    [SerializeField] private float apertureLocalZ;

    [Header("Visual")]
    [SerializeField] private LightBeamVisual beamVisual;
    [SerializeField] private LightRegionOutput regionOutput;
    [SerializeField, Range(8, 128)] private int beamPerimeterSegments = 48;

    private bool[] _validGrid;
    private bool[] _reachedWallGrid;

    private readonly List<Vector3> _quadCornersWorld = new List<Vector3>();
    private readonly List<Vector3> _nearRing = new List<Vector3>();
    private readonly List<Vector3> _farRing = new List<Vector3>();

    private void Awake()
    {
        EnsureSetup();
    }

    private void OnEnable()
    {
        EnsureSetup();
    }

    private void LateUpdate()
    {
        var wall = ResolveWall();
        if (wall == null)
        {
            return;
        }

        Rebuild(wall);
    }

    [ContextMenu("Ensure Setup")]
    public void EnsureSetup()
    {
        if (context == null)
        {
            context = FindFirstObjectByType<ShadowProjectionContext>(FindObjectsInactive.Include);
        }

        if (beamVisual == null)
        {
            beamVisual = GetComponentInChildren<LightBeamVisual>(true);
        }

        if (beamVisual == null)
        {
            var beamObject = new GameObject("LightBeamVisual_Generated");
            beamObject.transform.SetParent(transform, false);
            beamObject.layer = gameObject.layer;
            beamVisual = beamObject.AddComponent<LightBeamVisual>();
        }

        beamVisual.EnsureSetup();

        if (regionOutput == null)
        {
            var outputRoot = context != null ? context.EnsureGeneratedOutputRoot() : transform;
            var existing = outputRoot.Find($"{name}_LightRegionOutput");
            if (existing == null)
            {
                var regionObject = new GameObject($"{name}_LightRegionOutput");
                regionObject.transform.SetParent(outputRoot, false);
                regionObject.layer = gameObject.layer;
                regionOutput = regionObject.AddComponent<LightRegionOutput>();
            }
            else
            {
                regionOutput = existing.GetComponent<LightRegionOutput>();
                if (regionOutput == null)
                {
                    regionOutput = existing.gameObject.AddComponent<LightRegionOutput>();
                }
            }
        }

        regionOutput.EnsureSetup();
    }

    private Transform ResolveWall()
    {
        if (projectionWallOverride != null)
        {
            return projectionWallOverride;
        }

        if (context != null && context.TryGetProjectionReferences(out _, out var wall))
        {
            return wall;
        }

        return null;
    }

    private void Rebuild(Transform wall)
    {
        EnsureSetup();

        var n = Mathf.Max(4, samplesPerAxis);
        if (_validGrid == null || _validGrid.Length != n * n)
        {
            _validGrid = new bool[n * n];
            _reachedWallGrid = new bool[n * n];
        }

        var forward = transform.forward;
        var effectiveOccluderMask = ResolveOccluderMask();

        for (var iy = 0; iy < n; iy++)
        {
            for (var ix = 0; ix < n; ix++)
            {
                var index = iy * n + ix;
                var u = ((ix + 0.5f) / n) * 2f - 1f;
                var v = ((iy + 0.5f) / n) * 2f - 1f;

                if (u * u + v * v > 1f)
                {
                    _validGrid[index] = false;
                    continue;
                }

                _validGrid[index] = true;

                var castOrigin = GetLocalCirclePointWorld(u, v) + forward * originForwardOffset;

                if (!TryGetWallHitWorld(castOrigin, forward, wall, out _, out var wallDistance))
                {
                    _validGrid[index] = false;
                    continue;
                }

                var reachedWall = !Physics.Raycast(
                    castOrigin, forward, wallDistance, effectiveOccluderMask, QueryTriggerInteraction.Ignore);

                _reachedWallGrid[index] = reachedWall;
            }
        }

        BuildLitQuads(wall, n);
        BuildBeamTube(wall);
    }

    private int ResolveOccluderMask()
    {
        if (!glassIsTransparent)
        {
            return occluderMask;
        }

        var glassLayer = LayerMask.NameToLayer(GlassLayerName);
        return glassLayer >= 0 ? occluderMask & ~(1 << glassLayer) : occluderMask;
    }

    private Vector3 GetLocalCirclePointWorld(float u, float v)
    {
        var localPoint = new Vector3(u * 0.5f, v * 0.5f, apertureLocalZ);
        return transform.TransformPoint(localPoint);
    }

    private bool TryGetWallHitWorld(Vector3 origin, Vector3 direction, Transform wall, out Vector3 wallPoint, out float distance)
    {
        distance = 0f;

        if (!ShadowMathUtility.TryProjectPointToPlane(origin, origin + direction, wall, out wallPoint))
        {
            return false;
        }

        distance = Vector3.Distance(origin, wallPoint);
        return distance > Mathf.Epsilon;
    }

    private void BuildLitQuads(Transform wall, int n)
    {
        _quadCornersWorld.Clear();

        for (var iy = 0; iy < n; iy++)
        {
            var ix = 0;
            while (ix < n)
            {
                var index = iy * n + ix;
                if (!_validGrid[index] || !_reachedWallGrid[index])
                {
                    ix++;
                    continue;
                }

                var runStart = ix;
                while (ix < n)
                {
                    var runIndex = iy * n + ix;
                    if (!_validGrid[runIndex] || !_reachedWallGrid[runIndex])
                    {
                        break;
                    }

                    ix++;
                }

                var runEnd = ix - 1;
                AppendQuadForRun(wall, n, iy, runStart, runEnd);
            }
        }

        regionOutput.SetQuads(_quadCornersWorld, wall.up);
    }

    private void AppendQuadForRun(Transform wall, int n, int rowIndex, int startIndex, int endIndex)
    {
        var uLow = (startIndex / (float)n) * 2f - 1f;
        var uHigh = ((endIndex + 1) / (float)n) * 2f - 1f;
        var vLow = (rowIndex / (float)n) * 2f - 1f;
        var vHigh = ((rowIndex + 1) / (float)n) * 2f - 1f;

        if (!TryGetBoundaryWallPoint(wall, uLow, vLow, out var bottomLeft)) return;
        if (!TryGetBoundaryWallPoint(wall, uHigh, vLow, out var bottomRight)) return;
        if (!TryGetBoundaryWallPoint(wall, uHigh, vHigh, out var topRight)) return;
        if (!TryGetBoundaryWallPoint(wall, uLow, vHigh, out var topLeft)) return;

        _quadCornersWorld.Add(bottomLeft);
        _quadCornersWorld.Add(bottomRight);
        _quadCornersWorld.Add(topRight);
        _quadCornersWorld.Add(topLeft);
    }

    private bool TryGetBoundaryWallPoint(Transform wall, float u, float v, out Vector3 wallPoint)
    {
        var worldOrigin = GetLocalCirclePointWorld(u, v);
        var castOrigin = worldOrigin + transform.forward * originForwardOffset;
        return TryGetWallHitWorld(castOrigin, transform.forward, wall, out wallPoint, out _);
    }

    private void BuildBeamTube(Transform wall)
    {
        _nearRing.Clear();
        _farRing.Clear();

        for (var i = 0; i < beamPerimeterSegments; i++)
        {
            var angle = (i / (float)beamPerimeterSegments) * Mathf.PI * 2f;
            var u = Mathf.Cos(angle);
            var v = Mathf.Sin(angle);

            var nearWorld = GetLocalCirclePointWorld(u, v);
            _nearRing.Add(nearWorld);

            var castOrigin = nearWorld + transform.forward * originForwardOffset;
            _farRing.Add(TryGetWallHitWorld(castOrigin, transform.forward, wall, out var wallPoint, out _)
                ? wallPoint
                : nearWorld);
        }

        beamVisual.UpdateTube(_nearRing, _farRing);
    }
}
