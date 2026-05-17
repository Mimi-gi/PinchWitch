using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

[DisallowMultipleComponent]
/// <summary>
/// チュートリアル全体の進行を 1 本の steps で順番に制御するコントローラ。
/// Narration や Trigger 待ち、2D/3D の操作開放、フェード演出などを
/// Inspector 上で並べてシーケンスとして実行する。
/// </summary>
public sealed class TutorialSequenceController : MonoBehaviour
{
  public enum TutorialStepKind
  {
    // 既存シーンの enum 値を壊さないように明示的に番号を固定する。
    Narration = 0,
    WaitForTrigger = 1,
    WaitForSeconds = 2,
    Set2DGameplay = 3,
    Set3DInteraction = 4,
    SetViewMode = 5,
    CompleteTutorial = 6,
    FadeScreen = 7,
  }

  [Serializable]
  public sealed class TutorialNarrationPage
  {
    // ページ管理用の任意 ID。
    public string pageId;
    // UI 上に表示する話者名。
    public string speakerName;
    // 文字送り対象の本文。
    [TextArea(2, 6)] public string message;
    // ページごとに差し替えるイラスト。
    public Sprite illustration;
  }

  [Serializable]
  public sealed class TutorialStep
  {
    // ログや管理用の任意 ID。
    public string stepId;
    // このステップで何をするか。
    public TutorialStepKind kind = TutorialStepKind.Narration;
    // Inspector 上で用途をメモしておく欄。
    [TextArea(1, 3)] public string note;

    // Narration 用
    public List<TutorialNarrationPage> pages = new List<TutorialNarrationPage>();

    // WaitForTrigger 用
    public string triggerId;

    // WaitForSeconds 用
    public float waitSeconds;
    public bool useUnscaledTime = true;

    // FadeScreen 用
    public Color fadeColor = Color.white;
    [Range(0f, 1f)] public float fadeFromAlpha = 1f;
    [Range(0f, 1f)] public float fadeToAlpha = 0f;
    public float fadeDuration = 0.75f;
    public bool fadeUseUnscaledTime = true;

    // Set2DGameplay 用
    public bool enable2DGameplay = true;

    // Set3DInteraction 用
    public bool enable3DInteraction = true;

    // SetViewMode 用
    public SpaceViewMode targetViewMode = SpaceViewMode.World3D;

    // ステップ開始時 / 完了時に Inspector から追加で演出を差し込める。
    public UnityEvent onStepStarted;
    public UnityEvent onStepCompleted;
  }

  public event Action StateChanged;
  public event Action<int, string> StepChanged;
  public event Action<int, int> NarrationPageChanged;
  public event Action SequenceCompleted;

  [Header("Sequence")]
  [SerializeField] private bool playOnStart = true;
  [SerializeField] private List<TutorialStep> steps = new List<TutorialStep>();
  [SerializeField] private bool enableDebugLogging = true;

  [Header("Initial Gameplay State")]
  [SerializeField] private bool initial2DGameplayEnabled;
  [SerializeField] private bool initial3DInteractionEnabled;

  [Header("Narration")]
  [SerializeField] private float typewriterCharactersPerSecond = 32f;
  [SerializeField] private bool acceptAdvanceInput = true;
  [SerializeField] private bool autoAdvanceNarration;
  [SerializeField] private float autoAdvanceNarrationDelay = 1.25f;
  [SerializeField] private KeyCode[] advanceKeys = { KeyCode.Space, KeyCode.Return, KeyCode.Mouse0 };
  [SerializeField] private float advanceInputGracePeriod = 0.1f;

  [Header("Scene References")]
  [SerializeField] private bool autoResolveSceneReferences = true;
  [SerializeField] private SpaceViewModeSwitcher spaceViewModeSwitcher;
  [SerializeField] private Wall2DPlayerRespawner wall2DPlayerRespawner;
  [SerializeField] private HandPinchController handPinchController;
  [SerializeField] private MouseGrabInputController mouseGrabInputController;
  [SerializeField] private GoalController goalController;

  [Header("Screen Fade")]
  [SerializeField] private GameObject screenFadeRoot;
  [SerializeField] private CanvasGroup screenFadeCanvasGroup;
  [SerializeField] private Image screenFadeImage;
  [SerializeField] private bool disableScreenFadeRootWhenTransparent = true;

  [Header("Goal Handling")]
  [SerializeField] private bool interceptGoalClear = true;
  [SerializeField] private string goalTriggerId = "Goal";
  [SerializeField] private bool goStageSelectWhenSequenceCompletes = true;
  [SerializeField] private bool waitForGoalReactionBeforeAdvancing = true;
  [SerializeField] private float additionalGoalAdvanceDelay = 0f;

  public bool IsSequenceRunning => _isSequenceRunning;
  public bool IsSequenceCompleted => _isSequenceCompleted;
  public int CurrentStepIndex => _currentStepIndex;
  public int CurrentPageIndex => _currentPageIndex;
  public bool IsWaitingForTrigger => _isWaitingForTrigger;
  public string ExpectedTriggerId => _expectedTriggerId;
  public bool IsNarrationStepActive => CurrentStep != null && CurrentStep.kind == TutorialStepKind.Narration;
  public bool IsCurrentNarrationFullyRevealed => _visibleCharacterCount >= CurrentFullText.Length;
  public string CurrentSpeakerName => CurrentPage != null ? CurrentPage.speakerName ?? string.Empty : string.Empty;
  public string CurrentFullText => CurrentPage != null ? CurrentPage.message ?? string.Empty : string.Empty;
  public string CurrentDisplayedText => BuildDisplayedText();
  public Sprite CurrentIllustration => CurrentPage != null ? CurrentPage.illustration : null;

  public TutorialStep CurrentStep => IsStepIndexValid(_currentStepIndex) ? steps[_currentStepIndex] : null;

  public TutorialNarrationPage CurrentPage
  {
    get
    {
      var currentStep = CurrentStep;
      return currentStep != null && currentStep.kind == TutorialStepKind.Narration && IsPageIndexValid(currentStep, _currentPageIndex)
        ? currentStep.pages[_currentPageIndex]
        : null;
    }
  }

  private Coroutine _activeStepCoroutine;
  private int _currentStepIndex = -1;
  private int _currentPageIndex = -1;
  private int _visibleCharacterCount;
  private float _typewriterProgress;
  private bool _isSequenceRunning;
  private bool _isSequenceCompleted;
  private bool _isWaitingForTrigger;
  private string _expectedTriggerId = string.Empty;
  private bool _is2DGameplayEnabled;
  private bool _is3DInteractionEnabled;
  private bool _isSubscribedToGoal;
  private float _advanceInputBlockedUntilUnscaledTime;
  private bool _waitForAdvanceInputRelease;
  private float _autoAdvanceNarrationAtUnscaledTime = -1f;
  private Coroutine _pendingGoalAdvanceCoroutine;

  private void Reset()
  {
    AutoResolveSceneReferences();
    ClampSerializedValues();
  }

  private void Awake()
  {
    AutoResolveSceneReferences();
    ClampSerializedValues();
    ResetGameplayState();
    ApplyGameplayState();
  }

  private void OnEnable()
  {
    AutoResolveSceneReferences();
    SubscribeToGoalController();
  }

  private void Start()
  {
    if (playOnStart)
    {
      BeginSequence();
    }
  }

  private void Update()
  {
    if (!_isSequenceRunning)
    {
      return;
    }

    UpdateTypewriter();
    UpdateNarrationAutoAdvance();

    if (!acceptAdvanceInput || !IsNarrationStepActive || IsAdvanceInputBlocked() || !HasAdvanceInputThisFrame())
    {
      return;
    }

    AdvanceNarration();
  }

  private void OnDisable()
  {
    StopActiveStepCoroutine();
    StopPendingGoalAdvanceCoroutine();
    UnsubscribeFromGoalController();
  }

  private void OnValidate()
  {
    ClampSerializedValues();
  }

  public void BeginSequence()
  {
    StopActiveStepCoroutine();
    StopPendingGoalAdvanceCoroutine();
    ResetGameplayState();
    ResetSequenceProgress();
    ApplyGameplayState();

    _isSequenceRunning = true;
    _isSequenceCompleted = false;
    LogDebug("Sequence started.");
    AdvanceToStep(0);
  }

  public void RestartSequence()
  {
    BeginSequence();
  }

  public void ConfigureRuntimeSequence(
    IList<TutorialStep> runtimeSteps,
    bool startImmediately = true,
    bool runtimeInitial2DGameplayEnabled = false,
    bool runtimeInitial3DInteractionEnabled = false,
    bool runtimeGoStageSelectWhenComplete = false,
    bool runtimeInterceptGoalClear = false,
    float runtimeTypewriterCharactersPerSecond = -1f,
    bool? runtimeAcceptAdvanceInput = null,
    bool? runtimeAutoAdvanceNarration = null,
    float runtimeAutoAdvanceNarrationDelay = -1f)
  {
    StopActiveStepCoroutine();
    StopPendingGoalAdvanceCoroutine();
    playOnStart = false;
    initial2DGameplayEnabled = runtimeInitial2DGameplayEnabled;
    initial3DInteractionEnabled = runtimeInitial3DInteractionEnabled;
    goStageSelectWhenSequenceCompletes = runtimeGoStageSelectWhenComplete;
    interceptGoalClear = runtimeInterceptGoalClear;

    if (runtimeAcceptAdvanceInput.HasValue)
    {
      acceptAdvanceInput = runtimeAcceptAdvanceInput.Value;
    }

    if (runtimeAutoAdvanceNarration.HasValue)
    {
      autoAdvanceNarration = runtimeAutoAdvanceNarration.Value;
    }

    if (runtimeTypewriterCharactersPerSecond >= 0f)
    {
      typewriterCharactersPerSecond = runtimeTypewriterCharactersPerSecond;
    }

    if (runtimeAutoAdvanceNarrationDelay >= 0f)
    {
      autoAdvanceNarrationDelay = runtimeAutoAdvanceNarrationDelay;
    }

    steps = CloneSteps(runtimeSteps);

    ClampSerializedValues();
    ResetSequenceProgress();
    _isSequenceRunning = false;
    _isSequenceCompleted = false;
    AutoResolveSceneReferences();
    ResetGameplayState();
    ApplyGameplayState();

    if (_isSubscribedToGoal && goalController != null)
    {
      goalController.OverrideDefaultClearHandling = interceptGoalClear;
    }

    if (startImmediately)
    {
      BeginSequence();
    }
  }

  public bool AdvanceNarration()
  {
    if (!IsNarrationStepActive)
    {
      return false;
    }

    if (!IsCurrentNarrationFullyRevealed)
    {
      RevealCurrentNarrationImmediately();
      return true;
    }

    var step = CurrentStep;
    if (step == null)
    {
      return false;
    }

    var nextPageIndex = _currentPageIndex + 1;
    if (nextPageIndex < step.pages.Count)
    {
      StartNarrationPage(nextPageIndex);
      return true;
    }

    CompleteCurrentStep();
    return true;
  }

  public void NotifyTriggerReached(string triggerId)
  {
    if (!_isSequenceRunning || !_isWaitingForTrigger || string.IsNullOrEmpty(triggerId))
    {
      return;
    }

    if (!string.Equals(_expectedTriggerId, triggerId, StringComparison.OrdinalIgnoreCase))
    {
      return;
    }

    _isWaitingForTrigger = false;
    _expectedTriggerId = string.Empty;
    LogDebug($"Trigger reached: {triggerId}");
    StateChanged?.Invoke();
    CompleteCurrentStep();
  }

  public void CompleteSequence()
  {
    if (_isSequenceCompleted)
    {
      return;
    }

    StopActiveStepCoroutine();
    _isSequenceRunning = false;
    _isSequenceCompleted = true;
    _isWaitingForTrigger = false;
    _expectedTriggerId = string.Empty;
    LogDebug("Sequence completed.");
    SequenceCompleted?.Invoke();
    StateChanged?.Invoke();

    if (goStageSelectWhenSequenceCompletes)
    {
      GameSceneManager.GoStageSelect();
    }
  }

  public void Set2DGameplayEnabled(bool isEnabled)
  {
    Apply2DGameplayStep(isEnabled);
  }

  public void Set3DInteractionEnabled(bool isEnabled)
  {
    _is3DInteractionEnabled = isEnabled;
    ApplyGameplayState();
  }

  public void SetViewMode(SpaceViewMode mode)
  {
    if (spaceViewModeSwitcher == null)
    {
      return;
    }

    spaceViewModeSwitcher.SetMode(mode);
  }

  private void AdvanceToStep(int stepIndex)
  {
    if (!IsStepIndexValid(stepIndex))
    {
      CompleteSequence();
      return;
    }

    StopActiveStepCoroutine();
    ClearCurrentStepTransientState();
    _currentStepIndex = stepIndex;

    var step = steps[stepIndex];
    LogDebug($"Step started: {BuildStepLabel(stepIndex, step)}");
    step.onStepStarted?.Invoke();
    StepChanged?.Invoke(stepIndex, step.stepId ?? string.Empty);
    StateChanged?.Invoke();

    switch (step.kind)
    {
      case TutorialStepKind.Narration:
        StartNarrationStep(step);
        break;
      case TutorialStepKind.WaitForTrigger:
        StartTriggerWait(step);
        break;
      case TutorialStepKind.WaitForSeconds:
        StartWaitForSeconds(step);
        break;
      case TutorialStepKind.FadeScreen:
        StartFadeScreen(step);
        break;
      case TutorialStepKind.Set2DGameplay:
        Apply2DGameplayStep(step.enable2DGameplay);
        CompleteCurrentStep();
        break;
      case TutorialStepKind.Set3DInteraction:
        _is3DInteractionEnabled = step.enable3DInteraction;
        ApplyGameplayState();
        CompleteCurrentStep();
        break;
      case TutorialStepKind.SetViewMode:
        SetViewMode(step.targetViewMode);
        CompleteCurrentStep();
        break;
      case TutorialStepKind.CompleteTutorial:
        CompleteSequence();
        break;
      default:
        CompleteCurrentStep();
        break;
    }
  }

  private void StartNarrationStep(TutorialStep step)
  {
    if (step.pages == null || step.pages.Count == 0)
    {
      CompleteCurrentStep();
      return;
    }

    StartNarrationPage(0);
  }

  private void StartNarrationPage(int pageIndex)
  {
    var step = CurrentStep;
    if (step == null || !IsPageIndexValid(step, pageIndex))
    {
      CompleteCurrentStep();
      return;
    }

    _currentPageIndex = pageIndex;
    _typewriterProgress = 0f;
    _visibleCharacterCount = 0;
    _autoAdvanceNarrationAtUnscaledTime = -1f;
    BlockAdvanceInputTemporarily();

    if (typewriterCharactersPerSecond <= 0f || string.IsNullOrEmpty(CurrentFullText))
    {
      RevealCurrentNarrationImmediately();
    }

    LogDebug($"Narration page: step={_currentStepIndex + 1}/{steps.Count}, page={pageIndex + 1}/{step.pages.Count}, speaker={CurrentSpeakerName}");
    NarrationPageChanged?.Invoke(_currentStepIndex, _currentPageIndex);
    StateChanged?.Invoke();
  }

  private void StartTriggerWait(TutorialStep step)
  {
    _expectedTriggerId = step.triggerId ?? string.Empty;
    _isWaitingForTrigger = !string.IsNullOrEmpty(_expectedTriggerId);
    LogDebug(_isWaitingForTrigger
      ? $"Waiting for trigger: {_expectedTriggerId}"
      : $"Trigger wait skipped: {BuildStepLabel(_currentStepIndex, step)}");
    StateChanged?.Invoke();

    if (!_isWaitingForTrigger)
    {
      CompleteCurrentStep();
    }
  }

  private void StartWaitForSeconds(TutorialStep step)
  {
    LogDebug($"Waiting for seconds: {step.waitSeconds:0.###} (unscaled={step.useUnscaledTime})");
    _activeStepCoroutine = StartCoroutine(WaitForSecondsStep(step.waitSeconds, step.useUnscaledTime));
  }

  private void StartFadeScreen(TutorialStep step)
  {
    CacheScreenFadeReferences();

    if (screenFadeRoot == null && screenFadeCanvasGroup == null && screenFadeImage == null)
    {
      Debug.LogWarning("[TutorialSequenceController] FadeScreen 用の UI 参照が未設定のためステップをスキップします。");
      CompleteCurrentStep();
      return;
    }

    LogDebug(
      $"Fade screen: color={step.fadeColor}, from={step.fadeFromAlpha:0.##}, to={step.fadeToAlpha:0.##}, duration={step.fadeDuration:0.###}, unscaled={step.fadeUseUnscaledTime}");
    _activeStepCoroutine = StartCoroutine(FadeScreenStep(step));
  }

  private IEnumerator WaitForSecondsStep(float waitSeconds, bool useUnscaledTime)
  {
    if (waitSeconds > 0f)
    {
      if (useUnscaledTime)
      {
        yield return new WaitForSecondsRealtime(waitSeconds);
      }
      else
      {
        yield return new WaitForSeconds(waitSeconds);
      }
    }

    _activeStepCoroutine = null;
    CompleteCurrentStep();
  }

  private IEnumerator FadeScreenStep(TutorialStep step)
  {
    SetScreenFadeImmediate(step.fadeColor, step.fadeFromAlpha);

    if (step.fadeDuration > 0f)
    {
      var elapsed = 0f;
      while (elapsed < step.fadeDuration)
      {
        elapsed += step.fadeUseUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        var t = Mathf.Clamp01(elapsed / step.fadeDuration);
        ApplyScreenFadeAlpha(Mathf.Lerp(step.fadeFromAlpha, step.fadeToAlpha, t));
        yield return null;
      }
    }

    ApplyScreenFadeAlpha(step.fadeToAlpha);

    _activeStepCoroutine = null;
    CompleteCurrentStep();
  }

  private void CompleteCurrentStep()
  {
    var step = CurrentStep;
    if (step != null)
    {
      LogDebug($"Step completed: {BuildStepLabel(_currentStepIndex, step)}");
      step.onStepCompleted?.Invoke();
    }

    var nextStepIndex = _currentStepIndex + 1;
    if (nextStepIndex >= steps.Count)
    {
      CompleteSequence();
      return;
    }

    AdvanceToStep(nextStepIndex);
  }

  private void ResetGameplayState()
  {
    _is2DGameplayEnabled = initial2DGameplayEnabled;
    _is3DInteractionEnabled = initial3DInteractionEnabled;
  }

  private void ResetSequenceProgress()
  {
    _currentStepIndex = -1;
    ClearCurrentStepTransientState();
  }

  private void ClearCurrentStepTransientState()
  {
    _currentPageIndex = -1;
    _visibleCharacterCount = 0;
    _typewriterProgress = 0f;
    _autoAdvanceNarrationAtUnscaledTime = -1f;
    _isWaitingForTrigger = false;
    _expectedTriggerId = string.Empty;
    _waitForAdvanceInputRelease = false;
  }

  private void UpdateTypewriter()
  {
    if (!IsNarrationStepActive || IsCurrentNarrationFullyRevealed)
    {
      return;
    }

    var fullText = CurrentFullText;
    if (string.IsNullOrEmpty(fullText))
    {
      RevealCurrentNarrationImmediately();
      return;
    }

    _typewriterProgress += typewriterCharactersPerSecond * Time.unscaledDeltaTime;
    var nextVisibleCharacterCount = Mathf.Clamp(Mathf.FloorToInt(_typewriterProgress), 0, fullText.Length);
    if (nextVisibleCharacterCount == _visibleCharacterCount)
    {
      return;
    }

    _visibleCharacterCount = nextVisibleCharacterCount;
    StateChanged?.Invoke();
  }

  private void RevealCurrentNarrationImmediately()
  {
    _visibleCharacterCount = CurrentFullText.Length;
    _typewriterProgress = _visibleCharacterCount;
    StateChanged?.Invoke();
  }

  private void UpdateNarrationAutoAdvance()
  {
    if (!autoAdvanceNarration || !IsNarrationStepActive)
    {
      _autoAdvanceNarrationAtUnscaledTime = -1f;
      return;
    }

    if (!IsCurrentNarrationFullyRevealed)
    {
      _autoAdvanceNarrationAtUnscaledTime = -1f;
      return;
    }

    if (_autoAdvanceNarrationAtUnscaledTime < 0f)
    {
      _autoAdvanceNarrationAtUnscaledTime = Time.unscaledTime + autoAdvanceNarrationDelay;
      return;
    }

    if (Time.unscaledTime < _autoAdvanceNarrationAtUnscaledTime)
    {
      return;
    }

    _autoAdvanceNarrationAtUnscaledTime = -1f;
    AdvanceNarration();
  }

  private void ApplyGameplayState()
  {
    if (spaceViewModeSwitcher == null && autoResolveSceneReferences)
    {
      spaceViewModeSwitcher = FindFirstObjectByType<SpaceViewModeSwitcher>(FindObjectsInactive.Include);
    }

    if (wall2DPlayerRespawner == null && autoResolveSceneReferences)
    {
      wall2DPlayerRespawner = FindFirstObjectByType<Wall2DPlayerRespawner>(FindObjectsInactive.Include);
    }

    if (spaceViewModeSwitcher != null)
    {
      spaceViewModeSwitcher.SetExternalPlayerGameplayLock(!_is2DGameplayEnabled);
      spaceViewModeSwitcher.UpdatePlayerMovementState();
    }
    else if (wall2DPlayerRespawner != null)
    {
      wall2DPlayerRespawner.SetPlayerGameplayEnabled(_is2DGameplayEnabled);
    }

    if (handPinchController != null)
    {
      if (!handPinchController.enabled)
      {
        handPinchController.enabled = true;
      }

      handPinchController.SetInteractionEnabled(_is3DInteractionEnabled);
    }

    if (mouseGrabInputController != null)
    {
      if (!mouseGrabInputController.enabled)
      {
        mouseGrabInputController.enabled = true;
      }

      mouseGrabInputController.SetInteractionEnabled(_is3DInteractionEnabled);
    }

    StateChanged?.Invoke();
  }

  private void Apply2DGameplayStep(bool isEnabled)
  {
    _is2DGameplayEnabled = isEnabled;
    ApplyGameplayState();

    if (!isEnabled || spaceViewModeSwitcher == null)
    {
      return;
    }

    // チュートリアル中は 2D 開放と同時に自動進行を始めたい。
    spaceViewModeSwitcher.EnsureRealTimeGameplayStarted(playGoodReaction: false);
  }

  private void AutoResolveSceneReferences()
  {
    if (!autoResolveSceneReferences)
    {
      return;
    }

    if (spaceViewModeSwitcher == null)
    {
      spaceViewModeSwitcher = FindFirstObjectByType<SpaceViewModeSwitcher>(FindObjectsInactive.Include);
    }

    if (wall2DPlayerRespawner == null)
    {
      wall2DPlayerRespawner = FindFirstObjectByType<Wall2DPlayerRespawner>(FindObjectsInactive.Include);
    }

    if (handPinchController == null)
    {
      handPinchController = FindFirstObjectByType<HandPinchController>(FindObjectsInactive.Include);
    }

    if (mouseGrabInputController == null)
    {
      mouseGrabInputController = FindFirstObjectByType<MouseGrabInputController>(FindObjectsInactive.Include);
    }

    if (goalController == null)
    {
      goalController = FindFirstObjectByType<GoalController>(FindObjectsInactive.Include);
    }

    CacheScreenFadeReferences();
  }

  private void SubscribeToGoalController()
  {
    if (goalController == null || _isSubscribedToGoal)
    {
      return;
    }

    goalController.GoalReached += HandleGoalReached;
    goalController.OverrideDefaultClearHandling = interceptGoalClear;
    _isSubscribedToGoal = true;
  }

  private void UnsubscribeFromGoalController()
  {
    if (goalController == null || !_isSubscribedToGoal)
    {
      return;
    }

    goalController.GoalReached -= HandleGoalReached;
    goalController.OverrideDefaultClearHandling = false;
    _isSubscribedToGoal = false;
  }

  private void HandleGoalReached(Collider2D _)
  {
    if (!interceptGoalClear)
    {
      return;
    }

    StopPendingGoalAdvanceCoroutine();

    if (_isWaitingForTrigger && string.Equals(_expectedTriggerId, goalTriggerId, StringComparison.OrdinalIgnoreCase))
    {
      _pendingGoalAdvanceCoroutine = StartCoroutine(HandleGoalReachedAfterDelay(_, completeSequenceDirectly: false));
      return;
    }

    _pendingGoalAdvanceCoroutine = StartCoroutine(HandleGoalReachedAfterDelay(_, completeSequenceDirectly: true));
  }

  private void StopActiveStepCoroutine()
  {
    if (_activeStepCoroutine == null)
    {
      return;
    }

    StopCoroutine(_activeStepCoroutine);
    _activeStepCoroutine = null;
  }

  private void StopPendingGoalAdvanceCoroutine()
  {
    if (_pendingGoalAdvanceCoroutine == null)
    {
      return;
    }

    StopCoroutine(_pendingGoalAdvanceCoroutine);
    _pendingGoalAdvanceCoroutine = null;
  }

  private IEnumerator HandleGoalReachedAfterDelay(Collider2D playerCollider, bool completeSequenceDirectly)
  {
    var waitSeconds = ResolveGoalAdvanceDelaySeconds(playerCollider);
    if (waitSeconds > 0f)
    {
      LogDebug($"Waiting for goal reaction: {waitSeconds:0.###} seconds");
      yield return new WaitForSecondsRealtime(waitSeconds);
    }

    _pendingGoalAdvanceCoroutine = null;

    if (!_isSequenceRunning || _isSequenceCompleted)
    {
      yield break;
    }

    if (completeSequenceDirectly)
    {
      CompleteSequence();
      yield break;
    }

    NotifyTriggerReached(goalTriggerId);
  }

  private float ResolveGoalAdvanceDelaySeconds(Collider2D playerCollider)
  {
    var delaySeconds = Mathf.Max(0f, additionalGoalAdvanceDelay);
    if (!waitForGoalReactionBeforeAdvancing || playerCollider == null)
    {
      return delaySeconds;
    }

    var playerAnimator = playerCollider.GetComponent<Wall2DPlayerAnimator>();
    if (playerAnimator == null)
    {
      playerAnimator = playerCollider.GetComponentInParent<Wall2DPlayerAnimator>();
    }

    if (playerAnimator != null)
    {
      delaySeconds += Mathf.Max(0f, playerAnimator.GoalReactionDuration);
    }

    return delaySeconds;
  }

  private void ClampSerializedValues()
  {
    typewriterCharactersPerSecond = Mathf.Max(0f, typewriterCharactersPerSecond);
    autoAdvanceNarrationDelay = Mathf.Max(0f, autoAdvanceNarrationDelay);
    advanceInputGracePeriod = Mathf.Max(0f, advanceInputGracePeriod);
    additionalGoalAdvanceDelay = Mathf.Max(0f, additionalGoalAdvanceDelay);
    goalTriggerId = goalTriggerId ?? string.Empty;

    if (steps == null)
    {
      steps = new List<TutorialStep>();
      return;
    }

    for (var i = 0; i < steps.Count; i++)
    {
      var step = steps[i];
      if (step == null)
      {
        continue;
      }

      step.waitSeconds = Mathf.Max(0f, step.waitSeconds);
      step.fadeDuration = Mathf.Max(0f, step.fadeDuration);
      step.fadeFromAlpha = Mathf.Clamp01(step.fadeFromAlpha);
      step.fadeToAlpha = Mathf.Clamp01(step.fadeToAlpha);

      if (step.pages == null)
      {
        step.pages = new List<TutorialNarrationPage>();
      }
    }
  }

  private static List<TutorialStep> CloneSteps(IList<TutorialStep> sourceSteps)
  {
    var clonedSteps = new List<TutorialStep>();
    if (sourceSteps == null)
    {
      return clonedSteps;
    }

    for (var i = 0; i < sourceSteps.Count; i++)
    {
      var sourceStep = sourceSteps[i];
      if (sourceStep == null)
      {
        continue;
      }

      var clonedStep = new TutorialStep
      {
        stepId = sourceStep.stepId,
        kind = sourceStep.kind,
        note = sourceStep.note,
        triggerId = sourceStep.triggerId,
        waitSeconds = sourceStep.waitSeconds,
        useUnscaledTime = sourceStep.useUnscaledTime,
        fadeColor = sourceStep.fadeColor,
        fadeFromAlpha = sourceStep.fadeFromAlpha,
        fadeToAlpha = sourceStep.fadeToAlpha,
        fadeDuration = sourceStep.fadeDuration,
        fadeUseUnscaledTime = sourceStep.fadeUseUnscaledTime,
        enable2DGameplay = sourceStep.enable2DGameplay,
        enable3DInteraction = sourceStep.enable3DInteraction,
        targetViewMode = sourceStep.targetViewMode,
        pages = ClonePages(sourceStep.pages),
        onStepStarted = new UnityEvent(),
        onStepCompleted = new UnityEvent(),
      };

      clonedSteps.Add(clonedStep);
    }

    return clonedSteps;
  }

  private static List<TutorialNarrationPage> ClonePages(IList<TutorialNarrationPage> sourcePages)
  {
    var clonedPages = new List<TutorialNarrationPage>();
    if (sourcePages == null)
    {
      return clonedPages;
    }

    for (var i = 0; i < sourcePages.Count; i++)
    {
      var sourcePage = sourcePages[i];
      if (sourcePage == null)
      {
        continue;
      }

      clonedPages.Add(new TutorialNarrationPage
      {
        pageId = sourcePage.pageId,
        speakerName = sourcePage.speakerName,
        message = sourcePage.message,
        illustration = sourcePage.illustration,
      });
    }

    return clonedPages;
  }

  private bool HasAdvanceInputThisFrame()
  {
    if (advanceKeys == null || advanceKeys.Length == 0)
    {
      return Input.GetMouseButtonDown(0);
    }

    for (var i = 0; i < advanceKeys.Length; i++)
    {
      if (Input.GetKeyDown(advanceKeys[i]))
      {
        return true;
      }
    }

    return false;
  }

  private void BlockAdvanceInputTemporarily()
  {
    _waitForAdvanceInputRelease = IsAnyAdvanceInputPressed();

    if (advanceInputGracePeriod <= 0f)
    {
      return;
    }

    _advanceInputBlockedUntilUnscaledTime = Time.unscaledTime + advanceInputGracePeriod;
  }

  private bool IsAdvanceInputBlocked()
  {
    if (_waitForAdvanceInputRelease)
    {
      _waitForAdvanceInputRelease = IsAnyAdvanceInputPressed();
    }

    return _waitForAdvanceInputRelease || Time.unscaledTime < _advanceInputBlockedUntilUnscaledTime;
  }

  private bool IsAnyAdvanceInputPressed()
  {
    if (advanceKeys == null || advanceKeys.Length == 0)
    {
      return Input.GetMouseButton(0);
    }

    for (var i = 0; i < advanceKeys.Length; i++)
    {
      if (Input.GetKey(advanceKeys[i]))
      {
        return true;
      }
    }

    return false;
  }

  private void LogDebug(string message)
  {
    if (!enableDebugLogging)
    {
      return;
    }

    Debug.Log($"[TutorialSequence] {message}", this);
  }

  private string BuildStepLabel(int stepIndex, TutorialStep step)
  {
    if (step == null)
    {
      return $"index={stepIndex}";
    }

    var stepNumber = stepIndex + 1;
    var totalSteps = steps != null ? steps.Count : 0;
    var stepId = string.IsNullOrWhiteSpace(step.stepId) ? "(no-id)" : step.stepId;
    return $"{stepNumber}/{totalSteps} id={stepId} kind={step.kind}";
  }

  private void CacheScreenFadeReferences()
  {
    if (screenFadeRoot == null && screenFadeCanvasGroup != null)
    {
      screenFadeRoot = screenFadeCanvasGroup.gameObject;
    }

    if (screenFadeRoot == null && screenFadeImage != null)
    {
      screenFadeRoot = screenFadeImage.gameObject;
    }

    if (screenFadeCanvasGroup == null && screenFadeRoot != null)
    {
      screenFadeCanvasGroup = screenFadeRoot.GetComponent<CanvasGroup>();
    }

    if (screenFadeImage == null && screenFadeRoot != null)
    {
      screenFadeImage = screenFadeRoot.GetComponentInChildren<Image>(true);
    }
  }

  private void SetScreenFadeImmediate(Color color, float alpha)
  {
    CacheScreenFadeReferences();
    SetScreenFadeRootVisible(true);

    if (screenFadeImage != null)
    {
      var opaqueColor = color;
      opaqueColor.a = 1f;
      screenFadeImage.color = opaqueColor;
    }

    ApplyScreenFadeAlpha(alpha);
  }

  private void ApplyScreenFadeAlpha(float alpha)
  {
    alpha = Mathf.Clamp01(alpha);

    if (screenFadeCanvasGroup != null)
    {
      screenFadeCanvasGroup.alpha = alpha;
      screenFadeCanvasGroup.blocksRaycasts = alpha > 0.001f;
      screenFadeCanvasGroup.interactable = false;
    }
    else if (screenFadeImage != null)
    {
      var imageColor = screenFadeImage.color;
      imageColor.a = alpha;
      screenFadeImage.color = imageColor;
    }

    if (disableScreenFadeRootWhenTransparent)
    {
      SetScreenFadeRootVisible(alpha > 0.001f);
    }
  }

  private void SetScreenFadeRootVisible(bool isVisible)
  {
    if (screenFadeRoot != null && screenFadeRoot.activeSelf != isVisible)
    {
      screenFadeRoot.SetActive(isVisible);
    }
  }

  private string BuildDisplayedText()
  {
    var fullText = CurrentFullText;
    if (string.IsNullOrEmpty(fullText))
    {
      return string.Empty;
    }

    var safeCharacterCount = Mathf.Clamp(_visibleCharacterCount, 0, fullText.Length);
    return fullText.Substring(0, safeCharacterCount);
  }

  private bool IsStepIndexValid(int stepIndex)
  {
    return steps != null && stepIndex >= 0 && stepIndex < steps.Count;
  }

  private static bool IsPageIndexValid(TutorialStep step, int pageIndex)
  {
    return step != null && step.pages != null && pageIndex >= 0 && pageIndex < step.pages.Count;
  }
}
