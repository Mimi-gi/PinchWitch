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
  private const float MinDisplayTime = 0.5f;

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
    _progressBar = GetComponentInChildren<Slider>();
    _curtain = FindObjectOfType<LoadingCurtainDirector>();
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
    // ① Loadingシーン開幕：FadeAndLoadで黒くなったまま来るので透明に戻す
    yield return StartCoroutine(GameSceneManager.Instance.FadeIn());

    // ② カーテンが画面外から中央へゆっくり閉じる
    if (_curtain != null)
    {
      yield return StartCoroutine(_curtain.CurtainClose());
    }

    // ③ バックグラウンドで次シーンの非同期ロード開始
    float elapsed = 0f;
    AsyncOperation op = GameSceneManager.StartPendingLoad();
    op.allowSceneActivation = false; // ロード完了しても即座に切り替えない

    // ④ ロード完了 かつ 最低表示時間を超えるまでループ
    while (!op.isDone)
    {
      elapsed += Time.deltaTime;

      // 進捗バーを更新（AsyncOperationの最大値は0.9）
      float progress = Mathf.Clamp01(op.progress / 0.9f);
      if (_progressBar != null) _progressBar.value = progress;

      // ⑤ ロード完了 かつ 最低表示時間を超えたら遷移処理へ
      if (op.progress >= 0.9f && elapsed >= MinDisplayTime)
      {
        // ⑥ カーテンが中央から画面外へぱっと開く（先に見せる）
        if (_curtain != null)
        {
          yield return StartCoroutine(_curtain.CurtainOpen());
        }

        // ⑦ カーテンが開いた後にフェードアウト（素の状態を隠す）
        GameSceneManager.Instance?.FadeOut();
        yield return new WaitForSeconds(0.3f);

        // ⑧ 次シーンへ切り替え許可
        op.allowSceneActivation = true;

        // ⑨ シーン切り替え後のFadeInをGameSceneManager側に任せる
        GameSceneManager.Instance.StartFadeInAfterLoad();

        // ⑩ 処理終了
        yield break;
      }

      yield return null;
    }
  }
}
