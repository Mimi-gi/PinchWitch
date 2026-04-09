// Copyright (c) 2025 チーム名. All rights reserved.

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
