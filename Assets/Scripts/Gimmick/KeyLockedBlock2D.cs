using LitMotion;
using UnityEngine;

// 鍵で消えるブロック。鍵を取る前は普通の地形(非 Trigger の Collider2D)としてプレイヤーや敵を止め、
// 同じ keyId の StageKey2D が取得されると当たり判定を消してフェードアウトする。
// 1 つの鍵で同じ keyId のブロックをすべて消せる。
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public sealed class KeyLockedBlock2D : MonoBehaviour, IRuntimeResettable
{
    [SerializeField] private string keyId = "A";
    [SerializeField] private SpriteRenderer[] spriteRenderers;
    [SerializeField] private float fadeDuration = 0.3f;

    private Collider2D[] _colliders;
    private Color[] _originalColors;
    private bool _isUnlocked;
    private MotionHandle _fadeHandle;

    public int ResetOrder => 85;
    public string KeyId => keyId;
    public bool IsUnlocked => _isUnlocked;

    private void Awake()
    {
        if (spriteRenderers == null || spriteRenderers.Length == 0)
        {
            spriteRenderers = GetComponentsInChildren<SpriteRenderer>();
        }

        _colliders = GetComponents<Collider2D>();
        _originalColors = new Color[spriteRenderers.Length];
        for (var i = 0; i < spriteRenderers.Length; i++)
        {
            _originalColors[i] = spriteRenderers[i] != null ? spriteRenderers[i].color : Color.white;
        }
    }

    private void OnEnable()
    {
        StageKey2D.AnyCollected += HandleKeyCollected;
    }

    private void OnDisable()
    {
        StageKey2D.AnyCollected -= HandleKeyCollected;
        _fadeHandle.TryCancel();
    }

    private void HandleKeyCollected(string collectedKeyId)
    {
        if (collectedKeyId == keyId)
        {
            Unlock();
        }
    }

    [ContextMenu("Unlock")]
    public void Unlock()
    {
        if (_isUnlocked)
        {
            return;
        }

        _isUnlocked = true;
        SetCollidersEnabled(false);

        _fadeHandle.TryCancel();
        if (fadeDuration <= 0f)
        {
            SetAlphaRatio(0f);
            return;
        }

        _fadeHandle = LMotion.Create(1f, 0f, fadeDuration)
            .WithEase(Ease.OutQuad)
            .Bind(SetAlphaRatio);
    }

    public void ResetRuntimeState()
    {
        _fadeHandle.TryCancel();
        _isUnlocked = false;
        SetAlphaRatio(1f);
        SetCollidersEnabled(true);
    }

    private void SetCollidersEnabled(bool isEnabled)
    {
        if (_colliders == null)
        {
            return;
        }

        foreach (var blockCollider in _colliders)
        {
            if (blockCollider != null)
            {
                blockCollider.enabled = isEnabled;
            }
        }
    }

    private void SetAlphaRatio(float ratio)
    {
        if (spriteRenderers == null || _originalColors == null)
        {
            return;
        }

        for (var i = 0; i < spriteRenderers.Length; i++)
        {
            if (spriteRenderers[i] == null)
            {
                continue;
            }

            var color = _originalColors[i];
            color.a *= ratio;
            spriteRenderers[i].color = color;
            spriteRenderers[i].enabled = ratio > 0f;
        }
    }
}
