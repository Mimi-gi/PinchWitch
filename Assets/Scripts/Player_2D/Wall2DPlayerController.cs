using System;
using UnityEngine;

/// <summary>
/// 2D プレイヤーの自動移動を制御する。
/// 基本は左右どちらかへ進み続け、前方の低い壁はよじ登り、
/// 高い壁は反転し、足元に床がなければ落下する。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public sealed class Wall2DPlayerController : MonoBehaviour
{
  private static readonly Vector2 UpDirection = Vector2.up;
  private static readonly Vector2 DownDirection = Vector2.down;

  [Header("Movement")]
  [SerializeField] private float moveSpeed = 6f;
  [SerializeField] private bool useGravityWhileMoving = false;
  [SerializeField] private float gravityScaleWhileMoving = 0f;

  [Header("Wall Interaction")]
  [SerializeField] private LayerMask obstacleLayers = ~0;
  [SerializeField] private float forwardProbeDistance = 0.12f;
  [SerializeField] private float minimumWallNormalAlignment = 0.65f;
  [SerializeField] private float climbClearance = 0.02f;
  [SerializeField] private float overlapCheckInset = 0.02f;
  [SerializeField] private float climbSpeed = 3f;

  [Header("Floor Detection")]
  [SerializeField] private float floorProbeDistance = 0.08f;
  [SerializeField, Range(0f, 1f)] private float minimumFloorNormalY = 0.35f;
  [SerializeField] private float fallGravityScale = 1f;

  [Header("Physics")]
  [SerializeField] private CollisionDetectionMode2D collisionDetectionMode = CollisionDetectionMode2D.Continuous;
  [SerializeField] private RigidbodyInterpolation2D interpolationMode = RigidbodyInterpolation2D.Interpolate;

  [Header("Stuck Recovery")]
  [SerializeField] private float movementStallRespawnDelay = 2f;
  [SerializeField] private float movementStallPositionEpsilon = 0.01f;

  public event Action MovementStalled;

  /// <summary>
  /// 既存アニメーションとの互換用。
  /// 現在の仕様では「壁や床に沿う」概念を使っていないため、常に接地扱いを返す。
  /// </summary>
  public bool IsAttachedToSurface => true;

  /// <summary>現在、横移動する速度が設定されているか。</summary>
  public bool IsMoving => moveSpeed > Mathf.Epsilon;

  /// <summary>
  /// 既存アニメーションとの互換用。
  /// 向き合わせは行っていないので、常に上向き法線を返す。
  /// </summary>
  public Vector2 CurrentSurfaceNormal => UpDirection;

  /// <summary>アニメーションや外部参照向けの現在速度。</summary>
  public float CurrentMoveSpeed => moveSpeed;

  private readonly RaycastHit2D[] _wallHits = new RaycastHit2D[16];
  private readonly RaycastHit2D[] _floorHits = new RaycastHit2D[16];
  private readonly Collider2D[] _overlapHits = new Collider2D[8];

  private Rigidbody2D _rigidbody2D;
  private Collider2D _collider2D;
  private Vector2 _lastObservedPosition;
  private bool _hasLastObservedPosition;
  private float _movementStallDuration;
  private float _moveDirectionX = 1f;

  // よじ登り中は通常移動を止め、開始地点から目標地点へ補間移動する。
  private bool _isClimbing;
  private Vector2 _climbStartPosition;
  private Vector2 _climbTargetPosition;
  private float _climbDuration;
  private float _climbElapsed;

  private void Reset()
  {
    ClampSerializedValues();
  }

  private void Awake()
  {
    CacheRequiredComponents();
    ClampSerializedValues();
    ApplyPhysicsConfiguration();
    CacheObservedPosition();
  }

  private void OnEnable()
  {
    ResetRuntimeState();
    ResetMovementDirection();
    ApplyImmediateMovementState();
  }

  private void OnValidate()
  {
    ClampSerializedValues();

    if (!Application.isPlaying)
    {
      return;
    }

    CacheRequiredComponents();
    ApplyPhysicsConfiguration();
    ApplyImmediateMovementState();
  }

  private void OnDisable()
  {
    ResetClimbState();

    if (_rigidbody2D != null)
    {
      _rigidbody2D.linearVelocity = Vector2.zero;
    }

    _movementStallDuration = 0f;
    _hasLastObservedPosition = false;
  }

  private void FixedUpdate()
  {
    UpdateMovementStallState();

    if (TryProcessClimb())
    {
      CacheObservedPosition();
      return;
    }

    var hasFloorBelow = HasFloorBelow();
    if (hasFloorBelow)
    {
      HandleForwardObstacle();
    }

    ApplyMovement(hasFloorBelow);
    CacheObservedPosition();
  }

  private void CacheRequiredComponents()
  {
    if (_rigidbody2D == null)
    {
      _rigidbody2D = GetComponent<Rigidbody2D>();
    }

    if (_collider2D == null)
    {
      _collider2D = GetComponent<Collider2D>();
    }
  }

  private void ResetRuntimeState()
  {
    ResetClimbState();
    ResetStuckTracking();
  }

  private bool TryProcessClimb()
  {
    if (!_isClimbing)
    {
      return false;
    }

    UpdateClimbMovement();
    return true;
  }

  private void UpdateMovementStallState()
  {
    if (_rigidbody2D == null)
    {
      ResetStuckTracking();
      return;
    }

    // 停止中やよじ登り中は「詰まり」とはみなさない。
    if (!IsMoving || _isClimbing)
    {
      ResetObservedPositionOnly();
      _movementStallDuration = 0f;
      return;
    }

    if (!_hasLastObservedPosition)
    {
      ResetObservedPositionOnly();
      return;
    }

    var currentPosition = _rigidbody2D.position;
    var positionDelta = currentPosition - _lastObservedPosition;
    var isAlmostStationary = positionDelta.sqrMagnitude
      <= movementStallPositionEpsilon * movementStallPositionEpsilon;

    if (isAlmostStationary)
    {
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

  private void HandleForwardObstacle()
  {
    if (_collider2D == null || _rigidbody2D == null || !IsMoving)
    {
      return;
    }

    if (!TryGetForwardWallHit(out var wallHit))
    {
      return;
    }

    var requiredClimbHeight = GetRequiredClimbHeight(wallHit);
    if (CanClimbWall(requiredClimbHeight) && TryStartClimb(requiredClimbHeight))
    {
      ResetStuckTracking();
      return;
    }

    ReverseDirection();
  }

  private bool TryGetForwardWallHit(out RaycastHit2D bestHit)
  {
    bestHit = default;

    if (_collider2D == null)
    {
      return false;
    }

    var moveDirection = GetHorizontalMoveDirection();
    var probeDistance = Mathf.Max(forwardProbeDistance, (moveSpeed * Time.fixedDeltaTime) + climbClearance);
    var contactFilter = CreateObstacleContactFilter();
    var hitCount = _collider2D.Cast(moveDirection, contactFilter, _wallHits, probeDistance);

    var bestDistance = float.PositiveInfinity;
    var found = false;
    for (var i = 0; i < hitCount; i++)
    {
      var hit = _wallHits[i];
      if (hit.collider == null)
      {
        continue;
      }

      // 真横の壁だけを拾いたいので、法線が進行方向と十分向き合っているものだけ採用する。
      if (Vector2.Dot(hit.normal, -moveDirection) < minimumWallNormalAlignment)
      {
        continue;
      }

      if (hit.distance >= bestDistance)
      {
        continue;
      }

      bestDistance = hit.distance;
      bestHit = hit;
      found = true;
    }

    return found;
  }

  private float GetRequiredClimbHeight(RaycastHit2D wallHit)
  {
    var playerBounds = _collider2D.bounds;
    var wallTopY = wallHit.collider.bounds.max.y;
    return wallTopY - playerBounds.min.y;
  }

  private bool CanClimbWall(float requiredClimbHeight)
  {
    var playerHeight = _collider2D.bounds.size.y;
    return requiredClimbHeight <= playerHeight;
  }

  private bool TryStartClimb(float climbHeight)
  {
    if (_rigidbody2D == null || _collider2D == null)
    {
      return false;
    }

    var climbOffsetY = Mathf.Max(0f, climbHeight) + climbClearance;
    var targetPosition = _rigidbody2D.position + (UpDirection * climbOffsetY);
    if (HasBlockingOverlapAt(targetPosition))
    {
      return false;
    }

    BeginClimb(targetPosition);
    return true;
  }

  private bool HasBlockingOverlapAt(Vector2 targetPosition)
  {
    var currentBounds = _collider2D.bounds;
    var positionOffset = targetPosition - _rigidbody2D.position;
    var overlapCheckSize = new Vector2(
      Mathf.Max(0.01f, currentBounds.size.x - (overlapCheckInset * 2f)),
      Mathf.Max(0.01f, currentBounds.size.y - (overlapCheckInset * 2f)));
    var overlapCheckCenter = (Vector2)currentBounds.center + positionOffset;

    var contactFilter = CreateObstacleContactFilter();
    var overlapCount = Physics2D.OverlapBox(
      overlapCheckCenter,
      overlapCheckSize,
      0f,
      contactFilter,
      _overlapHits);

    for (var i = 0; i < overlapCount; i++)
    {
      var overlap = _overlapHits[i];
      if (overlap == null || overlap == _collider2D)
      {
        continue;
      }

      return true;
    }

    return false;
  }

  private void BeginClimb(Vector2 targetPosition)
  {
    if (_rigidbody2D == null)
    {
      return;
    }

    _isClimbing = true;
    _climbStartPosition = _rigidbody2D.position;
    _climbTargetPosition = targetPosition;
    _climbElapsed = 0f;

    var climbDistance = Vector2.Distance(_climbStartPosition, _climbTargetPosition);
    _climbDuration = climbDistance <= Mathf.Epsilon
      ? 0f
      : Mathf.Max(Time.fixedDeltaTime, climbDistance / climbSpeed);

    // 登っている最中に物理挙動が混ざるとガタつくので、速度と重力を一旦止める。
    _rigidbody2D.gravityScale = 0f;
    _rigidbody2D.linearVelocity = Vector2.zero;
  }

  private void UpdateClimbMovement()
  {
    if (_rigidbody2D == null)
    {
      ResetClimbState();
      return;
    }

    if (_climbDuration <= Mathf.Epsilon)
    {
      _rigidbody2D.MovePosition(_climbTargetPosition);
      CompleteClimb();
      return;
    }

    _climbElapsed += Time.fixedDeltaTime;
    var normalizedTime = Mathf.Clamp01(_climbElapsed / _climbDuration);
    var easedTime = Mathf.SmoothStep(0f, 1f, normalizedTime);
    var nextPosition = Vector2.Lerp(_climbStartPosition, _climbTargetPosition, easedTime);

    _rigidbody2D.gravityScale = 0f;
    _rigidbody2D.linearVelocity = Vector2.zero;
    _rigidbody2D.MovePosition(nextPosition);

    if (normalizedTime >= 1f)
    {
      CompleteClimb();
    }
  }

  private void CompleteClimb()
  {
    if (_rigidbody2D != null)
    {
      _rigidbody2D.position = _climbTargetPosition;
      Physics2D.SyncTransforms();
    }

    ResetClimbState();
    ResetStuckTracking();
  }

  private void ResetClimbState()
  {
    _isClimbing = false;
    _climbElapsed = 0f;
    _climbDuration = 0f;
    _climbStartPosition = Vector2.zero;
    _climbTargetPosition = Vector2.zero;
  }

  private void ReverseDirection()
  {
    _moveDirectionX *= -1f;

    if (_rigidbody2D != null)
    {
      var velocity = _rigidbody2D.linearVelocity;
      velocity.x = moveSpeed * _moveDirectionX;
      _rigidbody2D.linearVelocity = velocity;
    }

    ResetStuckTracking();
  }

  private bool HasFloorBelow()
  {
    if (_collider2D == null)
    {
      return false;
    }

    var contactFilter = CreateObstacleContactFilter();
    var probeDistance = Mathf.Max(0.01f, floorProbeDistance);
    var hitCount = _collider2D.Cast(DownDirection, contactFilter, _floorHits, probeDistance);

    for (var i = 0; i < hitCount; i++)
    {
      var hit = _floorHits[i];
      if (hit.collider == null)
      {
        continue;
      }

      // 緩い斜面は床として扱いたいので、法線の Y 成分で判定する。
      if (hit.normal.y >= minimumFloorNormalY)
      {
        return true;
      }
    }

    return false;
  }

  private void ApplyMovement(bool hasFloorBelow)
  {
    if (_rigidbody2D == null)
    {
      return;
    }

    _rigidbody2D.gravityScale = GetCurrentGravityScale(hasFloorBelow);

    var velocity = _rigidbody2D.linearVelocity;
    velocity.x = moveSpeed * _moveDirectionX;

    // 地面上では縦速度を殺して水平移動を優先し、床がないときだけ自然落下させる。
    if (hasFloorBelow && !useGravityWhileMoving)
    {
      velocity.y = 0f;
    }

    _rigidbody2D.linearVelocity = velocity;
    _rigidbody2D.MoveRotation(0f);
  }

  private void ApplyImmediateMovementState()
  {
    if (_rigidbody2D == null)
    {
      return;
    }

    _rigidbody2D.gravityScale = GetCurrentGravityScale(HasFloorBelow());
    _rigidbody2D.rotation = 0f;
  }

  private float GetCurrentGravityScale(bool hasFloorBelow)
  {
    if (!hasFloorBelow)
    {
      return fallGravityScale;
    }

    return useGravityWhileMoving ? gravityScaleWhileMoving : 0f;
  }

  private Vector2 GetHorizontalMoveDirection()
  {
    return new Vector2(Mathf.Sign(_moveDirectionX), 0f);
  }

  private ContactFilter2D CreateObstacleContactFilter()
  {
    return Wall2DContactCastUtility.CreateContactFilter(obstacleLayers);
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

  private void ResetObservedPositionOnly()
  {
    if (_rigidbody2D == null)
    {
      _hasLastObservedPosition = false;
      return;
    }

    _lastObservedPosition = _rigidbody2D.position;
    _hasLastObservedPosition = true;
  }

  private void ResetStuckTracking()
  {
    _movementStallDuration = 0f;
    ResetObservedPositionOnly();
  }

  private void ClampSerializedValues()
  {
    moveSpeed = Mathf.Max(0f, moveSpeed);
    gravityScaleWhileMoving = Mathf.Max(0f, gravityScaleWhileMoving);
    forwardProbeDistance = Mathf.Max(0.01f, forwardProbeDistance);
    minimumWallNormalAlignment = Mathf.Clamp(minimumWallNormalAlignment, 0f, 1f);
    climbClearance = Mathf.Max(0f, climbClearance);
    overlapCheckInset = Mathf.Max(0f, overlapCheckInset);
    climbSpeed = Mathf.Max(0.1f, climbSpeed);
    floorProbeDistance = Mathf.Max(0.01f, floorProbeDistance);
    minimumFloorNormalY = Mathf.Clamp01(minimumFloorNormalY);
    fallGravityScale = Mathf.Max(0f, fallGravityScale);
    movementStallRespawnDelay = Mathf.Max(0.1f, movementStallRespawnDelay);
    movementStallPositionEpsilon = Mathf.Max(0.0001f, movementStallPositionEpsilon);
  }

  private void ApplyPhysicsConfiguration()
  {
    if (_rigidbody2D == null)
    {
      return;
    }

    _rigidbody2D.collisionDetectionMode = collisionDetectionMode;
    _rigidbody2D.interpolation = interpolationMode;
    _rigidbody2D.freezeRotation = true;
  }

  /// <summary>
  /// ゴール演出などで移動を止めたいときに呼ぶ。
  /// よじ登り中でも安全に停止できるよう、まず登り状態を解除する。
  /// </summary>
  public void MoveStop()
  {
    ResetClimbState();
    moveSpeed = 0f;

    if (_rigidbody2D == null)
    {
      return;
    }

    var velocity = _rigidbody2D.linearVelocity;
    velocity.x = 0f;
    if (!useGravityWhileMoving)
    {
      velocity.y = 0f;
    }

    _rigidbody2D.linearVelocity = velocity;
  }

  /// <summary>
  /// リスポーン直後などに進行方向を初期値へ戻す。
  /// 現在の仕様では「右向きスタート」を初期方向としている。
  /// </summary>
  public void ResetMovementDirection()
  {
    ResetClimbState();
    _moveDirectionX = 1f;

    if (_rigidbody2D == null)
    {
      return;
    }

    var velocity = _rigidbody2D.linearVelocity;
    velocity.x = Mathf.Abs(moveSpeed);
    _rigidbody2D.linearVelocity = velocity;
  }
}
