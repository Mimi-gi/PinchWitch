using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 2D プレイヤーの生成と復帰を管理する。
/// シーン配置済みのプレイヤーを優先して使い、保存位置・初期位置・spawnPoint の3段階で復帰する。
/// 落下、圧死、詰まり停止のどれからでも同じ復帰経路に入るようにまとめている。
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
  [Header("Initial Gameplay")]
  [SerializeField] private bool initialGameplayEnabled;
  [Header("Start/Stop Input")]
  [SerializeField] private bool enableStartStopInput;
  [SerializeField] private KeyCode startStopGameplayKey = KeyCode.Space;
  [SerializeField] private bool playGoodReactionOnStart = true;
  [Header("Game Over")]
  [SerializeField] private bool reloadSceneOnGameOver;
  [SerializeField] private bool reloadSceneOnShadowCrush = false;
  [SerializeField] private float gameOverReloadDelay = 0.15f;

  private GameObject _spawnedPlayerInstance;
  private Wall2DPlayerController _spawnedPlayerController;
  private Wall2DPlayerCrushDetector _spawnedPlayerCrushDetector;
  private Wall2DPlayerAnimator _spawnedPlayerAnimator;
  private Rigidbody2D _spawnedPlayerRigidbody2D;
  private Collider2D _spawnedPlayerCollider2D;
  private bool _hasSavedSpawnState;
  private Vector3 _savedSpawnPosition;
  private Quaternion _savedSpawnRotation;
  private bool _hasInitialPlacedPlayerState;
  private Vector3 _initialPlacedPlayerPosition;
  private Quaternion _initialPlacedPlayerRotation;
  private readonly Collider2D[] _overlapResults = new Collider2D[8];
  private CancellationTokenSource _overlapRecoveryCts;
  private bool _isRespawning;
  private float _respawnBlockedUntilTime;
  private bool _isPlayerDead;
  private bool _isShadowCrushPaused;
  private bool _hasRequestedGameOverReload;
  private RigidbodyConstraints2D _savedConstraintsBeforeShadowCrushPause;
  private bool _hasSavedConstraintsBeforeShadowCrushPause;
  private bool _requestedPlayerGameplayEnabled;
  private bool _hasReceivedGameplayRequest;
  private bool _waitForStartStopInputRelease;
  private bool _isStartReactionPlaying;
  private float _startReactionEndTime;
  private bool _hasPlayedGameplayStartSE;

  public int ResetOrder => 0;
  public bool IsShadowCrushPaused => _isShadowCrushPaused;

  private enum GameOverCause
  {
    Fall,
    Crush,
    EnemyContact,
  }

  private void Reset()
  {
    AutoAssignMissingReferences();
    ClampSerializedValues();
  }

  private void Awake()
  {
    ClampSerializedValues();
    AutoAssignMissingReferences();
    InitializeRequestedGameplayStateIfNeeded();
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
    UpdateGameplayStartReactionPlayback();
    UpdateGameplayStartStopInput();

    // 落下死は常時監視する。2D/3D 切り替え前提のゲートは持たない。
    if (!respawnAtInitialPointWhenFallen
        || _spawnedPlayerInstance == null
        || _isPlayerDead)
    {
      return;
    }

    if (_spawnedPlayerInstance.transform.position.y < fallRespawnYThreshold)
    {
      KillSpawnedPlayerAtCurrentPosition(GameOverCause.Fall);
    }
  }

  [ContextMenu("Respawn 2D Player")]
  public void RespawnPlayer()
  {
    // 常に初期位置からリスポーンするように変更
    RespawnPlayerAtInitialPoint();
  }

  /// <summary>
  /// 外部（視点切り替え等）からプレイヤーの物理挙動をまとめて制御する。
  /// </summary>
  public void SetPlayerGameplayEnabled(bool isEnabled)
  {
    _hasReceivedGameplayRequest = true;
    _requestedPlayerGameplayEnabled = isEnabled;
    if (!isEnabled)
    {
      ResetGameplayStartReactionState();
    }

    ApplyInitialGameplayState();
  }

  private void OnValidate()
  {
    ClampSerializedValues();
  }

  [ContextMenu("Respawn 2D Player At Initial Point")]
  public void RespawnPlayerAtInitialPoint()
  {
    if (!CanRespawnNow())
    {
      return;
    }

    BeginRespawnGuard();
    _isPlayerDead = false;
    ClearShadowCrushPauseState();
    _hasRequestedGameOverReload = false;

    try
    {
      // 既存プレイヤーがいればその場で初期位置へ戻し、いなければ spawnPoint から作り直す。
      ClearSavedRespawnPoint();

      if (_spawnedPlayerInstance != null)
      {
        RepositionExistingPlayer(PlayerPlacementTarget.InitialScenePlacement);
        ApplyInitialGameplayState();
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

  [ContextMenu("Trigger Game Over From Enemy Contact")]
  public void TriggerGameOverFromEnemyContact()
  {
    KillSpawnedPlayerAtCurrentPosition(GameOverCause.EnemyContact);
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
        // いまの位置を保存しておけば、あとで自然な場所へ戻しやすい。
        SaveCurrentPlayerTransform();
      }

      RepositionExistingPlayer(useSavedPosition ? PlayerPlacementTarget.SavedPosition : PlayerPlacementTarget.InitialScenePlacement);
      ApplyInitialGameplayState();
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

    // 保存済み位置を使うか、初期配置に戻すか、完全な再生成先かをここで決める。
    var placementTarget = useSavedPosition ? PlayerPlacementTarget.SavedPosition : PlayerPlacementTarget.RespawnPoint;
    var spawnPosition = GetPlacementPosition(placementTarget);
    var spawnRotation = GetPlacementRotation(placementTarget);

    _spawnedPlayerInstance = Instantiate(
      wall2DPlayerPrefab,
      spawnPosition,
      spawnRotation,
      spawnedPlayerParent);

    CacheSpawnedPlayerComponents(_spawnedPlayerInstance);

    if (_spawnedPlayerController != null)
    {
      _spawnedPlayerController.ResetMovementDirection();
    }

    SubscribeToSpawnedPlayerEvents();
    ApplyInitialGameplayState();
    SetSpawnedPlayerVisible(true);
  }

  private void ApplyInitialGameplayState()
  {
    InitializeRequestedGameplayStateIfNeeded();

    if (_isPlayerDead)
    {
      SetSpawnedPlayer2DGameplayEnabled(false);
      return;
    }

    if (_isShadowCrushPaused)
    {
      PauseSpawnedPlayerForShadowCrush();
      return;
    }

    SetSpawnedPlayer2DGameplayEnabled(ShouldEnableSpawnedPlayerGameplay());
  }

  private void InitializeRequestedGameplayStateIfNeeded()
  {
    if (_hasReceivedGameplayRequest)
    {
      return;
    }

    // 生成・配置直後の物理状態を、現在の視点モード設定に合わせる。
    var switcher = FindFirstObjectByType<SpaceViewModeSwitcher>();
    _requestedPlayerGameplayEnabled = switcher != null
      ? switcher.ShouldEnablePlayerGameplay
      : initialGameplayEnabled;
  }

  private bool ShouldEnableSpawnedPlayerGameplay()
  {
    return _requestedPlayerGameplayEnabled && !_isPlayerDead && !_isStartReactionPlaying;
  }

  private void UpdateGameplayStartStopInput()
  {
    if (!enableStartStopInput || _isShadowCrushPaused || _isStartReactionPlaying)
    {
      return;
    }

    if (_waitForStartStopInputRelease)
    {
      _waitForStartStopInputRelease = Input.GetKey(startStopGameplayKey);
      return;
    }

    if (!Input.GetKeyDown(startStopGameplayKey))
    {
      return;
    }

    TogglePlayerGameplayFromStartStopInput();
    _waitForStartStopInputRelease = Input.GetKey(startStopGameplayKey);
  }

  private void TogglePlayerGameplayFromStartStopInput()
  {
    if (!_requestedPlayerGameplayEnabled || _spawnedPlayerController == null || !_spawnedPlayerController.isActiveAndEnabled)
    {
      StartPlayerGameplayFromInput();
      return;
    }

    if (_spawnedPlayerController.IsPausedByGoodGesture || _spawnedPlayerController.HasPendingGoodGesturePause)
    {
      _spawnedPlayerController.ResumeFromGoodGesturePause();
      return;
    }

    if (!_spawnedPlayerController.IsMoving && !_spawnedPlayerController.IsFalling && !_spawnedPlayerController.IsClimbing)
    {
      return;
    }

    _spawnedPlayerController.RequestGoodGesturePause();
  }

  private void StartPlayerGameplayFromInput()
  {
    _hasReceivedGameplayRequest = true;
    _requestedPlayerGameplayEnabled = true;

    if (playGoodReactionOnStart && BeginGameplayStartReactionIfPossible())
    {
      ApplyInitialGameplayState();
      return;
    }

    CompleteGameplayStartFromInput();
  }

  private bool BeginGameplayStartReactionIfPossible()
  {
    var playerAnimator = _spawnedPlayerInstance != null
      ? _spawnedPlayerInstance.GetComponent<Wall2DPlayerAnimator>()
      : FindFirstObjectByType<Wall2DPlayerAnimator>(FindObjectsInactive.Include);
    if (playerAnimator == null)
    {
      return false;
    }

    playerAnimator.PlayGoodReaction();
    if (playerAnimator.GoodReactionDuration <= 0f)
    {
      return false;
    }

    _isStartReactionPlaying = true;
    _startReactionEndTime = Time.unscaledTime + playerAnimator.GoodReactionDuration;
    return true;
  }

  private void UpdateGameplayStartReactionPlayback()
  {
    if (!_isStartReactionPlaying)
    {
      return;
    }

    if (Time.unscaledTime < _startReactionEndTime)
    {
      return;
    }

    CompleteGameplayStartFromInput();
  }

  private void CompleteGameplayStartFromInput()
  {
    _isStartReactionPlaying = false;
    _startReactionEndTime = 0f;
    PlayGameplayStartSE();
    ApplyInitialGameplayState();
  }

  private void PlayGameplayStartSE()
  {
    if (_hasPlayedGameplayStartSE || AudioManager.Instance == null)
    {
      return;
    }

    _hasPlayedGameplayStartSE = true;
    AudioManager.Instance.PlaySE(AudioManager.Instance.SE_Start);
  }

  private void ResetGameplayStartReactionState()
  {
    _isStartReactionPlaying = false;
    _startReactionEndTime = 0f;
    _hasPlayedGameplayStartSE = false;
  }

  private void ResetStartStopInputState()
  {
    _waitForStartStopInputRelease = false;
    ResetGameplayStartReactionState();
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

    if (_spawnedPlayerController != null)
    {
      _spawnedPlayerController.ResetMovementDirection();
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
    bool preserveCurrentMotion = false,
    bool keepCrushDetectorEnabledWhenDisabled = false)
  {
    if (_spawnedPlayerInstance == null)
    {
      return;
    }

    if (_spawnedPlayerController != null)
    {
      // 自動移動の有効/無効をここでまとめて切り替える。
      _spawnedPlayerController.enabled = isEnabled;
    }

    if (_spawnedPlayerCrushDetector != null)
    {
      // 圧死検知も同時に止めて、余計な再リスポーンを防ぐ。
      _spawnedPlayerCrushDetector.enabled = isEnabled || keepCrushDetectorEnabledWhenDisabled;
    }

    if (_spawnedPlayerRigidbody2D != null)
    {
      if (!preserveCurrentMotion)
      {
        // 再配置時は、古い慣性が残ると想定外のめり込みにつながるので消す。
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
      // シーン上に先に置かれているプレイヤーがいれば、それを初期実体として使う。
      var playerController = FindFirstObjectByType<Wall2DPlayerController>(FindObjectsInactive.Include);
      if (playerController != null)
      {
        scenePlacedPlayerInstance = playerController.gameObject;
      }
    }

  }

  private void ClampSerializedValues()
  {
    respawnCooldownSeconds = Mathf.Max(0f, respawnCooldownSeconds);
    gameOverReloadDelay = Mathf.Max(0f, gameOverReloadDelay);
  }

  private void RegisterScenePlacedPlayerIfAvailable()
  {
    if (scenePlacedPlayerInstance == null)
    {
      return;
    }

    // 既にシーンに置かれているプレイヤーも、生成済みプレイヤーとして同じ経路で管理する。
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
    _spawnedPlayerAnimator = spawnedPlayer.GetComponent<Wall2DPlayerAnimator>();
    _spawnedPlayerRigidbody2D = spawnedPlayer.GetComponent<Rigidbody2D>();
    _spawnedPlayerCollider2D = spawnedPlayer.GetComponent<Collider2D>();
  }

  private void SubscribeToSpawnedPlayerEvents()
  {
    // 以前の購読が残ると二重リスポーンの原因になるので、先に全部外してから付け直す。
    UnsubscribeFromSpawnedPlayerEvents();

    if (_spawnedPlayerController != null)
    {
      _spawnedPlayerController.MovementStalled += OnSpawnedPlayerMovementStalled;
    }

    if (_spawnedPlayerCrushDetector != null)
    {
      _spawnedPlayerCrushDetector.Crushed += OnSpawnedPlayerShadowCrushed;
      _spawnedPlayerCrushDetector.ReleasedFromCrush += OnSpawnedPlayerReleasedFromCrush;
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
      _spawnedPlayerCrushDetector.Crushed -= OnSpawnedPlayerShadowCrushed;
      _spawnedPlayerCrushDetector.ReleasedFromCrush -= OnSpawnedPlayerReleasedFromCrush;
    }
  }

  private void ClearSpawnedPlayerComponentCache()
  {
    _spawnedPlayerController = null;
    _spawnedPlayerCrushDetector = null;
    _spawnedPlayerAnimator = null;
    _spawnedPlayerRigidbody2D = null;
    _spawnedPlayerCollider2D = null;
  }

  private void OnSpawnedPlayerCrushed()
  {
    // 圧死したら、その場で止めてプレイヤーを残す。
    KillSpawnedPlayerAtCurrentPosition(GameOverCause.Crush);
  }

  private void OnSpawnedPlayerMovementStalled()
  {
    if (TryHandleShadowCrushBeforeStallRespawn())
    {
      return;
    }

    // 移動停止が続いた場合も、壁詰まりや引っ掛かりとみなして復帰する。
    RespawnPlayerAtInitialPoint();
  }

  private bool TryHandleShadowCrushBeforeStallRespawn()
  {
    if (_isShadowCrushPaused || _isPlayerDead)
    {
      return true;
    }

    if (_spawnedPlayerCrushDetector == null || !_spawnedPlayerCrushDetector.isActiveAndEnabled)
    {
      return false;
    }

    _spawnedPlayerCrushDetector.EvaluateCrushImmediately();
    return _isShadowCrushPaused || _isPlayerDead;
  }

  public bool IsShadowCrushPausedFor(Wall2DPlayerController playerController)
  {
    return _isShadowCrushPaused
           && playerController != null
           && _spawnedPlayerController == playerController;
  }

  private void KillSpawnedPlayerAtCurrentPosition(GameOverCause cause)
  {
    if (_spawnedPlayerInstance == null || _isPlayerDead)
    {
      return;
    }

    ClearShadowCrushPauseState();
    _isPlayerDead = true;
    StopOverlapRecoveryCoroutine();
    SetSpawnedPlayer2DGameplayEnabled(false, keepPhysicsSimulationWhenDisabled: false, preserveCurrentMotion: false);
    SetSpawnedPlayerVisible(true);
    RequestGameOverReloadIfNeeded(cause);
  }

  private void RequestGameOverReloadIfNeeded(GameOverCause cause)
  {
    if (!reloadSceneOnGameOver || _hasRequestedGameOverReload)
    {
      return;
    }

    if (cause == GameOverCause.Crush && !reloadSceneOnShadowCrush)
    {
      return;
    }

    _hasRequestedGameOverReload = true;
    Debug.Log($"[Wall2DPlayerRespawner] GameOver reload requested. cause={cause}, position={_spawnedPlayerInstance.transform.position}", this);
    GameSceneManager.ReloadActiveSceneWithFade(Mathf.Max(0f, gameOverReloadDelay));
  }

  private void ResolveOverlapIfNeeded()
  {
    // 生成直後や再配置直後に壁へめり込んでいないかを再チェックする。
    Physics2D.SyncTransforms();

    if (!IsSpawnedPlayerOverlappingSolid())
    {
      return;
    }

    RepositionExistingPlayer(PlayerPlacementTarget.InitialScenePlacement);
    ApplyInitialGameplayState();
    SetSpawnedPlayerVisible(true);

    if (IsSpawnedPlayerOverlappingSolid())
    {
      Debug.LogWarning("Wall2D player is still overlapping a blocking collider after overlap recovery.");
    }
  }

  private void ScheduleOverlapRecovery()
  {
    // 1 フレーム遅らせてから再判定し、物理更新が終わってから安全に押し戻す。
    StopOverlapRecoveryCoroutine();
    _overlapRecoveryCts = new CancellationTokenSource();
    RecoverOverlapAfter2DEnterAsync(_overlapRecoveryCts.Token).Forget();
  }

  private async UniTaskVoid RecoverOverlapAfter2DEnterAsync(CancellationToken ct)
  {
    await UniTask.Yield(ct);
    ResolveOverlapIfNeeded();
    await UniTask.WaitForFixedUpdate(ct);
    ResolveOverlapIfNeeded();
    _overlapRecoveryCts = null;
  }

  private void StopOverlapRecoveryCoroutine()
  {
    if (_overlapRecoveryCts == null)
    {
      return;
    }

    _overlapRecoveryCts.Cancel();
    _overlapRecoveryCts.Dispose();
    _overlapRecoveryCts = null;
  }

  private bool IsSpawnedPlayerOverlappingSolid()
  {
    if (_spawnedPlayerCollider2D == null)
    {
      return false;
    }

    // 自分自身や子オブジェクトは除外し、外部の固いコライダーだけを対象にする。
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

      if (Wall2DPlayerCrushDetector.IsShadowCollider(overlapCollider))
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
    // リセット時は、詰まり検知の状態もリスポーンの制御状態もすべて初期化する。
    ClearSavedRespawnPoint();
    _isRespawning = false;
    _respawnBlockedUntilTime = 0f;
    _isPlayerDead = false;
    ClearShadowCrushPauseState();
    _hasRequestedGameOverReload = false;

    if (enableStartStopInput)
    {
      _hasReceivedGameplayRequest = false;
      _requestedPlayerGameplayEnabled = initialGameplayEnabled;
      ResetStartStopInputState();
    }

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
      ApplyInitialGameplayState();
      SetSpawnedPlayerVisible(true);
      ScheduleOverlapRecovery();
      return;
    }

    RespawnPlayer();
  }

  private bool CanRespawnNow()
  {
    // リスポーン中の再入と、短時間に連打される復帰要求を両方防ぐ。
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
    // 位置は「保存済み」→「初期配置」→「spawnPoint」の順で選ぶ。
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
    // 回転も位置と同じ優先順位で決める。
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

  private void OnSpawnedPlayerShadowCrushed()
  {
    if (reloadSceneOnShadowCrush)
    {
      KillSpawnedPlayerAtCurrentPosition(GameOverCause.Crush);
      return;
    }

    PauseSpawnedPlayerForShadowCrush();
  }

  private void OnSpawnedPlayerReleasedFromCrush()
  {
    ResumeSpawnedPlayerFromShadowCrush();
  }

  private void PauseSpawnedPlayerForShadowCrush()
  {
    if (_spawnedPlayerInstance == null || _isPlayerDead)
    {
      return;
    }

    _isShadowCrushPaused = true;
    StopOverlapRecoveryCoroutine();
    CacheConstraintsBeforeShadowCrushPause();
    SetSpawnedPlayer2DGameplayEnabled(
      false,
      keepPhysicsSimulationWhenDisabled: true,
      preserveCurrentMotion: false,
      keepCrushDetectorEnabledWhenDisabled: true);

    if (_spawnedPlayerAnimator != null)
    {
      _spawnedPlayerAnimator.SetForcedFallState(true);
    }

    if (_spawnedPlayerRigidbody2D != null)
    {
      _spawnedPlayerRigidbody2D.constraints = RigidbodyConstraints2D.FreezeAll;
    }

    SetSpawnedPlayerVisible(true);
  }

  private void ResumeSpawnedPlayerFromShadowCrush()
  {
    if (_spawnedPlayerInstance == null || _isPlayerDead || !_isShadowCrushPaused)
    {
      return;
    }

    RestoreConstraintsAfterShadowCrushPause();
    _isShadowCrushPaused = false;

    SetSpawnedPlayer2DGameplayEnabled(
      ShouldEnableSpawnedPlayerGameplay(),
      keepPhysicsSimulationWhenDisabled: false,
      preserveCurrentMotion: false);

    if (_spawnedPlayerAnimator != null)
    {
      _spawnedPlayerAnimator.SetForcedFallState(false);
    }

    if (_spawnedPlayerController != null)
    {
      _spawnedPlayerController.ResetMovementDirection();
    }

    if (_spawnedPlayerRigidbody2D != null)
    {
      _spawnedPlayerRigidbody2D.WakeUp();
    }

    SetSpawnedPlayerVisible(true);
  }

  private void CacheConstraintsBeforeShadowCrushPause()
  {
    if (_spawnedPlayerRigidbody2D == null || _hasSavedConstraintsBeforeShadowCrushPause)
    {
      return;
    }

    _savedConstraintsBeforeShadowCrushPause = _spawnedPlayerRigidbody2D.constraints;
    _hasSavedConstraintsBeforeShadowCrushPause = true;
  }

  private void RestoreConstraintsAfterShadowCrushPause()
  {
    if (_spawnedPlayerRigidbody2D != null && _hasSavedConstraintsBeforeShadowCrushPause)
    {
      _spawnedPlayerRigidbody2D.constraints = _savedConstraintsBeforeShadowCrushPause;
    }

    _hasSavedConstraintsBeforeShadowCrushPause = false;
  }

  private void ClearShadowCrushPauseState()
  {
    if (_spawnedPlayerAnimator != null)
    {
      _spawnedPlayerAnimator.SetForcedFallState(false);
    }

    RestoreConstraintsAfterShadowCrushPause();
    _isShadowCrushPaused = false;
  }

  private enum PlayerPlacementTarget
  {
    SavedPosition,
    InitialScenePlacement,
    RespawnPoint,
  }
}
