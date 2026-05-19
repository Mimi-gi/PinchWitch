using UnityEngine;

[DisallowMultipleComponent]
public sealed class EnemyPatrolMover2D : MonoBehaviour, IRuntimeResettable
{
  public enum PatrolAxis
  {
    Horizontal,
    Vertical,
  }

  [SerializeField] private PatrolAxis patrolAxis = PatrolAxis.Horizontal;
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
    var offset = Mathf.Sin(_elapsedTime * speed) * moveRange;
    var localPosition = _initialLocalPosition;

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

  private void ClampSerializedValues()
  {
    speed = Mathf.Max(0f, speed);
    moveRange = Mathf.Max(0f, moveRange);
  }
}
