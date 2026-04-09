using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class ReloadSceneOnKeyPress : MonoBehaviour
{
  [SerializeField] private string sceneName = "DemoScene";
  [SerializeField] private KeyCode reloadKey = KeyCode.R;

  private bool _isLoading;

  private void Update()
  {
    if (_isLoading || !Input.GetKeyDown(reloadKey))
    {
      return;
    }

    ReloadScene();
  }

  [ContextMenu("Reload Scene")]
  public void ReloadScene()
  {
    if (_isLoading)
    {
      return;
    }

    var targetSceneName = string.IsNullOrEmpty(sceneName)
      ? SceneManager.GetActiveScene().name
      : sceneName;

    if (string.IsNullOrEmpty(targetSceneName))
    {
      Debug.LogWarning("再読み込み対象のシーン名が見つからないため、中断しました。", this);
      return;
    }

    if (!Application.CanStreamedLevelBeLoaded(targetSceneName))
    {
      Debug.LogWarning($"シーン '{targetSceneName}' を読み込めません。Build Settings を確認してください。", this);
      return;
    }

    _isLoading = true;
    SceneManager.LoadScene(targetSceneName, LoadSceneMode.Single);
  }
}
