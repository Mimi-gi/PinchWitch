using System.Collections.Generic;
using LitMotion;
using UnityEngine;

// 敵のスタン状態。Stun(duration) で一定時間すべての IEnemyMover を停止させ、時間経過で復帰する。
// スタン中も接触死は有効のまま(当たり判定には触らない)。
// スタンの発火元(光のフラッシュ等)とはこの API だけでつながる。
[DisallowMultipleComponent]
public sealed class EnemyStun2D : MonoBehaviour, IRuntimeResettable
{
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Color stunTint = new Color(0.55f, 0.55f, 0.7f, 1f);
    [SerializeField] private float blinkDuration = 0.25f;
    [Tooltip("残り時間がこの秒数を切ったら点滅して復帰が近いことを知らせる。")]
    [SerializeField] private float recoveryWarningSeconds = 1f;

    private readonly List<IEnemyMover> _movers = new List<IEnemyMover>();
    private float _remainingSeconds;
    private bool _isStunned;
    private bool _isWarning;
    private Color _originalColor = Color.white;
    private bool _colorCaptured;
    private MotionHandle _blinkHandle;

    public int ResetOrder => 95;
    public bool IsStunned => _isStunned;
    public float RemainingSeconds => _remainingSeconds;

    private void Awake()
    {
        AutoAssignMissingReferences();
    }

    private void OnDisable()
    {
        _blinkHandle.TryCancel();
    }

    private void Update()
    {
        if (!_isStunned)
        {
            return;
        }

        _remainingSeconds -= Time.deltaTime;
        if (_remainingSeconds <= 0f)
        {
            EndStun();
            return;
        }

        if (!_isWarning && _remainingSeconds <= recoveryWarningSeconds)
        {
            _isWarning = true;
            StartBlink();
        }
    }

    /// <summary>
    /// duration 秒スタンさせる。スタン中に呼ばれた場合は残り時間の長い方を採用する。
    /// </summary>
    public void Stun(float duration)
    {
        if (duration <= 0f)
        {
            return;
        }

        _remainingSeconds = Mathf.Max(_remainingSeconds, duration);
        if (_remainingSeconds > recoveryWarningSeconds && _isWarning)
        {
            _isWarning = false;
            _blinkHandle.TryCancel();
            ApplyColor(stunTint);
        }

        if (_isStunned)
        {
            return;
        }

        _isStunned = true;
        SetMoversMultiplier(0f);
        CaptureOriginalColor();
        ApplyColor(stunTint);
    }

    public void ResetRuntimeState()
    {
        if (_isStunned)
        {
            EndStun();
        }

        _remainingSeconds = 0f;
    }

    private void EndStun()
    {
        _isStunned = false;
        _isWarning = false;
        _remainingSeconds = 0f;
        _blinkHandle.TryCancel();
        RestoreColor();
        SetMoversMultiplier(1f);
    }

    private void StartBlink()
    {
        _blinkHandle.TryCancel();
        if (spriteRenderer == null)
        {
            return;
        }

        _blinkHandle = LMotion.Create(stunTint, _originalColor, Mathf.Max(0.01f, blinkDuration))
            .WithLoops(-1, LoopType.Yoyo)
            .Bind(color => ApplyColor(color));
    }

    private void SetMoversMultiplier(float multiplier)
    {
        foreach (var mover in _movers)
        {
            mover.SetExternalSpeedMultiplier(multiplier);
        }
    }

    private void CaptureOriginalColor()
    {
        if (spriteRenderer == null || _colorCaptured)
        {
            return;
        }

        _originalColor = spriteRenderer.color;
        _colorCaptured = true;
    }

    private void ApplyColor(Color color)
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.color = color;
        }
    }

    private void RestoreColor()
    {
        if (_colorCaptured)
        {
            ApplyColor(_originalColor);
        }
    }

    private void AutoAssignMissingReferences()
    {
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        }

        _movers.Clear();
        GetComponents(_movers);
    }
}
