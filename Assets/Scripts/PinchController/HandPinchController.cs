using Mediapipe.Unity.HandTracking;
using UnityEngine;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
public class HandPinchController : MonoBehaviour
{
  [Header("MediaPipe")]
  [SerializeField] private HandTrackingGraph handTrackingGraph;
  [SerializeField] private Camera worldCamera;

  [Header("Hand Roles")]
  [SerializeField] private bool swapLeftRightRoles;

  [Header("Cursor (Sphere)")]
  [SerializeField] private Transform cursorSphere;
  [SerializeField] private float cursorSphereDiameter = 0.22f;
  [SerializeField] private Color cursorIdleColor = new Color(0f, 0.898f, 1f, 1f);
  [SerializeField] private Color cursorPinchColor = new Color(1f, 0.922f, 0.231f, 1f);
  [SerializeField] private float cursorPlaneY;

  [Header("Grabbing")]
  [SerializeField] private LayerMask grabbableLayerMask = 1 << 3;
  [SerializeField] private float reach = 2.3f;
  [SerializeField] private Vector2 xRange = new Vector2(-4.5f, 4.5f);
  [SerializeField] private Vector2 yRange = new Vector2(0f, 9f);
  [SerializeField] private Vector2 zRange = new Vector2(-4.5f, 4.5f);
  [SerializeField] private float followLerpX = 0.3f;
  [SerializeField] private float followLerpY = 0.3f;
  [SerializeField] private float followLerpZ = 0.2f;
  [SerializeField] private bool restoreIsKinematicOnRelease;
  [SerializeField] private bool restoreUseGravityOnRelease;
  [SerializeField] private float cursorLerpX = 0.35f;
  [SerializeField] private float cursorLerpZ = 0.22f;

  [Header("Input Sensitivity")]
  [FormerlySerializedAs("xSensitivity")]
  [SerializeField] private float horizontalSensitivity = 0.65f;
  [FormerlySerializedAs("zSensitivity")]
  [SerializeField] private float depthSensitivity = 0.65f;

  [Header("Rotation")]
  [SerializeField] private float xSwipeRotationDegrees = 90f;
  [SerializeField] private float ySwipeRotationDegrees = 45f;
  [SerializeField] private float horizontalSwipeTriggerDistance = 0.18f;
  [SerializeField] private float verticalSwipeTriggerDistance = 0.16f;
  [SerializeField] private float swipeDominanceRatio = 1.25f;
  [SerializeField] private float swipeCooldownSeconds = 0.3f;
  [SerializeField] private float rearmStillnessDistance = 0.012f;
  [SerializeField] private float rearmStillnessDurationSeconds = 0.16f;
  [SerializeField] private float rotationDegreesPerSecond = 180f;

  [Header("Fail Safe")]
  [SerializeField] private float handLostTimeoutSeconds = 0.25f;

  private MediaPipeReceiver _mediaPipeReceiver;
  private TrackedHandStateStore _trackedHandStateStore;
  private HandCursorController _handCursorController;
  private GrabInteractor _grabInteractor;
  private RotationGestureDetector _rotationGestureDetector;
  private HandInteractionProcessor _handInteractionProcessor;
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
    _handInteractionProcessor?.ResetInteractionState();
  }

  // 受信停止後に操作状態と生成リソースを破棄する。
  private void OnDestroy()
  {
    _mediaPipeReceiver?.Stop(this);
    _handInteractionProcessor?.ResetInteractionState();
    _handCursorController?.Dispose();
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
    UpdateMovementInteraction(handRoles);
    UpdateRotationInteraction(handRoles);
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

  // 分割した各責務クラスを初期化する。
  private void InitializeSystems()
  {
    _trackedHandStateStore = new TrackedHandStateStore();
    _mediaPipeReceiver = new MediaPipeReceiver(handTrackingGraph, handLostTimeoutSeconds);
    _handCursorController = new HandCursorController(transform, BuildCursorConfig());
    _grabInteractor = new GrabInteractor(BuildGrabConfig());
    _rotationGestureDetector = new RotationGestureDetector(BuildRotationConfig());
    _handInteractionProcessor = new HandInteractionProcessor(_handCursorController, _grabInteractor, _rotationGestureDetector);
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
    _handInteractionProcessor?.UpdateDependencies(_handCursorController, _grabInteractor, _rotationGestureDetector);
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
      cursorLerpX = cursorLerpX,
      cursorLerpZ = cursorLerpZ,
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
      followLerpX = followLerpX,
      followLerpY = followLerpY,
      followLerpZ = followLerpZ,
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
    handLostTimeoutSeconds = Mathf.Max(0.01f, handLostTimeoutSeconds);
    followLerpX = Mathf.Clamp01(followLerpX);
    followLerpY = Mathf.Clamp01(followLerpY);
    followLerpZ = Mathf.Clamp01(followLerpZ);
    cursorLerpX = Mathf.Clamp01(cursorLerpX);
    cursorLerpZ = Mathf.Clamp01(cursorLerpZ);
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
      _trackedHandStateStore.GetHandState(handRoles.RotationHandType),
      handRoles);
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
