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
            ApplyOutline(_currentHighlightedBody, Color.black, 0f);
            _currentHighlightedBody = null;
        }
    }

    private void UpdateOutline(Rigidbody targetBody, Color color)
    {
        if (_currentHighlightedBody != null && _currentHighlightedBody != targetBody)
        {
            ApplyOutline(_currentHighlightedBody, Color.black, 0f);
        }

        _currentHighlightedBody = targetBody;

        if (targetBody != null)
        {
            ApplyOutline(targetBody, color, outlineWidth);
        }
    }

    private void ApplyOutline(Rigidbody body, Color color, float width)
    {
        var renderers = body.GetComponentsInChildren<Renderer>();
        foreach (var r in renderers)
        {
            r.GetPropertyBlock(_propBlock);
            _propBlock.SetColor(OutlineColorID, color);
            _propBlock.SetFloat(OutlineWidthID, width);
            r.SetPropertyBlock(_propBlock);
        }
    }
}
