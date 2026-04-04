using UnityEngine;

/// <summary>
/// 2Dプレイヤーの基本操作を担当するスクリプト。
/// 左右移動、ジャンプ、接地判定をまとめて管理する。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public sealed class Wall2DPlayerController : MonoBehaviour
{
  [Header("Movement")]
  [SerializeField] private float moveSpeed = 6f;
  [SerializeField] private float airControlMultiplier = 0.8f;

  [Header("Jump")]
  [SerializeField] private float jumpVelocity = 11f;
  [SerializeField] private KeyCode jumpKey = KeyCode.W;
  [SerializeField] private KeyCode alternateJumpKey = KeyCode.UpArrow;

  [Header("Ground Check")]
  [SerializeField] private LayerMask groundLayers = ~0;
  [SerializeField] private float groundCheckDistance = 0.08f;
  [SerializeField] private float minimumGroundNormalY = 0.65f;

  [Header("Physics")]
  [SerializeField] private CollisionDetectionMode2D collisionDetectionMode = CollisionDetectionMode2D.Continuous;
  [SerializeField] private RigidbodyInterpolation2D interpolationMode = RigidbodyInterpolation2D.Interpolate;

  private readonly RaycastHit2D[] _groundHits = new RaycastHit2D[8];

  private Rigidbody2D _rigidbody2D;
  private Collider2D _collider2D;
  private float _moveInput;
  private bool _jumpRequested;
  private bool _isGrounded;

  private void Reset()
  {
    ClampSerializedValues();
  }

  private void Awake()
  {
    // 毎フレーム使う参照は最初に確保しておく。
    _rigidbody2D = GetComponent<Rigidbody2D>();
    _collider2D = GetComponent<Collider2D>();
    ClampSerializedValues();
    ApplyPhysicsConfiguration();
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

    ApplyPhysicsConfiguration();
  }

  private void OnDisable()
  {
    // 3D表示中に前フレームの入力が残らないようにする。
    _moveInput = 0f;
    _jumpRequested = false;
  }

  private void Update()
  {
    // 入力は Update で受け取り、物理反映は FixedUpdate に任せる。
    _moveInput = GetHorizontalInput();

    // ジャンプ入力は 1 フレームだけ保持し、FixedUpdate 側で消費する。
    if (Input.GetKeyDown(jumpKey) || Input.GetKeyDown(alternateJumpKey))
    {
      _jumpRequested = true;
    }
  }

  private void FixedUpdate()
  {
    // 移動前に接地状態を更新する。
    UpdateGroundedState();

    var velocity = _rigidbody2D.linearVelocity;
    var horizontalMultiplier = _isGrounded ? 1f : airControlMultiplier;
    velocity.x = _moveInput * moveSpeed * horizontalMultiplier;

    if (_jumpRequested && _isGrounded)
    {
      // 接地中だけ Y 速度を上書きしてジャンプ開始にする。
      velocity.y = jumpVelocity;
      _isGrounded = false;
    }

    _rigidbody2D.linearVelocity = velocity;
    _jumpRequested = false;
  }

  private float GetHorizontalInput()
  {
    // Unity の Horizontal 軸を基準にしつつ、明示的なキー入力も拾う。
    var input = Input.GetAxisRaw("Horizontal");

    if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
    {
      input = -1f;
    }
    else if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
    {
      input = 1f;
    }

    return Mathf.Clamp(input, -1f, 1f);
  }

  private void UpdateGroundedState()
  {
    if (_collider2D == null)
    {
      _isGrounded = false;
      return;
    }

    // プレイヤーのコライダーを少し下に Cast し、
    // 上向き法線を持つ接触があれば地面に立っているとみなす。
    var contactFilter = Wall2DContactCastUtility.CreateContactFilter(groundLayers);
    _isGrounded = Wall2DContactCastUtility.HasHitWithMinimumNormalY(
      _collider2D,
      Vector2.down,
      contactFilter,
      _groundHits,
      groundCheckDistance,
      minimumGroundNormalY);
  }

  private void ClampSerializedValues()
  {
    // Inspector から極端な値が入っても破綻しにくいよう補正する。
    moveSpeed = Mathf.Max(0.1f, moveSpeed);
    airControlMultiplier = Mathf.Clamp(airControlMultiplier, 0f, 1f);
    jumpVelocity = Mathf.Max(0.1f, jumpVelocity);
    groundCheckDistance = Mathf.Max(0.01f, groundCheckDistance);
    minimumGroundNormalY = Mathf.Clamp(minimumGroundNormalY, 0f, 1f);
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
