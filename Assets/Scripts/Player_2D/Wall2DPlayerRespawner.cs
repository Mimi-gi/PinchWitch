using UnityEngine;

/// <summary>
/// 2Dプレイヤーの生成と破棄を管理するスクリプト。
/// 2Dモード突入時のスポーン、3D復帰時の破棄、進行位置の保存、
/// 落下や圧死時の初期地点リスポーンをまとめて担当する。
/// </summary>

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
  private Wall2DPlayerCrushDetector _spawnedPlayerCrushDetector;
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
      // 視点変更イベントを購読して、2D/3D 切り替えと同期する。
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
    // 2D モード中だけ落下チェックを行い、閾値を下回ったら初期地点へ戻す。
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
    // 2D に入ったら生成、3D に戻ったら破棄する。
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

    // 有効化された時点の視点状態に合わせてプレイヤー状態を同期する。
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
    // 通常の再スポーンでは、必要に応じて最後に保存した位置を使う。
    SpawnPlayer(useSavedPosition: respawnFromLastKnownPosition && _hasSavedSpawnState, saveCurrentTransformBeforeDestroy: true);
  }

  [ContextMenu("Respawn 2D Player At Initial Point")]
  public void RespawnPlayerAtInitialPoint()
  {
    // 落下や圧死の時は保存位置を捨てて、必ず初期地点から出し直す。
    ClearSavedRespawnPoint();
    SpawnPlayer(useSavedPosition: false, saveCurrentTransformBeforeDestroy: false);
  }

  private void SpawnPlayer(bool useSavedPosition, bool saveCurrentTransformBeforeDestroy)
  {
    // すでにプレイヤーがいれば必要に応じて位置を保存してから作り直す。
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

    // 生成先の親があればそこへぶら下げ、無ければシーン直下に生成する。
    _spawnedPlayerInstance = Instantiate(
      wall2DPlayerPrefab,
      spawnPosition,
      spawnRotation,
      spawnedPlayerParent);

    SubscribeToSpawnedPlayerCrushDetector(_spawnedPlayerInstance);
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
      // 未設定時はシーン内の SpaceViewModeSwitcher を自動で探す。
      viewModeSwitcher = FindFirstObjectByType<SpaceViewModeSwitcher>();
    }
  }

  private void SaveCurrentPlayerTransform()
  {
    if (_spawnedPlayerInstance == null)
    {
      return;
    }

    // 次回 2D モードに戻る時のために現在の位置と向きを保存する。
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
      // 通常の視点切り替えでは、最後にいた場所を保存しておく。
      SaveCurrentPlayerTransform();
    }

    UnsubscribeFromSpawnedPlayerCrushDetector();
    Destroy(_spawnedPlayerInstance);
    _spawnedPlayerInstance = null;
  }

  private void SubscribeToSpawnedPlayerCrushDetector(GameObject spawnedPlayer)
  {
    // 古い購読が残らないように、まず前回のイベント購読を解除する。
    UnsubscribeFromSpawnedPlayerCrushDetector();

    if (spawnedPlayer == null)
    {
      return;
    }

    _spawnedPlayerCrushDetector = spawnedPlayer.GetComponent<Wall2DPlayerCrushDetector>();
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

  private void OnSpawnedPlayerCrushed()
  {
    // 圧死時は最後の位置を使わず、初期地点へ戻す。
    RespawnPlayerAtInitialPoint();
  }
}
