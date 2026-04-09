using UnityEngine;

/// <summary>
/// ゲーム終了処理専用。終了ボタンにアタッチして使う。
/// </summary>
public class QuitHandler : MonoBehaviour
{
  [SerializeField] private GameObject _confirmDialog;

  public void OnQuitButtonPressed()
  {
    if (_confirmDialog != null)
    {
      _confirmDialog.SetActive(true);
    }
    else
    {
      QuitGame();
    }
  }

  public void OnConfirmYes()
  {
    QuitGame();
  }

  public void OnConfirmNo()
  {
    if (_confirmDialog != null)
    {
      _confirmDialog.SetActive(false);
    }
  }

  private void QuitGame()
  {
#if UNITY_EDITOR
    UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
  }
}
