using UnityEngine;

[DisallowMultipleComponent]
[ExecuteAlways]
[RequireComponent(typeof(LineRenderer))]
public sealed class SpiderThreadLineRenderer2D : MonoBehaviour
{
  [SerializeField] private Transform anchorPoint;
  [SerializeField] private Transform targetPoint;
  [SerializeField] private LineRenderer targetLineRenderer;
  [SerializeField] private Vector3 anchorOffset;
  [SerializeField] private Vector3 targetOffset;
  [SerializeField] private float minimumLineWidth = 0.01f;

  private void Reset()
  {
    AutoAssignMissingReferences();
    ClampSerializedValues();
    RefreshLine(forceSetup: true);
  }

  private void Awake()
  {
    AutoAssignMissingReferences();
    ClampSerializedValues();
    RefreshLine(forceSetup: true);
  }

  private void OnEnable()
  {
    RefreshLine(forceSetup: true);
  }

  private void OnValidate()
  {
    AutoAssignMissingReferences();
    ClampSerializedValues();
    RefreshLine(forceSetup: true);
  }

  private void LateUpdate()
  {
    RefreshLine(forceSetup: false);
  }

  private void RefreshLine(bool forceSetup)
  {
    if (targetLineRenderer == null || anchorPoint == null || targetPoint == null)
    {
      return;
    }

    if (forceSetup)
    {
      EnsureLineRendererSetup();
    }

    targetLineRenderer.SetPosition(0, anchorPoint.position + anchorOffset);
    targetLineRenderer.SetPosition(1, targetPoint.position + targetOffset);
  }

  private void EnsureLineRendererSetup()
  {
    targetLineRenderer.useWorldSpace = true;
    targetLineRenderer.positionCount = 2;
    targetLineRenderer.startWidth = Mathf.Max(minimumLineWidth, targetLineRenderer.startWidth);
    targetLineRenderer.endWidth = Mathf.Max(minimumLineWidth, targetLineRenderer.endWidth);
  }

  private void AutoAssignMissingReferences()
  {
    if (targetLineRenderer == null)
    {
      targetLineRenderer = GetComponent<LineRenderer>();
    }
  }

  private void ClampSerializedValues()
  {
    minimumLineWidth = Mathf.Max(0f, minimumLineWidth);
  }
}
