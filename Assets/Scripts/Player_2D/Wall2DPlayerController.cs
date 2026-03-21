using System;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public sealed class Wall2DPlayerController : MonoBehaviour
{
  // 外部クラスはこのイベントを購読して、押しつぶし発生時の処理を実行できる。
  public event Action Crushed;

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

  [Header("Crush Check")]
  [SerializeField] private LayerMask crushLayers = ~0;
  [SerializeField] private float crushCheckDistance = 0.04f;
  [SerializeField] private float minimumCrushContactAlignment = 0.65f;

  private readonly RaycastHit2D[] _groundHits = new RaycastHit2D[8];
  private readonly RaycastHit2D[] _crushHits = new RaycastHit2D[8];

  private Rigidbody2D _rigidbody2D;
  private Collider2D _collider2D;
  private float _moveInput;
  private bool _jumpRequested;
  private bool _isGrounded;
  private bool _isCrushed;

  private void Reset()
  {
    moveSpeed = Mathf.Max(0.1f, moveSpeed);
    airControlMultiplier = Mathf.Clamp(airControlMultiplier, 0f, 1f);
    jumpVelocity = Mathf.Max(0.1f, jumpVelocity);
    groundCheckDistance = Mathf.Max(0.01f, groundCheckDistance);
    minimumGroundNormalY = Mathf.Clamp(minimumGroundNormalY, 0f, 1f);
    crushCheckDistance = Mathf.Max(0.01f, crushCheckDistance);
    minimumCrushContactAlignment = Mathf.Clamp(minimumCrushContactAlignment, 0f, 1f);
  }

  private void Awake()
  {
    // 必須コンポーネントを初期化時に取得する。
    _rigidbody2D = GetComponent<Rigidbody2D>();
    _collider2D = GetComponent<Collider2D>();
    ClampSerializedValues();
  }

  private void OnValidate()
  {
    ClampSerializedValues();
  }

  private void Update()
  {
    // 入力取得は毎フレーム行う。
    _moveInput = GetHorizontalInput();

    // ジャンプ要求だけを記録し、実際の速度変更はFixedUpdateで行う。
    if (Input.GetKeyDown(jumpKey) || Input.GetKeyDown(alternateJumpKey))
    {
      _jumpRequested = true;
    }
  }

  private void FixedUpdate()
  {
    // 物理更新タイミングで接地状態と圧死状態を判定する。
    UpdateGroundedState();
    UpdateCrushState();

    var velocity = _rigidbody2D.linearVelocity;
    var horizontalMultiplier = _isGrounded ? 1f : airControlMultiplier;
    velocity.x = _moveInput * moveSpeed * horizontalMultiplier;

    if (_jumpRequested && _isGrounded)
    {
      // 接地時のみ上向き速度を与えてジャンプさせる。
      velocity.y = jumpVelocity;
      _isGrounded = false;
    }

    _rigidbody2D.linearVelocity = velocity;
    _jumpRequested = false;
  }

  private float GetHorizontalInput()
  {
    // まず標準のHorizontal軸を読む。
    var input = Input.GetAxisRaw("Horizontal");

    // キーボード入力を優先して明示的に上書きする。
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

    // Collider2DのCastで足元方向に形状判定し、法線のY成分で地面らしさを確認する。
    var contactFilter = new ContactFilter2D
    {
      useLayerMask = true,
      layerMask = groundLayers,
      useTriggers = false,
    };

    var hitCount = _collider2D.Cast(Vector2.down, contactFilter, _groundHits, groundCheckDistance);
    _isGrounded = false;

    for (var i = 0; i < hitCount; i++)
    {
      var hit = _groundHits[i];
      if (hit.collider == null)
      {
        continue;
      }

      if (hit.normal.y >= minimumGroundNormalY)
      {
        _isGrounded = true;
        return;
      }
    }
  }

  private void UpdateCrushState()
  {
    if (_collider2D == null)
    {
      _isCrushed = false;
      return;
    }

    // 左右または上下の両側が同時に塞がれたら押しつぶしとみなす。
    var contactFilter = new ContactFilter2D
    {
      useLayerMask = true,
      layerMask = crushLayers,
      useTriggers = false,
    };

    var crushedHorizontally = HasBlockingHit(Vector2.left, contactFilter) && HasBlockingHit(Vector2.right, contactFilter);
    var crushedVertically = HasBlockingHit(Vector2.up, contactFilter) && HasBlockingHit(Vector2.down, contactFilter);
    var isCrushedNow = crushedHorizontally || crushedVertically;

    if (isCrushedNow && !_isCrushed)
    {
      // 状態遷移の瞬間だけイベントを発火する。
      _isCrushed = true;
      Crushed?.Invoke();
      return;
    }

    _isCrushed = isCrushedNow;
  }

  private bool HasBlockingHit(Vector2 direction, ContactFilter2D contactFilter)
  {
    // 判定方向の反対向き法線を持つ接触を「壁に当たっている」と判断する。
    var hitCount = _collider2D.Cast(direction, contactFilter, _crushHits, crushCheckDistance);
    var expectedNormal = -direction.normalized;

    for (var i = 0; i < hitCount; i++)
    {
      var hit = _crushHits[i];
      if (hit.collider == null)
      {
        continue;
      }

      if (Vector2.Dot(hit.normal, expectedNormal) >= minimumCrushContactAlignment)
      {
        return true;
      }
    }

    return false;
  }

  private void ClampSerializedValues()
  {
    // インスペクタ設定値の安全域を保証する。
    moveSpeed = Mathf.Max(0.1f, moveSpeed);
    airControlMultiplier = Mathf.Clamp(airControlMultiplier, 0f, 1f);
    jumpVelocity = Mathf.Max(0.1f, jumpVelocity);
    groundCheckDistance = Mathf.Max(0.01f, groundCheckDistance);
    minimumGroundNormalY = Mathf.Clamp(minimumGroundNormalY, 0f, 1f);
    crushCheckDistance = Mathf.Max(0.01f, crushCheckDistance);
    minimumCrushContactAlignment = Mathf.Clamp(minimumCrushContactAlignment, 0f, 1f);
  }
}
