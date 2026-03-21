using UnityEngine;

[DisallowMultipleComponent]
public sealed class Wall2DPlayerRespawner : MonoBehaviour
{
  [SerializeField] private SpaceViewModeSwitcher viewModeSwitcher;
  [SerializeField] private GameObject wall2DPlayerPrefab;
  [SerializeField] private Transform spawnPoint;
  [SerializeField] private Transform spawnedPlayerParent;
  [SerializeField] private bool respawnFromLastKnownPosition = true;
  [SerializeField] private bool respawnAtInitialPointWhenFallen = true;
  [SerializeField] private float fallRespawnYThreshold = -10f;

  private GameObject _spawnedPlayerInstance;
  private Wall2DPlayerController _spawnedPlayerController;
  private bool _hasSavedSpawnState;
  private Vector3 _savedSpawnPosition;
  private Quaternion _savedSpawnRotation;

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
      // 表示モード変更を監視し、2Dモード時だけプレイヤーを維持する。
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

  private void Update()
  {
    // 2Dモード中かつ有効時のみ、落下による初期位置リスポーンを監視する。
    if (!respawnAtInitialPointWhenFallen
        || _spawnedPlayerInstance == null
        || viewModeSwitcher == null
        || viewModeSwitcher.CurrentMode != SpaceViewMode.Wall2D)
    {
      return;
    }

    if (_spawnedPlayerInstance.transform.position.y < fallRespawnYThreshold)
    {
      RespawnPlayerAtInitialPoint();
    }
  }

  private void OnModeChanged(SpaceViewMode mode)
  {
    // 2Dモードへ入ったら生成し、それ以外へ出たら破棄する。
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

    // 有効化時に現在モードへ同期して、生成状態のズレを防ぐ。
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
    // 設定に応じて、前回位置または初期スポーン地点へ生成する。
    SpawnPlayer(useSavedPosition: respawnFromLastKnownPosition && _hasSavedSpawnState, saveCurrentTransformBeforeDestroy: true);
  }

  [ContextMenu("Respawn 2D Player At Initial Point")]
  public void RespawnPlayerAtInitialPoint()
  {
    // 保存済み位置を無効化して、必ず初期地点から再生成する。
    ClearSavedRespawnPoint();
    SpawnPlayer(useSavedPosition: false, saveCurrentTransformBeforeDestroy: false);
  }

  private void SpawnPlayer(bool useSavedPosition, bool saveCurrentTransformBeforeDestroy)
  {
    // 先に既存インスタンスを片付けてから新規生成する。
    DestroySpawnedPlayerInternal(saveCurrentTransform: saveCurrentTransformBeforeDestroy);

    if (wall2DPlayerPrefab == null)
    {
      Debug.LogWarning("Wall2DPlayerRespawner requires wall2DPlayerPrefab to be assigned.");
      return;
    }

    var spawnTransform = spawnPoint != null ? spawnPoint : transform;
    var spawnPosition = useSavedPosition
      ? _savedSpawnPosition
      : spawnTransform.position;
    var spawnRotation = useSavedPosition
      ? _savedSpawnRotation
      : spawnTransform.rotation;

    // 親Transformは任意。未設定ならルート階層に生成される。
    _spawnedPlayerInstance = Instantiate(
      wall2DPlayerPrefab,
      spawnPosition,
      spawnRotation,
      spawnedPlayerParent);
    SubscribeToSpawnedPlayerController(_spawnedPlayerInstance);
  }

  [ContextMenu("Destroy Spawned 2D Player")]
  public void DestroySpawnedPlayer()
  {
    DestroySpawnedPlayerInternal(saveCurrentTransform: true);
  }

  [ContextMenu("Clear Saved Respawn Point")]
  public void ClearSavedRespawnPoint()
  {
    _hasSavedSpawnState = false;
  }

  private void AutoAssignMissingReferences()
  {
    if (viewModeSwitcher == null)
    {
      // インスペクタ未設定時の補助。シーン内の先頭インスタンスを探す。
      viewModeSwitcher = FindFirstObjectByType<SpaceViewModeSwitcher>();
    }
  }

  private void SaveCurrentPlayerTransform()
  {
    if (_spawnedPlayerInstance == null)
    {
      return;
    }

    // 次回リスポーン用に位置・回転を保存する。
    _savedSpawnPosition = _spawnedPlayerInstance.transform.position;
    _savedSpawnRotation = _spawnedPlayerInstance.transform.rotation;
    _hasSavedSpawnState = true;
  }

  private void DestroySpawnedPlayerInternal(bool saveCurrentTransform)
  {
    if (_spawnedPlayerInstance == null)
    {
      return;
    }

    if (saveCurrentTransform)
    {
      // 必要時だけ現在位置を保存してから破棄する。
      SaveCurrentPlayerTransform();
    }

    UnsubscribeFromSpawnedPlayerController();
    Destroy(_spawnedPlayerInstance);
    _spawnedPlayerInstance = null;
  }

  private void SubscribeToSpawnedPlayerController(GameObject spawnedPlayer)
  {
    // 多重購読防止のため、毎回先に既存購読を解除する。
    UnsubscribeFromSpawnedPlayerController();

    if (spawnedPlayer == null)
    {
      return;
    }

    _spawnedPlayerController = spawnedPlayer.GetComponent<Wall2DPlayerController>();
    if (_spawnedPlayerController != null)
    {
      _spawnedPlayerController.Crushed += OnSpawnedPlayerCrushed;
    }
  }

  private void UnsubscribeFromSpawnedPlayerController()
  {
    if (_spawnedPlayerController == null)
    {
      return;
    }

    _spawnedPlayerController.Crushed -= OnSpawnedPlayerCrushed;
    _spawnedPlayerController = null;
  }

  private void OnSpawnedPlayerCrushed()
  {
    // 押しつぶし時は安全のため初期地点リスポーンを選ぶ。
    RespawnPlayerAtInitialPoint();
  }
}
