using UnityEngine;

/// <summary>
/// GameObject の Transform と物理状態を記録し、ランタイムリセット時に初期状態へ戻すスクリプト。
/// 初期位置へ戻したい 3D / 2D オブジェクトに付けて使う。
/// </summary>
[DisallowMultipleComponent]
public sealed class TransformStateResettable : MonoBehaviour, IRuntimeResettable
{
  [Header("Reset Target")]
  [SerializeField] private bool resetLocalPosition = true;
  [SerializeField] private bool resetLocalRotation = true;
  [SerializeField] private bool resetLocalScale = true;
  [SerializeField] private bool resetActiveState = false;

  [Header("Physics")]
  [SerializeField] private bool resetRigidbodyState = true;
  [SerializeField] private bool resetRigidbody2DState = true;

  private Vector3 _initialLocalPosition;
  private Quaternion _initialLocalRotation;
  private Vector3 _initialLocalScale;
  private bool _initialActiveSelf;

  private Rigidbody _rigidbody3D;
  private bool _initialIsKinematic3D;
  private bool _initialUseGravity3D;

  private Rigidbody2D _rigidbody2D;
  private bool _initialSimulated2D;
  private RigidbodyType2D _initialBodyType2D;
  private float _initialGravityScale2D;

  public int ResetOrder => 100;

  private void Awake()
  {
    CacheInitialState();
  }

  /// <summary>
  /// 現在の状態を初期状態として取り直す。
  /// 配置を調整し直したあとに呼ぶと、その状態が新しい復帰先になる。
  /// </summary>
  [ContextMenu("Capture Initial State")]
  public void CaptureInitialState()
  {
    CacheInitialState();
  }

  public void ResetRuntimeState()
  {
    RestoreTransformState();
    RestorePhysicsState();
  }

  private void CacheInitialState()
  {
    _initialLocalPosition = transform.localPosition;
    _initialLocalRotation = transform.localRotation;
    _initialLocalScale = transform.localScale;
    _initialActiveSelf = gameObject.activeSelf;

    _rigidbody3D = GetComponent<Rigidbody>();
    if (_rigidbody3D != null)
    {
      _initialIsKinematic3D = _rigidbody3D.isKinematic;
      _initialUseGravity3D = _rigidbody3D.useGravity;
    }

    _rigidbody2D = GetComponent<Rigidbody2D>();
    if (_rigidbody2D != null)
    {
      _initialSimulated2D = _rigidbody2D.simulated;
      _initialBodyType2D = _rigidbody2D.bodyType;
      _initialGravityScale2D = _rigidbody2D.gravityScale;
    }
  }

  private void RestoreTransformState()
  {
    if (resetLocalPosition)
    {
      transform.localPosition = _initialLocalPosition;
    }

    if (resetLocalRotation)
    {
      transform.localRotation = _initialLocalRotation;
    }

    if (resetLocalScale)
    {
      transform.localScale = _initialLocalScale;
    }

    if (resetActiveState)
    {
      gameObject.SetActive(_initialActiveSelf);
    }
  }

  private void RestorePhysicsState()
  {
    if (resetRigidbodyState && _rigidbody3D != null)
    {
      _rigidbody3D.isKinematic = true;
      _rigidbody3D.position = transform.position;
      _rigidbody3D.rotation = transform.rotation;
      _rigidbody3D.linearVelocity = Vector3.zero;
      _rigidbody3D.angularVelocity = Vector3.zero;
      _rigidbody3D.useGravity = _initialUseGravity3D;
      _rigidbody3D.isKinematic = _initialIsKinematic3D;
    }

    if (resetRigidbody2DState && _rigidbody2D != null)
    {
      _rigidbody2D.simulated = false;
      _rigidbody2D.bodyType = _initialBodyType2D;
      _rigidbody2D.gravityScale = _initialGravityScale2D;
      _rigidbody2D.position = transform.position;
      _rigidbody2D.rotation = transform.eulerAngles.z;
      _rigidbody2D.linearVelocity = Vector2.zero;
      _rigidbody2D.angularVelocity = 0f;
      _rigidbody2D.simulated = _initialSimulated2D;
    }
  }
}
