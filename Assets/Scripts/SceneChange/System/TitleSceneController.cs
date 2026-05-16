//=============================================================
// ファイル名  : TitleSceneController.cs
// 作成者     : 彩乃
// 作成日     : 2025/04/10
// 説明       : Titleシーン専用。ボタンイベントと初回起動判定を担当。
//              初回起動時はTutorialへ、2回目以降はStageSelectへ遷移する。
//              終了ボタンはQuitHandlerを使わない場合の代替として保持。
//=============================================================

using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Titleシーン専用。ボタンイベントと初回起動判定を担当。
/// </summary>
public class TitleSceneController : MonoBehaviour
{
  private const string _KeyHasLaunched = "HasLaunchedBefore";

  [SerializeField] private HandPinchController _handPinchController;
  [SerializeField] private bool _startWhenHandDetected = true;
  [SerializeField] private float _handDetectedStartDelaySeconds = 1f;

  private bool _isStarting;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  private static void BootstrapOnTitleSceneLoad()
  {
    Scene activeScene = SceneManager.GetActiveScene();
    if (!IsTitleScene(activeScene.name)
        || FindFirstObjectByType<TitleSceneController>(FindObjectsInactive.Include) != null)
    {
      return;
    }

    var controllerObject = new GameObject(nameof(TitleSceneController));
    controllerObject.AddComponent<TitleSceneController>();
  }

  private void Awake()
  {
    ResolveHandPinchController();
  }

  private void OnValidate()
  {
    _handDetectedStartDelaySeconds = Mathf.Max(0f, _handDetectedStartDelaySeconds);
  }

  private void Update()
  {
    if (!_startWhenHandDetected || _isStarting || !IsAnyHandDetected())
    {
      return;
    }

    StartCoroutine(StartAfterHandDetectedRoutine());
  }

  /// <summary>
  /// Startボタンに繋ぐ。
  /// 初回起動はTutorialへ、2回目以降はStageSelectへ。
  /// </summary>
  public void OnStartButtonPressed()
  {
    if (_isStarting)
    {
      return;
    }

    _isStarting = true;
    StartGame();
  }

  private IEnumerator StartAfterHandDetectedRoutine()
  {
    _isStarting = true;
    yield return new WaitForSecondsRealtime(_handDetectedStartDelaySeconds);
    StartGame();
  }

  private void StartGame()
  {
    if (PlayerPrefs.GetInt(_KeyHasLaunched, 0) == 0)
    {
      PlayerPrefs.SetInt(_KeyHasLaunched, 1);
      PlayerPrefs.Save();
      GameSceneManager.GoTutorial();
    }
    else
    {
      GameSceneManager.GoStageSelect();
    }
  }

  private bool IsAnyHandDetected()
  {
    ResolveHandPinchController();
    return _handPinchController != null && _handPinchController.HasAnyFreshHand();
  }

  private void ResolveHandPinchController()
  {
    if (_handPinchController != null)
    {
      return;
    }

    _handPinchController = FindFirstObjectByType<HandPinchController>(FindObjectsInactive.Include);
  }

  private static bool IsTitleScene(string sceneName)
  {
    return sceneName == "Title"
           || sceneName == "Title_old"
           || sceneName == "Ayano_Title";
  }

  /// <summary>
  /// 終了ボタンに繋ぐ（QuitHandlerを使わない場合の代替）。
  /// </summary>
  public void OnQuitButtonPressed()
  {
#if UNITY_EDITOR
    UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
  }
}
