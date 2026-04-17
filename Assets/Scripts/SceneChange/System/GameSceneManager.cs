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
  // シーン名設定
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
  [Header("デバッグ設定")]
  [SerializeField] private bool _isDebug = false;
  [SerializeField] private int _debugStartStage = 1;

  //=========================================================
  // フェード設定
  //=========================================================
  [Header("画面フェード")]
  [SerializeField] private CanvasGroup _fadeCanvasGroup;
  [SerializeField] private float _fadeDuration = 0.3f;

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
  /// 最終ステージクリア時はステージセレクトへ戻る。
  /// </summary>
  public static void GoNextStage()
  {
    ClearStageCount = Mathf.Max(ClearStageCount, CurrentStageNum);
    PlayerPrefs.SetInt("ClearStageCount", ClearStageCount);
    PlayerPrefs.Save();

    if (CurrentStageNum < TotalStageCount)
    {
      GoStage(CurrentStageNum + 1);
    }
    else
    {
      GoStageSelect();
    }
  }

  /// <summary>任意のシーン名で遷移する（柔軟対応用）。</summary>
  public static void GoScene(string sceneName)
  {
    LoadViaLoading(sceneName);
  }

  //=========================================================
  // フェード
  //=========================================================

  /// <summary>フェードを即座に透明にする。Loadingシーン開始時に呼ぶ。</summary>
  public void ClearFade()
  {
    if (_fadeCanvasGroup != null)
    {
      _fadeCanvasGroup.alpha = 0f;
    }
  }

  /// <summary>フェードを即座に黒くする。次シーン遷移直前に呼ぶ。</summary>
  public void FadeOut()
  {
    if (_fadeCanvasGroup != null)
    {
      _fadeCanvasGroup.alpha = 1f;
    }
  }

  /// <summary>フェードイン（黒→透明）。呼び出し元でyield returnして使う。</summary>
  public IEnumerator FadeIn()
  {
    Debug.Log("[GameSceneManager] FadeIn呼ばれた");
    if (_fadeCanvasGroup == null)
    {
      Debug.LogWarning("[GameSceneManager] _fadeCanvasGroup が null！");
      yield break;
    }

    _fadeCanvasGroup.alpha = 1f;
    Debug.Log("[GameSceneManager] alpha = 1 にセット");
    float elapsed = 0f;
    while (elapsed < _fadeDuration)
    {
      elapsed += Time.deltaTime;
      _fadeCanvasGroup.alpha = Mathf.Clamp01(1f - elapsed / _fadeDuration);
      yield return null;
    }
    _fadeCanvasGroup.alpha = 0f;
    Debug.Log("[GameSceneManager] FadeIn完了 alpha = 0");
  }

  //=========================================================
  // Loading経由の遷移
  //=========================================================
  private static void LoadViaLoading(string targetScene)
  {
    _pendingSceneName = targetScene;
    _instance.StartCoroutine(_instance.FadeAndLoad());
  }

  private IEnumerator FadeAndLoad()
  {
    // フェードアウト
    if (_fadeCanvasGroup != null)
    {
      _fadeCanvasGroup.alpha = 0f;
      float elapsed = 0f;
      while (elapsed < _fadeDuration)
      {
        elapsed += Time.deltaTime;
        _fadeCanvasGroup.alpha = Mathf.Clamp01(elapsed / _fadeDuration);
        yield return null;
      }
    }

    SceneManager.LoadScene(_instance._loadingSceneName);
  }


  /// <summary>
  /// シーン切り替え後にFadeInを実行する。
  /// LoadingSceneControllerが破棄された後も動き続ける。
  /// </summary>
  public void StartFadeInAfterLoad()
  {
    StartCoroutine(FadeInAfterLoad());
  }

  private IEnumerator FadeInAfterLoad()
  {
    // シーン切り替わりを待つ
    yield return null;
    yield return null; // 念のため2フレーム待つ
    Debug.Log("[GameSceneManager] シーン切り替え後FadeIn開始");
    yield return StartCoroutine(FadeIn());
    Debug.Log("[GameSceneManager] シーン切り替え後FadeIn完了");
  }

  /// <summary>
  /// Loadingシーンから呼ぶ。
  /// 記憶しておいた遷移先シーンの非同期ロードを開始する。
  /// </summary>
  public static AsyncOperation StartPendingLoad()
  {
    return SceneManager.LoadSceneAsync(_pendingSceneName);
  }
}
