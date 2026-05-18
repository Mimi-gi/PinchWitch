//=============================================================
// ファイル名  : StageSelectController.cs
// 作成者      : 未設定
// 作成日      : 2025/04/10
// 説明        : StageSelectシーン専用。ボタンイベント、ステージ解放状態の表示、
//               つかんだステージオブジェクトからのシーン遷移を制御する。
//=============================================================

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// StageSelectシーン専用。
/// ステージボタンの表示切替と、つかんだステージオブジェクトからの選択遷移を管理する。
/// </summary>
public class StageSelectController : MonoBehaviour
{
  [Serializable]
  private sealed class StageSelectNarrationPageConfig
  {
    public string speakerName = "\u30ca\u30ec\u30fc\u30b7\u30e7\u30f3";
    [TextArea(2, 6)] public string message;
    public Sprite illustration;
  }

  [SerializeField] private Button[] _stageButtons;
  [SerializeField] private HandPinchController _handPinchController;
  [SerializeField] private StageSelectTrumpLoadingController _loadingController;
  [SerializeField] private float _requiredGrabSeconds = 0.8f;

  [Header("Stage Select Narration")]
  [SerializeField] private bool _showTutorialArrivalNarration = true;
  [SerializeField] private bool _blockStageSelectionUntilNarrationEnds = true;
  [SerializeField] private TutorialNarrationUI _tutorialArrivalNarrationUi;
  [SerializeField] private bool _createRuntimeNarrationUiWhenMissing = true;
  [SerializeField] private string _tutorialArrivalDefaultSpeaker = "\u30ca\u30ec\u30fc\u30b7\u30e7\u30f3";
  [SerializeField, TextArea(2, 6)] private string _tutorialArrivalFallbackMessage =
    "\u65b0\u3057\u3044\u30b9\u30c6\u30fc\u30b8\u304c\u89e3\u653e\u3055\u308c\u307e\u3057\u305f\u3002\u6b21\u306e\u6311\u6226\u3092\u9078\u3093\u3067\u304f\u3060\u3055\u3044\u3002";
  [SerializeField] private List<StageSelectNarrationPageConfig> _tutorialArrivalNarrationPages = new List<StageSelectNarrationPageConfig>();
  [SerializeField] private float _tutorialArrivalTypewriterCharactersPerSecond = 36f;
  [SerializeField] private float _tutorialArrivalAutoAdvanceDelaySeconds = 1.25f;

  private Rigidbody _grabbedStageBody;
  private StageSelectGrabTarget _grabbedStageTarget;
  private float _grabStartedTime;
  private bool _isTransitioning;
  private TutorialSequenceController _tutorialArrivalNarrationSequence;

  private void OnEnable()
  {
    SubscribeGrabEvents();
  }

  private void Start()
  {
    if (_loadingController == null)
    {
      _loadingController = FindFirstObjectByType<StageSelectTrumpLoadingController>(FindObjectsInactive.Include);
    }

    UpdateStageButtons();
    TryStartTutorialArrivalNarration();
  }

  private void OnDisable()
  {
    UnsubscribeGrabEvents();
    ClearGrabSelection();
  }

  private void Update()
  {
    if (_isTransitioning || IsNarrationBlockingSelection() || _grabbedStageTarget == null)
    {
      return;
    }

    if (Time.unscaledTime - _grabStartedTime < _requiredGrabSeconds)
    {
      return;
    }

    GoStage(_grabbedStageTarget.StageNum);
  }

  /// <summary>
  /// クリア済みステージ数に応じてボタンのロック状態を更新する。
  /// </summary>
  private void UpdateStageButtons()
  {
    if (_stageButtons == null)
    {
      return;
    }

    for (int i = 0; i < _stageButtons.Length; i++)
    {
      if (_stageButtons[i] == null)
      {
        continue;
      }

      bool isUnlocked = i <= GameSceneManager.ClearStageCount;
      _stageButtons[i].interactable = isUnlocked;
    }
  }

  /// <summary>
  /// 各ステージボタンの OnClick に接続する。
  /// </summary>
  public void OnStageButtonPressed(int stageNum)
  {
    if (IsNarrationBlockingSelection())
    {
      return;
    }

    GoStage(stageNum);
  }

  /// <summary>
  /// タイトルに戻るボタンに接続する。
  /// </summary>
  public void OnBackButtonPressed()
  {
    GameSceneManager.GoTitle();
  }

  private void SubscribeGrabEvents()
  {
    if (_handPinchController == null)
    {
      _handPinchController = FindFirstObjectByType<HandPinchController>(FindObjectsInactive.Include);
    }

    if (_handPinchController == null)
    {
      return;
    }

    _handPinchController.GrabStarted -= HandleGrabStarted;
    _handPinchController.GrabReleased -= HandleGrabReleased;
    _handPinchController.GrabStarted += HandleGrabStarted;
    _handPinchController.GrabReleased += HandleGrabReleased;
  }

  private void UnsubscribeGrabEvents()
  {
    if (_handPinchController == null)
    {
      return;
    }

    _handPinchController.GrabStarted -= HandleGrabStarted;
    _handPinchController.GrabReleased -= HandleGrabReleased;
  }

  private void HandleGrabStarted(Rigidbody grabbedBody)
  {
    if (_isTransitioning || IsNarrationBlockingSelection() || grabbedBody == null)
    {
      return;
    }

    StageSelectGrabTarget grabTarget = ResolveGrabTarget(grabbedBody);
    if (grabTarget == null || !IsValidStageNum(grabTarget.StageNum))
    {
      ClearGrabSelection();
      return;
    }

    _grabbedStageBody = grabbedBody;
    _grabbedStageTarget = grabTarget;
    _grabStartedTime = Time.unscaledTime;
  }

  private void HandleGrabReleased(Rigidbody releasedBody)
  {
    if (releasedBody != null && releasedBody == _grabbedStageBody)
    {
      ClearGrabSelection();
    }
  }

  private void GoStage(int stageNum)
  {
    if (_isTransitioning || !IsValidStageNum(stageNum))
    {
      return;
    }

    int destinationStageNum = ResolveDestinationStageNum(stageNum);
    if (!IsValidStageNum(destinationStageNum))
    {
      return;
    }

    _isTransitioning = true;
    if (AudioManager.Instance != null)
    {
      AudioManager.Instance.PlaySE(AudioManager.Instance.SE_Select);
    }

    if (_loadingController != null)
    {
      _loadingController.Play(_grabbedStageTarget, destinationStageNum, GameSceneManager.GoStage);
      return;
    }

    GameSceneManager.GoStage(destinationStageNum);
  }

  private static int ResolveDestinationStageNum(int selectedStageNum)
  {
    if (StageProgressManager.IsCleared(selectedStageNum))
    {
      return selectedStageNum;
    }

    return StageProgressManager.NextPlayableStageNum;
  }

  private bool IsNarrationBlockingSelection()
  {
    return _blockStageSelectionUntilNarrationEnds
      && _tutorialArrivalNarrationSequence != null
      && _tutorialArrivalNarrationSequence.IsSequenceRunning
      && !_tutorialArrivalNarrationSequence.IsSequenceCompleted;
  }

  private void TryStartTutorialArrivalNarration()
  {
    if (!_showTutorialArrivalNarration || !GameSceneManager.ConsumeStageSelectTutorialNarrationRequest())
    {
      return;
    }

    List<TutorialSequenceController.TutorialNarrationPage> narrationPages = BuildTutorialArrivalNarrationPages();
    if (narrationPages.Count == 0)
    {
      return;
    }

    _tutorialArrivalNarrationSequence = CreateTutorialArrivalNarrationSequence(narrationPages);
  }

  private List<TutorialSequenceController.TutorialNarrationPage> BuildTutorialArrivalNarrationPages()
  {
    var narrationPages = new List<TutorialSequenceController.TutorialNarrationPage>();

    if (_tutorialArrivalNarrationPages != null)
    {
      for (var i = 0; i < _tutorialArrivalNarrationPages.Count; i++)
      {
        var page = _tutorialArrivalNarrationPages[i];
        if (page == null || (string.IsNullOrWhiteSpace(page.message) && page.illustration == null))
        {
          continue;
        }

        narrationPages.Add(new TutorialSequenceController.TutorialNarrationPage
        {
          pageId = $"stage_select_{i + 1}",
          speakerName = string.IsNullOrWhiteSpace(page.speakerName) ? _tutorialArrivalDefaultSpeaker : page.speakerName,
          message = page.message ?? string.Empty,
          illustration = page.illustration,
        });
      }
    }

    if (narrationPages.Count > 0 || string.IsNullOrWhiteSpace(_tutorialArrivalFallbackMessage))
    {
      return narrationPages;
    }

    narrationPages.Add(new TutorialSequenceController.TutorialNarrationPage
    {
      pageId = "stage_select_fallback",
      speakerName = _tutorialArrivalDefaultSpeaker,
      message = _tutorialArrivalFallbackMessage,
    });

    return narrationPages;
  }

  private TutorialSequenceController CreateTutorialArrivalNarrationSequence(
    List<TutorialSequenceController.TutorialNarrationPage> narrationPages)
  {
    TutorialNarrationUI narrationUi = ResolveTutorialArrivalNarrationUi();
    if (narrationUi == null)
    {
      return null;
    }

    var sequenceHost = new GameObject("StageSelectNarrationSequence");
    sequenceHost.transform.SetParent(transform, false);

    var sequenceController = sequenceHost.AddComponent<TutorialSequenceController>();
    sequenceController.ConfigureRuntimeSequence(
      BuildTutorialArrivalSequenceSteps(narrationPages),
      startImmediately: false,
      runtimeInitial2DGameplayEnabled: false,
      runtimeInitial3DInteractionEnabled: !_blockStageSelectionUntilNarrationEnds,
      runtimeGoStageSelectWhenComplete: false,
      runtimeInterceptGoalClear: false,
      runtimeTypewriterCharactersPerSecond: _tutorialArrivalTypewriterCharactersPerSecond,
      runtimeAcceptAdvanceInput: false,
      runtimeAutoAdvanceNarration: true,
      runtimeAutoAdvanceNarrationDelay: _tutorialArrivalAutoAdvanceDelaySeconds);

    narrationUi.SetSequenceController(sequenceController);
    sequenceController.BeginSequence();

    return sequenceController;
  }

  private TutorialNarrationUI ResolveTutorialArrivalNarrationUi()
  {
    if (_tutorialArrivalNarrationUi != null)
    {
      return _tutorialArrivalNarrationUi;
    }

    _tutorialArrivalNarrationUi = FindFirstObjectByType<TutorialNarrationUI>(FindObjectsInactive.Include);
    if (_tutorialArrivalNarrationUi != null)
    {
      return _tutorialArrivalNarrationUi;
    }

    if (!_createRuntimeNarrationUiWhenMissing)
    {
      Debug.LogWarning("[StageSelectController] StageSelect narration UI is not assigned.", this);
      return null;
    }

    _tutorialArrivalNarrationUi = CreateRuntimeNarrationUi();
    return _tutorialArrivalNarrationUi;
  }

  private TutorialNarrationUI CreateRuntimeNarrationUi()
  {
    var canvasObject = new GameObject(
      "StageSelectNarrationCanvas",
      typeof(RectTransform),
      typeof(Canvas),
      typeof(CanvasScaler),
      typeof(GraphicRaycaster));
    canvasObject.transform.SetParent(transform, false);

    var canvas = canvasObject.GetComponent<Canvas>();
    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
    canvas.sortingOrder = short.MaxValue;

    var canvasScaler = canvasObject.GetComponent<CanvasScaler>();
    canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
    canvasScaler.referenceResolution = new Vector2(1920f, 1080f);
    canvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
    canvasScaler.matchWidthOrHeight = 0.5f;

    RectTransform narrationRoot = CreateUiObject("TutorialNarrationRoot", canvasObject.transform);
    StretchToFullScreen(narrationRoot);

    var canvasGroup = narrationRoot.gameObject.AddComponent<CanvasGroup>();
    canvasGroup.alpha = 0f;
    canvasGroup.interactable = false;
    canvasGroup.blocksRaycasts = false;

    RectTransform illustrationRoot = CreateUiObject("Illustration", narrationRoot);
    illustrationRoot.anchorMin = new Vector2(0.16f, 0.28f);
    illustrationRoot.anchorMax = new Vector2(0.84f, 0.92f);
    illustrationRoot.offsetMin = Vector2.zero;
    illustrationRoot.offsetMax = Vector2.zero;

    illustrationRoot.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);

    var illustrationImage = CreateImage("IllustrationSprite", illustrationRoot, Color.white);
    illustrationImage.preserveAspect = true;
    illustrationImage.rectTransform.anchorMin = new Vector2(0.03f, 0.03f);
    illustrationImage.rectTransform.anchorMax = new Vector2(0.97f, 0.97f);
    illustrationImage.rectTransform.offsetMin = Vector2.zero;
    illustrationImage.rectTransform.offsetMax = Vector2.zero;

    RectTransform windowRoot = CreateUiObject("Window", narrationRoot);
    windowRoot.anchorMin = new Vector2(0.04f, 0.03f);
    windowRoot.anchorMax = new Vector2(0.96f, 0.29f);
    windowRoot.offsetMin = Vector2.zero;
    windowRoot.offsetMax = Vector2.zero;

    windowRoot.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.78f);

    TMP_FontAsset fontAsset = ResolveNarrationFontAsset();

    var speakerText = CreateText("SpeakerText", windowRoot, fontAsset, 34f, FontStyles.Bold, TextAlignmentOptions.TopLeft);
    speakerText.rectTransform.anchorMin = new Vector2(0.05f, 0.7f);
    speakerText.rectTransform.anchorMax = new Vector2(0.95f, 0.95f);
    speakerText.rectTransform.offsetMin = Vector2.zero;
    speakerText.rectTransform.offsetMax = Vector2.zero;

    var bodyText = CreateText("BodyText", windowRoot, fontAsset, 44f, FontStyles.Normal, TextAlignmentOptions.TopLeft);
    bodyText.enableWordWrapping = true;
    bodyText.rectTransform.anchorMin = new Vector2(0.05f, 0.14f);
    bodyText.rectTransform.anchorMax = new Vector2(0.95f, 0.7f);
    bodyText.rectTransform.offsetMin = Vector2.zero;
    bodyText.rectTransform.offsetMax = Vector2.zero;

    var promptText = CreateText("PromptText", windowRoot, fontAsset, 28f, FontStyles.Normal, TextAlignmentOptions.BottomRight);
    promptText.rectTransform.anchorMin = new Vector2(0.55f, 0.02f);
    promptText.rectTransform.anchorMax = new Vector2(0.95f, 0.16f);
    promptText.rectTransform.offsetMin = Vector2.zero;
    promptText.rectTransform.offsetMax = Vector2.zero;

    var narrationUi = narrationRoot.gameObject.AddComponent<TutorialNarrationUI>();
    narrationUi.ConfigureRuntimeUI(
      narrationRoot.gameObject,
      canvasGroup,
      windowRoot.gameObject,
      speakerText,
      bodyText,
      promptText,
      illustrationImage,
      illustrationRoot.gameObject,
      null,
      _tutorialArrivalDefaultSpeaker,
      string.Empty,
      string.Empty);
    return narrationUi;
  }

  private List<TutorialSequenceController.TutorialStep> BuildTutorialArrivalSequenceSteps(
    List<TutorialSequenceController.TutorialNarrationPage> narrationPages)
  {
    var narrationStep = new TutorialSequenceController.TutorialStep
    {
      stepId = "stage_select_arrival_narration",
      kind = TutorialSequenceController.TutorialStepKind.Narration,
      pages = narrationPages,
    };

    var steps = new List<TutorialSequenceController.TutorialStep> { narrationStep };
    if (_blockStageSelectionUntilNarrationEnds)
    {
      steps.Add(new TutorialSequenceController.TutorialStep
      {
        stepId = "stage_select_enable_interaction",
        kind = TutorialSequenceController.TutorialStepKind.Set3DInteraction,
        enable3DInteraction = true,
      });
    }

    return steps;
  }

  private TMP_FontAsset ResolveNarrationFontAsset()
  {
    var existingText = FindFirstObjectByType<TMP_Text>(FindObjectsInactive.Include);
    if (existingText != null && existingText.font != null)
    {
      return existingText.font;
    }

    return TMP_Settings.defaultFontAsset;
  }

  private static RectTransform CreateUiObject(string objectName, Transform parent)
  {
    var uiObject = new GameObject(objectName, typeof(RectTransform));
    var rectTransform = uiObject.GetComponent<RectTransform>();
    rectTransform.SetParent(parent, false);
    rectTransform.localScale = Vector3.one;
    return rectTransform;
  }

  private static void StretchToFullScreen(RectTransform rectTransform)
  {
    rectTransform.anchorMin = Vector2.zero;
    rectTransform.anchorMax = Vector2.one;
    rectTransform.offsetMin = Vector2.zero;
    rectTransform.offsetMax = Vector2.zero;
    rectTransform.anchoredPosition = Vector2.zero;
  }

  private static Image CreateImage(string objectName, Transform parent, Color color)
  {
    RectTransform rectTransform = CreateUiObject(objectName, parent);
    StretchToFullScreen(rectTransform);

    var image = rectTransform.gameObject.AddComponent<Image>();
    image.color = color;
    return image;
  }

  private static TextMeshProUGUI CreateText(
    string objectName,
    Transform parent,
    TMP_FontAsset fontAsset,
    float fontSize,
    FontStyles fontStyle,
    TextAlignmentOptions alignment)
  {
    RectTransform rectTransform = CreateUiObject(objectName, parent);
    var text = rectTransform.gameObject.AddComponent<TextMeshProUGUI>();
    text.font = fontAsset;
    text.fontSize = fontSize;
    text.fontStyle = fontStyle;
    text.alignment = alignment;
    text.color = Color.white;
    text.raycastTarget = false;
    text.text = string.Empty;
    return text;
  }

  private static bool IsValidStageNum(int stageNum)
  {
    return stageNum > 0 && stageNum <= StageProgressManager.TotalStageCount;
  }

  private void ClearGrabSelection()
  {
    _grabbedStageBody = null;
    _grabbedStageTarget = null;
    _grabStartedTime = 0f;
  }

  private static StageSelectGrabTarget ResolveGrabTarget(Rigidbody grabbedBody)
  {
    if (grabbedBody.TryGetComponent(out StageSelectGrabTarget grabTarget))
    {
      return grabTarget;
    }

    grabTarget = grabbedBody.GetComponentInParent<StageSelectGrabTarget>();
    if (grabTarget != null)
    {
      return grabTarget;
    }

    return grabbedBody.GetComponentInChildren<StageSelectGrabTarget>();
  }
}
