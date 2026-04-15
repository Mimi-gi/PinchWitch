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

  /// <summary>カーテン演出担当。なければnullのまま動作する。</summary>
  private LoadingCurtainDirector _curtain;

  //=========================================================
  // Unity イベント
  //=========================================================
  private void Awake()
  {
    // ★ FindObjectOfType でシーン全体から探す（兄弟階層対応）
    _progressBar = GetComponentInChildren<Slider>();
    _curtain = FindObjectOfType<LoadingCurtainDirector>();

    // デバッグログで取得確認
    Debug.Log($"[Loading] _curtain: {(_curtain != null ? "取得OK" : "null！")}");
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
    // フェードを即座に透明に戻す
    GameSceneManager.Instance?.ClearFade();
    Debug.Log("[Loading] ClearFade 呼び出し");

    // 開幕カーテン演出
    if (_curtain != null)
    {
      Debug.Log("[Loading] PlayOpen 開始");
      yield return StartCoroutine(_curtain.PlayOpen());
      Debug.Log("[Loading] PlayOpen 完了");
    }
    else
    {
      Debug.LogWarning("[Loading] _curtain が null のため演出スキップ");
    }

    float elapsed = 0f;
    AsyncOperation op = GameSceneManager.StartPendingLoad();
    op.allowSceneActivation = false;
    Debug.Log("[Loading] 非同期ロード開始");

    while (!op.isDone)
    {
      elapsed += Time.deltaTime;
      float progress = Mathf.Clamp01(op.progress / 0.9f);
      if (_progressBar != null) _progressBar.value = progress;

      if (op.progress >= 0.9f && elapsed >= MinDisplayTime)
      {
        if (_curtain != null)
        {
          Debug.Log("[Loading] PlayClose 開始");
          yield return StartCoroutine(_curtain.PlayClose());
          Debug.Log("[Loading] PlayClose 完了");
        }
        op.allowSceneActivation = true;
      }
      yield return null;
    }
  }
}
