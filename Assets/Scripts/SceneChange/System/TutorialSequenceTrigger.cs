using UnityEngine;

[DisallowMultipleComponent]
public sealed class TutorialSequenceTrigger : MonoBehaviour
{
  public enum TriggerMode
  {
    Enter2D,
    Enter3D,
    ManualOnly,
  }

  [SerializeField] private TutorialSequenceController sequenceController;
  [SerializeField] private string triggerId;
  [SerializeField] private TriggerMode triggerMode = TriggerMode.Enter2D;
  [SerializeField] private string requiredTag = "Player";
  [SerializeField] private bool fireOnce = true;

  private bool _hasFired;

  private void Reset()
  {
    if (sequenceController == null)
    {
      sequenceController = FindFirstObjectByType<TutorialSequenceController>(FindObjectsInactive.Include);
    }
  }

  public void Fire()
  {
    if (fireOnce && _hasFired)
    {
      return;
    }

    if (sequenceController == null)
    {
      sequenceController = FindFirstObjectByType<TutorialSequenceController>(FindObjectsInactive.Include);
    }

    if (sequenceController == null || string.IsNullOrEmpty(triggerId))
    {
      return;
    }

    _hasFired = true;
    sequenceController.NotifyTriggerReached(triggerId);
  }

  public void ResetTrigger()
  {
    _hasFired = false;
  }

  private void OnTriggerEnter(Collider other)
  {
    if (triggerMode != TriggerMode.Enter3D || !MatchesRequiredTag(other.gameObject))
    {
      return;
    }

    Fire();
  }

  private void OnTriggerEnter2D(Collider2D other)
  {
    if (triggerMode != TriggerMode.Enter2D || !MatchesRequiredTag(other.gameObject))
    {
      return;
    }

    Fire();
  }

  private bool MatchesRequiredTag(GameObject otherObject)
  {
    return !string.IsNullOrEmpty(otherObject.tag)
      && (string.IsNullOrEmpty(requiredTag) || otherObject.CompareTag(requiredTag));
  }
}
