using UnityEngine;

[DisallowMultipleComponent]
public sealed class Wall2DPlayerRespawner : MonoBehaviour
{
  [SerializeField] private SpaceViewModeSwitcher viewModeSwitcher;
  [SerializeField] private GameObject wall2DPlayerPrefab;
  [SerializeField] private Transform spawnPoint;
  [SerializeField] private Transform spawnedPlayerParent;

  private GameObject _spawnedPlayerInstance;

  private void Reset()
  {
    AutoAssignMissingReferences();
  }

  private void Awake()
  {
    AutoAssignMissingReferences();
  }

  private void OnEnable()
  {
    AutoAssignMissingReferences();

    if (viewModeSwitcher != null)
    {
      viewModeSwitcher.ModeChanged += OnModeChanged;
      SyncToCurrentMode();
    }
  }

  private void OnDisable()
  {
    if (viewModeSwitcher != null)
    {
      viewModeSwitcher.ModeChanged -= OnModeChanged;
    }
  }

  private void OnModeChanged(SpaceViewMode mode)
  {
    if (mode == SpaceViewMode.Wall2D)
    {
      RespawnPlayer();
    }
    else
    {
      DestroySpawnedPlayer();
    }
  }

  private void SyncToCurrentMode()
  {
    if (viewModeSwitcher == null)
    {
      return;
    }

    if (viewModeSwitcher.CurrentMode == SpaceViewMode.Wall2D)
    {
      RespawnPlayer();
    }
    else
    {
      DestroySpawnedPlayer();
    }
  }

  [ContextMenu("Respawn 2D Player")]
  public void RespawnPlayer()
  {
    DestroySpawnedPlayer();

    if (wall2DPlayerPrefab == null)
    {
      Debug.LogWarning("Wall2DPlayerRespawner requires wall2DPlayerPrefab to be assigned.");
      return;
    }

    var spawnTransform = spawnPoint != null ? spawnPoint : transform;
    _spawnedPlayerInstance = Instantiate(
      wall2DPlayerPrefab,
      spawnTransform.position,
      spawnTransform.rotation,
      spawnedPlayerParent);
  }

  [ContextMenu("Destroy Spawned 2D Player")]
  public void DestroySpawnedPlayer()
  {
    if (_spawnedPlayerInstance == null)
    {
      return;
    }

    Destroy(_spawnedPlayerInstance);
    _spawnedPlayerInstance = null;
  }

  private void AutoAssignMissingReferences()
  {
    if (viewModeSwitcher == null)
    {
      viewModeSwitcher = FindFirstObjectByType<SpaceViewModeSwitcher>();
    }
  }
}
