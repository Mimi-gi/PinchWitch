using UnityEngine;

public class UIManager : MonoBehaviour
{
  [Header("ゲームクリア時に表示するUI")]
  [SerializeField] private GameObject gameClearUI;
  [SerializeField] private ResultPanelController resultPanelController;
  [SerializeField] private ResultPanelManager resultPanelManager;

  private void Awake()
  {
    CacheResultPanel();
  }

  private void Start()
  {
    if (gameClearUI == null)
    {
      gameClearUI = GameObject.Find("GameClear_text");
    }

    // ゲーム開始時はクリアのUIは消しておく
    if (gameClearUI != null)
    {
      gameClearUI.SetActive(false);
    }
  }

  /// <summary>
  /// GoalControllerなどから直接呼ばれる想定の描画関数
  /// </summary>
  public void GameClear()
  {
    if (gameClearUI != null)
    {
      gameClearUI.SetActive(true);
    }

    ShowResultPanel();
  }

  private void CacheResultPanel()
  {
    if (resultPanelController == null)
    {
      resultPanelController = FindFirstObjectByType<ResultPanelController>(FindObjectsInactive.Include);
    }

    if (resultPanelManager == null)
    {
      resultPanelManager = FindFirstObjectByType<ResultPanelManager>(FindObjectsInactive.Include);
    }
  }

  private void ShowResultPanel()
  {
    CacheResultPanel();

    if (resultPanelController != null)
    {
      resultPanelController.Show();
      return;
    }

    if (resultPanelManager != null)
    {
      resultPanelManager.Show();
      return;
    }

    Debug.LogWarning("[UIManager] ResultPanel が見つかりません");
  }
}
