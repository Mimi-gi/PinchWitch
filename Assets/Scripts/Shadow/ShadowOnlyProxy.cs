using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

[DisallowMultipleComponent]
public sealed class ShadowOnlyProxy : MonoBehaviour
{
  [SerializeField] private Transform source;
  [SerializeField] private bool copyPosition = true;
  [SerializeField] private bool copyRotation = true;
  [SerializeField] private bool copyScale = true;
  [SerializeField] private bool syncChildHierarchy = true;
  [SerializeField] private bool configureRenderersOnAwake = true;
  [SerializeField] private bool disablePhysicsComponents = true;
  private readonly HashSet<string> _reportedMissingSourceChildren = new HashSet<string>();

  public Transform Source => source;

  private void Reset()
  {
    if (source == null && transform.parent != null)
    {
      source = transform.parent;
    }
  }

  private void Awake()
  {
    SyncNow();

    if (configureRenderersOnAwake)
    {
      ConfigureProxy();
    }
  }

  private void LateUpdate()
  {
    SyncNow();
  }

  private void OnValidate()
  {
    if (source == null && transform.parent != null)
    {
      source = transform.parent;
    }

    if (source == null)
    {
      return;
    }

    SyncNow();

    if (configureRenderersOnAwake)
    {
      ConfigureProxy();
    }
  }

  [ContextMenu("Configure Proxy")]
  public void ConfigureProxy()
  {
    foreach (var childRenderer in GetComponentsInChildren<Renderer>(true))
    {
      childRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
      childRenderer.receiveShadows = false;
    }

    if (!disablePhysicsComponents)
    {
      return;
    }

    foreach (var collider3D in GetComponentsInChildren<Collider>(true))
    {
      collider3D.enabled = false;
    }

    foreach (var collider2D in GetComponentsInChildren<Collider2D>(true))
    {
      collider2D.enabled = false;
    }

    foreach (var rigidbody3D in GetComponentsInChildren<Rigidbody>(true))
    {
      rigidbody3D.isKinematic = true;
      rigidbody3D.linearVelocity = Vector3.zero;
      rigidbody3D.angularVelocity = Vector3.zero;
    }

    foreach (var rigidbody2D in GetComponentsInChildren<Rigidbody2D>(true))
    {
      rigidbody2D.bodyType = RigidbodyType2D.Kinematic;
      rigidbody2D.linearVelocity = Vector2.zero;
      rigidbody2D.angularVelocity = 0f;
    }
  }

  public void SetupFromSource(Transform newSource, int targetLayer)
  {
    source = newSource;
    SyncNow();
    SetLayerRecursively(transform, targetLayer);
    ConfigureProxy();
  }

  public void SyncNow()
  {
    if (source == null)
    {
      return;
    }

    if (copyPosition)
    {
      transform.position = source.position;
    }

    if (copyRotation)
    {
      transform.rotation = source.rotation;
    }

    if (copyScale)
    {
      transform.localScale = ResolveLocalScaleForParent(source.lossyScale, transform.parent);
    }

    if (syncChildHierarchy)
    {
      SyncChildHierarchyRecursive(source, transform, transform.name);
    }
  }

  private void SyncChildHierarchyRecursive(Transform sourceParent, Transform proxyParent, string proxyPath)
  {
    if (sourceParent == null || proxyParent == null)
    {
      return;
    }

    foreach (Transform proxyChild in proxyParent)
    {
      var sourceChild = FindMatchingSourceChild(sourceParent, proxyChild);
      if (sourceChild == null)
      {
        ReportMissingSourceChild(sourceParent, proxyChild, proxyPath);
        continue;
      }

      proxyChild.localPosition = sourceChild.localPosition;
      proxyChild.localRotation = sourceChild.localRotation;
      proxyChild.localScale = sourceChild.localScale;
      SyncChildHierarchyRecursive(sourceChild, proxyChild, $"{proxyPath}/{proxyChild.name}");
    }
  }

  private static Transform FindMatchingSourceChild(Transform sourceParent, Transform proxyChild)
  {
    if (sourceParent == null || proxyChild == null)
    {
      return null;
    }

    var siblingIndex = proxyChild.GetSiblingIndex();
    if (siblingIndex < sourceParent.childCount)
    {
      var sourceChildAtSameIndex = sourceParent.GetChild(siblingIndex);
      if (sourceChildAtSameIndex != null && sourceChildAtSameIndex.name == proxyChild.name)
      {
        return sourceChildAtSameIndex;
      }
    }

    foreach (Transform sourceChild in sourceParent)
    {
      if (sourceChild != null && sourceChild.name == proxyChild.name)
      {
        return sourceChild;
      }
    }

    return null;
  }

  private void ReportMissingSourceChild(Transform sourceParent, Transform proxyChild, string proxyPath)
  {
    if (sourceParent == null || proxyChild == null)
    {
      return;
    }

    var missingPath = $"{proxyPath}/{proxyChild.name}";
    if (!_reportedMissingSourceChildren.Add(missingPath))
    {
      return;
    }

    Debug.LogWarning(
      $"ShadowOnlyProxy could not find a matching source child for '{missingPath}' under '{sourceParent.name}'. Child sync skipped for this branch.",
      this);
  }

  private static Vector3 ResolveLocalScaleForParent(Vector3 worldScale, Transform parent)
  {
    if (parent == null)
    {
      return worldScale;
    }

    var parentScale = parent.lossyScale;
    return new Vector3(
      DivideOrFallback(worldScale.x, parentScale.x),
      DivideOrFallback(worldScale.y, parentScale.y),
      DivideOrFallback(worldScale.z, parentScale.z));
  }

  private static float DivideOrFallback(float value, float divisor)
  {
    return Mathf.Approximately(divisor, 0f) ? value : value / divisor;
  }

  private static void SetLayerRecursively(Transform root, int targetLayer)
  {
    if (root == null || targetLayer < 0)
    {
      return;
    }

    root.gameObject.layer = targetLayer;
    foreach (Transform child in root)
    {
      SetLayerRecursively(child, targetLayer);
    }
  }
}
