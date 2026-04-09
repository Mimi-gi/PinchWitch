using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

/// <summary>
/// シーン名はここで一元管理。Build Settingsのシーン名と合わせること。
/// </summary>
public enum GameScene
{
  Title,
  StageSelect,
  Tutorial,
  Loading,
  // ステージはステージ番号で動的指定（例: "Stage_1"）
}

/// <summary>
/// シーン遷移を一元管理するシングルトン。
/// DontDestroyOnLoadで全シーンをまたいで生存する。
/// </summary>
public class GameSceneManager : MonoBehaviour
{
  // ---- シングルトン ----
  private static GameSceneManager _instance;
  public static GameSceneManager Instance => _instance;

  // ---- シーン名設定（Inspector or デフォルト値） ----
  [Header("シーン名設定")]
  [SerializeField] private string _titleSceneName = "Title";
  [SerializeField] private string _selectSceneName = "StageSelect";
  [SerializeField] private string _tutorialSceneName = "Tutorial";
  [SerializeField] private string _loadingSceneName = "Loading";
  [SerializeField] private string _stageScenePrefix = "Stage_"; // Stage_1, Stage_2 ...

  // ---- ステージ情報 ----
  public static int TotalStageCount = 3;   // ステージ総数（後で差し替え）
  public static int CurrentStageNum = 1;   // 現在のステージ番号（1始まり）
  public static int ClearStageCount = 0;   // クリア済みステージ数

  // ---- デバッグ ----
  [Header("デバッグ")]
  [SerializeField] private bool _isDebug = false;
  [SerializeField] private int _debugStartStage = 1;

  // ---- 内部 ----
  private static string _pendingSceneName; // Loadingシーン経由で読み込む先

  // ==================================================
  private void Awake()
  {
    if (_instance == null)
    {
      _instance = this;
      DontDestroyOnLoad(gameObject);

      if (_isDebug)
        CurrentStageNum = _debugStartStage;
    }
    else
    {
      Destroy(gameObject);
    }
  }

  // ==================================================
  // Public API
  // ==================================================

  /// <summary>タイトルへ</summary>
  public static void GoTitle()
  {
    LoadViaLoading(_instance._titleSceneName);
  }

  /// <summary>ステージセレクトへ</summary>
  public static void GoStageSelect()
  {
    LoadViaLoading(_instance._selectSceneName);
  }

  /// <summary>チュートリアルへ</summary>
  public static void GoTutorial()
  {
    LoadViaLoading(_instance._tutorialSceneName);
  }

  /// <summary>ステージ番号を指定して遷移（1始まり）</summary>
  public static void GoStage(int stageNum)
  {
    CurrentStageNum = stageNum;
    string sceneName = _instance._stageScenePrefix + stageNum; // "Stage_1" など
    LoadViaLoading(sceneName);
  }

  /// <summary>現在のステージをリトライ</summary>
  public static void RetryCurrentStage()
  {
    GoStage(CurrentStageNum);

  }

  /// <summary>次のステージへ（最終ステージならセレクトへ戻る）</summary>
  public static void GoNextStage()
  {
    ClearStageCount = Mathf.Max(ClearStageCount, CurrentStageNum);

    if (CurrentStageNum < TotalStageCount)
    {
      GoStage(CurrentStageNum + 1);
    }
    else
    {
      GoStageSelect(); // 全ステージクリア
    }
  }

  /// <summary>任意のシーン名で直接遷移（柔軟対応用）</summary>
  public static void GoScene(string sceneName)
  {
    LoadViaLoading(sceneName);
  }

  // ==================================================
  // ローディング経由の遷移
  // ==================================================

  private static void LoadViaLoading(string targetScene)
  {
    _pendingSceneName = targetScene;
    SceneManager.LoadScene(_instance._loadingSceneName);
  }

  /// <summary>
  /// Loadingシーンから呼ぶ。
  /// 記憶しておいた遷移先シーンの非同期ロードを開始し、
  /// AsyncOperationを返す。進捗管理は呼び出し側で行う。
  /// </summary>
  public static AsyncOperation StartPendingLoad()
  {
    return SceneManager.LoadSceneAsync(_pendingSceneName);
  }
}
