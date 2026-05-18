using System;
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
  private string _cachedRequiredLayerName;
  private int _cachedRequiredLayer = -1;

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

    if (sequenceController == null
        || string.IsNullOrEmpty(triggerId)
        || !sequenceController.IsWaitingForTrigger
        || !string.Equals(sequenceController.ExpectedTriggerId, triggerId, StringComparison.OrdinalIgnoreCase))
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
    if (triggerMode != TriggerMode.Enter3D || !MatchesRequiredIdentifier(other))
    {
      return;
    }

    Fire();
  }

  private void OnTriggerStay(Collider other)
  {
    if (triggerMode != TriggerMode.Enter3D || !MatchesRequiredIdentifier(other))
    {
      return;
    }

    Fire();
  }

  private void OnTriggerEnter2D(Collider2D other)
  {
    if (triggerMode != TriggerMode.Enter2D || !MatchesRequiredIdentifier(other))
    {
      return;
    }

    Fire();
  }

  private void OnTriggerStay2D(Collider2D other)
  {
    if (triggerMode != TriggerMode.Enter2D || !MatchesRequiredIdentifier(other))
    {
      return;
    }

    Fire();
  }

  private bool MatchesRequiredIdentifier(Collider other)
  {
    if (other == null)
    {
      return false;
    }

    if (MatchesRequiredIdentifier(other.gameObject))
    {
      return true;
    }

    if (other.attachedRigidbody != null && MatchesRequiredIdentifier(other.attachedRigidbody.gameObject))
    {
      return true;
    }

    return MatchesRequiredIdentifierInParents(other.transform);
  }

  private bool MatchesRequiredIdentifier(Collider2D other)
  {
    if (other == null)
    {
      return false;
    }

    if (MatchesRequiredIdentifier(other.gameObject))
    {
      return true;
    }

    if (other.attachedRigidbody != null && MatchesRequiredIdentifier(other.attachedRigidbody.gameObject))
    {
      return true;
    }

    return MatchesRequiredIdentifierInParents(other.transform);
  }

  private bool MatchesRequiredIdentifierInParents(Transform target)
  {
    var current = target != null ? target.parent : null;
    while (current != null)
    {
      if (MatchesRequiredIdentifier(current.gameObject))
      {
        return true;
      }

      current = current.parent;
    }

    return false;
  }

  private bool MatchesRequiredIdentifier(GameObject otherObject)
  {
    if (otherObject == null)
    {
      return false;
    }

    if (string.IsNullOrEmpty(requiredTag))
    {
      return true;
    }

    return string.Equals(otherObject.tag, requiredTag, StringComparison.Ordinal)
      || otherObject.layer == ResolveRequiredLayer();
  }

  private int ResolveRequiredLayer()
  {
    if (!string.Equals(_cachedRequiredLayerName, requiredTag, StringComparison.Ordinal))
    {
      _cachedRequiredLayerName = requiredTag;
      _cachedRequiredLayer = string.IsNullOrEmpty(requiredTag) ? -1 : LayerMask.NameToLayer(requiredTag);
    }

    return _cachedRequiredLayer;
  }
}
