//=============================================================
// ファイル名  : LoadingCurtainDirector.cs
// 作成者     : 彩乃
// 作成日     : 2025/04/15
// 説明       : Loadingシーンのカーテン演出のみを担当。
//              LoadingSceneControllerから呼び出して使う。
//              閉じる：Linear / 開く：InOutBack
//=============================================================
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// Loadingシーンのカーテン演出を担当。
/// PlayOpen / PlayClose をコルーチンとして呼び出す。
/// </summary>
public class LoadingCurtainDirector : MonoBehaviour
{
  //=========================================================
  // Inspector設定
  //=========================================================
  [Header("カーテンパーツ")]
  [SerializeField] private RectTransform _curtainInLeft;
  [SerializeField] private RectTransform _curtainInRight;
  [SerializeField] private RectTransform _curtainOutside;
  [SerializeField] private Image _curtainOutsideImage;

  [Header("アニメーション時間")]
  [SerializeField] private float _openDuration = 0.9f;
  [SerializeField] private float _closeDuration = 0.8f;

  [Header("InSideカーテン座標 (Pos X)")]
  [Tooltip("開いた状態の左カーテンX座標")]
  [SerializeField] private float _inLeftOpenX = -585f;
  [Tooltip("閉じた状態の左カーテンX座標")]
  [SerializeField] private float _inLeftCloseX = 0f;
  [Tooltip("開いた状態の右カーテンX座標")]
  [SerializeField] private float _inRightOpenX = 585f;
  [Tooltip("閉じた状態の右カーテンX座標")]
  [SerializeField] private float _inRightCloseX = 0f;

  [Header("Outsideカーテン設定")]
  [Tooltip("PlayCloseで拡大する倍率")]
  [SerializeField] private float _outsideCloseScale = 1.3f;

  //=========================================================
  // 公開メソッド
  //=========================================================

  /// <summary>
  /// カーテンを閉じてロード待機状態にする演出。
  /// シーン開始時に呼ぶ。
  /// InSide：外側から中央に閉じてくる（InOutBack）
  /// Outside：alpha:1のまま静止
  /// </summary>
  public IEnumerator PlayOpen()
  {
    // Outside：alpha:1・等倍でリセット（静止）
    _curtainOutside.localScale = Vector3.one;
    _curtainOutsideImage.color = new Color(
        _curtainOutsideImage.color.r,
        _curtainOutsideImage.color.g,
        _curtainOutsideImage.color.b,
        1f);

    // InSide：開いた位置にリセット
    _curtainInLeft.anchoredPosition = new Vector2(_inLeftOpenX, _curtainInLeft.anchoredPosition.y);
    _curtainInRight.anchoredPosition = new Vector2(_inRightOpenX, _curtainInRight.anchoredPosition.y);

    bool done = false;

    DOTween.Sequence()
        // InSide：外側から中央に閉じてくる（InOutBack）
        .Join(_curtainInLeft.DOAnchorPosX(_inLeftCloseX, _openDuration)
            .SetEase(Ease.InOutBack))
        .Join(_curtainInRight.DOAnchorPosX(_inRightCloseX, _openDuration)
            .SetEase(Ease.InOutBack))
        // Outside：アニメなし、alpha:1のまま静止
        .OnComplete(() => done = true);

    yield return new WaitUntil(() => done);
  }

  /// <summary>
  /// カーテンを開いて次シーンへ移行する演出。
  /// シーン遷移直前に呼ぶ。
  /// InSide：中央から外側に開く（Linear）
  /// Outside：alphaそのまま・拡大して消える（Linear）
  /// </summary>
  public IEnumerator PlayClose()
  {
    bool done = false;

    DOTween.Sequence()
        // InSide：中央から外側に開く（Linear）
        .Join(_curtainInLeft.DOAnchorPosX(_inLeftOpenX, _closeDuration)
            .SetEase(Ease.Linear))
        .Join(_curtainInRight.DOAnchorPosX(_inRightOpenX, _closeDuration)
            .SetEase(Ease.Linear))
        // Outside：拡大のみ、alphaはいじらない（Linear）
        .Join(_curtainOutside.DOScale(Vector3.one * _outsideCloseScale, _closeDuration)
            .SetEase(Ease.Linear))
        .OnComplete(() => done = true);

    yield return new WaitUntil(() => done);
  }
}
