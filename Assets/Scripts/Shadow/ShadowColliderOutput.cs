using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class ShadowColliderOutput : MonoBehaviour
{
  [SerializeField] private PolygonCollider2D sourcePolygon;
  [SerializeField] private CompositeCollider2D compositeCollider;
  [SerializeField] private Rigidbody2D outputBody;
  [SerializeField] private ShadowColliderBuilder ownerBuilder;
  [SerializeField] private bool useCompositeCollider = true;
  [SerializeField] private LightPassablePlatform2D lightPassablePlatform;
  private readonly List<PolygonCollider2D> _polygonSources = new List<PolygonCollider2D>();

  // 光ギミックがコライダーに穴を開ける前の「本来の影形状」(このtransformのローカル座標)。
  // 穴あけ用の LightHole2D は子オブジェクトにあるため、ここには含まれない。
  private readonly List<Vector2[]> _shadowPathsLocal = new List<Vector2[]>();

  public PolygonCollider2D SourcePolygon => sourcePolygon;
  public CompositeCollider2D CompositeCollider => compositeCollider;
  public ShadowColliderBuilder OwnerBuilder => ownerBuilder;
  public bool HasOwner => ownerBuilder != null;
  public bool HasGeometry => GetPolygonSources().Exists(collider => collider != null && collider.pathCount > 0);

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
    // ベイク済みのコライダーをそのまま使うステージでは実行時に再生成されないため、
    // ここでシリアライズ済みの path からキャッシュを作る。
    RebuildShadowGeometryCache();
    ShadowGeometryRegistry.Register(this);
  }

  private void OnDisable()
  {
    ShadowGeometryRegistry.Unregister(this);
  }

  private void OnValidate()
  {
    EnsureSetup();
  }

  [ContextMenu("Ensure Output Setup")]
  public void EnsureSetup()
  {
    if (sourcePolygon == null)
    {
      sourcePolygon = GetComponent<PolygonCollider2D>();
      if (sourcePolygon == null)
      {
        sourcePolygon = gameObject.AddComponent<PolygonCollider2D>();
      }
    }

    if (outputBody == null)
    {
      outputBody = GetComponent<Rigidbody2D>();
      if (outputBody == null)
      {
        outputBody = gameObject.AddComponent<Rigidbody2D>();
      }
    }

    outputBody.bodyType = RigidbodyType2D.Static;
    outputBody.simulated = true;

    if (compositeCollider == null)
    {
      compositeCollider = GetComponent<CompositeCollider2D>();
      if (compositeCollider == null)
      {
        compositeCollider = gameObject.AddComponent<CompositeCollider2D>();
      }
    }

    // 影足場は光ギミックで透過できるよう、穴あけコンポーネントを自動付与する。
    if (lightPassablePlatform == null)
    {
      lightPassablePlatform = GetComponent<LightPassablePlatform2D>();
      if (lightPassablePlatform == null)
      {
        lightPassablePlatform = gameObject.AddComponent<LightPassablePlatform2D>();
      }
    }

    ConfigureColliderMode(sourcePolygon.enabled);
  }

  public bool TryAssignOwner(ShadowColliderBuilder builder)
  {
    if (builder == null)
    {
      return false;
    }

    if (ownerBuilder != null && ownerBuilder != builder)
    {
      return false;
    }

    ownerBuilder = builder;
    return true;
  }

  public void ApplyTrianglePaths(IReadOnlyList<Vector2[]> trianglePaths)
  {
    EnsureSetup();

    if (trianglePaths == null || trianglePaths.Count == 0)
    {
      Clear();
      return;
    }

    if (useCompositeCollider && compositeCollider != null)
    {
      // 1 個の PolygonCollider2D に三角形 path を詰め込むと shape が空になるため、
      // Composite の入力は「三角形ごとに 1 collider」に分解して持たせる。
      EnsurePolygonSourceCount(trianglePaths.Count);
      var polygonSources = GetPolygonSources();
      for (var i = 0; i < polygonSources.Count; i++)
      {
        var polygonSource = polygonSources[i];
        if (polygonSource == null)
        {
          continue;
        }

        if (i < trianglePaths.Count)
        {
          polygonSource.pathCount = 1;
          polygonSource.SetPath(0, trianglePaths[i]);
        }
        else
        {
          polygonSource.pathCount = 0;
        }
      }

      SetGeometryEnabled(true);
      RebuildShadowGeometryCache();
      return;
    }

    DisableExtraPolygonSources();
    sourcePolygon.pathCount = trianglePaths.Count;
    for (var i = 0; i < trianglePaths.Count; i++)
    {
      sourcePolygon.SetPath(i, trianglePaths[i]);
    }

    SetGeometryEnabled(true);
    RebuildShadowGeometryCache();
  }

  public void SetGeometryEnabled(bool isEnabled)
  {
    EnsureSetup();
    ConfigureColliderMode(isEnabled && HasGeometry);
  }

  public void Clear()
  {
    EnsureSetup();

    foreach (var polygonSource in GetPolygonSources())
    {
      if (polygonSource == null)
      {
        continue;
      }

      polygonSource.pathCount = 0;
    }

    _shadowPathsLocal.Clear();
    ConfigureColliderMode(false);
  }

  /// <summary>
  /// 光ギミックが開けた穴を無視した影形状に、指定のワールド座標が含まれるか。
  /// 敵など「光では挙動が変わらないが影には反応する」対象の判定に使う。
  /// </summary>
  public bool ContainsWorldPointIgnoringLightHoles(Vector2 worldPoint)
  {
    if (_shadowPathsLocal.Count == 0 || !IsShadowGeometryActive())
    {
      return false;
    }

    var localPoint = (Vector2)transform.InverseTransformPoint(worldPoint);
    for (var i = 0; i < _shadowPathsLocal.Count; i++)
    {
      if (IsPointInPolygon(localPoint, _shadowPathsLocal[i]))
      {
        return true;
      }
    }

    return false;
  }

  /// <summary>
  /// ソースの PolygonCollider2D から影形状のキャッシュを作り直す。
  /// 実行時生成・エディタでのベイクのどちらでも同じように機能させるため、
  /// 生成時の引数ではなくコライダーの現在の path を読む。
  /// </summary>
  [ContextMenu("Rebuild Shadow Geometry Cache")]
  public void RebuildShadowGeometryCache()
  {
    _shadowPathsLocal.Clear();

    foreach (var polygonSource in GetPolygonSources())
    {
      if (polygonSource == null)
      {
        continue;
      }

      var colliderOffset = polygonSource.offset;
      for (var pathIndex = 0; pathIndex < polygonSource.pathCount; pathIndex++)
      {
        var path = polygonSource.GetPath(pathIndex);
        if (path == null || path.Length < 3)
        {
          continue;
        }

        for (var pointIndex = 0; pointIndex < path.Length; pointIndex++)
        {
          path[pointIndex] += colliderOffset;
        }

        _shadowPathsLocal.Add(path);
      }
    }
  }

  // 実際に物理が見ているコライダーの有効状態をそのまま参照する。
  private bool IsShadowGeometryActive()
  {
    if (compositeCollider != null && compositeCollider.enabled)
    {
      return true;
    }

    return sourcePolygon != null && sourcePolygon.enabled;
  }

  [ContextMenu("Log Shadow Geometry Cache")]
  private void LogShadowGeometryCache()
  {
    Debug.Log($"[{name}] shadow paths: {_shadowPathsLocal.Count}, active: {IsShadowGeometryActive()}", this);
  }

  // 凹形状にも対応するため、交差数(even-odd)で内外を判定する。
  private static bool IsPointInPolygon(Vector2 point, Vector2[] polygon)
  {
    var isInside = false;
    for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
    {
      var current = polygon[i];
      var previous = polygon[j];

      if (current.y > point.y == previous.y > point.y)
      {
        continue;
      }

      var intersectionX = ((previous.x - current.x) * (point.y - current.y) / (previous.y - current.y)) + current.x;
      if (point.x < intersectionX)
      {
        isInside = !isInside;
      }
    }

    return isInside;
  }

  private void ConfigureColliderMode(bool enableGeometry)
  {
    if (sourcePolygon == null)
    {
      return;
    }

    var enableCompositeCollider = useCompositeCollider && compositeCollider != null;
    if (enableCompositeCollider)
    {
      foreach (var polygonSource in GetPolygonSources())
      {
        if (polygonSource == null)
        {
          continue;
        }

        var hasPath = polygonSource.pathCount > 0;
        polygonSource.compositeOperation = hasPath ? Collider2D.CompositeOperation.Merge : Collider2D.CompositeOperation.None;
        polygonSource.enabled = enableGeometry && hasPath;
      }

      compositeCollider.geometryType = CompositeCollider2D.GeometryType.Polygons;
      compositeCollider.enabled = enableGeometry;
      return;
    }

    DisableExtraPolygonSources();
    sourcePolygon.compositeOperation = Collider2D.CompositeOperation.None;
    sourcePolygon.enabled = enableGeometry;

    if (compositeCollider != null)
    {
      compositeCollider.enabled = false;
    }
  }

  private void EnsurePolygonSourceCount(int desiredCount)
  {
    if (desiredCount <= 0)
    {
      return;
    }

    var polygonSources = GetPolygonSources();
    while (polygonSources.Count < desiredCount)
    {
      var polygonSource = gameObject.AddComponent<PolygonCollider2D>();
      polygonSource.pathCount = 0;
      polygonSources.Add(polygonSource);
    }
  }

  private void DisableExtraPolygonSources()
  {
    var polygonSources = GetPolygonSources();
    for (var i = 1; i < polygonSources.Count; i++)
    {
      var polygonSource = polygonSources[i];
      if (polygonSource == null)
      {
        continue;
      }

      polygonSource.pathCount = 0;
      polygonSource.enabled = false;
      polygonSource.compositeOperation = Collider2D.CompositeOperation.None;
    }
  }

  private List<PolygonCollider2D> GetPolygonSources()
  {
    _polygonSources.Clear();
    GetComponents(_polygonSources);

    if (sourcePolygon != null)
    {
      _polygonSources.Remove(sourcePolygon);
      _polygonSources.Insert(0, sourcePolygon);
    }

    return _polygonSources;
  }
}
