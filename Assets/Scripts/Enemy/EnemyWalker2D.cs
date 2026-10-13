using UnityEngine;

// 地形の上を歩く敵(トランプ兵)。Kinematic Rigidbody2D + Collider2D.Cast で接地・壁・崖を自前判定する。
// - 壁に当たったら反転
// - 崖: TurnAtLedge(赤)は手前で反転、FallOff(緑)はそのまま落ちる
// - 2D 世界の枠(既定では "Wall" オブジェクトの bounds。プレイヤーと同じ規約)の端で反転
// - 枠の下へ落ち切ったら非アクティブ化(R リセットで復帰)
// 当たり判定(Collider2D)は Trigger にしておくこと。プレイヤーが壁として扱わず、接触死は EnemyTouchRespawn2D が担う。
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public sealed class EnemyWalker2D : MonoBehaviour, IEnemyMover, IRuntimeResettable
{
    public enum LedgeBehavior
    {
        TurnAtLedge,
        FallOff,
    }

    private const string PlayerLayerName = "Player";
    private const float SkinWidth = 0.01f;
    private const float PenetrationTolerance = 0.02f;
    private const string WorldBoundsObjectName = "Wall";

    [Header("Movement")]
    [SerializeField] private LedgeBehavior ledgeBehavior = LedgeBehavior.TurnAtLedge;
    [SerializeField] private float moveSpeed = 1.5f;
    [SerializeField] private bool startMovingRight;

    [Header("Gravity")]
    [SerializeField] private float gravity = 20f;
    [SerializeField] private float maxFallSpeed = 12f;

    [Header("Probes")]
    [Tooltip("地形として扱うレイヤー。Player レイヤーと自分自身のレイヤーは実行時に除外される。")]
    [SerializeField] private LayerMask groundLayers = ~0;
    [SerializeField] private float floorProbeDistance = 0.05f;
    [SerializeField, Range(0f, 1f)] private float minimumFloorNormalY = 0.35f;
    [SerializeField, Range(0f, 1f)] private float minimumWallNormalAlignment = 0.65f;
    [Tooltip("崖判定で、進行方向の足元をどれだけ先まで見るか。")]
    [SerializeField] private float ledgeProbeForward = 0.05f;
    [Tooltip("崖判定で、足元からどれだけ下まで床を探すか。これより深い段差は崖とみなす。")]
    [SerializeField] private float ledgeProbeDepth = 0.3f;

    [Header("World Bounds")]
    [SerializeField] private bool autoFindWorldBounds = true;
    [SerializeField] private Transform worldBoundsSource;
    [Tooltip("枠の下端からこの距離だけ下に落ちたら非アクティブにする。")]
    [SerializeField] private float despawnMarginBelowBounds = 2f;

    [Header("Shadow")]
    [SerializeField] private bool stopWhileShadowCrushed = true;
    [SerializeField] private Wall2DPlayerCrushDetector shadowCrushDetector;

    [Header("Debug")]
    [SerializeField] private bool logTurns;

    private readonly RaycastHit2D[] _hits = new RaycastHit2D[16];
    private readonly System.Collections.Generic.List<Collider2D> _overlaps = new System.Collections.Generic.List<Collider2D>();
    private Rigidbody2D _rigidbody2D;
    private Collider2D _collider2D;
    private ContactFilter2D _contactFilter;
    private float _direction = -1f;
    private float _verticalVelocity;
    private float _externalSpeedMultiplier = 1f;
    private bool _isGrounded;
    private bool _hasWorldBounds;
    private Bounds _worldBounds;
    private Vector3 _initialPosition;
    private bool _initialActive;

    public int ResetOrder => 100;
    public LedgeBehavior Ledge => ledgeBehavior;
    public bool IsGrounded => _isGrounded;
    public float Direction => _direction;

    public void SetExternalSpeedMultiplier(float multiplier)
    {
        _externalSpeedMultiplier = Mathf.Max(0f, multiplier);
    }

    private void Reset()
    {
        CacheComponents();
        ConfigureRigidbody();
    }

    private void Awake()
    {
        _initialPosition = transform.position;
        _initialActive = gameObject.activeSelf;
        _direction = startMovingRight ? 1f : -1f;

        CacheComponents();
        ConfigureRigidbody();
        BuildContactFilter();
        RefreshWorldBounds();
    }

    private void OnValidate()
    {
        moveSpeed = Mathf.Max(0f, moveSpeed);
        gravity = Mathf.Max(0f, gravity);
        maxFallSpeed = Mathf.Max(0f, maxFallSpeed);
        floorProbeDistance = Mathf.Max(0.001f, floorProbeDistance);
        ledgeProbeForward = Mathf.Max(0f, ledgeProbeForward);
        ledgeProbeDepth = Mathf.Max(0.01f, ledgeProbeDepth);
    }

    private void FixedUpdate()
    {
        if (_collider2D == null || _rigidbody2D == null)
        {
            return;
        }

        if (stopWhileShadowCrushed && shadowCrushDetector != null && shadowCrushDetector.IsCrushed)
        {
            return;
        }

        var deltaTime = Time.fixedDeltaTime;
        var position = _rigidbody2D.position + ResolvePenetration();

        position.y += ResolveVerticalMovement(deltaTime);
        position.x += ResolveHorizontalMovement(deltaTime);

        _rigidbody2D.MovePosition(position);

        if (_hasWorldBounds && _collider2D.bounds.max.y < _worldBounds.min.y - despawnMarginBelowBounds)
        {
            gameObject.SetActive(false);
        }
    }

    public void ResetRuntimeState()
    {
        gameObject.SetActive(_initialActive);
        transform.position = _initialPosition;
        if (_rigidbody2D != null)
        {
            _rigidbody2D.position = _initialPosition;
            _rigidbody2D.linearVelocity = Vector2.zero;
        }

        _direction = startMovingRight ? 1f : -1f;
        _verticalVelocity = 0f;
        _externalSpeedMultiplier = 1f;
        _isGrounded = false;
    }

    // 地形にめり込んでいたら押し出す(配置ミスや足場の生成でめり込んだ場合の保険)。
    // めり込んだまま Cast すると距離 0 の当たりしか返らず、壁と誤判定して往復し続けるため。
    private Vector2 ResolvePenetration()
    {
        _overlaps.Clear();
        if (_collider2D.Overlap(_contactFilter, _overlaps) == 0)
        {
            return Vector2.zero;
        }

        var correction = Vector2.zero;
        foreach (var other in _overlaps)
        {
            var separation = _collider2D.Distance(other);
            if (separation.isValid && separation.isOverlapped && separation.distance < -PenetrationTolerance)
            {
                // normal は自分→相手の向き、distance は負なので、この積で相手から離れる方向へ押し出せる。
                var push = separation.normal * separation.distance;
                if (push.sqrMagnitude > correction.sqrMagnitude)
                {
                    correction = push;
                }
            }
        }

        return correction;
    }

    // 接地していれば床に吸着、していなければ重力で落下させ、床に当たったらそこで止める。
    private float ResolveVerticalMovement(float deltaTime)
    {
        if (TryCast(Vector2.down, floorProbeDistance + SkinWidth, out var floorHit, IsFloorHit))
        {
            _isGrounded = true;
            _verticalVelocity = 0f;
            return -Mathf.Max(0f, floorHit.distance - SkinWidth);
        }

        _isGrounded = false;
        _verticalVelocity = Mathf.Max(_verticalVelocity - gravity * deltaTime, -maxFallSpeed);
        var fallDistance = -_verticalVelocity * deltaTime;
        if (TryCast(Vector2.down, fallDistance + SkinWidth, out var landingHit, IsFloorHit))
        {
            _isGrounded = true;
            _verticalVelocity = 0f;
            return -Mathf.Max(0f, landingHit.distance - SkinWidth);
        }

        return -fallDistance;
    }

    private float ResolveHorizontalMovement(float deltaTime)
    {
        var speed = moveSpeed * _externalSpeedMultiplier;
        if (speed <= 0f)
        {
            return 0f;
        }

        var step = speed * deltaTime;
        if (ShouldTurn(step))
        {
            _direction = -_direction;
            if (ShouldTurn(step))
            {
                // 両側とも進めない(穴の底など)ときはその場で待つ。
                return 0f;
            }
        }

        return _direction * step;
    }

    private bool ShouldTurn(float step)
    {
        var moveDirection = new Vector2(_direction, 0f);

        if (TryCast(moveDirection, step + SkinWidth, out var wallHit, IsWallHit))
        {
            LogTurn($"wall '{wallHit.collider.name}'");
            return true;
        }

        if (_isGrounded && ledgeBehavior == LedgeBehavior.TurnAtLedge && !HasFloorAhead())
        {
            LogTurn("ledge");
            return true;
        }

        if (IsLeavingWorldBounds(step))
        {
            LogTurn("world bounds");
            return true;
        }

        return false;
    }

    private void LogTurn(string reason)
    {
        if (logTurns)
        {
            Debug.Log($"[EnemyWalker] {name} turn at {_rigidbody2D.position:F2} dir={_direction} reason={reason}", this);
        }
    }

    private bool HasFloorAhead()
    {
        var bounds = _collider2D.bounds;
        var origin = new Vector2(
            bounds.center.x + _direction * (bounds.extents.x + ledgeProbeForward),
            bounds.min.y + SkinWidth);
        var hitCount = Physics2D.Raycast(origin, Vector2.down, _contactFilter, _hits, ledgeProbeDepth + SkinWidth);
        for (var i = 0; i < hitCount; i++)
        {
            if (_hits[i].collider != null && _hits[i].collider != _collider2D)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsLeavingWorldBounds(float step)
    {
        if (!_hasWorldBounds)
        {
            return false;
        }

        var bounds = _collider2D.bounds;
        return _direction > 0f
            ? bounds.max.x + step > _worldBounds.max.x
            : bounds.min.x - step < _worldBounds.min.x;
    }

    private bool TryCast(Vector2 direction, float distance, out RaycastHit2D bestHit, System.Func<RaycastHit2D, Vector2, bool> predicate)
    {
        bestHit = default;
        var hitCount = _collider2D.Cast(direction, _contactFilter, _hits, distance);
        var bestDistance = float.PositiveInfinity;
        for (var i = 0; i < hitCount; i++)
        {
            var hit = _hits[i];
            if (hit.collider == null || hit.distance >= bestDistance || !predicate(hit, direction))
            {
                continue;
            }

            bestDistance = hit.distance;
            bestHit = hit;
        }

        return bestDistance < float.PositiveInfinity;
    }

    private bool IsFloorHit(RaycastHit2D hit, Vector2 direction)
    {
        return hit.normal.y >= minimumFloorNormalY;
    }

    private bool IsWallHit(RaycastHit2D hit, Vector2 direction)
    {
        return hit.normal.y < minimumFloorNormalY && Vector2.Dot(hit.normal, -direction) >= minimumWallNormalAlignment;
    }

    private void CacheComponents()
    {
        if (_rigidbody2D == null) _rigidbody2D = GetComponent<Rigidbody2D>();
        if (_collider2D == null) _collider2D = GetComponent<Collider2D>();
        if (shadowCrushDetector == null) shadowCrushDetector = GetComponent<Wall2DPlayerCrushDetector>();
    }

    private void ConfigureRigidbody()
    {
        if (_rigidbody2D == null)
        {
            return;
        }

        _rigidbody2D.bodyType = RigidbodyType2D.Kinematic;
        _rigidbody2D.interpolation = RigidbodyInterpolation2D.Interpolate;
        _rigidbody2D.freezeRotation = true;
    }

    private void BuildContactFilter()
    {
        int mask = groundLayers;
        var playerLayer = LayerMask.NameToLayer(PlayerLayerName);
        if (playerLayer >= 0)
        {
            mask &= ~(1 << playerLayer);
        }

        // 敵同士はすり抜ける(敵は Trigger なので通常は当たらないが、念のため自レイヤーも外す)。
        mask &= ~(1 << gameObject.layer);
        _contactFilter = Wall2DContactCastUtility.CreateContactFilter(mask);
    }

    private void RefreshWorldBounds()
    {
        if (autoFindWorldBounds && worldBoundsSource == null)
        {
            worldBoundsSource = FindWorldBoundsObject();
        }

        _hasWorldBounds = TryGetBounds(worldBoundsSource, out _worldBounds);
    }

    // 2D 世界の枠は "Wall" という名前のメッシュで置かれている(プレイヤーと同じ規約)。
    // 既存ステージでは非アクティブのまま置かれているため、非アクティブも含めて探す。
    private Transform FindWorldBoundsObject()
    {
        foreach (var meshFilter in FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (meshFilter.name == WorldBoundsObjectName && meshFilter.gameObject.scene == gameObject.scene && meshFilter.sharedMesh != null)
            {
                return meshFilter.transform;
            }
        }

        var wallObject = GameObject.Find(WorldBoundsObjectName);
        return wallObject != null ? wallObject.transform : null;
    }

    private static bool TryGetBounds(Transform source, out Bounds bounds)
    {
        bounds = default;
        if (source == null)
        {
            return false;
        }

        // 非アクティブでも正しく取れるよう、メッシュがあればメッシュの bounds をワールドへ変換して使う。
        if (source.TryGetComponent<MeshFilter>(out var meshFilter) && meshFilter.sharedMesh != null)
        {
            bounds = TransformBounds(source, meshFilter.sharedMesh.bounds);
            return true;
        }

        if (source.TryGetComponent<Collider2D>(out var collider2D))
        {
            bounds = collider2D.bounds;
            return true;
        }

        if (source.TryGetComponent<Collider>(out var collider3D))
        {
            bounds = collider3D.bounds;
            return true;
        }

        if (source.TryGetComponent<Renderer>(out var rendererComponent))
        {
            bounds = rendererComponent.bounds;
            return true;
        }

        return false;
    }

    private static Bounds TransformBounds(Transform source, Bounds localBounds)
    {
        var center = localBounds.center;
        var extents = localBounds.extents;
        var worldBounds = new Bounds(source.TransformPoint(center), Vector3.zero);
        for (var i = 0; i < 8; i++)
        {
            var corner = center + Vector3.Scale(extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
            worldBounds.Encapsulate(source.TransformPoint(corner));
        }

        return worldBounds;
    }

    private void OnDrawGizmosSelected()
    {
        var targetCollider = _collider2D != null ? _collider2D : GetComponent<Collider2D>();
        if (targetCollider == null)
        {
            return;
        }

        var bounds = targetCollider.bounds;
        var direction = Application.isPlaying ? _direction : (startMovingRight ? 1f : -1f);
        var foot = new Vector3(bounds.center.x + direction * (bounds.extents.x + ledgeProbeForward), bounds.min.y, bounds.center.z);

        Gizmos.color = ledgeBehavior == LedgeBehavior.TurnAtLedge ? Color.red : Color.green;
        Gizmos.DrawLine(bounds.center, bounds.center + new Vector3(direction * (bounds.extents.x + 0.4f), 0f, 0f));
        Gizmos.DrawLine(foot, foot + Vector3.down * ledgeProbeDepth);
    }
}
