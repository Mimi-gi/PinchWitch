//=============================================================
// ファイル名  : TitleSceneController.cs
// 作成者     : 彩乃
// 作成日     : 2025/04/10
// 説明       : Titleシーン専用。ボタンイベントと初回起動判定を担当。
//              stage_1クリア前はOpeningへ、クリア後はStageSelectへ遷移する。
//              終了ボタンから直接アプリを終了できる。
//=============================================================

using System.Collections;
using UnityEngine;

/// <summary>
/// Titleシーン専用。ボタンイベントと初回起動判定を担当。
/// </summary>
public class TitleSceneController : MonoBehaviour
{
  private const int FirstStageNum = 1;

  [SerializeField] private HandPinchController _handPinchController;
  [SerializeField] private bool _startWhenHandDetected = true;
  [SerializeField] private float _handDetectedStartDelaySeconds = 1f;
  [SerializeField] private KeyCode _startKey = KeyCode.Space;

  private bool _isStarting;

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
    if (_isStarting)
    {
      return;
    }

    if (Input.GetKeyDown(_startKey))
    {
      StartFromTitleInput();
      return;
    }

    if (!_startWhenHandDetected || !IsAnyHandDetected())
    {
      return;
    }

    StartCoroutine(StartAfterHandDetectedRoutine());
  }

  /// <summary>
  /// Startボタンに繋ぐ。
  /// stage_1クリア前はOpeningへ、クリア後はStageSelectへ。
  /// </summary>
  public void OnStartButtonPressed()
  {
    if (_isStarting)
    {
      return;
    }

    StartFromTitleInput();
  }

  private void StartFromTitleInput()
  {
    _isStarting = true;
    StartGame();
  }

  private IEnumerator StartAfterHandDetectedRoutine()
  {
    _isStarting = true;
    yield return new WaitForSecondsRealtime(_handDetectedStartDelaySeconds);
    PlayTitleSe();
    StartGame();
  }

  private void StartGame()
  {
    if (!StageProgressManager.IsCleared(FirstStageNum))
    {
      GameSceneManager.GoOpening();
      return;
    }

    GameSceneManager.GoStageSelect();
  }

  private static void PlayTitleSe()
  {
    if (AudioManager.Instance != null)
    {
      AudioManager.Instance.PlaySE(AudioManager.Instance.SE_Title);
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

  /// <summary>
  /// 終了ボタンに繋ぐ。
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
