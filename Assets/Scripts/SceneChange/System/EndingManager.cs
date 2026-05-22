using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class EndingManager : MonoBehaviour
{
  [Serializable]
  public sealed class EndingMessage
  {
    public string speakerName;
    [TextArea(1, 3)] public string text;
  }

  [Serializable]
  public sealed class EndingPage
  {
    public Sprite image;
    public string speakerName;
    public EndingMessage[] messages = { new EndingMessage() };
  }

  private sealed class ResolvedEndingMessage
  {
    public string speakerName;
    public string text;
  }

  [Header("Sequence")]
  [SerializeField] private bool playOnStart = true;
  [SerializeField] private EndingPage[] pages = new EndingPage[2];
  [SerializeField] private float initialImageFadeDuration = 1.5f;
  [SerializeField] private float transitionFadeDuration = 0.75f;
  [SerializeField] private float firstMessageWindowDelay = 1f;
  [SerializeField] private float lineCompleteDelay = 0f;
  [SerializeField] private float imageOnlyDisplayDuration = 2f;
  [SerializeField] private bool useUnscaledTime = true;

  [Header("Narration")]
  [SerializeField] private float typewriterCharactersPerSecond = 32f;
  [SerializeField] private string defaultSpeakerLabel = string.Empty;
  [SerializeField] private string revealPromptText = "...";
  [SerializeField] private string completePromptText = string.Empty;

  [Header("Scene Transition")]
  [SerializeField] private bool useGameSceneManagerTitleTransition = true;
  [SerializeField] private string fallbackTitleSceneName = "Title";

  [Header("UI References")]
  [SerializeField] private Image endingImage;
  [SerializeField] private CanvasGroup narrationCanvasGroup;
  [SerializeField] private GameObject textWindowRoot;
  [SerializeField] private TMP_Text speakerText;
  [SerializeField] private TMP_Text bodyText;
  [SerializeField] private TMP_Text promptText;

  [Header("Auto Resolve")]
  [SerializeField] private bool autoResolveReferences = true;
  [SerializeField] private bool enableDebugLogging;

  private Coroutine _sequenceCoroutine;
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
      BeginEnding();
    }
  }

  private void OnDisable()
  {
    StopSequenceCoroutine();
  }

  private void OnValidate()
  {
    ClampSerializedValues();
  }

  public void BeginEnding()
  {
    StopSequenceCoroutine();
    _sequenceCoroutine = StartCoroutine(EndingSequence());
  }

  public void SkipToTitle()
  {
    StopSequenceCoroutine();
    LoadTitleScene();
  }

  private IEnumerator EndingSequence()
  {
    InitializeVisualState();

    if (pages == null || pages.Length == 0)
    {
      LogDebug("Ending pages are empty. Loading title scene.");
      LoadTitleScene();
      yield break;
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
      ApplyEndingImage(page.image);

      var fadeDuration = i == 0 ? initialImageFadeDuration : transitionFadeDuration;
      yield return FadeEndingImage(0f, 1f, fadeDuration);

      if (i == 0 && firstMessageWindowDelay > 0f)
      {
        yield return Wait(firstMessageWindowDelay);
      }

      var messages = BuildMessages(page);
      if (messages.Count > 0)
      {
        ShowNarrationWindow();

        for (var messageIndex = 0; messageIndex < messages.Count; messageIndex++)
        {
          yield return RevealMessage(messages[messageIndex]);

          if (lineCompleteDelay > 0f)
          {
            yield return Wait(lineCompleteDelay);
          }
        }
      }
      else
      {
        HideNarrationWindow();
        ClearNarrationText();
        yield return Wait(imageOnlyDisplayDuration);
      }

      if (i < pages.Length - 1)
      {
        HideNarrationWindow();
        ClearNarrationText();
        yield return FadeEndingImage(1f, 0f, transitionFadeDuration);
      }
    }

    _sequenceCoroutine = null;
    LoadTitleScene();
  }

  private IEnumerator RevealMessage(ResolvedEndingMessage message)
  {
    _currentFullText = message != null ? message.text ?? string.Empty : string.Empty;
    _visibleCharacterCount = 0;
    _typewriterProgress = 0f;

    ApplySpeaker(message != null ? message.speakerName : string.Empty);
    ApplyDisplayedText();
    ApplyPrompt(isFullyRevealed: string.IsNullOrEmpty(_currentFullText));

    if (typewriterCharactersPerSecond <= 0f || string.IsNullOrEmpty(_currentFullText))
    {
      RevealCurrentMessageImmediately();
      yield break;
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

      yield return null;
    }

    ApplyPrompt(isFullyRevealed: true);
  }

  private static List<ResolvedEndingMessage> BuildMessages(EndingPage page)
  {
    var messages = new List<ResolvedEndingMessage>();
    if (page == null || page.messages == null)
    {
      return messages;
    }

    for (var i = 0; i < page.messages.Length; i++)
    {
      var message = page.messages[i];
      if (message == null || string.IsNullOrWhiteSpace(message.text))
      {
        continue;
      }

      messages.Add(new ResolvedEndingMessage
      {
        speakerName = string.IsNullOrWhiteSpace(message.speakerName) ? page.speakerName : message.speakerName,
        text = message.text,
      });
    }

    return messages;
  }

  private void RevealCurrentMessageImmediately()
  {
    _visibleCharacterCount = _currentFullText.Length;
    _typewriterProgress = _visibleCharacterCount;
    ApplyDisplayedText();
    ApplyPrompt(isFullyRevealed: true);
  }

  private IEnumerator FadeEndingImage(float fromAlpha, float toAlpha, float duration)
  {
    SetEndingImageAlpha(fromAlpha);

    if (duration <= 0f)
    {
      SetEndingImageAlpha(toAlpha);
      yield break;
    }

    var elapsed = 0f;
    while (elapsed < duration)
    {
      elapsed += DeltaTime;
      var t = Mathf.Clamp01(elapsed / duration);
      SetEndingImageAlpha(Mathf.Lerp(fromAlpha, toAlpha, t));
      yield return null;
    }

    SetEndingImageAlpha(toAlpha);
  }

  private IEnumerator Wait(float seconds)
  {
    if (seconds <= 0f)
    {
      yield break;
    }

    if (useUnscaledTime)
    {
      yield return new WaitForSecondsRealtime(seconds);
    }
    else
    {
      yield return new WaitForSeconds(seconds);
    }
  }

  private void InitializeVisualState()
  {
    HideNarrationWindow();
    ClearNarrationText();
    SetEndingImageAlpha(0f);
  }

  private void ApplyEndingImage(Sprite sprite)
  {
    if (endingImage == null)
    {
      Debug.LogWarning("[EndingManager] endingImage が未設定です。", this);
      return;
    }

    endingImage.sprite = sprite;
    endingImage.enabled = sprite != null;
    endingImage.raycastTarget = false;
  }

  private void SetEndingImageAlpha(float alpha)
  {
    if (endingImage == null)
    {
      return;
    }

    var color = endingImage.color;
    color.a = Mathf.Clamp01(alpha);
    endingImage.color = color;
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

  private void LoadTitleScene()
  {
    if (useGameSceneManagerTitleTransition)
    {
      GameSceneManager.GoTitle();
      return;
    }

    if (string.IsNullOrWhiteSpace(fallbackTitleSceneName))
    {
      Debug.LogWarning("[EndingManager] fallbackTitleSceneName が空です。", this);
      return;
    }

    SceneManager.LoadScene(fallbackTitleSceneName, LoadSceneMode.Single);
  }

  private void StopSequenceCoroutine()
  {
    if (_sequenceCoroutine == null)
    {
      return;
    }

    StopCoroutine(_sequenceCoroutine);
    _sequenceCoroutine = null;
  }

  private void AutoResolveReferences()
  {
    if (!autoResolveReferences)
    {
      return;
    }

    if (endingImage == null)
    {
      endingImage = FindComponentByObjectName<Image>("Sprite");
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
    imageOnlyDisplayDuration = Mathf.Max(0f, imageOnlyDisplayDuration);
    typewriterCharactersPerSecond = Mathf.Max(0f, typewriterCharactersPerSecond);

    if (pages == null)
    {
      pages = new EndingPage[2];
    }

    for (var i = 0; i < pages.Length; i++)
    {
      if (pages[i] == null)
      {
        pages[i] = new EndingPage();
      }

      if (pages[i].messages == null)
      {
        pages[i].messages = new[] { new EndingMessage() };
      }
    }
  }

  private void LogDebug(string message)
  {
    if (!enableDebugLogging)
    {
      return;
    }

    Debug.Log($"[EndingManager] {message}", this);
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
