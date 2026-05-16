//=============================================================
// ファイル名  : GameSceneManager.cs
// 作成者     : 彩乃
// 作成日     : 2025/04/10
// 説明       : シーン遷移を一元管理するシングルトン。
//              DontDestroyOnLoadで全シーンをまたいで生存する。
//              シーン名はenumで管理し、フェード付きで直接遷移する。
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
}

public class GameSceneManager : MonoBehaviour
{
  private const int FadeCanvasSortingOrder = 32767;

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
  [SerializeField] private float _fadeDuration = 1f;

  //=========================================================
  // 内部変数
  //=========================================================
  private Coroutine _transitionCoroutine;
  private Coroutine _fadeCoroutine;
  private string _lastSceneStartFadeScenePath;

  //=========================================================
  // Unity イベント
  //=========================================================
  private void Awake()
  {
    if (_instance == null)
    {
      _instance = this;
      DontDestroyOnLoad(gameObject);

      EnsureFadeCanvasGroup();
      SetFadeAlpha(0f);

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

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  private static void BootstrapIfNeeded()
  {
    if (!EnsureInstance())
    {
      return;
    }

    _instance.StartSceneOpeningFadeIfNeeded();
  }

  //=========================================================
  // Public API（シーン遷移）
  //=========================================================
  /// <summary>タイトルシーンへ遷移する。</summary>
  public static void GoTitle()
  {
    if (!EnsureInstance())
    {
      return;
    }

    LoadDirect(_instance._titleSceneName);
  }

  /// <summary>ステージセレクトシーンへ遷移する。</summary>
  public static void GoStageSelect()
  {
    if (!EnsureInstance())
    {
      return;
    }

    LoadDirect(_instance._selectSceneName);
  }

  /// <summary>チュートリアルシーンへ遷移する。</summary>
  public static void GoTutorial()
  {
    if (!EnsureInstance())
    {
      return;
    }

    LoadDirect(_instance._tutorialSceneName);
  }

  /// <summary>
  /// 指定したステージ番号のシーンへ遷移する（1始まり）。
  /// </summary>
  public static void GoStage(int stageNum)
  {
    if (!EnsureInstance())
    {
      return;
    }

    CurrentStageNum = stageNum;
    string sceneName = _instance.ResolveStageSceneName(stageNum);
    LoadDirect(sceneName);
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
    LoadDirect(sceneName);
  }

  /// <summary>現在アクティブなシーンをフェード付きで再読み込みする。</summary>
  public static void ReloadActiveSceneWithFade(float delaySeconds = 0f)
  {
    if (!EnsureInstance())
    {
      return;
    }

    _instance.StartTransition(_instance.ReloadActiveSceneRoutine(delaySeconds));
  }

  //=========================================================
  // フェード
  //=========================================================

  /// <summary>フェードを即座に透明にする。</summary>
  public void ClearFade()
  {
    SetFadeAlpha(0f);
  }

  /// <summary>フェードを即座に黒くする。次シーン遷移直前に呼ぶ。</summary>
  public void FadeOut()
  {
    SetFadeAlpha(1f);
  }

  /// <summary>フェードイン（黒→透明）。呼び出し元でyield returnして使う。</summary>
  public IEnumerator FadeIn()
  {
    yield return FadeTo(1f, 0f);
  }

  /// <summary>フェードアウト（透明→黒）。呼び出し元でyield returnして使う。</summary>
  public IEnumerator FadeOutRoutine()
  {
    yield return FadeTo(0f, 1f);
  }

  private IEnumerator FadeTo(float fromAlpha, float toAlpha)
  {
    EnsureFadeCanvasGroup();
    if (_fadeCanvasGroup == null)
    {
      Debug.LogWarning("[GameSceneManager] _fadeCanvasGroup が null！");
      yield break;
    }

    _fadeCanvasGroup.alpha = fromAlpha;
    if (_fadeDuration <= 0f)
    {
      _fadeCanvasGroup.alpha = toAlpha;
      yield break;
    }

    float elapsed = 0f;
    while (elapsed < _fadeDuration)
    {
      elapsed += Time.unscaledDeltaTime;
      _fadeCanvasGroup.alpha = Mathf.Lerp(fromAlpha, toAlpha, Mathf.Clamp01(elapsed / _fadeDuration));
      yield return null;
    }
    _fadeCanvasGroup.alpha = toAlpha;
  }

  //=========================================================
  // 直接遷移
  //=========================================================
  private static void LoadDirect(string targetScene)
  {
    if (!EnsureInstance())
    {
      return;
    }

    if (string.IsNullOrEmpty(targetScene))
    {
      Debug.LogWarning("[GameSceneManager] 遷移先シーン名が空です。");
      return;
    }

    _instance.StartTransition(_instance.FadeAndLoadDirect(targetScene));
  }

  private string ResolveStageSceneName(int stageNum)
  {
    string sceneName = _stageScenePrefix + stageNum;
    if (Application.CanStreamedLevelBeLoaded(sceneName))
    {
      return sceneName;
    }

    string fallbackPrefix = ResolveLowerFirstStagePrefix();
    string fallbackSceneName = fallbackPrefix + stageNum;
    if (fallbackSceneName != sceneName && Application.CanStreamedLevelBeLoaded(fallbackSceneName))
    {
      return fallbackSceneName;
    }

    return sceneName;
  }

  private string ResolveLowerFirstStagePrefix()
  {
    if (string.IsNullOrEmpty(_stageScenePrefix))
    {
      return _stageScenePrefix;
    }

    char firstCharacter = char.ToLowerInvariant(_stageScenePrefix[0]);
    if (_stageScenePrefix.Length == 1)
    {
      return firstCharacter.ToString();
    }

    return firstCharacter + _stageScenePrefix.Substring(1);
  }

  private IEnumerator FadeAndLoadDirect(string targetScene)
  {
    Time.timeScale = 1f;
    yield return FadeOutRoutine();

    AsyncOperation loadOperation = SceneManager.LoadSceneAsync(targetScene, LoadSceneMode.Single);
    if (loadOperation == null)
    {
      Debug.LogWarning($"[GameSceneManager] シーン '{targetScene}' を読み込めませんでした。");
      yield return FadeIn();
      yield break;
    }

    while (!loadOperation.isDone)
    {
      yield return null;
    }

    yield return null;
    StartFadeInFromBlack();
  }


  /// <summary>
  /// シーン切り替え後にFadeInを実行する。
  /// </summary>
  public void StartFadeInAfterLoad()
  {
    StartFadeInFromBlack();
  }

  private IEnumerator FadeInAfterLoad()
  {
    // シーン切り替わりを待つ
    yield return null;
    yield return null; // 念のため2フレーム待つ
    yield return StartCoroutine(FadeIn());
  }

  private IEnumerator ReloadActiveSceneRoutine(float delaySeconds)
  {
    Time.timeScale = 1f;

    if (delaySeconds > 0f)
    {
      yield return new WaitForSecondsRealtime(delaySeconds);
    }

    yield return FadeOutRoutine();

    Scene activeScene = SceneManager.GetActiveScene();
    AsyncOperation loadOperation = LoadSceneForReload(activeScene);
    if (loadOperation == null)
    {
      Debug.LogWarning($"[GameSceneManager] シーン '{activeScene.name}' を再読み込みできませんでした。");
      yield return FadeIn();
      yield break;
    }

    while (!loadOperation.isDone)
    {
      yield return null;
    }

    yield return null;
    StartFadeInFromBlack();
  }

  private static AsyncOperation LoadSceneForReload(Scene activeScene)
  {
    if (activeScene.buildIndex >= 0)
    {
      return SceneManager.LoadSceneAsync(activeScene.buildIndex, LoadSceneMode.Single);
    }

    if (!string.IsNullOrEmpty(activeScene.name) && Application.CanStreamedLevelBeLoaded(activeScene.name))
    {
      return SceneManager.LoadSceneAsync(activeScene.name, LoadSceneMode.Single);
    }

#if UNITY_EDITOR
    if (!string.IsNullOrEmpty(activeScene.path))
    {
      return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
        activeScene.path,
        new LoadSceneParameters(LoadSceneMode.Single));
    }
#endif

    return null;
  }

  private void StartTransition(IEnumerator transitionRoutine)
  {
    if (_transitionCoroutine != null)
    {
      return;
    }

    if (_fadeCoroutine != null)
    {
      StopCoroutine(_fadeCoroutine);
      _fadeCoroutine = null;
    }

    _transitionCoroutine = StartCoroutine(RunTransition(transitionRoutine));
  }

  private IEnumerator RunTransition(IEnumerator transitionRoutine)
  {
    yield return StartCoroutine(transitionRoutine);
    _transitionCoroutine = null;
  }

  private void StartSceneOpeningFadeIfNeeded()
  {
    Scene activeScene = SceneManager.GetActiveScene();
    string sceneKey = string.IsNullOrEmpty(activeScene.path) ? activeScene.name : activeScene.path;
    if (_lastSceneStartFadeScenePath == sceneKey)
    {
      return;
    }

    _lastSceneStartFadeScenePath = sceneKey;
    StartFadeInFromBlack();
  }

  private void StartFadeInFromBlack()
  {
    EnsureFadeCanvasGroup();
    if (_fadeCanvasGroup == null)
    {
      return;
    }

    if (_fadeCoroutine != null)
    {
      StopCoroutine(_fadeCoroutine);
    }

    SetFadeAlpha(1f);
    _fadeCoroutine = StartCoroutine(FadeInAfterLoad());
  }

  private static bool EnsureInstance()
  {
    if (_instance != null)
    {
      return true;
    }

    GameSceneManager existing = FindFirstObjectByType<GameSceneManager>(FindObjectsInactive.Include);
    if (existing != null)
    {
      existing.gameObject.SetActive(true);
      if (_instance == null)
      {
        _instance = existing;
        DontDestroyOnLoad(existing.gameObject);
        existing.EnsureFadeCanvasGroup();
        existing.SetFadeAlpha(0f);
      }

      return true;
    }

    var managerObject = new GameObject(nameof(GameSceneManager));
    managerObject.AddComponent<GameSceneManager>();
    return _instance != null;
  }

  private void EnsureFadeCanvasGroup()
  {
    if (_fadeCanvasGroup != null)
    {
      ConfigureFadeCanvas(_fadeCanvasGroup.GetComponentInParent<Canvas>());
      _fadeCanvasGroup.interactable = false;
      _fadeCanvasGroup.blocksRaycasts = false;
      return;
    }

    var canvasObject = new GameObject("FadeCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
    canvasObject.transform.SetParent(transform, false);

    var canvas = canvasObject.GetComponent<Canvas>();
    ConfigureFadeCanvas(canvas);

    var canvasScaler = canvasObject.GetComponent<CanvasScaler>();
    canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
    canvasScaler.referenceResolution = new Vector2(1920f, 1080f);

    var fadeObject = new GameObject("FadeImage_Black", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
    fadeObject.transform.SetParent(canvasObject.transform, false);

    var fadeRect = fadeObject.GetComponent<RectTransform>();
    fadeRect.anchorMin = Vector2.zero;
    fadeRect.anchorMax = Vector2.one;
    fadeRect.offsetMin = Vector2.zero;
    fadeRect.offsetMax = Vector2.zero;

    var fadeImage = fadeObject.GetComponent<Image>();
    fadeImage.color = Color.black;
    fadeImage.raycastTarget = false;

    _fadeCanvasGroup = fadeObject.GetComponent<CanvasGroup>();
    _fadeCanvasGroup.alpha = 0f;
    _fadeCanvasGroup.interactable = false;
    _fadeCanvasGroup.blocksRaycasts = false;
  }

  private static void ConfigureFadeCanvas(Canvas canvas)
  {
    if (canvas == null)
    {
      return;
    }

    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
    canvas.overrideSorting = true;
    canvas.sortingOrder = FadeCanvasSortingOrder;
  }

  private void SetFadeAlpha(float alpha)
  {
    EnsureFadeCanvasGroup();
    if (_fadeCanvasGroup != null)
    {
      _fadeCanvasGroup.alpha = Mathf.Clamp01(alpha);
    }
  }
}
