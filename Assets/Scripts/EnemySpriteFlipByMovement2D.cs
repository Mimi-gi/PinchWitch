using UnityEngine;

[DisallowMultipleComponent]
public sealed class EnemySpriteFlipByMovement2D : MonoBehaviour
{
  [SerializeField] private Transform movementSource;
  [SerializeField] private SpriteRenderer targetSpriteRenderer;
  [SerializeField] private bool spriteDefaultFacesLeft = true;
  [SerializeField] private float minimumHorizontalDelta = 0.0001f;

  private float _previousX;

  private void Reset()
  {
    AutoAssignMissingReferences();
    ClampSerializedValues();
  }

  private void Awake()
  {
    AutoAssignMissingReferences();
    ClampSerializedValues();
    _previousX = GetSourcePositionX();
  }

  private void OnEnable()
  {
    _previousX = GetSourcePositionX();
  }

  private void OnValidate()
  {
    ClampSerializedValues();
  }

  private void LateUpdate()
  {
    if (targetSpriteRenderer == null)
    {
      return;
    }

    var currentX = GetSourcePositionX();
    var horizontalDelta = currentX - _previousX;

    if (Mathf.Abs(horizontalDelta) >= minimumHorizontalDelta)
    {
      targetSpriteRenderer.flipX = spriteDefaultFacesLeft
        ? horizontalDelta > 0f
        : horizontalDelta < 0f;
    }

    _previousX = currentX;
  }

  private float GetSourcePositionX()
  {
    var source = movementSource != null ? movementSource : transform;
    return source.position.x;
  }

  private void AutoAssignMissingReferences()
  {
    if (movementSource == null)
    {
      movementSource = transform;
    }

    if (targetSpriteRenderer == null)
    {
      targetSpriteRenderer = GetComponent<SpriteRenderer>();
      if (targetSpriteRenderer == null)
      {
        targetSpriteRenderer = GetComponentInChildren<SpriteRenderer>();
      }
    }
  }

  private void ClampSerializedValues()
  {
    minimumHorizontalDelta = Mathf.Max(0f, minimumHorizontalDelta);
  }
}
