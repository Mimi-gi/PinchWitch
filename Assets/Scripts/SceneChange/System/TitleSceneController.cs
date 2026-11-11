//=============================================================
// ファイル名  : TitleSceneController.cs
// 作成者     : 彩乃
// 作成日     : 2025/04/10
// 説明       : Titleシーン専用。ボタンイベントと初回起動判定を担当。
//              stage_1クリア前はOpeningへ、クリア後はStageSelectへ遷移する。
//              終了ボタンから直接アプリを終了できる。
//=============================================================

using UnityEngine;

/// <summary>
/// Titleシーン専用。ボタンイベントと初回起動判定を担当。
/// </summary>
public class TitleSceneController : MonoBehaviour
{
  private const int FirstStageNum = 1;

  [SerializeField] private KeyCode _startKey = KeyCode.Space;

  // 押すとクリア状況を消してタイトルを読み直す(展示などで最初から遊ばせる用)。
  [SerializeField] private KeyCode _resetSaveKey = KeyCode.R;

  private bool _isStarting;

  private void Update()
  {
    if (_isStarting)
    {
      return;
    }

    if (_resetSaveKey != KeyCode.None && Input.GetKeyDown(_resetSaveKey))
    {
      ResetSaveData();
      return;
    }

    if (Input.GetKeyDown(_startKey))
    {
      StartFromTitleInput();
    }
  }

  // 読み直しのフェードが、消えたことの合図になる。
  private void ResetSaveData()
  {
    _isStarting = true;
    GameSceneManager.DeleteSaveDataAndGoTitle();
  }

  /// <summary>
  /// Startボタンに繋ぐ。
  /// stage_1クリア前はOpeningへ、クリア後はStageSelectへ。
  /// </summary>
  public void OnStartButtonPressed()
  {
    if (_isStarting)
    {
      return;
    }

    StartFromTitleInput();
  }

  private void StartFromTitleInput()
  {
    _isStarting = true;
    StartGame();
  }

  private void StartGame()
  {
    if (!StageProgressManager.IsCleared(FirstStageNum))
    {
      GameSceneManager.GoOpening();
      return;
    }

    GameSceneManager.GoStageSelect();
  }

  /// <summary>
  /// 終了ボタンに繋ぐ。
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
