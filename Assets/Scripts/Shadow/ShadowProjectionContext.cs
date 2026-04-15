using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class ShadowProjectionContext : MonoBehaviour
{
  [Header("Projection")]
  [SerializeField] private Transform pointLight;
  [SerializeField] private Transform projectionWall;

  [Header("Generated Outputs")]
  [SerializeField] private Transform generatedOutputRoot;

  [Header("Runtime")]
  [SerializeField] private bool shadowEnabled = true;
  [SerializeField] private string shadowOnlyProxyLayerName = "ShadowCaster2D";

  private readonly HashSet<ShadowColliderBuilder> _builders = new HashSet<ShadowColliderBuilder>();

  public Transform PointLight => pointLight;
  public Transform ProjectionWall => projectionWall;
  public bool ShadowEnabled => shadowEnabled;

  public int ShadowOnlyProxyLayer
  {
    get
    {
      var layer = LayerMask.NameToLayer(shadowOnlyProxyLayerName);
      return layer >= 0 ? layer : 0;
    }
  }

  private void Reset()
  {
    // 参照不足をエディタ上で埋めるための初期化。
    AutoAssignMissingReferences();
  }

  private void Awake()
  {
    // 実行中は影を常時有効として扱う。
    AutoAssignMissingReferences();
    shadowEnabled = true;
    EnsureGeneratedOutputRoot();
  }

  private void OnEnable()
  {
    // 無効化してから戻ってきても、影の状態は有効に戻す。
    AutoAssignMissingReferences();
    shadowEnabled = true;
  }

  private void OnDisable()
  {
    // ここでは保持するだけで、特別な後始末は不要。
  }

  private void OnValidate()
  {
    // Inspector 変更時も、実行中なら常時有効の前提を崩さない。
    AutoAssignMissingReferences();
    shadowEnabled = true;
  }

  public void Register(ShadowColliderBuilder builder)
  {
    if (builder == null)
    {
      return;
    }

    _builders.Add(builder);
  }

  public void Unregister(ShadowColliderBuilder builder)
  {
    if (builder == null)
    {
      return;
    }

    _builders.Remove(builder);
  }

  public void SetShadowEnabled(bool enabled, bool rebuildImmediately = false)
  {
    // このプロジェクトでは影を常時使うため、無効化要求は受けず有効に固定する。
    shadowEnabled = true;
    if (shadowEnabled && rebuildImmediately)
    {
      RebuildAll();
    }
  }

  public bool TryGetProjectionReferences(out Transform light, out Transform wall)
  {
    AutoAssignMissingReferences();
    light = pointLight;
    wall = projectionWall;
    return light != null && wall != null;
  }

  public Transform EnsureGeneratedOutputRoot()
  {
    if (generatedOutputRoot != null)
    {
      return generatedOutputRoot;
    }

    var existingRoot = transform.Find("Shadow2DGeneratedOutputs");
    if (existingRoot != null)
    {
      generatedOutputRoot = existingRoot;
      return generatedOutputRoot;
    }

    var outputRootObject = new GameObject("Shadow2DGeneratedOutputs");
    generatedOutputRoot = outputRootObject.transform;
    generatedOutputRoot.SetParent(transform, false);
    return generatedOutputRoot;
  }

  public void RebuildAll()
  {
    AutoAssignMissingReferences();

    if (!shadowEnabled)
    {
      return;
    }

    // 登録済みのビルダーをまとめて再構築する。
    var snapshot = new List<ShadowColliderBuilder>(_builders);
    foreach (var builder in snapshot)
    {
      if (builder == null)
      {
        continue;
      }

      builder.BuildNow();
    }
  }

  private void AutoAssignMissingReferences()
  {
    if (pointLight == null)
    {
      // 点光源またはスポットライトを優先して使う。
      var lights = FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);
      foreach (var candidate in lights)
      {
        if (candidate == null)
        {
          continue;
        }

        if (candidate.type == LightType.Point || candidate.type == LightType.Spot)
        {
          pointLight = candidate.transform;
          break;
        }
      }

      if (pointLight == null && lights.Length > 0 && lights[0] != null)
      {
        pointLight = lights[0].transform;
      }
    }

    if (projectionWall == null)
    {
      // 影を落とす対象の壁は、慣例として "Wall" という名前のオブジェクトを探す。
      var wallObject = GameObject.Find("Wall");
      if (wallObject != null)
      {
        projectionWall = wallObject.transform;
      }
    }

  }
}
