using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public sealed class EnemyPatrolMover2D : MonoBehaviour, IRuntimeResettable
{
  public enum PatrolAxis
  {
    Horizontal,
    Vertical,
  }

  public enum InitialPositionReference
  {
    Center,
    LowerOrLeftEnd,
    UpperOrRightEnd,
  }

  [SerializeField] private PatrolAxis patrolAxis = PatrolAxis.Horizontal;
  [SerializeField] private InitialPositionReference initialPositionReference = InitialPositionReference.Center;
  [SerializeField] private float speed = 2f;
  [SerializeField] private float moveRange = 3f;
  [SerializeField] private bool stopWhileShadowCrushed = true;
  [SerializeField] private bool reverseWhenTouchingShadowEdge = true;
  [SerializeField] private Wall2DPlayerCrushDetector shadowCrushDetector;

  private Vector3 _initialLocalPosition;
  private float _currentOffset;
  private float _movementDirection = 1f;

  public int ResetOrder => 100;

  private void Reset()
  {
    CacheComponentReferences();
    ClampSerializedValues();
  }

  private void Awake()
  {
    _initialLocalPosition = transform.localPosition;
    CacheComponentReferences();
    ClampSerializedValues();
    InitializePatrolState();
  }

  private void OnValidate()
  {
    CacheComponentReferences();
    ClampSerializedValues();
  }

  private void Update()
  {
    if (stopWhileShadowCrushed && shadowCrushDetector != null && shadowCrushDetector.IsCrushed)
    {
      return;
    }

    if (reverseWhenTouchingShadowEdge
        && shadowCrushDetector != null
        && shadowCrushDetector.IsShadowBlockingDirection(GetPatrolWorldDirection()))
    {
      ReverseDirection();
    }

    AdvancePatrol(Time.deltaTime);
    ApplyPatrolPosition();
  }

  public void ResetRuntimeState()
  {
    InitializePatrolState();
    transform.localPosition = _initialLocalPosition;
    ApplyPatrolPosition();
  }

  private void ApplyPatrolPosition()
  {
    var localPosition = GetPatrolCenterLocalPosition();
    var offset = GetPatrolOffset();

    switch (patrolAxis)
    {
      case PatrolAxis.Horizontal:
        localPosition.x += offset;
        break;
      case PatrolAxis.Vertical:
        localPosition.y += offset;
        break;
    }

    transform.localPosition = localPosition;
  }

  private Vector3 GetPatrolCenterLocalPosition()
  {
    var localPosition = _initialLocalPosition;

    switch (initialPositionReference)
    {
      case InitialPositionReference.LowerOrLeftEnd:
        if (patrolAxis == PatrolAxis.Horizontal)
        {
          localPosition.x += moveRange;
        }
        else
        {
          localPosition.y += moveRange;
        }
        break;
      case InitialPositionReference.UpperOrRightEnd:
        if (patrolAxis == PatrolAxis.Horizontal)
        {
          localPosition.x -= moveRange;
        }
        else
        {
          localPosition.y -= moveRange;
        }
        break;
    }

    return localPosition;
  }

  private float GetPatrolOffset()
  {
    return _currentOffset;
  }

  private float GetInitialOffset()
  {
    return initialPositionReference switch
    {
      InitialPositionReference.LowerOrLeftEnd => -moveRange,
      InitialPositionReference.Center => 0f,
      InitialPositionReference.UpperOrRightEnd => moveRange,
      _ => 0f,
    };
  }

  private void ClampSerializedValues()
  {
    speed = Mathf.Max(0f, speed);
    moveRange = Mathf.Max(0f, moveRange);
  }

  private void CacheComponentReferences()
  {
    if (shadowCrushDetector == null)
    {
      shadowCrushDetector = GetComponent<Wall2DPlayerCrushDetector>();
    }
  }

  private void InitializePatrolState()
  {
    _currentOffset = GetInitialOffset();
    _movementDirection = initialPositionReference == InitialPositionReference.UpperOrRightEnd ? -1f : 1f;
  }

  private void AdvancePatrol(float deltaTime)
  {
    if (moveRange <= 0f || speed <= 0f || deltaTime <= 0f)
    {
      return;
    }

    _currentOffset += _movementDirection * GetConstantSpeed() * deltaTime;

    while (_currentOffset < -moveRange || _currentOffset > moveRange)
    {
      if (_currentOffset > moveRange)
      {
        _currentOffset = moveRange - (_currentOffset - moveRange);
        _movementDirection = -1f;
      }
      else if (_currentOffset < -moveRange)
      {
        _currentOffset = -moveRange + (-moveRange - _currentOffset);
        _movementDirection = 1f;
      }
    }
  }

  private float GetConstantSpeed()
  {
    return (2f * moveRange * speed) / Mathf.PI;
  }

  private Vector2 GetPatrolWorldDirection()
  {
    var localDirection = patrolAxis == PatrolAxis.Horizontal ? Vector3.right : Vector3.up;
    var referenceTransform = transform.parent;
    var worldDirection = referenceTransform != null
      ? referenceTransform.TransformDirection(localDirection)
      : localDirection;

    return ((Vector2)worldDirection).normalized * _movementDirection;
  }

  private void ReverseDirection()
  {
    _movementDirection *= -1f;
  }
}
