using UnityEngine;

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

  private readonly RaycastHit2D[] _groundHits = new RaycastHit2D[8];

  private Rigidbody2D _rigidbody2D;
  private Collider2D _collider2D;
  private float _moveInput;
  private bool _jumpRequested;
  private bool _isGrounded;

  private void Reset()
  {
    moveSpeed = Mathf.Max(0.1f, moveSpeed);
    airControlMultiplier = Mathf.Clamp(airControlMultiplier, 0f, 1f);
    jumpVelocity = Mathf.Max(0.1f, jumpVelocity);
    groundCheckDistance = Mathf.Max(0.01f, groundCheckDistance);
    minimumGroundNormalY = Mathf.Clamp(minimumGroundNormalY, 0f, 1f);
  }

  private void Awake()
  {
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
    _moveInput = GetHorizontalInput();

    if (Input.GetKeyDown(jumpKey) || Input.GetKeyDown(alternateJumpKey))
    {
      _jumpRequested = true;
    }
  }

  private void FixedUpdate()
  {
    UpdateGroundedState();

    var velocity = _rigidbody2D.linearVelocity;
    var horizontalMultiplier = _isGrounded ? 1f : airControlMultiplier;
    velocity.x = _moveInput * moveSpeed * horizontalMultiplier;

    if (_jumpRequested && _isGrounded)
    {
      velocity.y = jumpVelocity;
      _isGrounded = false;
    }

    _rigidbody2D.linearVelocity = velocity;
    _jumpRequested = false;
  }

  private float GetHorizontalInput()
  {
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

  private void ClampSerializedValues()
  {
    moveSpeed = Mathf.Max(0.1f, moveSpeed);
    airControlMultiplier = Mathf.Clamp(airControlMultiplier, 0f, 1f);
    jumpVelocity = Mathf.Max(0.1f, jumpVelocity);
    groundCheckDistance = Mathf.Max(0.01f, groundCheckDistance);
    minimumGroundNormalY = Mathf.Clamp(minimumGroundNormalY, 0f, 1f);
  }
}
