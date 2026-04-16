using UnityEngine;

public class UIManager : MonoBehaviour
{
  [Header("ゲームクリア時に表示するUI")]
  [SerializeField] private GameObject gameClearUI;

  void Start()
  {
    gameClearUI = GameObject.Find("GameClear_text");
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

    // 追加：ResultPanelを表示
    ResultPanelController result = FindObjectOfType<ResultPanelController>();

    if (result != null)
    {
      result.Show();
    }
    else
    {
      Debug.LogWarning("[UIManager] ResultPanelController が見つかりません");
    }
  }
}
