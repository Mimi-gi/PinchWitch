using System;
using UnityEngine;

/// <summary>
/// 近くの面に沿って 2D プレイヤーを自動移動させる。
/// 床・壁・天井のすべてを移動対象として扱い、詰まり検知も内包する。
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

  [Header("Wall Bounds")]
  [SerializeField] private bool reverseDirectionAtWallBounds = true;
  [SerializeField] private bool reverseDirectionAtBottomWallBoundary = false;
  [SerializeField] private bool autoFindWallBounds = true;
  [SerializeField] private Transform wallBoundsSource;
  [SerializeField] private Vector2 wallBoundsMin = new Vector2(-10f, -10f);
  [SerializeField] private Vector2 wallBoundsMax = new Vector2(10f, 10f);

  [Header("Rotation")]
  [SerializeField] private float rotationDegreesPerSecond = 360f;
  [SerializeField] private float rotationSnapThreshold = 2f;
  [SerializeField] private bool chooseTraversalDirectionFromFacingOnLanding = true;

  [Header("Physics")]
  [SerializeField] private CollisionDetectionMode2D collisionDetectionMode = CollisionDetectionMode2D.Continuous;
  [SerializeField] private RigidbodyInterpolation2D interpolationMode = RigidbodyInterpolation2D.Interpolate;

  [Header("Stuck Recovery")]
  [SerializeField] private float movementStallRespawnDelay = 2f;
  [SerializeField] private float movementStallPositionEpsilon = 0.01f;

  public event Action MovementStalled;

  /// <summary>現在、面に接地しているかどうか。</summary>
  public bool IsAttachedToSurface => _isAttachedToSurface;

  /// <summary>moveSpeed が正の値であり、実際に移動しようとしているかどうか。</summary>
  public bool IsMoving => moveSpeed > Mathf.Epsilon;

  /// <summary>現在の接地面の法線ベクトル。非接地時は Vector2.up。</summary>
  public Vector2 CurrentSurfaceNormal => _currentSurfaceNormal;

  /// <summary>現在設定されている移動速度。</summary>
  public float CurrentMoveSpeed => moveSpeed;

  private readonly RaycastHit2D[] _surfaceHits = new RaycastHit2D[16];

  private Rigidbody2D _rigidbody2D;
  private Collider2D _collider2D;
  private Vector2 _currentSurfaceNormal = Vector2.up;
  private bool _isAttachedToSurface;
  private bool _hasCachedWallBounds;
  private Vector2 _lastObservedPosition;
  private bool _hasLastObservedPosition;
  private float _movementStallDuration;

  private void Reset()
  {
    AutoAssignWallBoundsSource();
    RefreshWallBoundsCache();
    ClampSerializedValues();
  }

  private void Awake()
  {
    _rigidbody2D = GetComponent<Rigidbody2D>();
    _collider2D = GetComponent<Collider2D>();
    AutoAssignWallBoundsSource();
    RefreshWallBoundsCache();
    ClampSerializedValues();
    ApplyPhysicsConfiguration();
    CacheObservedPosition();
  }

  private void OnEnable()
  {
    AutoAssignWallBoundsSource();
    RefreshWallBoundsCache();
    _isAttachedToSurface = TryAcquireAnyNearbySurface();
    SnapRotationToSurface();
    ResetStuckTracking();
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

    AutoAssignWallBoundsSource();
    RefreshWallBoundsCache();
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
    _movementStallDuration = 0f;
    _hasLastObservedPosition = false;
  }

  private void FixedUpdate()
  {
    // 1 フレームごとに、接地状態の更新 → 移動停止の監視 → 実際の移動の順で処理する。
    UpdateMovementStallState();
    var wasAttachedToSurface = _isAttachedToSurface;
    UpdateSurfaceState();
    UpdateTraversalDirectionOnLandingIfNeeded(wasAttachedToSurface);
    ApplyAutomaticMovement();
    ReverseTraversalDirectionAtWallBoundsIfNeeded();
    CacheObservedPosition();
  }

  private void UpdateMovementStallState()
  {
    if (_rigidbody2D == null)
    {
      ResetStuckTracking();
      return;
    }

    if (!_hasLastObservedPosition)
    {
      // 最初の 1 回は比較対象がないので、現在位置だけ覚える。
      _lastObservedPosition = _rigidbody2D.position;
      _hasLastObservedPosition = true;
      return;
    }

    if (!_isAttachedToSurface)
    {
      // 浮いている間は「詰まり」とみなさず、カウンタをリセットする。
      _movementStallDuration = 0f;
      _lastObservedPosition = _rigidbody2D.position;
      return;
    }

    var currentPosition = _rigidbody2D.position;
    var positionDelta = currentPosition - _lastObservedPosition;
    if (positionDelta.sqrMagnitude <= movementStallPositionEpsilon * movementStallPositionEpsilon)
    {
      // 一定時間ほとんど動いていないなら、壁やコライダーに引っかかっていると判断する。
      _movementStallDuration += Time.fixedDeltaTime;
      if (_movementStallDuration >= movementStallRespawnDelay)
      {
        _movementStallDuration = 0f;
        MovementStalled?.Invoke();
      }
    }
    else
    {
      _movementStallDuration = 0f;
    }

    _lastObservedPosition = currentPosition;
  }

  private void CacheObservedPosition()
  {
    if (_rigidbody2D == null)
    {
      return;
    }

    _lastObservedPosition = _rigidbody2D.position;
    _hasLastObservedPosition = true;
  }

  private void ResetStuckTracking()
  {
    // 再有効化や初期化の直後に、古い判定を持ち越さないようにする。
    _movementStallDuration = 0f;

    if (_rigidbody2D == null)
    {
      _hasLastObservedPosition = false;
      return;
    }

    _lastObservedPosition = _rigidbody2D.position;
    _hasLastObservedPosition = true;
  }

  private void UpdateSurfaceState()
  {
    if (_collider2D == null)
    {
      _isAttachedToSurface = false;
      return;
    }

    // いま接地している面の前方を先読みし、角を曲がれるなら曲がる。
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
      // 接地中は、接線方向に進みつつ法線方向へ押し付けて面から離れにくくする。
      var tangent = GetTraversalTangent(_currentSurfaceNormal);
      var velocity = (tangent * moveSpeed) + ((-_currentSurfaceNormal) * surfaceStickVelocity);

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
    // 進行方向の少し先に別の面があれば、そこで向きを切り替える。
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
    // 周囲 8 方向を探して、最も近い面を現在の接地面として採用する。
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
    return (Mathf.Atan2(surfaceNormal.y, surfaceNormal.x) * Mathf.Rad2Deg) - 90f;
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

  private void ReverseTraversalDirectionAtWallBoundsIfNeeded()
  {
    if (!reverseDirectionAtWallBounds || !_hasCachedWallBounds || _rigidbody2D == null || _collider2D == null)
    {
      return;
    }

    var colliderBounds = _collider2D.bounds;
    var offset = Vector2.zero;
    var touchedBoundary = false;

    if (colliderBounds.min.x < wallBoundsMin.x)
    {
      offset.x = wallBoundsMin.x - colliderBounds.min.x;
      touchedBoundary = true;
    }
    else if (colliderBounds.max.x > wallBoundsMax.x)
    {
      offset.x = wallBoundsMax.x - colliderBounds.max.x;
      touchedBoundary = true;
    }

    if (reverseDirectionAtBottomWallBoundary && colliderBounds.min.y < wallBoundsMin.y)
    {
      offset.y = wallBoundsMin.y - colliderBounds.min.y;
      touchedBoundary = true;
    }
    else if (colliderBounds.max.y > wallBoundsMax.y)
    {
      offset.y = wallBoundsMax.y - colliderBounds.max.y;
      touchedBoundary = true;
    }

    if (!touchedBoundary)
    {
      return;
    }

    _rigidbody2D.position += offset;
    _rigidbody2D.linearVelocity = Vector2.zero;
    traversalDirection = traversalDirection == AutoTraversalDirection.Clockwise
      ? AutoTraversalDirection.CounterClockwise
      : AutoTraversalDirection.Clockwise;
  }

  private void AutoAssignWallBoundsSource()
  {
    if (!autoFindWallBounds || wallBoundsSource != null)
    {
      return;
    }

    var wallObject = GameObject.Find("Wall");
    if (wallObject != null)
    {
      wallBoundsSource = wallObject.transform;
    }
  }

  private void RefreshWallBoundsCache()
  {
    _hasCachedWallBounds = TryResolveWallBounds(out var min, out var max);
    if (!_hasCachedWallBounds)
    {
      return;
    }

    wallBoundsMin = min;
    wallBoundsMax = max;
  }

  private bool TryResolveWallBounds(out Vector2 min, out Vector2 max)
  {
    min = default;
    max = default;

    if (wallBoundsSource == null)
    {
      return false;
    }

    // 2D コライダーがあればそれを優先し、なければ 3D コライダーや Renderer から外形を拾う。
    var collider2D = wallBoundsSource.GetComponent<Collider2D>();
    if (collider2D != null)
    {
      var bounds = collider2D.bounds;
      min = bounds.min;
      max = bounds.max;
      return true;
    }

    var collider3D = wallBoundsSource.GetComponent<Collider>();
    if (collider3D != null)
    {
      var bounds = collider3D.bounds;
      min = bounds.min;
      max = bounds.max;
      return true;
    }

    var rendererComponent = wallBoundsSource.GetComponent<Renderer>();
    if (rendererComponent != null)
    {
      var bounds = rendererComponent.bounds;
      min = bounds.min;
      max = bounds.max;
      return true;
    }

    return false;
  }

  private float ResolveProbeDistance(float baseDistance)
  {
    return Mathf.Max(baseDistance, (moveSpeed * Time.fixedDeltaTime) + 0.02f);
  }

  private void ClampSerializedValues()
  {
    moveSpeed = Mathf.Max(0.1f, moveSpeed);
    surfaceStickVelocity = Mathf.Max(0.1f, surfaceStickVelocity);
    groundCheckDistance = Mathf.Max(0.01f, groundCheckDistance);
    forwardProbeDistance = Mathf.Max(0.01f, forwardProbeDistance);
    surfaceSearchDistance = Mathf.Max(groundCheckDistance, surfaceSearchDistance);
    detachedGravityScale = Mathf.Max(0f, detachedGravityScale);
    movementStallRespawnDelay = Mathf.Max(0.1f, movementStallRespawnDelay);
    movementStallPositionEpsilon = Mathf.Max(0.0001f, movementStallPositionEpsilon);
    if (wallBoundsMin.x > wallBoundsMax.x)
    {
      (wallBoundsMin.x, wallBoundsMax.x) = (wallBoundsMax.x, wallBoundsMin.x);
    }

    if (wallBoundsMin.y > wallBoundsMax.y)
    {
      (wallBoundsMin.y, wallBoundsMax.y) = (wallBoundsMax.y, wallBoundsMin.y);
    }
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

  public void MoveStop(){
    moveSpeed = 0;
    //  _rigidbody2D.linearVelocity = Vector2.zero;
  }
}
