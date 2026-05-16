using UnityEngine;

public class GoalController : MonoBehaviour
{
  //UIManagerを保持する変数
  private UIManager uiManager;
  private bool _hasCleared;

  void Start()
  {
    uiManager = Object.FindFirstObjectByType<UIManager>();
  }

  public void OnTriggerEnter2D(Collider2D collision)
  {
    if (collision.CompareTag("Player") && !_hasCleared)
    {
      _hasCleared = true;
      AudioManager.Instance.PlaySE(AudioManager.Instance.SE_Clear);
      Debug.Log("Goal!");

      Wall2DPlayerController playerController = collision.GetComponent<Wall2DPlayerController>();
      Wall2DPlayerAnimator playerAnimator = collision.GetComponent<Wall2DPlayerAnimator>();
      playerAnimator?.PlayGoalReaction();
      playerController.MoveStop();
      // UIManagerが見つかっていれば、ゲームクリアUIを表示する
      if (uiManager != null)
      {
        uiManager.GameClear();
      }
      else
      {
        Debug.Log("uiManager が見つからないよ");
      }
    }
  }
}
