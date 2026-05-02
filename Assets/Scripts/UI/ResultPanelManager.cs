using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class ResultPanelManager : MonoBehaviour
{
  [Header("ボタン")]
  public Button nextStage_Button;
  public Button stageSelect_Button;
  [Header("シーン名")]
  public const string StageSelectSceneName = "StageSelect";

  private void Start()
  {
    nextStage_Button = this.transform.Find("NextStage_Button").GetComponent<Button>();
    stageSelect_Button = this.transform.Find("StageSelect_Button").GetComponent<Button>();
    nextStage_Button.onClick.AddListener(OnNextStageButtonPressed);
    stageSelect_Button.onClick.AddListener(OnStageSelectButtonPressed);
  }
  //NextStageボタン押した時の処理
  public void OnNextStageButtonPressed()
  {
    Debug.Log("ここはDemoSceneです");// 一旦仮
  }
  //StageSelectボタン押した時の処理
  public void OnStageSelectButtonPressed()
  {
    SceneManager.LoadScene(StageSelectSceneName);
  }
}
