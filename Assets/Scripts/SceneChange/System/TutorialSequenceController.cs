using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
/// <summary>
/// チュートリアルの進行を 1 ステップずつ順番に管理するコントローラ。
/// Inspector で steps を並べるだけで、
/// 「ナレーション → 移動開始 → トリガー待ち → 3D操作解放 → クリア」
/// のような流れを構成できる。
/// </summary>
public sealed class TutorialSequenceController : MonoBehaviour
{
  public enum TutorialStepKind
  {
    // 複数ページ対応のナレーションを表示する。
    Narration,
    // 指定 ID のトリガーが通知されるまで待機する。
    WaitForTrigger,
    // 指定秒数だけ待機する。
    WaitForSeconds,
    // 2D プレイヤー進行の停止 / 再開を切り替える。
    Set2DGameplay,
    // 3D 操作の停止 / 再開を切り替える。
    Set3DInteraction,
    // 3D視点 / 2D視点を切り替える。
    SetViewMode,
    // チュートリアルを終了する。
    CompleteTutorial,
  }

  [Serializable]
  public sealed class TutorialNarrationPage
  {
    // データ管理用の任意 ID。処理上は未使用。
    public string pageId;
    // UI 側で話者名を出したい時に使う。
    public string speakerName;
    // 文字送りの対象になる本文。
    [TextArea(2, 6)] public string message;
    // ナレーションに添える画像。未設定でも可。
    public Sprite illustration;
  }

  [Serializable]
  public sealed class TutorialStep
  {
    // データ管理用の任意 ID。ログや識別用。
    public string stepId;
    // このステップで何をするか。
    public TutorialStepKind kind = TutorialStepKind.Narration;
    // Inspector 上で用途を書いておくメモ。
    [TextArea(1, 3)] public string note;
    // Narration のときに使うページ一覧。
    public List<TutorialNarrationPage> pages = new List<TutorialNarrationPage>();
    // WaitForTrigger のときに待つトリガー ID。
    public string triggerId;
    // WaitForSeconds の待機秒数。
    public float waitSeconds;
    // true なら Time.timeScale の影響を受けずに待つ。
    public bool useUnscaledTime = true;
    // Set2DGameplay の切り替え値。
    public bool enable2DGameplay = true;
    // Set3DInteraction の切り替え値。
    public bool enable3DInteraction = true;
    // SetViewMode の切り替え先。
    public SpaceViewMode targetViewMode = SpaceViewMode.World3D;
    // ステップ開始時に Inspector から追加処理を差し込みたい時に使う。
    public UnityEvent onStepStarted;
    // ステップ完了時に Inspector から追加処理を差し込みたい時に使う。
    public UnityEvent onStepCompleted;
  }

  public event Action StateChanged;
  public event Action<int, string> StepChanged;
  public event Action<int, int> NarrationPageChanged;
  public event Action SequenceCompleted;

  [Header("Sequence")]
  [SerializeField] private bool playOnStart = true;
  // ここに上から順番にステップを並べる。
  [SerializeField] private List<TutorialStep> steps = new List<TutorialStep>();

  [Header("Initial Gameplay State")]
  // シーケンス開始直後の 2D プレイヤー進行可否。
  [SerializeField] private bool initial2DGameplayEnabled;
  // シーケンス開始直後の 3D 操作可否。
  [SerializeField] private bool initial3DInteractionEnabled;

  [Header("Narration")]
  // 1 秒あたりに表示する文字数。
  [SerializeField] private float typewriterCharactersPerSecond = 32f;
  // true ならキー入力で文字送りを進められる。
  [SerializeField] private bool acceptAdvanceInput = true;
  // ナレーション送りに使うキー。
  [SerializeField] private KeyCode[] advanceKeys = { KeyCode.Space, KeyCode.Return, KeyCode.Mouse0 };

  [Header("Scene References")]
  // true なら未設定参照をシーンから自動で探す。
  [SerializeField] private bool autoResolveSceneReferences = true;
  [SerializeField] private SpaceViewModeSwitcher spaceViewModeSwitcher;
  [SerializeField] private Wall2DPlayerRespawner wall2DPlayerRespawner;
  [SerializeField] private HandPinchController handPinchController;
  [SerializeField] private MouseGrabInputController mouseGrabInputController;
  [SerializeField] private GoalController goalController;

  [Header("Goal Handling")]
  // true なら通常のクリアUIを止めて、チュートリアル側でゴール処理を受け持つ。
  [SerializeField] private bool interceptGoalClear = true;
  // ゴールをトリガー待ちとして扱う時に使う ID。
  [SerializeField] private string goalTriggerId = "Goal";
  // シーケンス完了時に StageSelect へ遷移する。
  [SerializeField] private bool goStageSelectWhenSequenceCompletes = true;

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
      // シーン開始と同時にチュートリアルを流したい場合の入口。
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

    if (!acceptAdvanceInput || !IsNarrationStepActive || !HasAdvanceInputThisFrame())
    {
      return;
    }

    AdvanceNarration();
  }

  private void OnDisable()
  {
    StopActiveStepCoroutine();
    UnsubscribeFromGoalController();
  }

  private void OnValidate()
  {
    ClampSerializedValues();
  }

  public void BeginSequence()
  {
    // 外部から何度でも呼べるよう、毎回初期化して 0 ステップ目から再開する。
    StopActiveStepCoroutine();
    ResetGameplayState();
    ResetSequenceProgress();
    ApplyGameplayState();

    _isSequenceRunning = true;
    _isSequenceCompleted = false;
    AdvanceToStep(0);
  }

  public void RestartSequence()
  {
    BeginSequence();
  }

  public bool AdvanceNarration()
  {
    if (!IsNarrationStepActive)
    {
      return false;
    }

    if (!IsCurrentNarrationFullyRevealed)
    {
      // 文字送り中に押されたら、まずは全文表示まで一気に進める。
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
      // ページがまだ残っていれば次ページへ。
      StartNarrationPage(nextPageIndex);
      return true;
    }

    // 最終ページまで終わったらステップ完了。
    CompleteCurrentStep();
    return true;
  }

  public void NotifyTriggerReached(string triggerId)
  {
    // Trigger 側から「指定 ID に到達した」と通知された時の入口。
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
    StateChanged?.Invoke();
    CompleteCurrentStep();
  }

  public void CompleteSequence()
  {
    // チュートリアルを明示的に終わらせる共通出口。
    if (_isSequenceCompleted)
    {
      return;
    }

    StopActiveStepCoroutine();
    _isSequenceRunning = false;
    _isSequenceCompleted = true;
    _isWaitingForTrigger = false;
    _expectedTriggerId = string.Empty;
    SequenceCompleted?.Invoke();
    StateChanged?.Invoke();

    if (goStageSelectWhenSequenceCompletes)
    {
      GameSceneManager.GoStageSelect();
    }
  }

  public void Set2DGameplayEnabled(bool isEnabled)
  {
    // UI や他スクリプトから直接 2D 進行を切り替えたい時用。
    Apply2DGameplayStep(isEnabled);
  }

  public void Set3DInteractionEnabled(bool isEnabled)
  {
    // UI や他スクリプトから直接 3D 操作を切り替えたい時用。
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
      // 最後まで終わったらシーケンス全体を完了扱いにする。
      CompleteSequence();
      return;
    }

    StopActiveStepCoroutine();
    ClearCurrentStepTransientState();
    _currentStepIndex = stepIndex;

    var step = steps[stepIndex];
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
      case TutorialStepKind.Set2DGameplay:
        // true の時は 2D を再開するだけでなく、必要ならスペース待ちも解除する。
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

    if (typewriterCharactersPerSecond <= 0f || string.IsNullOrEmpty(CurrentFullText))
    {
      RevealCurrentNarrationImmediately();
    }

    NarrationPageChanged?.Invoke(_currentStepIndex, _currentPageIndex);
    StateChanged?.Invoke();
  }

  private void StartTriggerWait(TutorialStep step)
  {
    // Trigger 側から同じ ID が通知されるまでここで待機する。
    _expectedTriggerId = step.triggerId ?? string.Empty;
    _isWaitingForTrigger = !string.IsNullOrEmpty(_expectedTriggerId);
    StateChanged?.Invoke();

    if (!_isWaitingForTrigger)
    {
      CompleteCurrentStep();
    }
  }

  private void StartWaitForSeconds(TutorialStep step)
  {
    _activeStepCoroutine = StartCoroutine(WaitForSecondsStep(step.waitSeconds, step.useUnscaledTime));
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

  private void CompleteCurrentStep()
  {
    // 現在ステップの終了処理をまとめて行い、次ステップへ進める。
    var step = CurrentStep;
    if (step != null)
    {
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
    _isWaitingForTrigger = false;
    _expectedTriggerId = string.Empty;
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

  private void ApplyGameplayState()
  {
    // 進行状態に応じて、2D / 3D の入力可否を実際のコンポーネントへ反映する。
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
      handPinchController.enabled = _is3DInteractionEnabled;
    }

    if (mouseGrabInputController != null)
    {
      mouseGrabInputController.enabled = _is3DInteractionEnabled;
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

    // チュートリアルでは「2D を再開した瞬間にそのまま進む」ことが多いため、
    // リアルタイム開始ゲートもここで明示的に開ける。
    spaceViewModeSwitcher.EnsureRealTimeGameplayStarted(playGoodReaction: false);
  }

  private void AutoResolveSceneReferences()
  {
    // 参照を手で入れ忘れても動きやすいように、シーンから自動探索する。
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
  }

  private void SubscribeToGoalController()
  {
    // ゴール到達時にチュートリアル側へ通知を受ける。
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

    // ゴールを triggerId 扱いにしている場合は、その待機を解除する。
    if (_isWaitingForTrigger && string.Equals(_expectedTriggerId, goalTriggerId, StringComparison.OrdinalIgnoreCase))
    {
      NotifyTriggerReached(goalTriggerId);
      return;
    }

    CompleteSequence();
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

  private void ClampSerializedValues()
  {
    typewriterCharactersPerSecond = Mathf.Max(0f, typewriterCharactersPerSecond);
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
      if (step.pages == null)
      {
        step.pages = new List<TutorialNarrationPage>();
      }
    }
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
