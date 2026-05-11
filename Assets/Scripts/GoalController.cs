using UnityEngine;

public class GoalController : MonoBehaviour
{
  //UIManagerを保持する変数
  private UIManager uiManager;

  void Start()
  {
    uiManager = Object.FindFirstObjectByType<UIManager>();
  }

  void Update()
  {

  }

  public void OnTriggerEnter2D(Collider2D collision)
  {
    if (collision.CompareTag("Player"))
    {
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
