using System.Collections.Generic;
using UnityEngine;

// 足場に付けると、光が当たっている領域だけコライダーに穴を開けて透過できるようにする。
// 影生成足場(ShadowColliderOutput)には EnsureSetup 時に自動付与される。
// 元から存在する足場には手動で付ける(Rigidbody2D(Static)+CompositeCollider2D が無ければ自動追加し、既存コライダーを Merge 化する)。
[DisallowMultipleComponent]
public sealed class LightPassablePlatform2D : MonoBehaviour
{
    [SerializeField] private PolygonCollider2D holeCollider;
    [SerializeField] private int holeCompositeOrder = 100;

    private Rigidbody2D _platformBody;
    private CompositeCollider2D _compositeCollider;
    private readonly List<Vector2[]> _quadBuffer = new List<Vector2[]>();

    private void Reset()
    {
        EnsureSetup();
    }

    private void Awake()
    {
        EnsureSetup();
    }

    private void OnEnable()
    {
        EnsureSetup();
        LightRegionRegistry.Changed += RebuildHole;
        RebuildHole();
    }

    private void OnDisable()
    {
        LightRegionRegistry.Changed -= RebuildHole;
    }

    [ContextMenu("Ensure Setup")]
    public void EnsureSetup()
    {
        if (_platformBody == null)
        {
            _platformBody = GetComponent<Rigidbody2D>();
        }

        if (_platformBody == null)
        {
            _platformBody = gameObject.AddComponent<Rigidbody2D>();
        }

        _platformBody.bodyType = RigidbodyType2D.Static;

        if (_compositeCollider == null)
        {
            _compositeCollider = GetComponent<CompositeCollider2D>();
        }

        if (_compositeCollider == null)
        {
            _compositeCollider = gameObject.AddComponent<CompositeCollider2D>();
        }

        _compositeCollider.geometryType = CompositeCollider2D.GeometryType.Polygons;

        // 元から付いていたコライダーをコンポジット合成(Merge)に切り替える。
        foreach (var collider2D in GetComponents<Collider2D>())
        {
            if (collider2D == _compositeCollider || collider2D == holeCollider)
            {
                continue;
            }

            collider2D.usedByComposite = true;
            if (collider2D.compositeOperation == Collider2D.CompositeOperation.None)
            {
                collider2D.compositeOperation = Collider2D.CompositeOperation.Merge;
            }
        }

        EnsureHoleCollider();
    }

    private void EnsureHoleCollider()
    {
        if (holeCollider == null)
        {
            var holeTransform = transform.Find("LightHole2D");
            GameObject holeObject;
            if (holeTransform != null)
            {
                holeObject = holeTransform.gameObject;
            }
            else
            {
                holeObject = new GameObject("LightHole2D");
                holeObject.transform.SetParent(transform, false);
            }

            holeCollider = holeObject.GetComponent<PolygonCollider2D>();
            if (holeCollider == null)
            {
                holeCollider = holeObject.AddComponent<PolygonCollider2D>();
            }

            holeCollider.pathCount = 0;
        }

        holeCollider.usedByComposite = true;
        holeCollider.compositeOperation = Collider2D.CompositeOperation.Difference;
        holeCollider.isTrigger = false;

        // Difference は「それまでに合成された結果」から引く操作のため、Merge 群より後に処理される必要がある。
        // 全コライダーが order 0 のままだと処理順が保証されず、穴が先に処理されると光の形がそのまま実体化する。
        holeCollider.compositeOrder = holeCompositeOrder;
    }

    private void RebuildHole()
    {
        if (holeCollider == null)
        {
            return;
        }

        LightRegionRegistry.CollectAllQuads(_quadBuffer);

        if (_quadBuffer.Count == 0)
        {
            holeCollider.pathCount = 0;
            return;
        }

        var holeTransform = holeCollider.transform;
        holeCollider.pathCount = _quadBuffer.Count;
        for (var i = 0; i < _quadBuffer.Count; i++)
        {
            var worldQuad = _quadBuffer[i];
            var localQuad = new Vector2[worldQuad.Length];
            for (var j = 0; j < worldQuad.Length; j++)
            {
                localQuad[j] = holeTransform.InverseTransformPoint(worldQuad[j]);
            }

            holeCollider.SetPath(i, localQuad);
        }
    }
}
