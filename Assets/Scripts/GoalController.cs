using System.Collections;
using UnityEngine;

public class GoalController : MonoBehaviour
{
  [SerializeField] private float _stageSelectDelaySeconds = 0.5f;

  private bool _hasCleared;

  public void OnTriggerEnter2D(Collider2D collision)
  {
    if (collision.CompareTag("Player") && !_hasCleared)
    {
      _hasCleared = true;
      StartCoroutine(ClearAndReturnStageSelect(collision));
    }
  }

  private IEnumerator ClearAndReturnStageSelect(Collider2D playerCollider)
  {
    StageProgressManager.RecordCurrentStageClear();

    if (AudioManager.Instance != null)
    {
      AudioManager.Instance.PlaySE(AudioManager.Instance.SE_Clear);
    }

    Debug.Log("Goal!");

    if (playerCollider.TryGetComponent(out Wall2DPlayerAnimator playerAnimator))
    {
      playerAnimator.PlayGoalReaction();
    }

    if (playerCollider.TryGetComponent(out Wall2DPlayerController playerController))
    {
      playerController.MoveStop();
    }

    if (_stageSelectDelaySeconds > 0f)
    {
      yield return new WaitForSeconds(_stageSelectDelaySeconds);
    }

    GameSceneManager.GoStageSelect();
  }
}
