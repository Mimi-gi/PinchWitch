//=============================================================
// ファイル名  : TutorialController.cs
// 作成者     : 彩乃
// 作成日     : 2025/04/10
// 説明       : Tutorialシーン専用。ボタンイベントを担当。
//              チュートリアル完了後はStageSelectへ遷移する。
//=============================================================

using UnityEngine;

/// <summary>
/// Tutorialシーン専用。チュートリアル完了後にStageSelectへ遷移する。
/// </summary>
public class TutorialController : MonoBehaviour
{
  /// <summary>
  /// 次へボタンに繋ぐ。
  /// </summary>
  public void OnNextButtonPressed()
  {
    GameSceneManager.GoStageSelect();
  }
}
