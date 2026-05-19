using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(Wall2DPlayerController))]
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(Rigidbody2D))]
public sealed class Wall2DPlayerAnimator : MonoBehaviour
{
  private enum PlayerAnimationState
  {
    Idle,
    Walk,
    Good,
    Goal,
    Climb,
    Fall,
  }

  [Header("Animator States")]
  [SerializeField] private string baseLayerName = "Base Layer";
  [SerializeField] private string idleStateName = "Idle";
  [SerializeField] private string walkStateName = "Walk";
  [SerializeField] private string goodStateName = "Good";
  [SerializeField] private string goalStateName = "Goal";
  [SerializeField] private string climbStateName = "Climb";
  [SerializeField] private string fallStateName = "Fall";

  [Header("Reaction Timing")]
  [SerializeField] private float goodReactionDuration = 1f;
  [SerializeField] private float goalReactionDuration = 1f;
  [SerializeField] private float walkSpeedThreshold = 0.05f;

  [Header("Sprite Flip")]
  [SerializeField] private bool flipSpriteWithMovement = true;
  [SerializeField] private bool spriteDefaultFacesLeft = false;
  [SerializeField] private float flipVelocityThreshold = 0.1f;

  private Animator _animator;
  private Wall2DPlayerController _controller;
  private SpriteRenderer _spriteRenderer;
  private Rigidbody2D _rigidbody2D;
  private AnimatorUpdateMode _defaultUpdateMode;
  private PlayerAnimationState? _forcedState;

  private int _idleStateHash;
  private int _walkStateHash;
  private int _goodStateHash;
  private int _goalStateHash;
  private int _climbStateHash;
  private int _fallStateHash;

  private PlayerAnimationState? _currentState;
  private float _goodReactionEndTime;
  private bool _isGoalStateLocked;

  public float GoodReactionDuration => goodReactionDuration;
  public float GoalReactionDuration => goalReactionDuration;

  private void Awake()
  {
    _animator = GetComponent<Animator>();
    _controller = GetComponent<Wall2DPlayerController>();
    _spriteRenderer = GetComponent<SpriteRenderer>();
    _rigidbody2D = GetComponent<Rigidbody2D>();
    _defaultUpdateMode = _animator.updateMode;
    CacheStateHashes();
  }

  private void OnEnable()
  {
    ResetTransientState();
    PlayState(ResolveBaseState(), restart: true);
  }

  private void OnDisable()
  {
    AudioManager.Instance.StopWalkSE();
  }

  private void OnValidate()
  {
    goodReactionDuration = Mathf.Max(0f, goodReactionDuration);
    goalReactionDuration = Mathf.Max(0f, goalReactionDuration);
    walkSpeedThreshold = Mathf.Max(0f, walkSpeedThreshold);
    flipVelocityThreshold = Mathf.Max(0f, flipVelocityThreshold);

    if (Application.isPlaying)
    {
      CacheStateHashes();
    }
  }

  private void Update()
  {
    if (_animator == null || _controller == null)
    {
      return;
    }

    var resolvedState = ResolveState();
    PlayState(resolvedState, restart: false);
    UpdateStateAudio(resolvedState);

    if (flipSpriteWithMovement && _rigidbody2D != null && _spriteRenderer != null)
    {
      UpdateSpriteFlip();
    }
  }

  public void PlayGoodReaction()
  {
    if (_isGoalStateLocked)
    {
      return;
    }

    _goodReactionEndTime = Time.unscaledTime + goodReactionDuration;
    PlayState(PlayerAnimationState.Good, restart: true);
  }

  public void ClearGoodReaction()
  {
    if (_isGoalStateLocked)
    {
      return;
    }

    _goodReactionEndTime = 0f;
    PlayState(ResolveBaseState(), restart: false);
  }

  public void PlayGoalReaction()
  {
    _isGoalStateLocked = true;
    _goodReactionEndTime = 0f;

    if (_animator != null)
    {
      _animator.updateMode = AnimatorUpdateMode.UnscaledTime;
    }

    PlayState(PlayerAnimationState.Goal, restart: true);
  }

  public void SetForcedFallState(bool isForced)
  {
    _forcedState = isForced ? PlayerAnimationState.Fall : null;

    if (_forcedState.HasValue)
    {
      PlayState(_forcedState.Value, restart: false);
      UpdateStateAudio(_forcedState.Value);
      return;
    }

    var resolvedState = ResolveState();
    PlayState(resolvedState, restart: false);
    UpdateStateAudio(resolvedState);
  }

  private void CacheStateHashes()
  {
    _idleStateHash = CreateStateHash(idleStateName);
    _walkStateHash = CreateStateHash(walkStateName);
    _goodStateHash = CreateStateHash(goodStateName);
    _goalStateHash = CreateStateHash(goalStateName);
    _climbStateHash = CreateStateHash(climbStateName);
    _fallStateHash = CreateStateHash(fallStateName);
  }

  private void ResetTransientState()
  {
    _currentState = null;
    _goodReactionEndTime = 0f;
    _isGoalStateLocked = false;
    _forcedState = null;

    if (_animator != null)
    {
      _animator.updateMode = _defaultUpdateMode;
    }
  }

  private PlayerAnimationState ResolveState()
  {
    if (_forcedState.HasValue)
    {
      return _forcedState.Value;
    }

    if (_isGoalStateLocked)
    {
      return PlayerAnimationState.Goal;
    }

    if (_goodReactionEndTime > Time.unscaledTime)
    {
      return PlayerAnimationState.Good;
    }

    return ResolveBaseState();
  }

  private PlayerAnimationState ResolveBaseState()
  {
    if (_controller == null || !_controller.enabled)
    {
      return PlayerAnimationState.Idle;
    }

    if (_controller.IsClimbing)
    {
      return PlayerAnimationState.Climb;
    }

    if (_controller.IsFalling)
    {
      return PlayerAnimationState.Fall;
    }

    if (_controller.IsGrounded && _controller.CurrentMoveSpeed > walkSpeedThreshold)
    {
      return PlayerAnimationState.Walk;
    }

    return PlayerAnimationState.Idle;
  }

  private void PlayState(PlayerAnimationState state, bool restart)
  {
    if (!restart && _currentState == state)
    {
      return;
    }

    var stateHash = GetStateHash(state);
    if (!_animator.HasState(0, stateHash))
    {
      return;
    }

    if (restart)
    {
      _animator.Play(stateHash, 0, 0f);
    }
    else
    {
      _animator.Play(stateHash, 0);
    }

    _currentState = state;
  }

  private void UpdateStateAudio(PlayerAnimationState state)
  {
    if (state != PlayerAnimationState.Walk)
    {
      AudioManager.Instance.StopWalkSE();
      return;
    }

    AudioManager.Instance.PlayWalkSE();
  }

  private int CreateStateHash(string stateName)
  {
    return Animator.StringToHash($"{baseLayerName}.{stateName}");
  }

  private int GetStateHash(PlayerAnimationState state)
  {
    return state switch
    {
      PlayerAnimationState.Idle => _idleStateHash,
      PlayerAnimationState.Walk => _walkStateHash,
      PlayerAnimationState.Good => _goodStateHash,
      PlayerAnimationState.Goal => _goalStateHash,
      PlayerAnimationState.Climb => _climbStateHash,
      PlayerAnimationState.Fall => _fallStateHash,
      _ => _idleStateHash,
    };
  }

  private void UpdateSpriteFlip()
  {
    if (_controller.IsAttachedToSurface)
    {
      var normal = _controller.CurrentSurfaceNormal;
      var isOnVerticalSurface = Mathf.Abs(normal.x) > Mathf.Abs(normal.y);
      if (isOnVerticalSurface)
      {
        return;
      }
    }

    var velocity = _rigidbody2D.linearVelocity;
    if (Mathf.Abs(velocity.x) < flipVelocityThreshold)
    {
      return;
    }

    _spriteRenderer.flipX = spriteDefaultFacesLeft ? velocity.x > 0f : velocity.x < 0f;
  }
}
