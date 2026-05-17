using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class TutorialNarrationUI : MonoBehaviour
{
  [Header("Sequence")]
  [SerializeField] private TutorialSequenceController sequenceController;
  [SerializeField] private bool autoResolveSequenceController = true;

  [Header("Root")]
  [SerializeField] private GameObject narrationRoot;
  [SerializeField] private CanvasGroup canvasGroup;
  [SerializeField] private GameObject textWindowRoot;

  [Header("UI References")]
  [SerializeField] private TMP_Text speakerText;
  [SerializeField] private TMP_Text bodyText;
  [SerializeField] private TMP_Text promptText;
  [SerializeField] private Image illustrationImage;
  [SerializeField] private GameObject illustrationRoot;
  [SerializeField] private Button advanceButton;

  [Header("Copy")]
  [SerializeField] private string defaultSpeakerLabel = "NARRATION";
  [SerializeField] private string revealPromptText = "...";
  [SerializeField] private string advancePromptText = "Click / Space";

  private bool _isSubscribed;

  private void Reset()
  {
    CacheLocalReferences();
    AutoResolveSequenceController();
    RefreshVisualState();
  }

  private void Awake()
  {
    CacheLocalReferences();
    AutoResolveSequenceController();
    BindAdvanceButton();
  }

  private void Start()
  {
    AutoResolveSequenceController();
    SubscribeToSequenceController();
    RefreshVisualState();
  }

  private void OnEnable()
  {
    AutoResolveSequenceController();
    SubscribeToSequenceController();
    BindAdvanceButton();
    RefreshVisualState();
  }

  private void OnDisable()
  {
    UnbindAdvanceButton();
    UnsubscribeFromSequenceController();
  }

  private void OnValidate()
  {
    CacheLocalReferences();
    RefreshVisualState();
  }

  private void Update()
  {
    if (sequenceController != null || !autoResolveSequenceController)
    {
      return;
    }

    AutoResolveSequenceController();
    SubscribeToSequenceController();
    RefreshVisualState();
  }

  public void SetSequenceController(TutorialSequenceController controller)
  {
    if (sequenceController == controller)
    {
      return;
    }

    UnsubscribeFromSequenceController();
    sequenceController = controller;
    SubscribeToSequenceController();
    RefreshVisualState();
  }

  public void ConfigureRuntimeUI(
    GameObject root,
    CanvasGroup rootCanvasGroup,
    GameObject textWindow,
    TMP_Text speaker,
    TMP_Text body,
    TMP_Text prompt,
    Image illustration,
    GameObject illustrationContainer = null,
    Button button = null,
    string fallbackSpeakerLabel = "",
    string revealPrompt = "...",
    string advancePrompt = "Click / Space")
  {
    narrationRoot = root != null ? root : gameObject;
    canvasGroup = rootCanvasGroup;
    textWindowRoot = textWindow;
    speakerText = speaker;
    bodyText = body;
    promptText = prompt;
    illustrationImage = illustration;
    illustrationRoot = illustrationContainer;
    advanceButton = button;
    defaultSpeakerLabel = fallbackSpeakerLabel ?? string.Empty;
    revealPromptText = revealPrompt ?? "...";
    advancePromptText = advancePrompt ?? "Click / Space";

    BindAdvanceButton();
    RefreshVisualState();
  }

  public void AdvanceNarration()
  {
    if (sequenceController == null)
    {
      return;
    }

    sequenceController.AdvanceNarration();
  }

  private void HandleSequenceStateChanged()
  {
    RefreshVisualState();
  }

  private void HandleSequenceCompleted()
  {
    RefreshVisualState();
  }

  private void SubscribeToSequenceController()
  {
    if (sequenceController == null || _isSubscribed)
    {
      return;
    }

    sequenceController.StateChanged += HandleSequenceStateChanged;
    sequenceController.SequenceCompleted += HandleSequenceCompleted;
    _isSubscribed = true;
  }

  private void UnsubscribeFromSequenceController()
  {
    if (sequenceController == null || !_isSubscribed)
    {
      return;
    }

    sequenceController.StateChanged -= HandleSequenceStateChanged;
    sequenceController.SequenceCompleted -= HandleSequenceCompleted;
    _isSubscribed = false;
  }

  private void AutoResolveSequenceController()
  {
    if (!autoResolveSequenceController || sequenceController != null)
    {
      return;
    }

    sequenceController = FindFirstObjectByType<TutorialSequenceController>(FindObjectsInactive.Include);
  }

  private void CacheLocalReferences()
  {
    if (narrationRoot == null)
    {
      narrationRoot = gameObject;
    }

    if (canvasGroup == null)
    {
      canvasGroup = narrationRoot.GetComponent<CanvasGroup>();
    }

    if (textWindowRoot == null)
    {
      textWindowRoot = ResolveSharedParent(speakerText, bodyText, promptText);
    }
  }

  private void BindAdvanceButton()
  {
    if (advanceButton == null)
    {
      return;
    }

    advanceButton.onClick.RemoveListener(AdvanceNarration);
    advanceButton.onClick.AddListener(AdvanceNarration);
  }

  private void UnbindAdvanceButton()
  {
    if (advanceButton == null)
    {
      return;
    }

    advanceButton.onClick.RemoveListener(AdvanceNarration);
  }

  private void RefreshVisualState()
  {
    if (narrationRoot == null)
    {
      narrationRoot = gameObject;
    }

    var shouldShow = sequenceController != null
      && sequenceController.IsSequenceRunning
      && !sequenceController.IsSequenceCompleted
      && sequenceController.IsNarrationStepActive;

    ApplyVisibility(shouldShow);

    if (!shouldShow)
    {
      ClearTexts();
      return;
    }

    var hasIllustration = sequenceController.CurrentIllustration != null;
    var hasMessage = !string.IsNullOrWhiteSpace(sequenceController.CurrentFullText);
    var speaker = string.IsNullOrWhiteSpace(sequenceController.CurrentSpeakerName)
      ? defaultSpeakerLabel
      : sequenceController.CurrentSpeakerName;

    SetElementVisible(textWindowRoot, hasMessage);

    if (speakerText != null)
    {
      speakerText.text = speaker;
    }

    if (bodyText != null)
    {
      bodyText.text = sequenceController.CurrentDisplayedText;
    }

    if (promptText != null)
    {
      promptText.text = sequenceController.IsCurrentNarrationFullyRevealed ? advancePromptText : revealPromptText;
    }

    if (illustrationRoot != null)
    {
      illustrationRoot.SetActive(hasIllustration);
    }

    if (illustrationImage != null)
    {
      illustrationImage.sprite = sequenceController.CurrentIllustration;
      illustrationImage.enabled = hasIllustration;
    }

    if (advanceButton != null)
    {
      advanceButton.gameObject.SetActive(true);
    }
  }

  private void ApplyVisibility(bool isVisible)
  {
    if (canvasGroup != null)
    {
      canvasGroup.alpha = isVisible ? 1f : 0f;
      canvasGroup.blocksRaycasts = isVisible;
      canvasGroup.interactable = isVisible;
      return;
    }

    if (narrationRoot != null)
    {
      narrationRoot.SetActive(isVisible);
    }
  }

  private void ClearTexts()
  {
    SetElementVisible(textWindowRoot, true);

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

    if (illustrationImage != null)
    {
      illustrationImage.sprite = null;
      illustrationImage.enabled = false;
    }

    if (illustrationRoot != null)
    {
      illustrationRoot.SetActive(false);
    }
  }

  private static void SetElementVisible(GameObject target, bool isVisible)
  {
    if (target != null && target.activeSelf != isVisible)
    {
      target.SetActive(isVisible);
    }
  }

  private static GameObject ResolveSharedParent(params TMP_Text[] textElements)
  {
    Transform candidateParent = null;

    for (var i = 0; i < textElements.Length; i++)
    {
      var textElement = textElements[i];
      if (textElement == null)
      {
        continue;
      }

      var parent = textElement.transform.parent;
      if (parent == null)
      {
        return null;
      }

      if (candidateParent == null)
      {
        candidateParent = parent;
        continue;
      }

      if (candidateParent != parent)
      {
        return null;
      }
    }

    return candidateParent != null ? candidateParent.gameObject : null;
  }
}
