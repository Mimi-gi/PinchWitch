using System;
using Mediapipe.Unity.HandTracking;
using UnityEngine;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
public class HandPinchController : MonoBehaviour
{
  public event Action<Rigidbody> GrabStarted;
  public event Action<Rigidbody> GrabReleased;

  [Header("MediaPipe")]
  // MediaPipe の手検出グラフ本体。
  [SerializeField] private HandTrackingGraph handTrackingGraph;
  // ワールド座標へ変換するときに使う基準カメラ。
  [SerializeField] private Camera worldCamera;

  [Header("Hand Roles")]
  // 左右の役割を入れ替えるかどうか。
  [SerializeField] private bool swapLeftRightRoles;

  [Header("Cursor (Sphere)")]
  // 手位置を示すカーソル球の Transform。
  [SerializeField] private Transform cursorSphere;
  // カーソル球の直径。
  [SerializeField] private float cursorSphereDiameter = 0.22f;
  // 待機時のカーソル色。
  [SerializeField] private Color cursorIdleColor = new Color(0f, 0.898f, 1f, 1f);
  // pinch 中のカーソル色。
  [SerializeField] private Color cursorPinchColor = new Color(1f, 0.922f, 0.231f, 1f);
  // カーソルを動かす床面の Y 座標。
  [SerializeField] private float cursorPlaneY;

  [Header("Grabbing")]
  // 掴めるオブジェクトを探す対象レイヤー。
  [SerializeField] private LayerMask grabbableLayerMask = 1 << 3;
  // 手元から掴み判定を行う半径。
  [SerializeField] private float reach = 2.3f;
  // 掴みを離した直後に再掴みを禁止する時間。
  [SerializeField] private float grabReacquireCooldownSeconds = 0.12f;
  // 再掴み前に、指を開いたまま確認する時間。
  [SerializeField] private float grabReopenConfirmSeconds = 0.08f;
  // pinch が一瞬切れても掴みを維持する猶予時間。
  [SerializeField] private float pinchReleaseGraceSeconds = 0.12f;
  // 掴み対象やカーソルが動ける X 範囲。
  [SerializeField] private Vector2 xRange = new Vector2(-4.5f, 4.5f);
  // 掴み対象やカーソルが動ける Y 範囲。
  [SerializeField] private Vector2 yRange = new Vector2(0f, 9f);
  // 掴み対象やカーソルが動ける Z 範囲。
  [SerializeField] private Vector2 zRange = new Vector2(-4.5f, 4.5f);
  // 解放時に元の isKinematic を復元するかどうか。
  [SerializeField] private bool restoreIsKinematicOnRelease;
  // 解放時に元の useGravity を復元するかどうか。
  [SerializeField] private bool restoreUseGravityOnRelease;

  [Header("Input Sensitivity")]
  [FormerlySerializedAs("xSensitivity")]
  // 手の左右移動をゲーム内 X へ反映する感度。
  // 1 以下では線形のまま使い、1 を超えたときだけ中心からの変位を増幅する。
  [SerializeField] private float horizontalSensitivity = 0.65f;
  [FormerlySerializedAs("zSensitivity")]
  // 手の前後移動をゲーム内 depth へ反映する感度。
  [SerializeField] private float depthSensitivity = 0.65f;

  [Header("Rotation")]
  // 縦スワイプで発生させる X 軸回転量。
  [SerializeField] private float xSwipeRotationDegrees = 90f;
  // 横スワイプで発生させる Y 軸回転量。
  [SerializeField] private float ySwipeRotationDegrees = 45f;
  // 横スワイプとして認める最小移動量。
  [SerializeField] private float horizontalSwipeTriggerDistance = 0.18f;
  // 縦スワイプとして認める最小移動量。
  [SerializeField] private float verticalSwipeTriggerDistance = 0.16f;
  // 主方向の移動がどれだけ優勢ならスワイプとみなすか。
  [SerializeField] private float swipeDominanceRatio = 1.25f;
  // スワイプ発火後の再発火待ち時間。
  [SerializeField] private float swipeCooldownSeconds = 0.3f;
  // 再アーム判定で静止とみなす移動量。
  [SerializeField] private float rearmStillnessDistance = 0.012f;
  // 再アームに必要な静止時間。
  [SerializeField] private float rearmStillnessDurationSeconds = 0.16f;
  // 掴み中オブジェクトが回転目標へ追従する速度。
  [SerializeField] private float rotationDegreesPerSecond = 180f;

  [Header("Fail Safe")]
  // 手を見失ったとみなすまでの猶予時間。
  [SerializeField] private float handLostTimeoutSeconds = 0.25f;

  // MediaPipe の出力を受け取る窓口。
  private MediaPipeReceiver _mediaPipeReceiver;
  // 左右の手状態を保持するストア。
  private TrackedHandStateStore _trackedHandStateStore;
  // 手カーソルの表示と座標変換を担当する制御クラス。
  private HandCursorController _handCursorController;
  // 掴み・追従・解放を担当する制御クラス。
  private GrabInteractor _grabInteractor;
  // 回転ジェスチャー判定を担当する制御クラス。
  private RotationGestureDetector _rotationGestureDetector;
  // アウトラインの表示制御を担当するクラス。
  private OutlineController _outlineController;
  // 実際の移動・回転操作をまとめる制御クラス。
  private HandInteractionProcessor _handInteractionProcessor;
  // 複数入力が同時に grab を操作しないようにする共有制御。
  private GrabInputCoordinator _grabInputCoordinator;
  // 前回適用済みの左右役割入れ替え設定。
  private bool _lastAppliedSwapLeftRightRoles;

  // Inspector 上の初期参照とデフォルト設定を補完する。
  private void Reset()
  {
    handTrackingGraph = GetComponent<HandTrackingGraph>();
    worldCamera = Camera.main;
    if (grabbableLayerMask.value == 0)
    {
      var grabbableLayer = LayerMask.NameToLayer("Grabbable");
      if (grabbableLayer >= 0)
      {
        grabbableLayerMask = 1 << grabbableLayer;
      }
    }

    NormalizeSerializedValues();
    ApplyHandTrackingConfiguration();
    _lastAppliedSwapLeftRightRoles = swapLeftRightRoles;
  }

  // 手状態と依存クラスを初期化する。
  private void Awake()
  {
    EnsureReferences();
    NormalizeSerializedValues();
    ApplyHandTrackingConfiguration();
    _lastAppliedSwapLeftRightRoles = swapLeftRightRoles;
    InitializeSystems();
    EnsureShadowDirtyRegistryExists();
  }

  // MediaPipe からの受信開始を行う。
  private void OnEnable()
  {
    _mediaPipeReceiver?.Start(this);
  }

  // 受信停止と操作状態のリセットを行う。
  private void OnDisable()
  {
    _mediaPipeReceiver?.Stop(this);
    ResetInteractionStateForControllerShutdown();
  }

  // 受信停止後に操作状態と生成リソースを破棄する。
  private void OnDestroy()
  {
    _mediaPipeReceiver?.Stop(this);
    ResetInteractionStateForControllerShutdown();
    _handCursorController?.Dispose();
    UnsubscribeFromGrabInteractor();
  }

  // Inspector から変更された設定値を反映する。
  private void OnValidate()
  {
    NormalizeSerializedValues();
    RefreshSystems();
    HandleRoleSwapChange();
  }

  // 最新の手入力に応じて移動と回転の操作を更新する。
  private void Update()
  {
    var handRoles = CreateHandRoleAssignment();
    _trackedHandStateStore?.Update(_mediaPipeReceiver);
    if (_grabInputCoordinator != null && _grabInputCoordinator.IsBlocked(GrabInputSource.Hand))
    {
      _rotationGestureDetector?.Reset();
      return;
    }

    UpdateMovementInteraction(handRoles);
    UpdateHandInputClaim();
    UpdateRotationInteraction(handRoles);
  }

  // 物理オブジェクトの追従を FixedUpdate で更新する。
  private void FixedUpdate()
  {
    _grabInteractor?.FixedTick(Time.fixedDeltaTime);
  }

  // 必要なコンポーネント参照を補完する。
  private void EnsureReferences()
  {
    if (handTrackingGraph == null)
    {
      handTrackingGraph = GetComponent<HandTrackingGraph>();
    }

    if (worldCamera == null)
    {
      worldCamera = Camera.main;
    }
  }

  private void EnsureShadowDirtyRegistryExists()
  {
    var existingRegistry = FindFirstObjectByType<ShadowDirtyRegistry>(FindObjectsInactive.Include);
    if (existingRegistry != null)
    {
      return;
    }

    var registryObject = new GameObject("ShadowDirtyRegistry");
    registryObject.AddComponent<ShadowDirtyRegistry>();
  }

  // 分割した各責務クラスを初期化する。
  private void InitializeSystems()
  {
    _trackedHandStateStore = new TrackedHandStateStore();
    _mediaPipeReceiver = new MediaPipeReceiver(handTrackingGraph, handLostTimeoutSeconds);
    _handCursorController = new HandCursorController(transform, BuildCursorConfig());
    _grabInteractor = new GrabInteractor(BuildGrabConfig());
    _rotationGestureDetector = new RotationGestureDetector(BuildRotationConfig());
    _outlineController = new OutlineController();
    _grabInputCoordinator = new GrabInputCoordinator();
    _handInteractionProcessor = new HandInteractionProcessor(_handCursorController, _grabInteractor, _rotationGestureDetector, _outlineController, BuildInteractionConfig());
    SubscribeToGrabInteractor();
  }

  // 変更された設定を各責務クラスへ反映する。
  private void RefreshSystems()
  {
    ApplyHandTrackingConfiguration();
    _mediaPipeReceiver?.UpdateGraph(handTrackingGraph);
    _mediaPipeReceiver?.UpdateSettings(handLostTimeoutSeconds);
    _handCursorController?.Configure(transform, BuildCursorConfig());
    _grabInteractor?.ApplyConfiguration(BuildGrabConfig());
    _rotationGestureDetector?.ApplyConfiguration(BuildRotationConfig());
    _handInteractionProcessor?.UpdateDependencies(_handCursorController, _grabInteractor, _rotationGestureDetector, _outlineController);
    _handInteractionProcessor?.ApplyConfiguration(BuildInteractionConfig());
  }

  private void SubscribeToGrabInteractor()
  {
    if (_grabInteractor == null)
    {
      return;
    }

    _grabInteractor.GrabStarted -= HandleGrabStarted;
    _grabInteractor.GrabReleased -= HandleGrabReleased;
    _grabInteractor.GrabStarted += HandleGrabStarted;
    _grabInteractor.GrabReleased += HandleGrabReleased;
  }

  private void UnsubscribeFromGrabInteractor()
  {
    if (_grabInteractor == null)
    {
      return;
    }

    _grabInteractor.GrabStarted -= HandleGrabStarted;
    _grabInteractor.GrabReleased -= HandleGrabReleased;
  }

  private void HandleGrabStarted(Rigidbody grabbedBody)
  {
    GrabStarted?.Invoke(grabbedBody);
  }

  private void HandleGrabReleased(Rigidbody releasedBody)
  {
    GrabReleased?.Invoke(releasedBody);
  }

  // 他入力コンポーネントへ grab 操作に必要な共有依存を渡す。
  public bool TryGetGrabInputContext(out GrabInputContext context)
  {
    context = default;
    if (!isActiveAndEnabled || _grabInteractor == null || _outlineController == null || _grabInputCoordinator == null)
    {
      return false;
    }

    context = new GrabInputContext(
      _grabInteractor,
      _outlineController,
      _grabInputCoordinator,
      worldCamera,
      grabbableLayerMask,
      cursorPlaneY,
      xSwipeRotationDegrees,
      ySwipeRotationDegrees);
    return true;
  }

  // Cursor 制御に渡す設定を構築する。
  private HandCursorController.Config BuildCursorConfig()
  {
    return new HandCursorController.Config
    {
      cursorSphere = cursorSphere,
      worldCamera = worldCamera,
      cursorSphereDiameter = cursorSphereDiameter,
      cursorIdleColor = cursorIdleColor,
      cursorPinchColor = cursorPinchColor,
      cursorPlaneY = cursorPlaneY,
      xRange = xRange,
      yRange = yRange,
      zRange = zRange,
      horizontalSensitivity = horizontalSensitivity,
      depthSensitivity = depthSensitivity,
    };
  }

  // grab 制御に渡す設定を構築する。
  private GrabInteractor.Config BuildGrabConfig()
  {
    return new GrabInteractor.Config
    {
      grabbableLayerMask = grabbableLayerMask,
      reach = reach,
      xRange = xRange,
      yRange = yRange,
      zRange = zRange,
      rotationDegreesPerSecond = rotationDegreesPerSecond,
      restoreIsKinematicOnRelease = restoreIsKinematicOnRelease,
      restoreUseGravityOnRelease = restoreUseGravityOnRelease,
    };
  }

  // 回転ジェスチャー制御に渡す設定を構築する。
  private RotationGestureDetector.Config BuildRotationConfig()
  {
    return new RotationGestureDetector.Config
    {
      xRotationDegrees = xSwipeRotationDegrees,
      yRotationDegrees = ySwipeRotationDegrees,
      horizontalSwipeTriggerDistance = horizontalSwipeTriggerDistance,
      verticalSwipeTriggerDistance = verticalSwipeTriggerDistance,
      swipeDominanceRatio = swipeDominanceRatio,
      swipeCooldownSeconds = swipeCooldownSeconds,
      rearmStillnessDistance = rearmStillnessDistance,
      rearmStillnessDurationSeconds = rearmStillnessDurationSeconds,
    };
  }

  // grab の再開クールダウンなど、操作制御に渡す設定を構築する。
  private HandInteractionProcessor.Config BuildInteractionConfig()
  {
    return new HandInteractionProcessor.Config
    {
      grabReacquireCooldownSeconds = grabReacquireCooldownSeconds,
      grabReopenConfirmSeconds = grabReopenConfirmSeconds,
      pinchReleaseGraceSeconds = pinchReleaseGraceSeconds,
    };
  }

  // Interaction 用に扱う手数を Graph 設定へ反映する。
  private void ApplyHandTrackingConfiguration()
  {
    if (handTrackingGraph == null)
    {
      return;
    }

    handTrackingGraph.maxNumHands = MediaPipeReceiver.InteractionHandCount;
  }

  // Inspector から編集された値を安全な範囲に正規化する。
  private void NormalizeSerializedValues()
  {
    NormalizeRange(ref xRange);
    NormalizeRange(ref yRange);
    NormalizeRange(ref zRange);
    cursorSphereDiameter = Mathf.Max(0.01f, cursorSphereDiameter);
    reach = Mathf.Max(0f, reach);
    grabReacquireCooldownSeconds = Mathf.Clamp(grabReacquireCooldownSeconds, 0f, 0.5f);
    grabReopenConfirmSeconds = Mathf.Clamp(grabReopenConfirmSeconds, 0f, 0.5f);
    pinchReleaseGraceSeconds = Mathf.Clamp(pinchReleaseGraceSeconds, 0f, 0.5f);
    handLostTimeoutSeconds = Mathf.Max(0.01f, handLostTimeoutSeconds);
    horizontalSensitivity = Mathf.Clamp(horizontalSensitivity, 0.1f, 2f);
    depthSensitivity = Mathf.Clamp(depthSensitivity, 0.1f, 4f);
    xSwipeRotationDegrees = Mathf.Max(0f, xSwipeRotationDegrees);
    ySwipeRotationDegrees = Mathf.Max(0f, ySwipeRotationDegrees);
    horizontalSwipeTriggerDistance = Mathf.Clamp(horizontalSwipeTriggerDistance, 0.02f, 0.75f);
    verticalSwipeTriggerDistance = Mathf.Clamp(verticalSwipeTriggerDistance, 0.02f, 0.75f);
    swipeDominanceRatio = Mathf.Clamp(swipeDominanceRatio, 1f, 3f);
    swipeCooldownSeconds = Mathf.Clamp(swipeCooldownSeconds, 0.01f, 1.5f);
    rearmStillnessDistance = Mathf.Clamp(rearmStillnessDistance, 0.001f, 0.08f);
    rearmStillnessDurationSeconds = Mathf.Clamp(rearmStillnessDurationSeconds, 0.02f, 1f);
    rotationDegreesPerSecond = Mathf.Clamp(rotationDegreesPerSecond, 1f, 720f);
  }

  // 現在の設定から左右の役割割り当てを生成する。
  private HandRoleAssignment CreateHandRoleAssignment()
  {
    return HandRoleAssignment.Create(swapLeftRightRoles);
  }

  // 移動担当の手でカーソル表示と grab の開始・追従・解放を制御する。
  private void UpdateMovementInteraction(HandRoleAssignment handRoles)
  {
    if (_trackedHandStateStore == null || _handInteractionProcessor == null)
    {
      return;
    }

    if (!_handInteractionProcessor.TryProcessMovementInteraction(_trackedHandStateStore.GetHandState(handRoles.MovementHandType)))
    {
      _trackedHandStateStore.SetUndetected(handRoles.MovementHandType);
    }
  }

  // 回転担当の手で grab 中オブジェクトの回転入力を処理する。
  private void UpdateRotationInteraction(HandRoleAssignment handRoles)
  {
    if (_trackedHandStateStore == null || _handInteractionProcessor == null)
    {
      return;
    }

    _handInteractionProcessor.ProcessRotationInteraction(
      _mediaPipeReceiver,
      _trackedHandStateStore.GetRotationHandState(handRoles.RotationHandType),
      handRoles);
  }

  // hand 入力が grab 中なら共有制御を hand 側に保持する。
  private void UpdateHandInputClaim()
  {
    if (_grabInputCoordinator == null)
    {
      return;
    }

    if (_grabInteractor != null && _grabInteractor.HasGrabbedBody)
    {
      _grabInputCoordinator.TryClaim(GrabInputSource.Hand);
    }
    else
    {
      _grabInputCoordinator.Release(GrabInputSource.Hand);
    }
  }

  // 左右役割の切り替えが変わったときに操作状態をリセットする。
  private void HandleRoleSwapChange()
  {
    if (_lastAppliedSwapLeftRightRoles == swapLeftRightRoles)
    {
      return;
    }

    _lastAppliedSwapLeftRightRoles = swapLeftRightRoles;
    _trackedHandStateStore?.ResetAll();
    _handInteractionProcessor?.ResetInteractionState();
    _grabInputCoordinator?.Release(GrabInputSource.Hand);
  }

  // FixedUpdate が止まる前に、一時的な grab / drop 状態を復元可能な状態へ畳む。
  private void ResetInteractionStateForControllerShutdown()
  {
    _handInteractionProcessor?.ResetInteractionState();
    _grabInteractor?.ForceFinishVerticalDrops();
    _grabInputCoordinator?.Release(GrabInputSource.Hand);
  }

  // Vector2 の最小値と最大値の順序が逆なら入れ替える。
  private static void NormalizeRange(ref Vector2 range)
  {
    if (range.x > range.y)
    {
      var temp = range.x;
      range.x = range.y;
      range.y = temp;
    }
  }
}
