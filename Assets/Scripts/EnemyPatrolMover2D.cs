using UnityEngine;

[DisallowMultipleComponent]
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

  private Vector3 _initialLocalPosition;
  private float _elapsedTime;

  public int ResetOrder => 100;

  private void Reset()
  {
    ClampSerializedValues();
  }

  private void Awake()
  {
    _initialLocalPosition = transform.localPosition;
    ClampSerializedValues();
  }

  private void OnValidate()
  {
    ClampSerializedValues();
  }

  private void Update()
  {
    _elapsedTime += Time.deltaTime;
    ApplyPatrolPosition();
  }

  public void ResetRuntimeState()
  {
    _elapsedTime = 0f;
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
    switch (initialPositionReference)
    {
      case InitialPositionReference.Center:
        return Mathf.Sin(_elapsedTime * speed) * moveRange;
      case InitialPositionReference.LowerOrLeftEnd:
        return Mathf.Cos(_elapsedTime * speed) * moveRange;
      case InitialPositionReference.UpperOrRightEnd:
        return Mathf.Cos(_elapsedTime * speed) * moveRange;
      default:
        return 0f;
    }
  }

  private void ClampSerializedValues()
  {
    speed = Mathf.Max(0f, speed);
    moveRange = Mathf.Max(0f, moveRange);
  }
}
