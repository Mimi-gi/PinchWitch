using UnityEngine;

public class OutlineController
{
    // ★ アウトラインの設定はここから変更できます ★
    public float outlineWidth = 0.08f;
    public Color hoverColor = Color.yellow;
    public Color grabColor = Color.cyan;
    public Color errorColor = Color.red;

    private readonly MaterialPropertyBlock _propBlock;
    private static readonly int OutlineColorID = Shader.PropertyToID("_OutlineColor");
    private static readonly int OutlineWidthID = Shader.PropertyToID("_OutlineWidth");

    private Rigidbody _currentHighlightedBody;

    private Renderer[] _cachedRenderers;

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
        if (_currentHighlightedBody != null)
        {
            ApplyOutlineToCachedRenderers(Color.black, 0f);
            _currentHighlightedBody = null;
            _cachedRenderers = null;
        }
    }

    private void UpdateOutline(Rigidbody targetBody, Color color)
    {
        // ターゲットが新しくなった時だけ GetComponentsInChildren を実行する (GC Alloc・パフォーマンス対策)
        if (_currentHighlightedBody != targetBody)
        {
            if (_currentHighlightedBody != null)
            {
                ApplyOutlineToCachedRenderers(Color.black, 0f);
            }

            _currentHighlightedBody = targetBody;

            if (targetBody != null)
            {
                _cachedRenderers = targetBody.GetComponentsInChildren<Renderer>();
            }
            else
            {
                _cachedRenderers = null;
            }
        }

        // キャッシュされた Renderer に対して色を適用
        if (_currentHighlightedBody != null)
        {
            ApplyOutlineToCachedRenderers(color, outlineWidth);
        }
    }

    // キャッシュされたRenderer配列に対してマテリアル情報を流し込む
    private void ApplyOutlineToCachedRenderers(Color color, float width)
    {
        if (_cachedRenderers == null) return;

        foreach (var r in _cachedRenderers)
        {
            if (r == null) continue; // 万が一途中でRendererが破壊された時の安全策
            r.GetPropertyBlock(_propBlock);
            _propBlock.SetColor(OutlineColorID, color);
            _propBlock.SetFloat(OutlineWidthID, width);
            r.SetPropertyBlock(_propBlock);
        }
    }
}
