//=============================================================
// ファイル名  : StageSelectController.cs
// 作成者     : 彩乃
// 作成日     : 2025/04/10
// 説明       : StageSelectシーン専用。ボタンイベントとステージ解放状態の表示を担当。
//              クリア済みステージ数に応じてボタンのロック状態を更新する。
//=============================================================

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// StageSelectシーン専用。
/// ステージボタンの表示制御と選択処理を担当。
/// </summary>
public class StageSelectController : MonoBehaviour
{
  [SerializeField] private Button[] _stageButtons; // ステージ数分ボタンを登録

  private void Start()
  {
    UpdateStageButtons();
  }

  /// <summary>
  /// クリア済みステージ数に応じてボタンのロック状態を更新する。
  /// </summary>
  private void UpdateStageButtons()
  {
    for (int i = 0; i < _stageButtons.Length; i++)
    {
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
    GameSceneManager.GoStage(stageNum);
  }

  /// <summary>
  /// タイトルに戻るボタンに繋ぐ。
  /// </summary>
  public void OnBackButtonPressed()
  {
    GameSceneManager.GoTitle();
  }
}
