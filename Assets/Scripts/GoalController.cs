using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class GoalController : MonoBehaviour
{
  [SerializeField] private float _stageSelectDelaySeconds = 0.5f;

  public event Action<Collider2D> GoalReached;

  public bool OverrideDefaultClearHandling { get; set; }

  private bool _hasReachedGoal;

  public void OnTriggerEnter2D(Collider2D collision)
  {
    if (!collision.CompareTag("Player") || _hasReachedGoal)
    {
      return;
    }

    _hasReachedGoal = true;

    // ステージ作成シーンでのテストプレイでは、クリア記録もステージセレクトへの遷移もしない。
    var isAuthoringTest = StageAuthoringMarker.IsAuthoringScene;
    if (!isAuthoringTest)
    {
      StageProgressManager.RecordCurrentStageClear();
    }

    if (AudioManager.Instance != null)
    {
      AudioManager.Instance.PlaySE(AudioManager.Instance.SE_Clear);
    }

    Debug.Log("Goal!");

    if (collision.TryGetComponent(out Wall2DPlayerAnimator playerAnimator))
    {
      playerAnimator.PlayGoalReaction();
    }

    if (collision.TryGetComponent(out Wall2DPlayerController playerController))
    {
      playerController.MoveStop();
    }

    GoalReached?.Invoke(collision);
    if (OverrideDefaultClearHandling)
    {
      return;
    }

    if (isAuthoringTest)
    {
      Debug.Log("[GoalController] テストプレイ: クリア(記録・遷移はしません)");
      return;
    }

    ReturnStageSelectAfterDelayAsync(destroyCancellationToken).Forget();
  }

  private async UniTask ReturnStageSelectAfterDelayAsync(CancellationToken ct)
  {
    if (_stageSelectDelaySeconds > 0f)
    {
      await UniTask.Delay(TimeSpan.FromSeconds(_stageSelectDelaySeconds), cancellationToken: ct);
    }

    GameSceneManager.GoStageSelect();
  }
}
