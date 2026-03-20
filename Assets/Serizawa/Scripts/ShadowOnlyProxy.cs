using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class ShadowOnlyProxy : MonoBehaviour
{
  [SerializeField] private Transform source;
  [SerializeField] private bool copyPosition = true;
  [SerializeField] private bool copyRotation = true;
  [SerializeField] private bool copyScale = true;
  [SerializeField] private bool configureRenderersOnAwake = true;
  [SerializeField] private bool disablePhysicsComponents = true;

  private void Reset()
  {
    if (source == null && transform.parent != null)
    {
      source = transform.parent;
    }
  }

  private void Awake()
  {
    if (configureRenderersOnAwake)
    {
      ConfigureProxy();
    }
  }

  private void LateUpdate()
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
      transform.localScale = source.lossyScale;
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
}
