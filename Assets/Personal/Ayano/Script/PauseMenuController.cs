//=============================================================
// ファイル名  : PauseMenuController.cs
// 作成者     : 彩乃
// 作成日     : 2025/04/10
// 説明       : ポーズメニュー管理。
//              ポーズボタンから OnPauseButtonPressed() を呼ぶ。
//              NextStageは念のため保持。
//=============================================================

using UnityEngine;

public class PauseMenuController : MonoBehaviour
{
  //=========================================================
  // フィールド
  //=========================================================

  /// <summary>ポーズメニューのパネル</summary>
  [SerializeField] private GameObject _pauseMenuPanel;

  /// <summary>現在ポーズ中かどうか</summary>
  private bool _isPaused = false;

  //=========================================================
  // Unity イベント
  //=========================================================

  private void Start()
  {
    _pauseMenuPanel.SetActive(false);
  }

  //=========================================================
  // ポーズ制御
  //=========================================================

  /// <summary>
  /// ポーズボタンに繋ぐ。トグル式でON/OFFを切り替える。
  /// </summary>
  public void OnPauseButtonPressed()
  {
    if (_isPaused)
    {
      ResumeGame();
    }
    else
    {
      PauseGame();
    }
  }

  private void PauseGame()
  {
    _isPaused = true;
    Time.timeScale = 0f;
    _pauseMenuPanel.SetActive(true);
  }

  private void ResumeGame()
  {
    _isPaused = false;
    Time.timeScale = 1f;
    _pauseMenuPanel.SetActive(false);
  }

  //=========================================================
  // ボタンイベント
  //=========================================================

  /// <summary>Resumeボタンに繋ぐ。</summary>
  public void OnResumeButtonPressed()
  {
    ResumeGame();
  }

  /// <summary>NextStageボタンに繋ぐ。念のため保持。</summary>
  public void OnNextStageButtonPressed()
  {
    Time.timeScale = 1f;
    GameSceneManager.GoNextStage();
  }

  /// <summary>StageSelectボタンに繋ぐ。</summary>
  public void OnStageSelectButtonPressed()
  {
    Time.timeScale = 1f;
    GameSceneManager.GoStageSelect();
  }

  /// <summary>Titleボタンに繋ぐ。</summary>
  public void OnTitleButtonPressed()
  {
    Time.timeScale = 1f;
    GameSceneManager.GoTitle();
  }

  /// <summary>Quitボタンに繋ぐ。</summary>
  public void OnQuitButtonPressed()
  {
    Time.timeScale = 1f;
#if UNITY_EDITOR
    UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
  }
}
