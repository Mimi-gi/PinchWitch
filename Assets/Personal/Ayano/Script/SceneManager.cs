using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneLoader : MonoBehaviour
{
  [SerializeField] private string _targetSceneName;
  [SerializeField] private GameObject _confirmDialog;

  // シーン遷移（ボタンにアタッチして使う）
  public void LoadScene()
  {
    SceneManager.LoadScene(_targetSceneName);
  }

  public void ReloadCurrentScene()
  {
    SceneManager.LoadScene(SceneManager.GetActiveScene().name);
  }

  // 終了ボタンを押したとき（ダイアログ表示）
  public void OnQuitButtonPressed()
  {
    if (_confirmDialog != null)
    {
      _confirmDialog.SetActive(true);
    }
    else
    {
      QuitGame(); // ダイアログなしの場合は即終了
    }
  }

  // ダイアログの「はい」
  public void OnConfirmYes()
  {
    QuitGame();
  }

  // ダイアログの「いいえ」
  public void OnConfirmNo()
  {
    if (_confirmDialog != null)
    {
      _confirmDialog.SetActive(false);
    }
  }

  // 終了処理本体
  private void QuitGame()
  {
#if UNITY_EDITOR
    UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
  }
}
