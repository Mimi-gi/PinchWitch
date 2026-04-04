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
  private readonly List<PolygonCollider2D> _polygonSources = new List<PolygonCollider2D>();

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
      return;
    }

    DisableExtraPolygonSources();
    sourcePolygon.pathCount = trianglePaths.Count;
    for (var i = 0; i < trianglePaths.Count; i++)
    {
      sourcePolygon.SetPath(i, trianglePaths[i]);
    }

    SetGeometryEnabled(true);
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

    ConfigureColliderMode(false);
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
