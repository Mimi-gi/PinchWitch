//=============================================================
// ファイル名  : QuitHandler.cs
// 作成者     : 彩乃
// 作成日     : 2025/04/10
// 説明       : ゲーム終了処理専用。
//              確認ダイアログがある場合は表示してから終了する。
//              ない場合は即座に終了する。
//=============================================================

using UnityEngine;

/// <summary>
/// ゲーム終了処理専用。終了ボタンにアタッチして使う。
/// 確認ダイアログはオプション。アタッチしない場合は即終了。
/// </summary>
public class QuitHandler : MonoBehaviour
{
  //=========================================================
  // フィールド
  //=========================================================

  /// <summary>
  /// 確認ダイアログ。任意。
  /// アタッチしない場合はOnQuitButtonPressed()で即終了する。
  /// </summary>
  [SerializeField] private GameObject _confirmDialog;

  //=========================================================
  // ボタンイベント
  //=========================================================

  /// <summary>終了ボタンに繋ぐ。</summary>
  public void OnQuitButtonPressed()
  {
    if (_confirmDialog != null)
    {
      _confirmDialog.SetActive(true);
    }
    else
    {
      QuitGame();
    }
  }

  /// <summary>確認ダイアログのYesボタンに繋ぐ。</summary>
  public void OnConfirmYes()
  {
    QuitGame();
  }

  /// <summary>確認ダイアログのNoボタンに繋ぐ。</summary>
  public void OnConfirmNo()
  {
    if (_confirmDialog != null)
    {
      _confirmDialog.SetActive(false);
    }
  }

  //=========================================================
  // 終了処理
  //=========================================================

  /// <summary>ゲームを終了する。Editor上ではPlayモードを停止する。</summary>
  private void QuitGame()
  {
#if UNITY_EDITOR
    UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
  }
}
