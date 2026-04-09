//=============================================================
// ファイル名  : ResultPanelController.cs
// 作成者     : 彩乃
// 作成日     : 2025/04/10
// 説明       : ステージクリア時に表示するパネル。
//              クリア判定側から Show() を呼んでもらう。
//              _isDebug にチェックを入れると数秒後に自動表示される。
//=============================================================

using System.Collections;
using UnityEngine;

public class ResultPanelController : MonoBehaviour
{
  //=========================================================
  // フィールド
  //=========================================================

  /// <summary>クリアパネル本体</summary>
  [SerializeField] private GameObject _resultPanel;

  [Header("デバッグ設定")]
  /// <summary>チェックを入れると数秒後にクリアパネルが自動表示される</summary>
  [SerializeField] private bool _isDebug = false;

  /// <summary>デバッグ時に自動表示するまでの秒数</summary>
  [SerializeField] private float _debugDisplayDelay = 3f;

  //=========================================================
  // Unity イベント
  //=========================================================

  private void Start()
  {
    _resultPanel.SetActive(false);

    if (_isDebug)
    {
      StartCoroutine(DebugAutoShowCoroutine());
    }
  }

  //=========================================================
  // デバッグ用
  //=========================================================

  /// <summary>
  /// デバッグ用。数秒後にクリアパネルを自動表示するコルーチン。
  /// </summary>
  private IEnumerator DebugAutoShowCoroutine()
  {
    yield return new WaitForSeconds(_debugDisplayDelay);
    Show();
  }

  //=========================================================
  // Public メソッド（外部からの繋ぎ口）
  //=========================================================

  /// <summary>
  /// クリア判定側からこれを呼んでもらう。繋ぎ口。
  /// </summary>
  public void Show()
  {
    Time.timeScale = 0f;
    _resultPanel.SetActive(true);
  }

  //=========================================================
  // ボタンイベント
  //=========================================================

  /// <summary>NextStageボタンに繋ぐ。</summary>
  public void OnNextStageButtonPressed()
  {
    Time.timeScale = 1f;
    GameSceneManager.GoNextStage();
  }

  /// <summary>
  /// Titleボタンに繋ぐ。機能として保持。
  /// </summary>
  public void OnTitleButtonPressed()
  {
    Time.timeScale = 1f;
    GameSceneManager.GoTitle();
  }
}
