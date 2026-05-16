using System;
using UnityEngine;

public class GoalController : MonoBehaviour
{
  public event Action<Collider2D> GoalReached;

  public bool OverrideDefaultClearHandling { get; set; }

  private UIManager uiManager;
  private bool _hasReachedGoal;

  private void Start()
  {
    uiManager = UnityEngine.Object.FindFirstObjectByType<UIManager>();
  }

  public void OnTriggerEnter2D(Collider2D collision)
  {
    if (!collision.CompareTag("Player") || _hasReachedGoal)
    {
      return;
    }

    _hasReachedGoal = true;
    Debug.Log("Goal!");

    var playerController = collision.GetComponent<Wall2DPlayerController>();
    var playerAnimator = collision.GetComponent<Wall2DPlayerAnimator>();
    if (playerAnimator != null)
    {
      playerAnimator.PlayGoalReaction();
    }

    if (playerController != null)
    {
      playerController.MoveStop();
    }

    GoalReached?.Invoke(collision);
    if (OverrideDefaultClearHandling)
    {
      return;
    }

    if (uiManager != null)
    {
      uiManager.GameClear();
      return;
    }

    Debug.Log("uiManager が見つからないよ");
  }
}
