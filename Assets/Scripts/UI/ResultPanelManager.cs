using UnityEngine;
using UnityEngine.UI;

public class ResultPanelManager : MonoBehaviour
{
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
    GameSceneManager.GoNextStage();
  }

  //StageSelectボタン押した時の処理
  public void OnStageSelectButtonPressed()
  {
    Time.timeScale = 1f;
    GameSceneManager.GoStageSelect();
  }
}
