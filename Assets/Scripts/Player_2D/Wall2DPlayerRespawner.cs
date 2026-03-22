using UnityEngine;

/// <summary>
/// 2Dプレイヤーの生成と再利用を管理するスクリプト。
/// シーンに配置済みのプレイヤーを優先して使い、進行位置の保存、
/// 落下や圧死時の初期位置復帰、削除後の spawnPoint 再生成、
/// そして 3D モード中に見た目だけ残して 2D 操作を止める処理を担当する。
/// </summary>
[DisallowMultipleComponent]
public sealed class Wall2DPlayerRespawner : MonoBehaviour, IRuntimeResettable
{
  [SerializeField] private SpaceViewModeSwitcher viewModeSwitcher;
  [SerializeField] private GameObject scenePlacedPlayerInstance;
  [SerializeField] private GameObject wall2DPlayerPrefab;
  [SerializeField] private Transform spawnPoint;
  [SerializeField] private Transform spawnedPlayerParent;
  [SerializeField] private bool respawnFromLastKnownPosition = true;
  [SerializeField] private bool respawnAtInitialPointWhenFallen = true;
  [SerializeField] private float fallRespawnYThreshold = -10f;

  private GameObject _spawnedPlayerInstance;
  private Wall2DPlayerController _spawnedPlayerController;
  private Wall2DPlayerCrushDetector _spawnedPlayerCrushDetector;
  private Rigidbody2D _spawnedPlayerRigidbody2D;
  private bool _hasSavedSpawnState;
  private Vector3 _savedSpawnPosition;
  private Quaternion _savedSpawnRotation;
  private bool _hasInitialPlacedPlayerState;
  private Vector3 _initialPlacedPlayerPosition;
  private Quaternion _initialPlacedPlayerRotation;

  public int ResetOrder => 0;

  private void Reset()
  {
    AutoAssignMissingReferences();
  }

  private void Awake()
  {
    AutoAssignMissingReferences();
    RegisterScenePlacedPlayerIfAvailable();
  }

  private void OnEnable()
  {
    AutoAssignMissingReferences();

    if (viewModeSwitcher != null)
    {
      // 視点変更イベントを購読して、2Dプレイヤーの状態を同期する。
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
    // 落下判定は 2D モード中だけ行う。
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
    if (mode == SpaceViewMode.Wall2D)
    {
      Enter2DMode();
    }
    else
    {
      Leave2DMode();
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
      Enter2DMode();
    }
    else
    {
      Leave2DMode();
    }
  }

  [ContextMenu("Respawn 2D Player")]
  public void RespawnPlayer()
  {
    // 通常の再スポーンでは、必要に応じて保存済み位置から再開する。
    SpawnPlayer(useSavedPosition: respawnFromLastKnownPosition && _hasSavedSpawnState, saveCurrentTransformBeforeReset: true);
  }

  [ContextMenu("Respawn 2D Player At Initial Point")]
  public void RespawnPlayerAtInitialPoint()
  {
    // 既存プレイヤーがいる間は、シーン配置された初期位置へ戻す。
    // プレイヤーが削除されている時だけ spawnPoint から再生成する。
    ClearSavedRespawnPoint();

    if (_spawnedPlayerInstance != null)
    {
      RepositionExistingPlayer(PlayerPlacementTarget.InitialScenePlacement);
      SetSpawnedPlayer2DGameplayEnabled(true);
      SetSpawnedPlayerVisible(true);
      return;
    }

    SpawnNewPlayerAtRespawnPoint();
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

  private void Enter2DMode()
  {
    if (_spawnedPlayerInstance != null)
    {
      // 既存インスタンスがあれば再利用し、2D操作だけ再開する。
      SetSpawnedPlayer2DGameplayEnabled(true);
      SetSpawnedPlayerVisible(true);
      return;
    }

    RespawnPlayer();
  }

  private void Leave2DMode()
  {
    if (_spawnedPlayerInstance == null)
    {
      return;
    }

    SaveCurrentPlayerTransform();

    // 3D中は見た目だけ残し、入力・圧死判定・2D物理だけ止める。
    SetSpawnedPlayer2DGameplayEnabled(false);
    SetSpawnedPlayerVisible(true);
  }

  private void SpawnPlayer(bool useSavedPosition, bool saveCurrentTransformBeforeReset)
  {
    if (_spawnedPlayerInstance != null)
    {
      if (saveCurrentTransformBeforeReset)
      {
        SaveCurrentPlayerTransform();
      }

      RepositionExistingPlayer(useSavedPosition ? PlayerPlacementTarget.SavedPosition : PlayerPlacementTarget.InitialScenePlacement);
      SetSpawnedPlayer2DGameplayEnabled(true);
      SetSpawnedPlayerVisible(true);
      return;
    }

    SpawnNewPlayer(useSavedPosition);
  }

  private void SpawnNewPlayer(bool useSavedPosition)
  {
    if (wall2DPlayerPrefab == null)
    {
      Debug.LogWarning("Wall2DPlayerRespawner requires wall2DPlayerPrefab to be assigned.");
      return;
    }

    var placementTarget = useSavedPosition ? PlayerPlacementTarget.SavedPosition : PlayerPlacementTarget.RespawnPoint;
    var spawnPosition = GetPlacementPosition(placementTarget);
    var spawnRotation = GetPlacementRotation(placementTarget);

    _spawnedPlayerInstance = Instantiate(
      wall2DPlayerPrefab,
      spawnPosition,
      spawnRotation,
      spawnedPlayerParent);

    CacheSpawnedPlayerComponents(_spawnedPlayerInstance);
    SubscribeToSpawnedPlayerCrushDetector();
    SetSpawnedPlayer2DGameplayEnabled(true);
    SetSpawnedPlayerVisible(true);
  }

  private void SpawnNewPlayerAtRespawnPoint()
  {
    SpawnNewPlayer(useSavedPosition: false);
  }

  private void RepositionExistingPlayer(PlayerPlacementTarget placementTarget)
  {
    if (_spawnedPlayerInstance == null)
    {
      return;
    }

    var targetPosition = GetPlacementPosition(placementTarget);
    var targetRotation = GetPlacementRotation(placementTarget);

    _spawnedPlayerInstance.transform.SetPositionAndRotation(targetPosition, targetRotation);

    // 再配置時は古い速度を残さないようにする。
    if (_spawnedPlayerRigidbody2D != null)
    {
      _spawnedPlayerRigidbody2D.linearVelocity = Vector2.zero;
      _spawnedPlayerRigidbody2D.angularVelocity = 0f;
    }
  }

  private void SetSpawnedPlayerVisible(bool isVisible)
  {
    if (_spawnedPlayerInstance == null)
    {
      return;
    }

    _spawnedPlayerInstance.SetActive(isVisible);
  }

  private void SetSpawnedPlayer2DGameplayEnabled(bool isEnabled)
  {
    if (_spawnedPlayerInstance == null)
    {
      return;
    }

    if (_spawnedPlayerController != null)
    {
      _spawnedPlayerController.enabled = isEnabled;
    }

    if (_spawnedPlayerCrushDetector != null)
    {
      _spawnedPlayerCrushDetector.enabled = isEnabled;
    }

    if (_spawnedPlayerRigidbody2D != null)
    {
      _spawnedPlayerRigidbody2D.linearVelocity = Vector2.zero;
      _spawnedPlayerRigidbody2D.angularVelocity = 0f;
      _spawnedPlayerRigidbody2D.simulated = isEnabled;
    }
  }

  private void AutoAssignMissingReferences()
  {
    if (viewModeSwitcher == null)
    {
      // 未設定ならシーン内の SpaceViewModeSwitcher を自動で探す。
      viewModeSwitcher = FindFirstObjectByType<SpaceViewModeSwitcher>();
    }

    if (scenePlacedPlayerInstance == null)
    {
      var playerController = FindFirstObjectByType<Wall2DPlayerController>(FindObjectsInactive.Include);
      if (playerController != null)
      {
        scenePlacedPlayerInstance = playerController.gameObject;
      }
    }
  }

  private void RegisterScenePlacedPlayerIfAvailable()
  {
    if (scenePlacedPlayerInstance == null)
    {
      return;
    }

    _spawnedPlayerInstance = scenePlacedPlayerInstance;
    CacheInitialPlacedPlayerState(scenePlacedPlayerInstance);
    CacheSpawnedPlayerComponents(scenePlacedPlayerInstance);
    SubscribeToSpawnedPlayerCrushDetector();
  }

  private void CacheInitialPlacedPlayerState(GameObject placedPlayer)
  {
    if (placedPlayer == null)
    {
      return;
    }

    _initialPlacedPlayerPosition = placedPlayer.transform.position;
    _initialPlacedPlayerRotation = placedPlayer.transform.rotation;
    _hasInitialPlacedPlayerState = true;
  }

  private void SaveCurrentPlayerTransform()
  {
    if (_spawnedPlayerInstance == null)
    {
      return;
    }

    // 次回 2D モードへ戻る時のために現在位置を保存する。
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
      SaveCurrentPlayerTransform();
    }

    UnsubscribeFromSpawnedPlayerCrushDetector();
    ClearSpawnedPlayerComponentCache();
    Destroy(_spawnedPlayerInstance);
    _spawnedPlayerInstance = null;
  }

  private void CacheSpawnedPlayerComponents(GameObject spawnedPlayer)
  {
    if (spawnedPlayer == null)
    {
      return;
    }

    _spawnedPlayerController = spawnedPlayer.GetComponent<Wall2DPlayerController>();
    _spawnedPlayerCrushDetector = spawnedPlayer.GetComponent<Wall2DPlayerCrushDetector>();
    _spawnedPlayerRigidbody2D = spawnedPlayer.GetComponent<Rigidbody2D>();
  }

  private void SubscribeToSpawnedPlayerCrushDetector()
  {
    // 古い購読が残らないように、再購読前に必ず解除する。
    UnsubscribeFromSpawnedPlayerCrushDetector();

    if (_spawnedPlayerCrushDetector != null)
    {
      _spawnedPlayerCrushDetector.Crushed += OnSpawnedPlayerCrushed;
      return;
    }

    Debug.LogWarning("Spawned player does not have Wall2DPlayerCrushDetector. Crush respawn will not trigger.");
  }

  private void UnsubscribeFromSpawnedPlayerCrushDetector()
  {
    if (_spawnedPlayerCrushDetector == null)
    {
      return;
    }

    _spawnedPlayerCrushDetector.Crushed -= OnSpawnedPlayerCrushed;
    _spawnedPlayerCrushDetector = null;
  }

  private void ClearSpawnedPlayerComponentCache()
  {
    _spawnedPlayerController = null;
    _spawnedPlayerCrushDetector = null;
    _spawnedPlayerRigidbody2D = null;
  }

  private void OnSpawnedPlayerCrushed()
  {
    // 圧死時は必ず初期地点へ戻す。
    RespawnPlayerAtInitialPoint();
  }

  /// <summary>
  /// 2D プレイヤーの進行状況を初期化し、現在の視点モードに合わせて初期地点へ戻す。
  /// </summary>
  public void ResetRuntimeState()
  {
    ClearSavedRespawnPoint();

    if (_spawnedPlayerInstance != null)
    {
      RepositionExistingPlayer(PlayerPlacementTarget.InitialScenePlacement);
    }

    SyncToCurrentMode();
  }

  private Vector3 GetPlacementPosition(PlayerPlacementTarget placementTarget)
  {
    switch (placementTarget)
    {
      case PlayerPlacementTarget.SavedPosition:
        return _savedSpawnPosition;
      case PlayerPlacementTarget.InitialScenePlacement:
        if (_hasInitialPlacedPlayerState)
        {
          return _initialPlacedPlayerPosition;
        }
        break;
    }

    var respawnTransform = spawnPoint != null ? spawnPoint : transform;
    return respawnTransform.position;
  }

  private Quaternion GetPlacementRotation(PlayerPlacementTarget placementTarget)
  {
    switch (placementTarget)
    {
      case PlayerPlacementTarget.SavedPosition:
        return _savedSpawnRotation;
      case PlayerPlacementTarget.InitialScenePlacement:
        if (_hasInitialPlacedPlayerState)
        {
          return _initialPlacedPlayerRotation;
        }
        break;
    }

    var respawnTransform = spawnPoint != null ? spawnPoint : transform;
    return respawnTransform.rotation;
  }

  private enum PlayerPlacementTarget
  {
    SavedPosition,
    InitialScenePlacement,
    RespawnPoint,
  }
}
