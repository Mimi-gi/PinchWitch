using LitMotion;
using UnityEngine;

public class OutlineController
{
    // ★ アウトラインの設定はここから変更できます ★
    public float outlineWidth = 0.08f;
    public static Color hoverColor = new Color(0.8f, 0.8f, 0.8f, 0.5f);
    public static Color grabColor = new Color(2.0f, 2.0f, 2.0f, 2.0f);
    public static Color errorColor = Color.red;

    // ★ 表示/非表示のトゥイーン設定（アウトラインの太さを 0 から遷移させる） ★
    public float tweenDuration = 0.15f;
    public Ease tweenEase = Ease.OutQuad;

    private readonly MaterialPropertyBlock _propBlock;
    private static readonly int OutlineColorID = Shader.PropertyToID("_OutlineColor");
    private static readonly int OutlineWidthID = Shader.PropertyToID("_OutlineWidth");

    private Rigidbody _currentHighlightedBody;
    private Renderer[] _cachedRenderers;

    private MotionHandle _tweenHandle;
    private float _displaySize; // OutlineSDFFeature に渡す太さの倍率（0〜1）

    private Color _appliedColor;
    private bool _hasAppliedColor;

    // フェードアウト完了時にオフへリセットする予定のRenderer（トゥイーンが完了前に中断された場合に備えて保持する）
    private Renderer[] _pendingCleanupRenderers;

    public OutlineController()
    {
        _propBlock = new MaterialPropertyBlock();
    }

    // ホバー時のアウトライン表示（掴める位置にあるオブジェクト用）
    public void ShowHover(Rigidbody targetBody)
    {
        UpdateOutline(targetBody, hoverColor);
    }

    // 掴み中のアウトライン表示（正常時）
    public void ShowGrabbed(Rigidbody targetBody)
    {
        UpdateOutline(targetBody, grabColor);
    }

    // エラー時のアウトライン表示（置けない場所など）
    public void ShowError(Rigidbody targetBody)
    {
        UpdateOutline(targetBody, errorColor);
    }

    // 現在ハイライト中のオブジェクトのアウトラインを消す
    public void ClearCurrentOutline()
    {
        if (_currentHighlightedBody == null)
        {
            return;
        }

        // トゥイーン完了後にまとめてオフにするため、対象のRendererをキャプチャしておく
        var renderersToClear = _cachedRenderers;
        StartSizeTween(0f, renderersToClear);

        _currentHighlightedBody = null;
        _cachedRenderers = null;
        _hasAppliedColor = false;
    }

    // 実行中のトゥイーンを止め、即座にアウトラインを消す（オーナー破棄・操作無効化時に呼ぶ）
    public void Dispose()
    {
        _tweenHandle.TryCancel();
        OutlineSDFFeature.SetWidthMultiplier(0f);
        ApplyOutlineToRenderers(_cachedRenderers, Color.black, 0f);
        FlushPendingCleanup();

        _currentHighlightedBody = null;
        _cachedRenderers = null;
        _hasAppliedColor = false;
        _displaySize = 0f;
    }

    private void UpdateOutline(Rigidbody targetBody, Color color)
    {
        // ターゲットが新しくなった時だけ GetComponentsInChildren を実行する (GC Alloc・パフォーマンス対策)
        if (_currentHighlightedBody != targetBody)
        {
            if (_cachedRenderers != null)
            {
                ApplyOutlineToRenderers(_cachedRenderers, Color.black, 0f);
            }

            _currentHighlightedBody = targetBody;
            _hasAppliedColor = false;

            if (targetBody != null)
            {
                _cachedRenderers = targetBody.GetComponentsInChildren<Renderer>();
                _displaySize = 0f;
                OutlineSDFFeature.SetWidthMultiplier(0f);
            }
            else
            {
                _cachedRenderers = null;
            }
        }

        if (_currentHighlightedBody == null)
        {
            return;
        }

        // 色・オン/オフは即時反映。太さ（大きさ）だけを 0→1 でトゥイーンする
        if (!_hasAppliedColor || _appliedColor != color)
        {
            _appliedColor = color;
            _hasAppliedColor = true;
            ApplyOutlineToRenderers(_cachedRenderers, color, outlineWidth);
            StartSizeTween(1f, null);
        }
    }

    // OutlineSDFFeature の太さ倍率を現在値から目標値までトゥイーンさせる。
    // cleanupRenderersOnComplete を渡すと、トゥイーン完了時にそのRendererをオフへリセットする。
    // 完了前に別のトゥイーンへ差し替えられた場合も、後始末を取りこぼさないよう即座に実行する。
    private void StartSizeTween(float targetSize, Renderer[] cleanupRenderersOnComplete)
    {
        FlushPendingCleanup();
        _tweenHandle.TryCancel();

        var fromSize = _displaySize;
        var builder = LMotion.Create(fromSize, targetSize, tweenDuration).WithEase(tweenEase);

        _pendingCleanupRenderers = cleanupRenderersOnComplete;
        if (cleanupRenderersOnComplete != null)
        {
            builder = builder.WithOnComplete(() =>
            {
                ApplyOutlineToRenderers(cleanupRenderersOnComplete, Color.black, 0f);
                if (_pendingCleanupRenderers == cleanupRenderersOnComplete)
                {
                    _pendingCleanupRenderers = null;
                }
            });
        }

        _tweenHandle = builder.Bind(this, (size, self) =>
        {
            self._displaySize = size;
            OutlineSDFFeature.SetWidthMultiplier(size);
        });
    }

    // 中断されて呼ばれなかった後始末があれば、ここで確定させる
    private void FlushPendingCleanup()
    {
        if (_pendingCleanupRenderers == null)
        {
            return;
        }

        ApplyOutlineToRenderers(_pendingCleanupRenderers, Color.black, 0f);
        _pendingCleanupRenderers = null;
    }

    // 指定したRenderer配列に対してマテリアル情報を流し込む
    private void ApplyOutlineToRenderers(Renderer[] renderers, Color color, float width)
    {
        if (renderers == null) return;

        foreach (var r in renderers)
        {
            if (r == null) continue; // 万が一途中でRendererが破壊された時の安全策
            r.GetPropertyBlock(_propBlock);
            _propBlock.SetColor(OutlineColorID, color);
            _propBlock.SetFloat(OutlineWidthID, width);
            r.SetPropertyBlock(_propBlock);
        }
    }
}
