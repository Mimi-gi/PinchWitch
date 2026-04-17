using UnityEngine;

/// <summary>
/// Wall2DPlayerController の移動状態を読み取り、Animator パラメータと
/// SpriteRenderer の左右反転を自動で更新する。
///
/// ■ Animator に用意するパラメータ（名前は変更可。その場合は Inspector から設定）
///   IsWalking  (Bool)  : 接地中かつ移動中のとき true
///   IsAirborne (Bool)  : 空中（非接地）のとき true
///   Speed      (Float) : 移動速度の大きさ（Blend Tree 用）
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(Wall2DPlayerController))]
[RequireComponent(typeof(SpriteRenderer))]
public sealed class Wall2DPlayerAnimator : MonoBehaviour
{
  [Header("Animator Parameter Names")]
  [Tooltip("接地中かつ移動中を示す Bool パラメータ名")]
  [SerializeField] private string isWalkingParam = "IsWalking";

  [Tooltip("空中（非接地）状態を示す Bool パラメータ名")]
  [SerializeField] private string isAirborneParam = "IsAirborne";

  [Tooltip("移動速度を示す Float パラメータ名（Blend Tree 用）")]
  [SerializeField] private string speedParam = "Speed";

  [Header("Sprite Flip")]
  [Tooltip("水平移動方向に合わせて SpriteRenderer を左右反転させるか")]
  [SerializeField] private bool flipSpriteWithMovement = true;

  [Tooltip("スプライト素材がデフォルトで左向きの場合は ON にする。右向きなら OFF。")]
  [SerializeField] private bool spriteDefaultFacesLeft = false;

  [Tooltip("反転を適用し始める速度のしきい値（この値未満のときは前の向きを維持）")]
  [SerializeField] private float flipVelocityThreshold = 0.1f;

  // キャッシュ済み Animator パラメータハッシュ。
  private int _isWalkingHash;
  private int _isAirborneHash;
  private int _speedHash;

  private Animator _animator;
  private Wall2DPlayerController _controller;
  private SpriteRenderer _spriteRenderer;
  private Rigidbody2D _rigidbody2D;

  private void Awake()
  {
    _animator = GetComponent<Animator>();
    _controller = GetComponent<Wall2DPlayerController>();
    _spriteRenderer = GetComponent<SpriteRenderer>();
    _rigidbody2D = GetComponent<Rigidbody2D>();

    CacheParameterHashes();
  }

  private void OnValidate()
  {
    // Inspector でパラメータ名を変更したとき、再生中でもハッシュを更新する。
    if (Application.isPlaying)
    {
      CacheParameterHashes();
    }
  }

  private void CacheParameterHashes()
  {
    _isWalkingHash = Animator.StringToHash(isWalkingParam);
    _isAirborneHash = Animator.StringToHash(isAirborneParam);
    _speedHash = Animator.StringToHash(speedParam);
  }

  private void Update()
  {
    if (_animator == null || _controller == null)
    {
      return;
    }

    var isAttached = _controller.IsAttachedToSurface;
    var isMoving = _controller.IsMoving;

    // ── Animator パラメータ更新 ──────────────────────────────
    _animator.SetBool(_isWalkingHash, isAttached && isMoving);
    _animator.SetBool(_isAirborneHash, !isAttached);
    _animator.SetFloat(_speedHash, isMoving ? _controller.CurrentMoveSpeed : 0f);

    // ── スプライト左右反転 ──────────────────────────────────
    if (flipSpriteWithMovement && _rigidbody2D != null && _spriteRenderer != null)
    {
      UpdateSpriteFlip();
    }
  }

  /// <summary>
  /// Rigidbody2D の水平速度成分を基にスプライトの向きを決める。
  /// 速度が小さいときは以前の向きをそのまま保つ。
  /// spriteDefaultFacesLeft が true なら、右移動時に反転して正面を向かせる。
  ///
  /// 壁（垂直面）接地中は velocity.x のほとんどが「面への押し付け成分」であり
  /// 進行方向を表さないため、反転の更新をスキップして直前の向きを維持する。
  /// </summary>
  private void UpdateSpriteFlip()
  {
    // 接地面の法線 X 成分が Y 成分より大きければ「壁（垂直面）」と判定する。
    // 壁登り中は反転を更新しない（最後に水平移動していたときの向きを維持）。
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
    if (Mathf.Abs(velocity.x) >= flipVelocityThreshold)
    {
      // 左向き素材：右に動くとき反転 / 右向き素材：左に動くとき反転
      _spriteRenderer.flipX = spriteDefaultFacesLeft ? velocity.x > 0f : velocity.x < 0f;
    }
  }
}
