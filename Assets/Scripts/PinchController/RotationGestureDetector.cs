using UnityEngine;

public sealed class RotationGestureDetector
{
  private enum RearmState
  {
    Ready,
    WaitingForStillness,
  }

  public struct Config
  {
    public float xRotationDegrees;
    public float yRotationDegrees;
    public float horizontalSwipeTriggerDistance;
    public float verticalSwipeTriggerDistance;
    public float swipeDominanceRatio;
    public float swipeCooldownSeconds;
    public float rearmStillnessDistance;
    public float rearmStillnessDurationSeconds;
  }

  public struct RotationCommand
  {
    public Vector3 worldAxis;
    public float angleDegrees;
  }

  private Config _config;
  private RearmState _rearmState;
  private bool _hasSwipeAnchor;
  private Vector2 _swipeAnchor;
  private float _cooldownUntilTime;
  private bool _hasStillnessSample;
  private Vector2 _lastStillnessSample;
  private float _stillnessTimer;

  public RotationGestureDetector(Config config)
  {
    ApplyConfiguration(config);
  }

  public void ApplyConfiguration(Config config)
  {
    _config = config;
  }

  public void Reset()
  {
    _rearmState = RearmState.Ready;
    _hasSwipeAnchor = false;
    _cooldownUntilTime = 0f;
    _hasStillnessSample = false;
    _stillnessTimer = 0f;
  }

  public bool TryDetectSwipe(HandState handState, out RotationCommand rotationCommand)
  {
    rotationCommand = default;
    if (!handState.detected)
    {
      Reset();
      return false;
    }

    var currentPoint = new Vector2(handState.x, handState.y);
    if (_rearmState == RearmState.WaitingForStillness)
    {
      UpdateStillnessState(currentPoint);
      return false;
    }

    if (Time.time < _cooldownUntilTime)
    {
      _swipeAnchor = currentPoint;
      _hasSwipeAnchor = true;
      return false;
    }

    if (!_hasSwipeAnchor)
    {
      _swipeAnchor = currentPoint;
      _hasSwipeAnchor = true;
      return false;
    }

    var delta = currentPoint - _swipeAnchor;
    var absDeltaX = Mathf.Abs(delta.x);
    var absDeltaY = Mathf.Abs(delta.y);

    if (absDeltaX >= _config.horizontalSwipeTriggerDistance
        && absDeltaX >= absDeltaY * _config.swipeDominanceRatio)
    {
      rotationCommand.worldAxis = Vector3.up;
      rotationCommand.angleDegrees = delta.x < 0f ? _config.yRotationDegrees : -_config.yRotationDegrees;
      StartCooldown(currentPoint);
      return true;
    }

    if (absDeltaY >= _config.verticalSwipeTriggerDistance
        && absDeltaY >= absDeltaX * _config.swipeDominanceRatio)
    {
      rotationCommand.worldAxis = Vector3.right;
      rotationCommand.angleDegrees = delta.y > 0f ? -_config.xRotationDegrees : _config.xRotationDegrees;
      StartCooldown(currentPoint);
      return true;
    }

    return false;
  }

  private void StartCooldown(Vector2 currentPoint)
  {
    _swipeAnchor = currentPoint;
    _hasSwipeAnchor = true;
    _cooldownUntilTime = Time.time + _config.swipeCooldownSeconds;
    _rearmState = RearmState.WaitingForStillness;
    _hasStillnessSample = false;
    _stillnessTimer = 0f;
  }

  private void UpdateStillnessState(Vector2 currentPoint)
  {
    if (!_hasStillnessSample)
    {
      _lastStillnessSample = currentPoint;
      _hasStillnessSample = true;
      _stillnessTimer = 0f;
      return;
    }

    var movementSinceLastSample = Vector2.Distance(currentPoint, _lastStillnessSample);
    if (movementSinceLastSample <= _config.rearmStillnessDistance)
    {
      _stillnessTimer += Time.deltaTime;
      if (_stillnessTimer >= _config.rearmStillnessDurationSeconds && Time.time >= _cooldownUntilTime)
      {
        _rearmState = RearmState.Ready;
        _swipeAnchor = currentPoint;
        _hasSwipeAnchor = true;
        _hasStillnessSample = false;
        _stillnessTimer = 0f;
      }
    }
    else
    {
      _stillnessTimer = 0f;
    }

    _lastStillnessSample = currentPoint;
  }
}
