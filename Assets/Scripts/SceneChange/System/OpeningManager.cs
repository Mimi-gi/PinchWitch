using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class OpeningManager : MonoBehaviour
{
  [Serializable]
  public sealed class OpeningPage
  {
    public Sprite image;
    public string speakerName;
    [TextArea(1, 3)] public string message;
  }

  [Header("Sequence")]
  [SerializeField] private bool playOnStart = true;
  [SerializeField] private OpeningPage[] pages = new OpeningPage[3];
  [SerializeField] private float initialImageFadeDuration = 1.5f;
  [SerializeField] private float transitionFadeDuration = 0.75f;
  [SerializeField] private float firstMessageWindowDelay = 1f;
  [SerializeField] private float lineCompleteDelay = 0f;
  [SerializeField] private bool useUnscaledTime = true;

  [Header("Narration")]
  [SerializeField] private float typewriterCharactersPerSecond = 32f;
  [SerializeField] private string defaultSpeakerLabel = string.Empty;
  [SerializeField] private string revealPromptText = "...";
  [SerializeField] private string completePromptText = string.Empty;

  [Header("Scene Transition")]
  [SerializeField] private bool useGameSceneManagerStageTransition = true;
  [SerializeField] private int nextStageNumber = 1;
  [SerializeField] private string fallbackNextSceneName = "stage_1";

  [Header("UI References")]
  [SerializeField] private Image openingImage;
  [SerializeField] private CanvasGroup narrationCanvasGroup;
  [SerializeField] private GameObject textWindowRoot;
  [SerializeField] private TMP_Text speakerText;
  [SerializeField] private TMP_Text bodyText;
  [SerializeField] private TMP_Text promptText;

  [Header("Auto Resolve")]
  [SerializeField] private bool autoResolveReferences = true;
  [SerializeField] private bool enableDebugLogging;

  private CancellationTokenSource _sequenceCts;
  private string _currentFullText = string.Empty;
  private int _visibleCharacterCount;
  private float _typewriterProgress;

  private void Reset()
  {
    AutoResolveReferences();
    ClampSerializedValues();
    ClearNarrationText();
  }

  private void Awake()
  {
    AutoResolveReferences();
    ClampSerializedValues();
    InitializeVisualState();
  }

  private void Start()
  {
    if (playOnStart)
    {
      BeginOpening();
    }
  }

  private void OnDisable()
  {
    StopSequence();
  }

  private void OnValidate()
  {
    ClampSerializedValues();
  }

  public void BeginOpening()
  {
    StopSequence();
    _sequenceCts = new CancellationTokenSource();
    OpeningSequenceAsync(_sequenceCts.Token).Forget();
  }

  public void SkipToNextScene()
  {
    StopSequence();
    LoadNextScene();
  }

  private async UniTask OpeningSequenceAsync(CancellationToken ct)
  {
    InitializeVisualState();

    if (pages == null || pages.Length == 0)
    {
      LogDebug("Opening pages are empty. Loading next scene.");
      LoadNextScene();
      return;
    }

    for (var i = 0; i < pages.Length; i++)
    {
      var page = pages[i];
      if (page == null)
      {
        continue;
      }

      HideNarrationWindow();
      ClearNarrationText();
      ApplyOpeningImage(page.image);

      var fadeDuration = i == 0 ? initialImageFadeDuration : transitionFadeDuration;
      await FadeOpeningImageAsync(0f, 1f, fadeDuration, ct);

      if (i == 0 && firstMessageWindowDelay > 0f)
      {
        await WaitAsync(firstMessageWindowDelay, ct);
      }

      ShowNarrationWindow();
      await RevealMessageAsync(page, ct);

      if (lineCompleteDelay > 0f)
      {
        await WaitAsync(lineCompleteDelay, ct);
      }

      if (i < pages.Length - 1)
      {
        HideNarrationWindow();
        ClearNarrationText();
        await FadeOpeningImageAsync(1f, 0f, transitionFadeDuration, ct);
      }
    }

    _sequenceCts = null;
    LoadNextScene();
  }

  private async UniTask RevealMessageAsync(OpeningPage page, CancellationToken ct)
  {
    _currentFullText = page.message ?? string.Empty;
    _visibleCharacterCount = 0;
    _typewriterProgress = 0f;

    ApplySpeaker(page.speakerName);
    ApplyDisplayedText();
    ApplyPrompt(isFullyRevealed: string.IsNullOrEmpty(_currentFullText));

    if (typewriterCharactersPerSecond <= 0f || string.IsNullOrEmpty(_currentFullText))
    {
      RevealCurrentMessageImmediately();
      return;
    }

    while (_visibleCharacterCount < _currentFullText.Length)
    {
      _typewriterProgress += typewriterCharactersPerSecond * DeltaTime;
      var nextVisibleCharacterCount = Mathf.Clamp(Mathf.FloorToInt(_typewriterProgress), 0, _currentFullText.Length);
      if (nextVisibleCharacterCount != _visibleCharacterCount)
      {
        _visibleCharacterCount = nextVisibleCharacterCount;
        ApplyDisplayedText();
      }

      await UniTask.Yield(ct);
    }

    ApplyPrompt(isFullyRevealed: true);
  }

  private void RevealCurrentMessageImmediately()
  {
    _visibleCharacterCount = _currentFullText.Length;
    _typewriterProgress = _visibleCharacterCount;
    ApplyDisplayedText();
    ApplyPrompt(isFullyRevealed: true);
  }

  private async UniTask FadeOpeningImageAsync(float fromAlpha, float toAlpha, float duration, CancellationToken ct)
  {
    SetOpeningImageAlpha(fromAlpha);

    if (duration <= 0f)
    {
      SetOpeningImageAlpha(toAlpha);
      return;
    }

    var elapsed = 0f;
    while (elapsed < duration)
    {
      elapsed += DeltaTime;
      var t = Mathf.Clamp01(elapsed / duration);
      SetOpeningImageAlpha(Mathf.Lerp(fromAlpha, toAlpha, t));
      await UniTask.Yield(ct);
    }

    SetOpeningImageAlpha(toAlpha);
  }

  private async UniTask WaitAsync(float seconds, CancellationToken ct)
  {
    if (seconds <= 0f)
    {
      return;
    }

    await UniTask.Delay(TimeSpan.FromSeconds(seconds), ignoreTimeScale: useUnscaledTime, cancellationToken: ct);
  }

  private void InitializeVisualState()
  {
    HideNarrationWindow();
    ClearNarrationText();
    SetOpeningImageAlpha(0f);
  }

  private void ApplyOpeningImage(Sprite sprite)
  {
    if (openingImage == null)
    {
      Debug.LogWarning("[OpeningManager] openingImage が未設定です。", this);
      return;
    }

    openingImage.sprite = sprite;
    openingImage.enabled = sprite != null;
    openingImage.raycastTarget = false;
  }

  private void SetOpeningImageAlpha(float alpha)
  {
    if (openingImage == null)
    {
      return;
    }

    var color = openingImage.color;
    color.a = Mathf.Clamp01(alpha);
    openingImage.color = color;
  }

  private void ShowNarrationWindow()
  {
    SetNarrationWindowVisible(true);
  }

  private void HideNarrationWindow()
  {
    SetNarrationWindowVisible(false);
  }

  private void SetNarrationWindowVisible(bool isVisible)
  {
    if (narrationCanvasGroup != null)
    {
      narrationCanvasGroup.alpha = isVisible ? 1f : 0f;
      narrationCanvasGroup.blocksRaycasts = isVisible;
      narrationCanvasGroup.interactable = isVisible;
      return;
    }

    if (textWindowRoot != null)
    {
      textWindowRoot.SetActive(isVisible);
    }
  }

  private void ApplySpeaker(string speakerName)
  {
    if (speakerText == null)
    {
      return;
    }

    speakerText.text = string.IsNullOrWhiteSpace(speakerName) ? defaultSpeakerLabel : speakerName;
  }

  private void ApplyDisplayedText()
  {
    if (bodyText == null)
    {
      return;
    }

    var safeCharacterCount = Mathf.Clamp(_visibleCharacterCount, 0, _currentFullText.Length);
    bodyText.text = string.IsNullOrEmpty(_currentFullText)
      ? string.Empty
      : _currentFullText.Substring(0, safeCharacterCount);
  }

  private void ApplyPrompt(bool isFullyRevealed)
  {
    if (promptText == null)
    {
      return;
    }

    promptText.text = isFullyRevealed ? completePromptText : revealPromptText;
  }

  private void ClearNarrationText()
  {
    _currentFullText = string.Empty;
    _visibleCharacterCount = 0;
    _typewriterProgress = 0f;

    if (speakerText != null)
    {
      speakerText.text = string.Empty;
    }

    if (bodyText != null)
    {
      bodyText.text = string.Empty;
    }

    if (promptText != null)
    {
      promptText.text = string.Empty;
    }
  }

  private void LoadNextScene()
  {
    if (useGameSceneManagerStageTransition)
    {
      GameSceneManager.GoStage(nextStageNumber);
      return;
    }

    if (string.IsNullOrWhiteSpace(fallbackNextSceneName))
    {
      Debug.LogWarning("[OpeningManager] fallbackNextSceneName が空です。", this);
      return;
    }

    SceneManager.LoadScene(fallbackNextSceneName, LoadSceneMode.Single);
  }

  private void StopSequence()
  {
    if (_sequenceCts == null)
    {
      return;
    }

    _sequenceCts.Cancel();
    _sequenceCts.Dispose();
    _sequenceCts = null;
  }

  private void AutoResolveReferences()
  {
    if (!autoResolveReferences)
    {
      return;
    }

    if (openingImage == null)
    {
      openingImage = FindComponentByObjectName<Image>("Sprite");
    }

    if (narrationCanvasGroup == null)
    {
      narrationCanvasGroup = FindComponentByObjectName<CanvasGroup>("TutorialNarrationRoot");
    }

    if (textWindowRoot == null)
    {
      var windowTransform = FindTransformByName("Window");
      textWindowRoot = windowTransform != null ? windowTransform.gameObject : null;
    }

    if (speakerText == null)
    {
      speakerText = FindComponentByObjectName<TMP_Text>("speakerName");
    }

    if (bodyText == null)
    {
      bodyText = FindComponentByObjectName<TMP_Text>("bodyName");
    }

    if (promptText == null)
    {
      promptText = FindComponentByObjectName<TMP_Text>("promptText");
    }
  }

  private void ClampSerializedValues()
  {
    initialImageFadeDuration = Mathf.Max(0f, initialImageFadeDuration);
    transitionFadeDuration = Mathf.Max(0f, transitionFadeDuration);
    firstMessageWindowDelay = Mathf.Max(0f, firstMessageWindowDelay);
    lineCompleteDelay = Mathf.Max(0f, lineCompleteDelay);
    typewriterCharactersPerSecond = Mathf.Max(0f, typewriterCharactersPerSecond);
    nextStageNumber = Mathf.Max(1, nextStageNumber);

    if (pages == null)
    {
      pages = new OpeningPage[3];
    }

    for (var i = 0; i < pages.Length; i++)
    {
      if (pages[i] == null)
      {
        pages[i] = new OpeningPage();
      }
    }
  }

  private void LogDebug(string message)
  {
    if (!enableDebugLogging)
    {
      return;
    }

    Debug.Log($"[OpeningManager] {message}", this);
  }

  private float DeltaTime => useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

  private static T FindComponentByObjectName<T>(string objectName) where T : Component
  {
    var transform = FindTransformByName(objectName);
    return transform != null ? transform.GetComponent<T>() : null;
  }

  private static Transform FindTransformByName(string objectName)
  {
    if (string.IsNullOrEmpty(objectName))
    {
      return null;
    }

    var transforms = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
    for (var i = 0; i < transforms.Length; i++)
    {
      if (transforms[i] != null && transforms[i].name == objectName)
      {
        return transforms[i];
      }
    }

    return null;
  }
}
