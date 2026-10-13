using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class ResultPanelManager : MonoBehaviour
{
  private const string StageScenePrefix = "Stage_";
  private const int FirstStageNum = 1;

  [Header("ボタン")]
  public Button nextStage_Button;
  public Button stageSelect_Button;

  private bool _callbacksRegistered;
  private bool _isShowing;

  private void Awake()
  {
    CacheButtons();
  }

  private void Start()
  {
    RegisterButtonCallbacks();
    if (!_isShowing)
    {
      Hide();
    }
  }

  private void OnDestroy()
  {
    if (!_callbacksRegistered)
    {
      return;
    }

    if (nextStage_Button != null)
    {
      nextStage_Button.onClick.RemoveListener(OnNextStageButtonPressed);
    }

    if (stageSelect_Button != null)
    {
      stageSelect_Button.onClick.RemoveListener(OnStageSelectButtonPressed);
    }
  }

  public void Show()
  {
    CacheButtons();
    RegisterButtonCallbacks();
    _isShowing = true;
    Time.timeScale = 0f;
    gameObject.SetActive(true);
  }

  public void Hide()
  {
    _isShowing = false;
    gameObject.SetActive(false);
  }

  private void CacheButtons()
  {
    if (nextStage_Button == null)
    {
      Transform nextStage = transform.Find("NextStage_Button");
      if (nextStage != null)
      {
        nextStage_Button = nextStage.GetComponent<Button>();
      }
    }

    if (stageSelect_Button == null)
    {
      Transform stageSelect = transform.Find("StageSelect_Button");
      if (stageSelect != null)
      {
        stageSelect_Button = stageSelect.GetComponent<Button>();
      }
    }
  }

  private void RegisterButtonCallbacks()
  {
    if (_callbacksRegistered)
    {
      return;
    }

    if (nextStage_Button == null || stageSelect_Button == null)
    {
      Debug.LogWarning("[ResultPanelManager] ボタン参照が見つかりません。", this);
      return;
    }

    nextStage_Button.onClick.AddListener(OnNextStageButtonPressed);
    stageSelect_Button.onClick.AddListener(OnStageSelectButtonPressed);
    _callbacksRegistered = true;
  }

  //NextStageボタン押した時の処理
  public void OnNextStageButtonPressed()
  {
    Time.timeScale = 1f;

    if (!TryGetCurrentStageNum(out int currentStageNum))
    {
      Debug.LogWarning($"[ResultPanelManager] 現在のシーン名 '{SceneManager.GetActiveScene().name}' からステージ番号を取得できません。", this);
      GameSceneManager.GoNextStage();
      return;
    }

    SaveClearStageProgress(currentStageNum);

    if (currentStageNum >= GameSceneManager.TotalStageCount)
    {
      GameSceneManager.GoStageSelect();
      return;
    }

    // GoStage はステージデータ(共通シーン)と個別シーンのどちらで読むかを判断する。
    GameSceneManager.GoStage(currentStageNum + 1);
  }

  //StageSelectボタン押した時の処理
  public void OnStageSelectButtonPressed()
  {
    Time.timeScale = 1f;
    GameSceneManager.GoStageSelect();
  }

  private static bool TryGetCurrentStageNum(out int stageNum)
  {
    stageNum = 0;

    string sceneName = SceneManager.GetActiveScene().name;
    if (string.IsNullOrEmpty(sceneName))
    {
      return false;
    }

    if (sceneName.StartsWith(StageScenePrefix, StringComparison.OrdinalIgnoreCase)
        && int.TryParse(sceneName.Substring(StageScenePrefix.Length), out stageNum))
    {
      return stageNum >= FirstStageNum && stageNum <= GameSceneManager.TotalStageCount;
    }

    // 共通シーン(Stage_Runtime)のようにシーン名に番号が無い場合は、読み込み時のステージ番号を使う。
    stageNum = GameSceneManager.CurrentStageNum;
    return stageNum >= FirstStageNum && stageNum <= GameSceneManager.TotalStageCount;
  }

  private static void SaveClearStageProgress(int clearedStageNum)
  {
    StageProgressManager.RecordClear(clearedStageNum);
  }
}
