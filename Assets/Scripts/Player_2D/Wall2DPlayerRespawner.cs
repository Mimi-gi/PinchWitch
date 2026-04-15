using System.Collections;
using UnityEngine;

/// <summary>
/// 2Dプレイヤーの生成と再利用を管理するスクリプト。
/// シーンに配置済みのプレイヤーを優先して使い、進行位置の保存、
/// 落下や圧死時の初期位置復帰、削除後の spawnPoint 再生成、
/// そして常時有効な 2D プレイヤーの復帰処理を担当する。
/// </summary>
[DisallowMultipleComponent]
public sealed class Wall2DPlayerRespawner : MonoBehaviour, IRuntimeResettable
{
  [SerializeField] private GameObject scenePlacedPlayerInstance;
  [SerializeField] private GameObject wall2DPlayerPrefab;
  [SerializeField] private Transform spawnPoint;
  [SerializeField] private Transform spawnedPlayerParent;
  [SerializeField] private bool respawnFromLastKnownPosition = true;
  [SerializeField] private bool respawnAtInitialPointWhenFallen = true;
  [SerializeField] private float fallRespawnYThreshold = -10f;
  [SerializeField] private LayerMask overlapRecoveryLayers = ~0;
  [SerializeField] private float respawnCooldownSeconds = 0.5f;

  private GameObject _spawnedPlayerInstance;
  private Wall2DPlayerController _spawnedPlayerController;
  private Wall2DPlayerCrushDetector _spawnedPlayerCrushDetector;
  private Rigidbody2D _spawnedPlayerRigidbody2D;
  private Collider2D _spawnedPlayerCollider2D;
  private bool _hasSavedSpawnState;
  private Vector3 _savedSpawnPosition;
  private Quaternion _savedSpawnRotation;
  private bool _hasInitialPlacedPlayerState;
  private Vector3 _initialPlacedPlayerPosition;
  private Quaternion _initialPlacedPlayerRotation;
  private readonly Collider2D[] _overlapResults = new Collider2D[8];
  private Coroutine _overlapRecoveryCoroutine;
  private bool _isRespawning;
  private float _respawnBlockedUntilTime;

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
    EnsureSpawnedPlayerExists();
  }

  private void OnDisable()
  {
    StopOverlapRecoveryCoroutine();
  }

  private void Update()
  {
    if (!respawnAtInitialPointWhenFallen
        || _spawnedPlayerInstance == null)
    {
      return;
    }

    if (_spawnedPlayerInstance.transform.position.y < fallRespawnYThreshold)
    {
      RespawnPlayerAtInitialPoint();
    }
  }

  [ContextMenu("Respawn 2D Player")]
  public void RespawnPlayer()
  {
    if (!CanRespawnNow())
    {
      return;
    }

    BeginRespawnGuard();

    try
    {
      // 通常の再スポーンでは、必要に応じて保存済み位置から再開する。
      SpawnPlayer(useSavedPosition: respawnFromLastKnownPosition && _hasSavedSpawnState, saveCurrentTransformBeforeReset: true);
      ScheduleOverlapRecovery();
    }
    finally
    {
      EndRespawnGuard();
    }
  }

  [ContextMenu("Respawn 2D Player At Initial Point")]
  public void RespawnPlayerAtInitialPoint()
  {
    if (!CanRespawnNow())
    {
      return;
    }

    BeginRespawnGuard();

    try
    {
      // 既存プレイヤーがいる間は、シーン配置された初期位置へ戻す。
      // プレイヤーが削除されている時だけ spawnPoint から再生成する。
      ClearSavedRespawnPoint();

      if (_spawnedPlayerInstance != null)
      {
        RepositionExistingPlayer(PlayerPlacementTarget.InitialScenePlacement);
        SetSpawnedPlayer2DGameplayEnabled(true);
        SetSpawnedPlayerVisible(true);
        ScheduleOverlapRecovery();
        return;
      }

      SpawnNewPlayerAtRespawnPoint();
      ScheduleOverlapRecovery();
    }
    finally
    {
      EndRespawnGuard();
    }
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
      ScheduleOverlapRecovery();
      return;
    }

    SpawnNewPlayer(useSavedPosition);
    ScheduleOverlapRecovery();
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
    SubscribeToSpawnedPlayerEvents();
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
      _spawnedPlayerRigidbody2D.position = targetPosition;
      _spawnedPlayerRigidbody2D.rotation = targetRotation.eulerAngles.z;
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

  private void SetSpawnedPlayer2DGameplayEnabled(
    bool isEnabled,
    bool keepPhysicsSimulationWhenDisabled = false,
    bool preserveCurrentMotion = false)
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
      if (!preserveCurrentMotion)
      {
        _spawnedPlayerRigidbody2D.linearVelocity = Vector2.zero;
        _spawnedPlayerRigidbody2D.angularVelocity = 0f;
      }

      _spawnedPlayerRigidbody2D.simulated = isEnabled || keepPhysicsSimulationWhenDisabled;
    }
  }

  private void AutoAssignMissingReferences()
  {
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
    SubscribeToSpawnedPlayerEvents();
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

    UnsubscribeFromSpawnedPlayerEvents();
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
    _spawnedPlayerCollider2D = spawnedPlayer.GetComponent<Collider2D>();
  }

  private void SubscribeToSpawnedPlayerEvents()
  {
    // 古い購読が残らないように、再購読前に必ず解除する。
    UnsubscribeFromSpawnedPlayerEvents();

    if (_spawnedPlayerController != null)
    {
      _spawnedPlayerController.MovementStalled += OnSpawnedPlayerMovementStalled;
    }

    if (_spawnedPlayerCrushDetector != null)
    {
      _spawnedPlayerCrushDetector.Crushed += OnSpawnedPlayerCrushed;
    }
    else
    {
      Debug.LogWarning("Spawned player does not have Wall2DPlayerCrushDetector. Crush respawn will not trigger.");
    }
  }

  private void UnsubscribeFromSpawnedPlayerEvents()
  {
    if (_spawnedPlayerController != null)
    {
      _spawnedPlayerController.MovementStalled -= OnSpawnedPlayerMovementStalled;
    }

    if (_spawnedPlayerCrushDetector != null)
    {
      _spawnedPlayerCrushDetector.Crushed -= OnSpawnedPlayerCrushed;
    }
  }

  private void ClearSpawnedPlayerComponentCache()
  {
    _spawnedPlayerController = null;
    _spawnedPlayerCrushDetector = null;
    _spawnedPlayerRigidbody2D = null;
    _spawnedPlayerCollider2D = null;
  }

  private void OnSpawnedPlayerCrushed()
  {
    // 圧死時は必ず初期地点へ戻す。
    RespawnPlayerAtInitialPoint();
  }

  private void OnSpawnedPlayerMovementStalled()
  {
    RespawnPlayerAtInitialPoint();
  }

  private void ResolveOverlapIfNeeded()
  {
    Physics2D.SyncTransforms();

    if (!IsSpawnedPlayerOverlappingSolid())
    {
      return;
    }

    RepositionExistingPlayer(PlayerPlacementTarget.InitialScenePlacement);
    SetSpawnedPlayer2DGameplayEnabled(true);
    SetSpawnedPlayerVisible(true);

    if (IsSpawnedPlayerOverlappingSolid())
    {
      Debug.LogWarning("Wall2D player is still overlapping a blocking collider after overlap recovery.");
    }
  }

  private void ScheduleOverlapRecovery()
  {
    StopOverlapRecoveryCoroutine();
    _overlapRecoveryCoroutine = StartCoroutine(RecoverOverlapAfter2DEnter());
  }

  private IEnumerator RecoverOverlapAfter2DEnter()
  {
    yield return null;
    ResolveOverlapIfNeeded();
    yield return new WaitForFixedUpdate();
    ResolveOverlapIfNeeded();
    _overlapRecoveryCoroutine = null;
  }

  private void StopOverlapRecoveryCoroutine()
  {
    if (_overlapRecoveryCoroutine == null)
    {
      return;
    }

    StopCoroutine(_overlapRecoveryCoroutine);
    _overlapRecoveryCoroutine = null;
  }

  private bool IsSpawnedPlayerOverlappingSolid()
  {
    if (_spawnedPlayerCollider2D == null)
    {
      return false;
    }

    var filter = new ContactFilter2D
    {
      useLayerMask = true,
      layerMask = overlapRecoveryLayers,
      useTriggers = false,
    };

    var overlapCount = _spawnedPlayerCollider2D.Overlap(filter, _overlapResults);
    for (var i = 0; i < overlapCount; i++)
    {
      var overlapCollider = _overlapResults[i];
      if (overlapCollider == null)
      {
        continue;
      }

      if (overlapCollider == _spawnedPlayerCollider2D || overlapCollider.transform.IsChildOf(_spawnedPlayerInstance.transform))
      {
        continue;
      }

      return true;
    }

    return false;
  }

  /// <summary>
  /// 2D プレイヤーの進行状況を初期化し、現在の視点モードに合わせて初期地点へ戻す。
  /// </summary>
  public void ResetRuntimeState()
  {
    ClearSavedRespawnPoint();
    _isRespawning = false;
    _respawnBlockedUntilTime = 0f;

    if (_spawnedPlayerInstance != null)
    {
      RepositionExistingPlayer(PlayerPlacementTarget.InitialScenePlacement);
    }

    EnsureSpawnedPlayerExists();
  }

  private void EnsureSpawnedPlayerExists()
  {
    if (_spawnedPlayerInstance != null)
    {
      SetSpawnedPlayer2DGameplayEnabled(true);
      SetSpawnedPlayerVisible(true);
      ScheduleOverlapRecovery();
      return;
    }

    RespawnPlayer();
  }

  private bool CanRespawnNow()
  {
    return !_isRespawning && Time.time >= _respawnBlockedUntilTime;
  }

  private void BeginRespawnGuard()
  {
    _isRespawning = true;
  }

  private void EndRespawnGuard()
  {
    _isRespawning = false;
    _respawnBlockedUntilTime = Time.time + Mathf.Max(0f, respawnCooldownSeconds);
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
