using UnityEngine;

public sealed class GrabInteractor
{
  public struct Config
  {
    public LayerMask grabbableLayerMask;
    public float reach;
    public Vector2 xRange;
    public Vector2 yRange;
    public Vector2 zRange;
    public float followLerpX;
    public float followLerpY;
    public float followLerpZ;
    public float rotationDegreesPerSecond;
    public bool restoreIsKinematicOnRelease;
    public bool restoreUseGravityOnRelease;
  }

  private readonly Collider[] _overlapColliders = new Collider[64];

  private Config _config;
  private Rigidbody _grabbedBody;
  private Vector3 _grabOffset;
  private bool _grabbedOriginalIsKinematic;
  private bool _grabbedOriginalUseGravity;
  private RigidbodyConstraints _grabbedOriginalConstraints;
  private Quaternion _targetRotation;

  public GrabInteractor(Config config)
  {
    ApplyConfiguration(config);
  }

  public bool HasGrabbedBody => _grabbedBody != null;

  public void ApplyConfiguration(Config config)
  {
    _config = config;
  }

  public void TryBeginGrab(Vector3 handTarget)
  {
    var capsuleBottom = new Vector3(handTarget.x, -1000f, handTarget.z);
    var capsuleTop = new Vector3(handTarget.x, 1000f, handTarget.z);
    var hitCount = Physics.OverlapCapsuleNonAlloc(capsuleBottom, capsuleTop, _config.reach, _overlapColliders, _config.grabbableLayerMask, QueryTriggerInteraction.Ignore);

    Rigidbody nearestBody = null;
    var nearestHorizontalDistanceSq = float.MaxValue;
    var maxHorizontalDistanceSq = _config.reach * _config.reach;

    for (var i = 0; i < hitCount; i++)
    {
      var collider = _overlapColliders[i];
      if (collider == null)
      {
        continue;
      }

      var body = collider.attachedRigidbody;
      if (body == null)
      {
        continue;
      }
      if ((_config.grabbableLayerMask.value & (1 << body.gameObject.layer)) == 0)
      {
        continue;
      }

      var toBody = body.worldCenterOfMass - handTarget;
      var horizontalDistanceSq = toBody.x * toBody.x + toBody.z * toBody.z;
      if (horizontalDistanceSq > maxHorizontalDistanceSq || horizontalDistanceSq >= nearestHorizontalDistanceSq)
      {
        continue;
      }

      nearestHorizontalDistanceSq = horizontalDistanceSq;
      nearestBody = body;
    }

    if (nearestBody == null)
    {
      return;
    }

    _grabbedBody = nearestBody;
    _grabOffset = _grabbedBody.position - handTarget;
    _grabOffset.y = 0f;
    _grabbedOriginalIsKinematic = _grabbedBody.isKinematic;
    _grabbedOriginalUseGravity = _grabbedBody.useGravity;
    _grabbedOriginalConstraints = _grabbedBody.constraints;

    _grabbedBody.linearVelocity = Vector3.zero;
    _grabbedBody.angularVelocity = Vector3.zero;
    _grabbedBody.isKinematic = true;
    _grabbedBody.useGravity = false;
    _grabbedBody.constraints = _grabbedOriginalConstraints & ~RigidbodyConstraints.FreezeRotation;
    _targetRotation = _grabbedBody.rotation;
  }

  public void UpdateGrabbedObject(Vector3 handTarget)
  {
    if (_grabbedBody == null)
    {
      return;
    }

    var tx = Mathf.Clamp(handTarget.x + _grabOffset.x, _config.xRange.x, _config.xRange.y);
    var ty = Mathf.Clamp(handTarget.y + _grabOffset.y, _config.yRange.x, _config.yRange.y);
    var tz = Mathf.Clamp(handTarget.z + _grabOffset.z, _config.zRange.x, _config.zRange.y);

    var current = _grabbedBody.position;
    var next = new Vector3(
      Mathf.Lerp(current.x, tx, _config.followLerpX),
      Mathf.Lerp(current.y, ty, _config.followLerpY),
      Mathf.Lerp(current.z, tz, _config.followLerpZ));

    _grabbedBody.position = next;
    _grabbedBody.linearVelocity = Vector3.zero;
    _grabbedBody.angularVelocity = Vector3.zero;
    UpdateGrabbedRotation();
  }

  public void RotateGrabbedObject(Vector3 worldAxis, float angleDegrees)
  {
    if (_grabbedBody == null || worldAxis.sqrMagnitude <= Mathf.Epsilon || Mathf.Approximately(angleDegrees, 0f))
    {
      return;
    }

    _targetRotation = Quaternion.AngleAxis(angleDegrees, worldAxis.normalized) * _targetRotation;
  }

  public void ReleaseGrabbedObject()
  {
    if (_grabbedBody == null)
    {
      return;
    }

    _grabbedBody.linearVelocity = Vector3.zero;
    _grabbedBody.angularVelocity = Vector3.zero;
    _grabbedBody.constraints = _grabbedOriginalConstraints;

    if (_config.restoreIsKinematicOnRelease)
    {
      _grabbedBody.isKinematic = _grabbedOriginalIsKinematic;
    }
    else
    {
      _grabbedBody.isKinematic = false;
    }

    if (_config.restoreUseGravityOnRelease)
    {
      _grabbedBody.useGravity = _grabbedOriginalUseGravity;
    }
    else
    {
      _grabbedBody.useGravity = true;
    }

    _grabbedBody = null;
  }

  private void UpdateGrabbedRotation()
  {
    if (_grabbedBody == null)
    {
      return;
    }

    var maxStep = Mathf.Max(0f, _config.rotationDegreesPerSecond) * Time.deltaTime;
    if (maxStep <= 0f)
    {
      _grabbedBody.rotation = _targetRotation;
      return;
    }

    _grabbedBody.rotation = Quaternion.RotateTowards(_grabbedBody.rotation, _targetRotation, maxStep);
  }
}
