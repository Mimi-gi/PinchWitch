using UnityEngine;

/// <summary>
/// Automatically moves the 2D player along nearby surfaces.
/// Floors, walls, and ceilings are all treated as valid traversal surfaces.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public sealed class Wall2DPlayerController : MonoBehaviour
{
  private enum AutoTraversalDirection
  {
    Clockwise,
    CounterClockwise,
  }

  private static readonly Vector2[] SurfaceSearchDirections =
  {
    Vector2.down,
    Vector2.right,
    Vector2.up,
    Vector2.left,
    new Vector2(1f, 1f).normalized,
    new Vector2(1f, -1f).normalized,
    new Vector2(-1f, 1f).normalized,
    new Vector2(-1f, -1f).normalized,
  };

  [Header("Auto Movement")]
  [SerializeField] private float moveSpeed = 6f;
  [SerializeField] private AutoTraversalDirection traversalDirection = AutoTraversalDirection.Clockwise;
  [SerializeField] private float surfaceStickVelocity = 5f;

  [Header("Surface Detection")]
  [SerializeField] private LayerMask groundLayers = ~0;
  [SerializeField] private float groundCheckDistance = 0.08f;
  [SerializeField] private float forwardProbeDistance = 0.18f;
  [SerializeField] private float surfaceSearchDistance = 0.2f;

  [Header("Detached Movement")]
  [SerializeField] private float detachedGravityScale = 2f;
  [SerializeField] private bool preserveMomentumWhenDetached = true;

  [Header("Rotation")]
  [SerializeField] private float rotationDegreesPerSecond = 360f;
  [SerializeField] private float rotationSnapThreshold = 2f;
  [SerializeField] private bool chooseTraversalDirectionFromFacingOnLanding = true;

  [Header("Physics")]
  [SerializeField] private CollisionDetectionMode2D collisionDetectionMode = CollisionDetectionMode2D.Continuous;
  [SerializeField] private RigidbodyInterpolation2D interpolationMode = RigidbodyInterpolation2D.Interpolate;

  private readonly RaycastHit2D[] _surfaceHits = new RaycastHit2D[16];

  private Rigidbody2D _rigidbody2D;
  private Collider2D _collider2D;
  private Vector2 _currentSurfaceNormal = Vector2.up;
  private bool _isAttachedToSurface;

  private void Reset()
  {
    ClampSerializedValues();
  }

  private void Awake()
  {
    _rigidbody2D = GetComponent<Rigidbody2D>();
    _collider2D = GetComponent<Collider2D>();
    ClampSerializedValues();
    ApplyPhysicsConfiguration();
  }

  private void OnEnable()
  {
    TryAcquireAnyNearbySurface();
    SnapRotationToSurface();
  }

  private void OnValidate()
  {
    ClampSerializedValues();

    if (!Application.isPlaying)
    {
      return;
    }

    if (_rigidbody2D == null)
    {
      _rigidbody2D = GetComponent<Rigidbody2D>();
    }

    if (_collider2D == null)
    {
      _collider2D = GetComponent<Collider2D>();
    }

    ApplyPhysicsConfiguration();
  }

  private void OnDisable()
  {
    if (_rigidbody2D != null)
    {
      _rigidbody2D.gravityScale = detachedGravityScale;
      _rigidbody2D.linearVelocity = Vector2.zero;
    }

    _isAttachedToSurface = false;
    _currentSurfaceNormal = Vector2.up;
  }

  private void FixedUpdate()
  {
    var wasAttachedToSurface = _isAttachedToSurface;
    UpdateSurfaceState();
    UpdateTraversalDirectionOnLandingIfNeeded(wasAttachedToSurface);
    ApplyAutomaticMovement();
  }

  private void UpdateSurfaceState()
  {
    if (_collider2D == null)
    {
      _isAttachedToSurface = false;
      return;
    }

    if (_isAttachedToSurface)
    {
      TryTurnAlongForwardSurface();
    }

    if (TryFindSurfaceInDirection(-_currentSurfaceNormal, ResolveProbeDistance(groundCheckDistance), out var supportHit))
    {
      _currentSurfaceNormal = supportHit.normal.normalized;
      _isAttachedToSurface = true;
      return;
    }

    _isAttachedToSurface = TryAcquireAnyNearbySurface();
  }

  private void ApplyAutomaticMovement()
  {
    if (_rigidbody2D == null)
    {
      return;
    }

    if (_isAttachedToSurface)
    {
      var tangent = GetTraversalTangent(_currentSurfaceNormal);
      var velocity = tangent * moveSpeed + (-_currentSurfaceNormal) * surfaceStickVelocity;

      _rigidbody2D.gravityScale = 0f;
      _rigidbody2D.linearVelocity = velocity;

      AlignToSurfaceNormal();

      return;
    }

    _rigidbody2D.gravityScale = detachedGravityScale;

    if (!preserveMomentumWhenDetached)
    {
      var velocity = _rigidbody2D.linearVelocity;
      velocity.x = 0f;
      _rigidbody2D.linearVelocity = velocity;
    }
  }

  private void TryTurnAlongForwardSurface()
  {
    var forwardDirection = GetTraversalTangent(_currentSurfaceNormal);
    if (!TryFindSurfaceInDirection(forwardDirection, ResolveProbeDistance(forwardProbeDistance), out var forwardHit))
    {
      return;
    }

    var forwardNormal = forwardHit.normal.normalized;
    if (forwardNormal == Vector2.zero)
    {
      return;
    }

    // Ignore the same supporting surface and only turn when a different face is found.
    if (Vector2.Dot(forwardNormal, _currentSurfaceNormal) > 0.98f)
    {
      return;
    }

    _currentSurfaceNormal = forwardNormal;
    _isAttachedToSurface = true;
  }

  private bool TryAcquireAnyNearbySurface()
  {
    var bestDistance = float.PositiveInfinity;
    RaycastHit2D bestHit = default;
    var hasHit = false;
    var probeDistance = ResolveProbeDistance(surfaceSearchDistance);

    foreach (var direction in SurfaceSearchDirections)
    {
      if (!TryFindSurfaceInDirection(direction, probeDistance, out var hit))
      {
        continue;
      }

      if (hit.distance >= bestDistance)
      {
        continue;
      }

      bestDistance = hit.distance;
      bestHit = hit;
      hasHit = true;
    }

    if (!hasHit)
    {
      return false;
    }

    _currentSurfaceNormal = bestHit.normal.normalized;
    return true;
  }

  private bool TryFindSurfaceInDirection(Vector2 direction, float castDistance, out RaycastHit2D bestHit)
  {
    bestHit = default;

    if (_collider2D == null || direction.sqrMagnitude <= Mathf.Epsilon)
    {
      return false;
    }

    var contactFilter = Wall2DContactCastUtility.CreateContactFilter(groundLayers);
    var hitCount = _collider2D.Cast(direction.normalized, contactFilter, _surfaceHits, castDistance);

    var hasHit = false;
    var bestDistance = float.PositiveInfinity;
    for (var i = 0; i < hitCount; i++)
    {
      var hit = _surfaceHits[i];
      if (hit.collider == null || hit.normal == Vector2.zero)
      {
        continue;
      }

      if (hit.distance >= bestDistance)
      {
        continue;
      }

      bestDistance = hit.distance;
      bestHit = hit;
      hasHit = true;
    }

    return hasHit;
  }

  private Vector2 GetTraversalTangent(Vector2 surfaceNormal)
  {
    var tangent = traversalDirection == AutoTraversalDirection.Clockwise
      ? new Vector2(surfaceNormal.y, -surfaceNormal.x)
      : new Vector2(-surfaceNormal.y, surfaceNormal.x);

    return tangent.normalized;
  }

  private void AlignToSurfaceNormal()
  {
    if (_rigidbody2D == null)
    {
      return;
    }

    var targetAngle = GetSurfaceAlignmentAngle(_currentSurfaceNormal);
    var nextAngle = Mathf.MoveTowardsAngle(
      _rigidbody2D.rotation,
      targetAngle,
      rotationDegreesPerSecond * Time.fixedDeltaTime);

    if (Mathf.Abs(Mathf.DeltaAngle(nextAngle, targetAngle)) <= rotationSnapThreshold)
    {
      nextAngle = targetAngle;
    }

    _rigidbody2D.MoveRotation(nextAngle);
  }

  private void SnapRotationToSurface()
  {
    if (_rigidbody2D == null)
    {
      return;
    }

    _rigidbody2D.SetRotation(GetSurfaceAlignmentAngle(_currentSurfaceNormal));
  }

  private static float GetSurfaceAlignmentAngle(Vector2 surfaceNormal)
  {
    return Mathf.Atan2(surfaceNormal.y, surfaceNormal.x) * Mathf.Rad2Deg - 90f;
  }

  private void UpdateTraversalDirectionOnLandingIfNeeded(bool wasAttachedToSurface)
  {
    if (!chooseTraversalDirectionFromFacingOnLanding || wasAttachedToSurface || !_isAttachedToSurface)
    {
      return;
    }

    var headDirection = (Vector2)transform.up;
    if (headDirection.sqrMagnitude <= Mathf.Epsilon)
    {
      return;
    }

    headDirection.Normalize();

    var shouldMoveRight = Mathf.Abs(headDirection.x) >= Mathf.Abs(headDirection.y)
      ? headDirection.x >= 0f
      : headDirection.y >= 0f;

    traversalDirection = shouldMoveRight
      ? AutoTraversalDirection.Clockwise
      : AutoTraversalDirection.CounterClockwise;
  }

  private float ResolveProbeDistance(float baseDistance)
  {
    return Mathf.Max(baseDistance, moveSpeed * Time.fixedDeltaTime + 0.02f);
  }

  private void ClampSerializedValues()
  {
    moveSpeed = Mathf.Max(0.1f, moveSpeed);
    surfaceStickVelocity = Mathf.Max(0.1f, surfaceStickVelocity);
    groundCheckDistance = Mathf.Max(0.01f, groundCheckDistance);
    forwardProbeDistance = Mathf.Max(0.01f, forwardProbeDistance);
    surfaceSearchDistance = Mathf.Max(groundCheckDistance, surfaceSearchDistance);
    detachedGravityScale = Mathf.Max(0f, detachedGravityScale);
    rotationDegreesPerSecond = Mathf.Max(1f, rotationDegreesPerSecond);
    rotationSnapThreshold = Mathf.Clamp(rotationSnapThreshold, 0f, 45f);
  }

  private void ApplyPhysicsConfiguration()
  {
    if (_rigidbody2D == null)
    {
      return;
    }

    _rigidbody2D.collisionDetectionMode = collisionDetectionMode;
    _rigidbody2D.interpolation = interpolationMode;
  }
}
