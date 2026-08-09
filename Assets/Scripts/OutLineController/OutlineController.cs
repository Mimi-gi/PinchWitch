using System.Collections.Generic;
using LitMotion;
using UnityEngine;

// Grabbable のアウトライン表示を状態(ホバー/射程外ホバー/掴み中/エラー)ごとに制御する。
// 太さはオブジェクト単位で 0↔目標値 をトゥイーンするため、
// 対象の切り替え時も「前の対象はニュッと消え、次の対象はニュッと出る」が同時に進行する。
public class OutlineController
{
    // ★ アウトラインの設定はここから変更できます ★
    public static Color hoverColor = new Color(0.8f, 0.8f, 0.8f, 0.5f);
    public static Color hoverOutOfRangeColor = new Color(0.8f, 0.8f, 0.8f, 0.25f);
    public static Color grabColor = new Color(2.0f, 2.0f, 2.0f, 2.0f);
    public static Color errorColor = Color.red;

    // ★ 表示/非表示のトゥイーン設定 ★
    public float tweenInDuration = 0.18f;
    public Ease tweenInEase = Ease.OutBack; // 少し行き過ぎて戻る「ニュッ」
    public float tweenOutDuration = 0.25f;
    public Ease tweenOutEase = Ease.OutQuad;

    // 射程外の Grabbable に出す細いアウトラインの太さ(通常の何割か)
    public float outOfRangeWidthScale = 0.35f;

    private static readonly int OutlineColorID = Shader.PropertyToID("_OutlineColor");
    private static readonly int OutlineWidthID = Shader.PropertyToID("_OutlineWidth");

    // 1オブジェクト分のアウトライン表示状態。消えかけの物も個別に保持する。
    private sealed class OutlineState
    {
        public Rigidbody Body;
        public Renderer[] Renderers;
        public float Size;       // 現在の太さ倍率(0〜1)。トゥイーンで動く
        public float TargetSize; // 進行中トゥイーンの目標値
        public Color Color;
        public MotionHandle Handle;
    }

    private readonly MaterialPropertyBlock _propBlock;
    private OutlineState _current;
    private readonly List<OutlineState> _fadingStates = new List<OutlineState>();

    public OutlineController()
    {
        _propBlock = new MaterialPropertyBlock();
    }

    // ホバー時のアウトライン表示（掴める位置にあるオブジェクト用）
    public void ShowHover(Rigidbody targetBody)
    {
        SetOutline(targetBody, hoverColor, 1f);
    }

    // 射程外の Grabbable 用の細いアウトライン表示
    public void ShowHoverOutOfRange(Rigidbody targetBody)
    {
        SetOutline(targetBody, hoverOutOfRangeColor, outOfRangeWidthScale);
    }

    // 掴み中のアウトライン表示（正常時）
    public void ShowGrabbed(Rigidbody targetBody)
    {
        SetOutline(targetBody, grabColor, 1f);
    }

    // エラー時のアウトライン表示（置けない場所など）
    public void ShowError(Rigidbody targetBody)
    {
        SetOutline(targetBody, errorColor, 1f);
    }

    // 現在ハイライト中のオブジェクトのアウトラインをニュッと消す
    public void ClearCurrentOutline()
    {
        if (_current == null)
        {
            return;
        }

        BeginFadeOut(_current);
        _current = null;
    }

    // 実行中のトゥイーンを止め、即座に全アウトラインを消す（オーナー破棄・操作無効化時に呼ぶ）
    public void Dispose()
    {
        if (_current != null)
        {
            _current.Handle.TryCancel();
            ApplyToRenderers(_current.Renderers, Color.black, 0f);
            _current = null;
        }

        foreach (var state in _fadingStates)
        {
            state.Handle.TryCancel();
            ApplyToRenderers(state.Renderers, Color.black, 0f);
        }

        _fadingStates.Clear();
        NotifyActiveCount();
    }

    private void SetOutline(Rigidbody targetBody, Color color, float targetSize)
    {
        if (targetBody == null)
        {
            ClearCurrentOutline();
            return;
        }

        // 同じ対象なら色を即時反映し、目標太さが変わった時だけ現在値から追従トゥイーン
        // (射程外の細い表示 ⇔ 射程内の通常表示 もここで滑らかに繋がる)
        if (_current != null && _current.Body == targetBody)
        {
            if (_current.Color != color)
            {
                _current.Color = color;
                ApplyToRenderers(_current.Renderers, color, _current.Size);
            }

            if (!Mathf.Approximately(_current.TargetSize, targetSize))
            {
                StartSizeTween(_current, targetSize, tweenInDuration, tweenInEase);
            }

            return;
        }

        // 対象が替わる: 今の対象は個別にフェードアウトさせる(即消ししない)
        if (_current != null)
        {
            BeginFadeOut(_current);
        }

        // 消えかけの同じ対象が居れば、その太さから再成長させる(0リセットのポップを防ぐ)
        var state = TakeFadingState(targetBody);
        if (state == null)
        {
            state = new OutlineState
            {
                Body = targetBody,
                Renderers = targetBody.GetComponentsInChildren<Renderer>(),
                Size = 0f,
            };
        }

        state.Color = color;
        _current = state;
        ApplyToRenderers(state.Renderers, color, state.Size);
        StartSizeTween(state, targetSize, tweenInDuration, tweenInEase);
        NotifyActiveCount();
    }

    private void BeginFadeOut(OutlineState state)
    {
        _fadingStates.Add(state);
        StartSizeTween(state, 0f, tweenOutDuration, tweenOutEase, removeOnComplete: true);
        NotifyActiveCount();
    }

    private OutlineState TakeFadingState(Rigidbody body)
    {
        for (var i = 0; i < _fadingStates.Count; i++)
        {
            if (_fadingStates[i].Body != body)
            {
                continue;
            }

            var state = _fadingStates[i];
            state.Handle.TryCancel();
            _fadingStates.RemoveAt(i);
            return state;
        }

        return null;
    }

    private void StartSizeTween(OutlineState state, float targetSize, float duration, Ease ease, bool removeOnComplete = false)
    {
        state.Handle.TryCancel();
        state.TargetSize = targetSize;

        var builder = LMotion.Create(state.Size, targetSize, duration).WithEase(ease);

        if (removeOnComplete)
        {
            builder = builder.WithOnComplete(() =>
            {
                ApplyToRenderers(state.Renderers, Color.black, 0f);
                _fadingStates.Remove(state);
                NotifyActiveCount();
            });
        }

        // トゥイーンはホバー切り替え時にしか作られないため、クロージャ確保のコストは許容する
        state.Handle = builder.Bind(size =>
        {
            state.Size = size;
            ApplyToRenderers(state.Renderers, state.Color, size);
        });
    }

    // 表示中(消えかけ含む)のアウトライン数を RenderFeature に伝え、0ならパス自体をスキップさせる
    private void NotifyActiveCount()
    {
        var count = (_current != null ? 1 : 0) + _fadingStates.Count;
        OutlineSDFFeature.SetActiveOutlineCount(count);
    }

    // 指定したRenderer配列にアウトラインの色と太さ倍率(0〜1)を流し込む
    private void ApplyToRenderers(Renderer[] renderers, Color color, float widthMultiplier)
    {
        if (renderers == null) return;

        foreach (var r in renderers)
        {
            if (r == null) continue; // 万が一途中でRendererが破壊された時の安全策
            r.GetPropertyBlock(_propBlock);
            _propBlock.SetColor(OutlineColorID, color);
            _propBlock.SetFloat(OutlineWidthID, widthMultiplier);
            r.SetPropertyBlock(_propBlock);
        }
    }
}
