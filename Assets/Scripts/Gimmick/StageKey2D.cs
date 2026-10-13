using System;
using LitMotion;
using UnityEngine;

// ステージの鍵。プレイヤーが触れると取得され、同じ keyId の KeyLockedBlock2D が消える。
// 当たり判定(Collider2D)は Trigger にしておくこと。
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public sealed class StageKey2D : MonoBehaviour, IRuntimeResettable
{
    /// <summary>
    /// いずれかの鍵が取得されたときに keyId 付きで通知される。
    /// </summary>
    public static event Action<string> AnyCollected;

    /// <summary>
    /// いずれかの鍵がリセットで未取得に戻ったときに keyId 付きで通知される。
    /// </summary>
    public static event Action<string> AnyReset;

    [SerializeField] private string keyId = "A";
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private SpriteRenderer spriteRenderer;
    [Tooltip("未設定なら AudioManager の SE_Select を鳴らす。")]
    [SerializeField] private AudioClip collectSe;

    [Header("Idle Motion")]
    [SerializeField] private float bobHeight = 0.1f;
    [SerializeField] private float bobDuration = 0.8f;

    private Collider2D[] _colliders;
    private Vector3 _initialLocalPosition;
    private bool _isCollected;
    private MotionHandle _bobHandle;

    public event Action Collected;

    public int ResetOrder => 80;
    public string KeyId => keyId;
    public bool IsCollected => _isCollected;

    private void Awake()
    {
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        }

        _colliders = GetComponents<Collider2D>();
        _initialLocalPosition = spriteRenderer != null ? spriteRenderer.transform.localPosition : Vector3.zero;
    }

    private void OnEnable()
    {
        StartBob();
    }

    private void OnDisable()
    {
        _bobHandle.TryCancel();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (_isCollected || other == null || !other.CompareTag(playerTag))
        {
            return;
        }

        Collect();
    }

    [ContextMenu("Collect")]
    public void Collect()
    {
        if (_isCollected)
        {
            return;
        }

        _isCollected = true;
        SetPresence(false);
        PlayCollectSe();

        Collected?.Invoke();
        AnyCollected?.Invoke(keyId);
    }

    public void ResetRuntimeState()
    {
        var wasCollected = _isCollected;
        _isCollected = false;
        SetPresence(true);
        StartBob();

        if (wasCollected)
        {
            AnyReset?.Invoke(keyId);
        }
    }

    private void SetPresence(bool isPresent)
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.enabled = isPresent;
        }

        if (_colliders == null)
        {
            return;
        }

        foreach (var keyCollider in _colliders)
        {
            if (keyCollider != null)
            {
                keyCollider.enabled = isPresent;
            }
        }
    }

    private void StartBob()
    {
        _bobHandle.TryCancel();
        if (spriteRenderer == null || bobHeight <= 0f || bobDuration <= 0f || !Application.isPlaying)
        {
            return;
        }

        var bobTransform = spriteRenderer.transform;
        bobTransform.localPosition = _initialLocalPosition;
        _bobHandle = LMotion.Create(0f, bobHeight, bobDuration)
            .WithEase(Ease.InOutSine)
            .WithLoops(-1, LoopType.Yoyo)
            .Bind(offset => bobTransform.localPosition = _initialLocalPosition + Vector3.up * offset);
    }

    private void PlayCollectSe()
    {
        var audioManager = AudioManager.Instance;
        if (audioManager == null)
        {
            return;
        }

        audioManager.PlaySE(collectSe != null ? collectSe : audioManager.SE_Select);
    }
}
