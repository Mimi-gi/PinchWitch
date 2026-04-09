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
