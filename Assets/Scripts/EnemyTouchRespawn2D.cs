using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public sealed class EnemyTouchRespawn2D : MonoBehaviour
{
  [SerializeField] private Wall2DPlayerRespawner playerRespawner;
  [SerializeField] private string playerTag = "Player";
  [SerializeField] private float retriggerCooldownSeconds = 0.1f;

  private float _nextAllowedTriggerTime;

  private void Reset()
  {
    AutoAssignMissingReferences();
    ClampSerializedValues();
  }

  private void Awake()
  {
    AutoAssignMissingReferences();
    ClampSerializedValues();
  }

  private void OnValidate()
  {
    ClampSerializedValues();
  }

  private void OnTriggerEnter2D(Collider2D other)
  {
    TryRespawnPlayer(other);
  }

  private void OnCollisionEnter2D(Collision2D collision)
  {
    TryRespawnPlayer(collision.collider);
  }

  private void TryRespawnPlayer(Collider2D other)
  {
    if (other == null
        || !other.CompareTag(playerTag)
        || Time.time < _nextAllowedTriggerTime)
    {
      return;
    }

    AutoAssignMissingReferences();
    if (playerRespawner == null)
    {
      Debug.LogWarning("EnemyTouchRespawn2D could not find Wall2DPlayerRespawner.", this);
      return;
    }

    _nextAllowedTriggerTime = Time.time + retriggerCooldownSeconds;
    playerRespawner.TriggerGameOverFromEnemyContact();
  }

  private void AutoAssignMissingReferences()
  {
    if (playerRespawner == null)
    {
      playerRespawner = FindFirstObjectByType<Wall2DPlayerRespawner>(FindObjectsInactive.Include);
    }
  }

  private void ClampSerializedValues()
  {
    retriggerCooldownSeconds = Mathf.Max(0f, retriggerCooldownSeconds);
  }
}
