//=============================================================
// ファイル名  : LoadingSceneController.cs
// 作成者     : 彩乃
// 作成日     : 2025/04/15
// 説明       : Loadingシーンに置く。
//              GameSceneManagerが記憶した遷移先シーンを非同期ロードし、
//              進捗をProgressBarに反映する。
//              最低表示時間を設けることでローディングの瞬殺を防ぐ。
//=============================================================

using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Loadingシーンに置く。進捗をUIに反映しながら非同期ロードする。
/// </summary>
public class LoadingSceneController : MonoBehaviour
{
  //=========================================================
  // 定数
  //=========================================================

  /// <summary>最低表示秒数。</summary>
  private const float MinDisplayTime = 1.5f;

  //=========================================================
  // 内部変数
  //=========================================================

  /// <summary>進捗バー。なければnullのまま動作する。</summary>
  private Slider _progressBar;

  //=========================================================
  // Unity イベント
  //=========================================================

  private void Awake()
  {
    // 子オブジェクトからSliderを自動取得。なくてもエラーにならない。
    _progressBar = GetComponentInChildren<Slider>();
  }

  private void Start()
  {
    StartCoroutine(LoadRoutine());
  }

  //=========================================================
  // ローディング処理
  //=========================================================

  private IEnumerator LoadRoutine()
  {
    float elapsed = 0f;
    AsyncOperation op = GameSceneManager.StartPendingLoad();
    op.allowSceneActivation = false;

    while (!op.isDone)
    {
      elapsed += Time.deltaTime;

      // 進捗を0〜1に変換（AsyncOperationは最大0.9まで）
      float progress = Mathf.Clamp01(op.progress / 0.9f);

      if (_progressBar != null)
      {
        _progressBar.value = progress;
      }

      // ロード完了 かつ 最低表示時間を超えたら遷移
      if (op.progress >= 0.9f && elapsed >= MinDisplayTime)
      {
        op.allowSceneActivation = true;
      }

      yield return null;
    }
  }
}
