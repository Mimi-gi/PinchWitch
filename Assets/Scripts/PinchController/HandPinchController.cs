using Mediapipe.Unity.HandTracking;
using UnityEngine;

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
  [SerializeField] private float xSensitivity = 0.65f;
  [SerializeField] private float zSensitivity = 0.65f;

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

  private readonly Vector3[] _leftFrameLandmarks = new Vector3[MediaPipeReceiver.HandLandmarkCount];
  private readonly Vector3[] _rightFrameLandmarks = new Vector3[MediaPipeReceiver.HandLandmarkCount];

  private MediaPipeReceiver _mediaPipeReceiver;
  private HandStateCalculator _leftHandStateCalculator;
  private HandStateCalculator _rightHandStateCalculator;
  private HandCursorController _handCursorController;
  private GrabInteractor _grabInteractor;
  private RotationGestureDetector _rotationGestureDetector;
  private HandState _leftHandState;
  private HandState _rightHandState;
  private bool _prevPinch;
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

  // 受信停止と grab 解放を行う。
  private void OnDisable()
  {
    _mediaPipeReceiver?.Stop(this);
    ResetInteractionState();
  }

  // 生成したリソースを破棄する。
  private void OnDestroy()
  {
    _mediaPipeReceiver?.Stop(this);
    ResetInteractionState();
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
    ProcessLatestHandFrames();
    UpdateHandInteraction();
    UpdateRotationInteraction();
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
    _leftHandStateCalculator = new HandStateCalculator();
    _rightHandStateCalculator = new HandStateCalculator();
    _leftHandState = _leftHandStateCalculator.CreateInitialState();
    _rightHandState = _rightHandStateCalculator.CreateInitialState();
    _mediaPipeReceiver = new MediaPipeReceiver(handTrackingGraph, handLostTimeoutSeconds);
    _handCursorController = new HandCursorController(transform, BuildCursorConfig());
    _grabInteractor = new GrabInteractor(BuildGrabConfig());
    _rotationGestureDetector = new RotationGestureDetector(BuildRotationConfig());
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
      xSensitivity = xSensitivity,
      zSensitivity = zSensitivity,
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
    xSensitivity = Mathf.Clamp(xSensitivity, 0.1f, 2f);
    zSensitivity = Mathf.Clamp(zSensitivity, 0.1f, 2f);
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

  // 最新フレームを受け取り、有効なら手状態へ反映する。
  private void ProcessLatestHandFrames()
  {
    UpdateTrackedHandState(MediaPipeReceiver.TrackedHandType.Left, _leftFrameLandmarks);
    UpdateTrackedHandState(MediaPipeReceiver.TrackedHandType.Right, _rightFrameLandmarks);
  }

  // 移動担当の手でカーソル表示と grab の開始・追従・解放を制御する。
  private void UpdateHandInteraction()
  {
    var movementHandState = GetHandState(GetMovementHandType());
    if (_handCursorController == null
        || _grabInteractor == null
        || !movementHandState.detected
        || !_handCursorController.TryUpdateCursor(movementHandState, out var handTarget))
    {
      SetMovementHandUndetected();
      return;
    }

    var pinchStarted = movementHandState.pinch && !_prevPinch;
    var pinchEnded = !movementHandState.pinch && _prevPinch;
    _prevPinch = movementHandState.pinch;

    if (pinchStarted && !_grabInteractor.HasGrabbedBody)
    {
      _grabInteractor.TryBeginGrab(handTarget);
    }

    if (_grabInteractor.HasGrabbedBody && movementHandState.pinch)
    {
      _grabInteractor.UpdateGrabbedObject(handTarget);
    }

    if (pinchEnded)
    {
      _grabInteractor.ReleaseGrabbedObject();
      _rotationGestureDetector?.Reset();
    }
  }

  // 回転担当の手で grab 中オブジェクトの回転入力を処理する。
  private void UpdateRotationInteraction()
  {
    var movementHandType = GetMovementHandType();
    var rotationHandType = GetRotationHandType();
    var rotationHandState = GetHandState(rotationHandType);

    if (_rotationGestureDetector == null
        || _grabInteractor == null
        || _mediaPipeReceiver == null
        || !_grabInteractor.HasGrabbedBody
        || !_mediaPipeReceiver.HasFreshHand(movementHandType)
        || !_mediaPipeReceiver.HasFreshHand(rotationHandType))
    {
      _rotationGestureDetector?.Reset();
      return;
    }

    if (!rotationHandState.pinch)
    {
      _rotationGestureDetector.Reset();
      return;
    }

    if (_rotationGestureDetector.TryDetectSwipe(rotationHandState, out var rotationCommand))
    {
      _grabInteractor.RotateGrabbedObject(rotationCommand.worldAxis, rotationCommand.angleDegrees);
    }
  }

  // 指定した手の最新フレームを手状態へ反映する。
  private void UpdateTrackedHandState(MediaPipeReceiver.TrackedHandType handType, Vector3[] frameLandmarks)
  {
    switch (handType)
    {
      case MediaPipeReceiver.TrackedHandType.Left:
        UpdateTrackedHandState(_leftHandStateCalculator, ref _leftHandState, handType, frameLandmarks);
        return;
      case MediaPipeReceiver.TrackedHandType.Right:
        UpdateTrackedHandState(_rightHandStateCalculator, ref _rightHandState, handType, frameLandmarks);
        return;
      default:
        return;
    }
  }

  // 受信結果に応じて手状態を更新し、未検知ならリセットする。
  private void UpdateTrackedHandState(HandStateCalculator calculator, ref HandState handState, MediaPipeReceiver.TrackedHandType handType, Vector3[] frameLandmarks)
  {
    if (calculator == null || _mediaPipeReceiver == null || !_mediaPipeReceiver.TryGetLatestLandmarks(handType, frameLandmarks))
    {
      calculator?.SetUndetected(ref handState);
      return;
    }

    calculator.UpdateHandState(frameLandmarks, ref handState);
  }

  // 現在の役割設定に応じて移動担当の手を返す。
  private MediaPipeReceiver.TrackedHandType GetMovementHandType()
  {
    return swapLeftRightRoles ? MediaPipeReceiver.TrackedHandType.Left : MediaPipeReceiver.TrackedHandType.Right;
  }

  // 現在の役割設定に応じて回転担当の手を返す。
  private MediaPipeReceiver.TrackedHandType GetRotationHandType()
  {
    return swapLeftRightRoles ? MediaPipeReceiver.TrackedHandType.Right : MediaPipeReceiver.TrackedHandType.Left;
  }

  // 指定した手に対応する最新の手状態を返す。
  private HandState GetHandState(MediaPipeReceiver.TrackedHandType handType)
  {
    switch (handType)
    {
      case MediaPipeReceiver.TrackedHandType.Left:
        return _leftHandState;
      case MediaPipeReceiver.TrackedHandType.Right:
        return _rightHandState;
      default:
        return default;
    }
  }

  // 指定した手を未検知状態として扱う。
  private void SetTrackedHandUndetected(MediaPipeReceiver.TrackedHandType handType)
  {
    switch (handType)
    {
      case MediaPipeReceiver.TrackedHandType.Left:
        _leftHandStateCalculator?.SetUndetected(ref _leftHandState);
        return;
      case MediaPipeReceiver.TrackedHandType.Right:
        _rightHandStateCalculator?.SetUndetected(ref _rightHandState);
        return;
      default:
        return;
    }
  }

  // 移動担当の手を見失ったときの内部状態を更新する。
  private void SetMovementHandUndetected()
  {
    SetTrackedHandUndetected(GetMovementHandType());
    ResetInteractionState();
  }

  // grab と回転まわりの一時状態をまとめてリセットする。
  private void ResetInteractionState()
  {
    _handCursorController?.ResetTrackingCache();
    _handCursorController?.HideCursor();
    _grabInteractor?.ReleaseGrabbedObject();
    _rotationGestureDetector?.Reset();
    _prevPinch = false;
  }

  // 左右役割の切り替えが変わったときに操作状態をリセットする。
  private void HandleRoleSwapChange()
  {
    if (_lastAppliedSwapLeftRightRoles == swapLeftRightRoles)
    {
      return;
    }

    _lastAppliedSwapLeftRightRoles = swapLeftRightRoles;
    _leftHandStateCalculator?.SetUndetected(ref _leftHandState);
    _rightHandStateCalculator?.SetUndetected(ref _rightHandState);
    ResetInteractionState();
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
