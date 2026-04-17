//=============================================================
// ファイル名  : LoadingCurtainDirector.cs
// 作成者     : 彩乃
// 作成日     : 2025/04/15
// 説明       : Loadingシーンのカーテン演出のみを担当。
//              LoadingSceneControllerから呼び出して使う。
//              CurtainClose = カーテンが閉じる（Linear・ゆっくり）
//              CurtainOpen  = カーテンが開く（InOutBack・ぱっと）
//=============================================================
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// Loadingシーンのカーテン演出を担当。
/// CurtainClose / CurtainOpen をコルーチンとして呼び出す。
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
  [SerializeField] private float _closeDuration = 0.9f; // 閉じる時間（ゆっくり）
  [SerializeField] private float _openDuration = 0.8f; // 開く時間（ぱっと）

  [Header("InSideカーテン座標 (Pos X)")]
  [Tooltip("画面外（開いた状態）の左カーテンX座標")]
  [SerializeField] private float _inLeftOpenX = -585f;
  [Tooltip("中央（閉じた状態）の左カーテンX座標")]
  [SerializeField] private float _inLeftCloseX = 0f;
  [Tooltip("画面外（開いた状態）の右カーテンX座標")]
  [SerializeField] private float _inRightOpenX = 585f;
  [Tooltip("中央（閉じた状態）の右カーテンX座標")]
  [SerializeField] private float _inRightCloseX = 0f;

  [Header("Outsideカーテン設定")]
  [Tooltip("カーテンが開くときの拡大倍率")]
  [SerializeField] private float _outsideOpenScale = 1.3f;

  //=========================================================
  // 公開メソッド
  //=========================================================

  /// <summary>
  /// カーテンが閉じる演出。Loadingシーン開幕時に呼ぶ。
  /// InSide  : 画面外 → 中央へゆっくり閉じる（Linear）
  /// Outside : alpha:1・等倍のまま静止
  /// </summary>
  public IEnumerator CurtainClose()
  {
    // Outside：alpha:1・等倍でリセット（静止）
    _curtainOutside.localScale = Vector3.one;
    _curtainOutsideImage.color = new Color(
        _curtainOutsideImage.color.r,
        _curtainOutsideImage.color.g,
        _curtainOutsideImage.color.b,
        1f);

    // InSide：画面外（開いた位置）にリセット
    _curtainInLeft.anchoredPosition = new Vector2(_inLeftOpenX, _curtainInLeft.anchoredPosition.y);
    _curtainInRight.anchoredPosition = new Vector2(_inRightOpenX, _curtainInRight.anchoredPosition.y);

    bool done = false;

    DOTween.Sequence()
        // InSide：画面外 → 中央へゆっくり閉じる（Linear）
        .Join(_curtainInLeft.DOAnchorPosX(_inLeftCloseX, _closeDuration)
            .SetEase(Ease.Linear))
        .Join(_curtainInRight.DOAnchorPosX(_inRightCloseX, _closeDuration)
            .SetEase(Ease.Linear))
        // Outside：動かさない
        .OnComplete(() => done = true);

    yield return new WaitUntil(() => done);
  }

  /// <summary>
  /// カーテンが開く演出。次シーン遷移直前に呼ぶ。
  /// InSide  : 中央 → 画面外へぱっと開く（InOutBack）
  /// Outside : alphaそのまま・拡大して消える（InOutBack）
  /// </summary>
  public IEnumerator CurtainOpen()
  {
    bool done = false;

    DOTween.Sequence()
        // InSide：中央 → 画面外へぱっと開く（InOutBack）
        .Join(_curtainInLeft.DOAnchorPosX(_inLeftOpenX, _openDuration)
            .SetEase(Ease.InOutBack))
        .Join(_curtainInRight.DOAnchorPosX(_inRightOpenX, _openDuration)
            .SetEase(Ease.InOutBack))
        // Outside：拡大のみ、alphaはいじらない（InOutBack）
        .Join(_curtainOutside.DOScale(Vector3.one * _outsideOpenScale, _openDuration)
            .SetEase(Ease.InOutBack))
        .OnComplete(() => done = true);

    yield return new WaitUntil(() => done);
  }
}
