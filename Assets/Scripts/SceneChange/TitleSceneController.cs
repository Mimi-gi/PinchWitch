//=============================================================
// ファイル名  : TitleSceneController.cs
// 作成者     : 彩乃
// 作成日     : 2025/04/10
// 説明       : Titleシーン専用。ボタンイベントと初回起動判定を担当。
//              初回起動時はTutorialへ、2回目以降はStageSelectへ遷移する。
//              終了ボタンはQuitHandlerを使わない場合の代替として保持。
//=============================================================

using UnityEngine;

/// <summary>
/// Titleシーン専用。ボタンイベントと初回起動判定を担当。
/// </summary>
public class TitleSceneController : MonoBehaviour
{
  private const string _KeyHasLaunched = "HasLaunchedBefore";

  /// <summary>
  /// Startボタンに繋ぐ。
  /// 初回起動はTutorialへ、2回目以降はStageSelectへ。
  /// </summary>
  public void OnStartButtonPressed()
  {
    if (PlayerPrefs.GetInt(_KeyHasLaunched, 0) == 0)
    {
      PlayerPrefs.SetInt(_KeyHasLaunched, 1);
      GameSceneManager.GoTutorial();
    }
    else
    {
      GameSceneManager.GoStageSelect();
    }
  }

  /// <summary>
  /// 終了ボタンに繋ぐ（QuitHandlerを使わない場合の代替）。
  /// </summary>
  public void OnQuitButtonPressed()
  {
#if UNITY_EDITOR
    UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
  }
}
