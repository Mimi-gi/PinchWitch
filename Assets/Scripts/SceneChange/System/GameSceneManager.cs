//=============================================================
// ファイル名  : GameSceneManager.cs
// 作成者     : 彩乃
// 作成日     : 2025/04/10
// 説明       : シーン遷移を一元管理するシングルトン。
//              DontDestroyOnLoadで全シーンをまたいで生存する。
//              シーン名はenumで管理し、必ずLoading経由で遷移する。
//=============================================================

using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// シーン名はここで一元管理。Build Settingsのシーン名と合わせること。
/// ステージはステージ番号で動的指定（例: Stage_1, Stage_2）。
/// </summary>
public enum GameScene
{
  Title,
  StageSelect,
  Tutorial,
  Loading,
}

public class GameSceneManager : MonoBehaviour
{
  //=========================================================
  // シングルトン
  //=========================================================

  private static GameSceneManager _instance;
  public static GameSceneManager Instance => _instance;

  //=========================================================
  // シーン名設定（Inspectorで変更可能、デフォルト値あり）
  //=========================================================

  [Header("シーン名設定")]
  [SerializeField] private string _titleSceneName = "Title";
  [SerializeField] private string _selectSceneName = "StageSelect";
  [SerializeField] private string _tutorialSceneName = "Tutorial";
  [SerializeField] private string _loadingSceneName = "Loading";

  /// <summary>ステージシーンの命名プレフィックス。Stage_1, Stage_2 ...の形式。</summary>
  [SerializeField] private string _stageScenePrefix = "Stage_";

  //=========================================================
  // ステージ情報
  //=========================================================

  /// <summary>ステージ総数。ステージを追加したら変更すること。</summary>
  public static int TotalStageCount = 3;

  /// <summary>現在プレイ中のステージ番号（1始まり）。</summary>
  public static int CurrentStageNum = 1;

  /// <summary>クリア済みステージ数。PlayerPrefsで永続保存される。</summary>
  public static int ClearStageCount = 0;

  //=========================================================
  // デバッグ設定
  //=========================================================

  [Header("画面フェード")]
  [SerializeField] private CanvasGroup _fadeCanvasGroup; // ← 追加
  [SerializeField] private float _fadeDuration = 0.3f;   // ← 追加

  [Header("デバッグ設定")]
  [SerializeField] private bool _isDebug = false;
  [SerializeField] private int _debugStartStage = 1;

  //=========================================================
  // 内部変数
  //=========================================================

  /// <summary>Loadingシーン経由で読み込む遷移先シーン名。</summary>
  private static string _pendingSceneName;

  //=========================================================
  // Unity イベント
  //=========================================================

  private void Awake()
  {
    if (_instance == null)
    {
      _instance = this;
      DontDestroyOnLoad(gameObject);

      // FadePanelを最初は透明にする
      if (_fadeCanvasGroup != null)
      {
        _fadeCanvasGroup.alpha = 0f;
      }


      // 起動時にPlayerPrefsからクリア済みステージ数を読み込む
      ClearStageCount = PlayerPrefs.GetInt("ClearStageCount", 0);

      if (_isDebug)
      {
        CurrentStageNum = _debugStartStage;
      }
    }
    else
    {
      Destroy(gameObject);
    }
  }

  //=========================================================
  // Public API（シーン遷移）
  //=========================================================

  /// <summary>タイトルシーンへ遷移する。</summary>
  public static void GoTitle()
  {
    LoadViaLoading(_instance._titleSceneName);
  }

  /// <summary>ステージセレクトシーンへ遷移する。</summary>
  public static void GoStageSelect()
  {
    LoadViaLoading(_instance._selectSceneName);
  }

  /// <summary>チュートリアルシーンへ遷移する。</summary>
  public static void GoTutorial()
  {
    LoadViaLoading(_instance._tutorialSceneName);
  }

  /// <summary>
  /// 指定したステージ番号のシーンへ遷移する（1始まり）。
  /// シーン名は _stageScenePrefix + stageNum で決まる（例: Stage_1）。
  /// </summary>
  public static void GoStage(int stageNum)
  {
    CurrentStageNum = stageNum;
    string sceneName = _instance._stageScenePrefix + stageNum;
    LoadViaLoading(sceneName);
  }

  /// <summary>現在のステージをリトライする。</summary>
  public static void RetryCurrentStage()
  {
    GoStage(CurrentStageNum);
  }

  /// <summary>
  /// 次のステージへ遷移する。
  /// クリア済みステージ数をPlayerPrefsに保存してからステージを進める。
  /// 最終ステージクリア時はステージセレクトへ戻る。
  /// </summary>
  public static void GoNextStage()
  {
    ClearStageCount = Mathf.Max(ClearStageCount, CurrentStageNum);

    // クリア済みステージ数をPlayerPrefsに保存する
    PlayerPrefs.SetInt("ClearStageCount", ClearStageCount);
    PlayerPrefs.Save();

    if (CurrentStageNum < TotalStageCount)
    {
      GoStage(CurrentStageNum + 1);
    }
    else
    {
      GoStageSelect(); // 全ステージクリア時はセレクトへ
    }
  }

  /// <summary>任意のシーン名で遷移する（柔軟対応用）。</summary>
  public static void GoScene(string sceneName)
  {
    LoadViaLoading(sceneName);
  }

  //=========================================================
  // Loading経由の遷移
  //=========================================================

  /// <summary>
  /// 遷移先シーン名を記憶してからLoadingシーンへ飛ぶ。
  /// 全シーン遷移はこのメソッドを経由する。
  /// </summary>
  private static void LoadViaLoading(string targetScene)
  {

    Debug.Log($"[GameSceneManager] LoadViaLoading 呼ばれた: {targetScene}");
    _pendingSceneName = targetScene;
    _instance.StartCoroutine(_instance.FadeAndLoad()); // ← 変更
  }

  private IEnumerator FadeAndLoad()
  {
    Debug.Log("[GameSceneManager] FadeAndLoad 開始");

    if (_fadeCanvasGroup != null)
    {
      Debug.Log("[GameSceneManager] フェード開始");
      _fadeCanvasGroup.alpha = 0f;
      float elapsed = 0f;
      while (elapsed < _fadeDuration)
      {
        elapsed += Time.deltaTime;
        _fadeCanvasGroup.alpha = Mathf.Clamp01(elapsed / _fadeDuration);
        yield return null;
      }
      Debug.Log("[GameSceneManager] フェード完了");
    }
    else
    {
      Debug.LogWarning("[GameSceneManager] _fadeCanvasGroup が null！");
    }

    Debug.Log($"[GameSceneManager] LoadScene 呼ぶ: {_instance._loadingSceneName}");
    SceneManager.LoadScene(_instance._loadingSceneName);
  }

  /// <summary>
  /// Loadingシーンから呼ぶ。
  /// 記憶しておいた遷移先シーンの非同期ロードを開始し、
  /// AsyncOperationを返す。進捗管理は呼び出し側（LoadingSceneController）で行う。
  /// </summary>
  public static AsyncOperation StartPendingLoad()
  {
    return SceneManager.LoadSceneAsync(_pendingSceneName);
  }

  /// <summary>フェードパネルを即座に透明にする。</summary>
  public void ClearFade()
  {
    if (_fadeCanvasGroup != null)
    {
      _fadeCanvasGroup.alpha = 0f;
    }
  }
}
