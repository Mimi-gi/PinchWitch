//=============================================================
// ファイル名  : StageSelectController.cs
// 作成者     : 彩乃
// 作成日     : 2025/04/10
// 説明       : StageSelectシーン専用。ボタンイベント、ステージ解放状態の表示、
//              掴んだステージオブジェクトからのシーン遷移を担当する。
//=============================================================

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// StageSelectシーン専用。
/// ステージボタンの表示制御と、掴んだステージオブジェクトからの選択処理を担当。
/// </summary>
public class StageSelectController : MonoBehaviour
{
  [SerializeField] private Button[] _stageButtons; // ステージ数分ボタンを登録
  [SerializeField] private HandPinchController _handPinchController;
  [SerializeField] private StageSelectTrumpLoadingController _loadingController;
  [SerializeField] private float _requiredGrabSeconds = 0.8f;

  private Rigidbody _grabbedStageBody;
  private StageSelectGrabTarget _grabbedStageTarget;
  private float _grabStartedTime;
  private bool _isTransitioning;

  private void OnEnable()
  {
    SubscribeGrabEvents();
  }

  private void Start()
  {
    if (_loadingController == null)
    {
      _loadingController = FindFirstObjectByType<StageSelectTrumpLoadingController>(FindObjectsInactive.Include);
    }

    UpdateStageButtons();
  }

  private void OnDisable()
  {
    UnsubscribeGrabEvents();
    ClearGrabSelection();
  }

  private void Update()
  {
    if (_isTransitioning || _grabbedStageTarget == null)
    {
      return;
    }

    if (Time.unscaledTime - _grabStartedTime < _requiredGrabSeconds)
    {
      return;
    }

    GoStage(_grabbedStageTarget.StageNum);
  }

  /// <summary>
  /// クリア済みステージ数に応じてボタンのロック状態を更新する。
  /// </summary>
  private void UpdateStageButtons()
  {
    if (_stageButtons == null)
    {
      return;
    }

    for (int i = 0; i < _stageButtons.Length; i++)
    {
      if (_stageButtons[i] == null)
      {
        continue;
      }

      // クリア済みステージ数 + 1 まで選択可能（1始まり）
      bool isUnlocked = i <= GameSceneManager.ClearStageCount;
      _stageButtons[i].interactable = isUnlocked;
    }
  }

  /// <summary>
  /// 各ステージボタンのOnClickに繋ぐ。ステージ番号は1始まり。
  /// </summary>
  public void OnStageButtonPressed(int stageNum)
  {
    GoStage(stageNum);
  }

  /// <summary>
  /// タイトルに戻るボタンに繋ぐ。
  /// </summary>
  public void OnBackButtonPressed()
  {
    GameSceneManager.GoTitle();
  }

  private void SubscribeGrabEvents()
  {
    if (_handPinchController == null)
    {
      _handPinchController = FindFirstObjectByType<HandPinchController>(FindObjectsInactive.Include);
    }

    if (_handPinchController == null)
    {
      return;
    }

    _handPinchController.GrabStarted -= HandleGrabStarted;
    _handPinchController.GrabReleased -= HandleGrabReleased;
    _handPinchController.GrabStarted += HandleGrabStarted;
    _handPinchController.GrabReleased += HandleGrabReleased;
  }

  private void UnsubscribeGrabEvents()
  {
    if (_handPinchController == null)
    {
      return;
    }

    _handPinchController.GrabStarted -= HandleGrabStarted;
    _handPinchController.GrabReleased -= HandleGrabReleased;
  }

  private void HandleGrabStarted(Rigidbody grabbedBody)
  {
    if (_isTransitioning || grabbedBody == null)
    {
      return;
    }

    StageSelectGrabTarget grabTarget = ResolveGrabTarget(grabbedBody);
    if (grabTarget == null || !IsValidStageNum(grabTarget.StageNum))
    {
      ClearGrabSelection();
      return;
    }

    _grabbedStageBody = grabbedBody;
    _grabbedStageTarget = grabTarget;
    _grabStartedTime = Time.unscaledTime;
  }

  private void HandleGrabReleased(Rigidbody releasedBody)
  {
    if (releasedBody != null && releasedBody == _grabbedStageBody)
    {
      ClearGrabSelection();
    }
  }

  private void GoStage(int stageNum)
  {
    if (_isTransitioning || !IsValidStageNum(stageNum))
    {
      return;
    }

    int destinationStageNum = ResolveDestinationStageNum(stageNum);
    if (!IsValidStageNum(destinationStageNum))
    {
      return;
    }

    _isTransitioning = true;
    if (AudioManager.Instance != null)
    {
      AudioManager.Instance.PlaySE(AudioManager.Instance.SE_Select);
    }

    if (_loadingController != null)
    {
      _loadingController.Play(_grabbedStageTarget, destinationStageNum, GameSceneManager.GoStage);
      return;
    }

    GameSceneManager.GoStage(destinationStageNum);
  }

  private static int ResolveDestinationStageNum(int selectedStageNum)
  {
    if (StageProgressManager.IsCleared(selectedStageNum))
    {
      return selectedStageNum;
    }

    return StageProgressManager.NextPlayableStageNum;
  }

  private static bool IsValidStageNum(int stageNum)
  {
    return stageNum > 0 && stageNum <= StageProgressManager.TotalStageCount;
  }

  private void ClearGrabSelection()
  {
    _grabbedStageBody = null;
    _grabbedStageTarget = null;
    _grabStartedTime = 0f;
  }

  private static StageSelectGrabTarget ResolveGrabTarget(Rigidbody grabbedBody)
  {
    if (grabbedBody.TryGetComponent(out StageSelectGrabTarget grabTarget))
    {
      return grabTarget;
    }

    grabTarget = grabbedBody.GetComponentInParent<StageSelectGrabTarget>();
    if (grabTarget != null)
    {
      return grabTarget;
    }

    return grabbedBody.GetComponentInChildren<StageSelectGrabTarget>();
  }
}
