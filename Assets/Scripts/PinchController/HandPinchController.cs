using Mediapipe.Unity.HandTracking;
using UnityEngine;

[DisallowMultipleComponent]
public class HandPinchController : MonoBehaviour
{
  private enum GrabControlHand
  {
    None,
    Right,
    Fallback,
  }

  [Header("MediaPipe")]
  [SerializeField] private HandTrackingGraph handTrackingGraph;
  [SerializeField] private Camera worldCamera;

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

  private readonly Vector3[] _fallbackFrameLandmarks = new Vector3[MediaPipeReceiver.HandLandmarkCount];
  private readonly Vector3[] _leftFrameLandmarks = new Vector3[MediaPipeReceiver.HandLandmarkCount];
  private readonly Vector3[] _rightFrameLandmarks = new Vector3[MediaPipeReceiver.HandLandmarkCount];

  private MediaPipeReceiver _mediaPipeReceiver;
  private HandStateCalculator _fallbackHandStateCalculator;
  private HandStateCalculator _leftHandStateCalculator;
  private HandStateCalculator _rightHandStateCalculator;
  private HandCursorController _handCursorController;
  private GrabInteractor _grabInteractor;
  private RotationGestureDetector _rotationGestureDetector;
  private HandState _fallbackHandState;
  private HandState _leftHandState;
  private HandState _rightHandState;
  private bool _prevPinch;
  private GrabControlHand _activeGrabHand;
  private GrabControlHand _lastResolvedGrabHand;

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
  }

  // 手状態と依存クラスを初期化する。
  private void Awake()
  {
    EnsureReferences();
    NormalizeSerializedValues();
    ApplyHandTrackingConfiguration();
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
    _grabInteractor?.ReleaseGrabbedObject();
    _handCursorController?.HideCursor();
    _rotationGestureDetector?.Reset();
    _activeGrabHand = GrabControlHand.None;
    _lastResolvedGrabHand = GrabControlHand.None;
    _prevPinch = false;
  }

  // 生成したリソースを破棄する。
  private void OnDestroy()
  {
    _mediaPipeReceiver?.Stop(this);
    _grabInteractor?.ReleaseGrabbedObject();
    _handCursorController?.Dispose();
    _rotationGestureDetector?.Reset();
  }

  // Inspector から変更された設定値を反映する。
  private void OnValidate()
  {
    NormalizeSerializedValues();
    RefreshSystems();
  }

  // 最新の手入力に応じてカーソルと grab 状態を更新する。
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
    _fallbackHandStateCalculator = new HandStateCalculator();
    _leftHandStateCalculator = new HandStateCalculator();
    _rightHandStateCalculator = new HandStateCalculator();
    _fallbackHandState = _fallbackHandStateCalculator.CreateInitialState();
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
    if (_mediaPipeReceiver == null || !_mediaPipeReceiver.TryGetLatestLandmarks(_fallbackFrameLandmarks))
    {
      _fallbackHandStateCalculator?.SetUndetected(ref _fallbackHandState);
    }
    else
    {
      _fallbackHandStateCalculator.UpdateHandState(_fallbackFrameLandmarks, ref _fallbackHandState);
    }

    if (_mediaPipeReceiver == null || !_mediaPipeReceiver.TryGetLatestLandmarks(MediaPipeReceiver.TrackedHandType.Left, _leftFrameLandmarks))
    {
      _leftHandStateCalculator?.SetUndetected(ref _leftHandState);
    }
    else
    {
      _leftHandStateCalculator.UpdateHandState(_leftFrameLandmarks, ref _leftHandState);
    }

    if (_mediaPipeReceiver == null || !_mediaPipeReceiver.TryGetLatestLandmarks(MediaPipeReceiver.TrackedHandType.Right, _rightFrameLandmarks))
    {
      _rightHandStateCalculator?.SetUndetected(ref _rightHandState);
    }
    else
    {
      _rightHandStateCalculator.UpdateHandState(_rightFrameLandmarks, ref _rightHandState);
    }
  }

  // 現在の手状態に応じてカーソル表示と grab の開始・追従・解放を制御する。
  private void UpdateHandInteraction()
  {
    if (!TryResolveGrabHandState(out var grabHandState, out var grabControlHand))
    {
      SetGrabHandUndetected();
      return;
    }

    if (!_grabInteractor.HasGrabbedBody)
    {
      _activeGrabHand = GrabControlHand.None;
    }

    if (_lastResolvedGrabHand != grabControlHand)
    {
      _prevPinch = false;
    }

    if (_handCursorController == null
        || _grabInteractor == null
        || !_handCursorController.TryUpdateCursor(grabHandState, out var handTarget))
    {
      SetGrabHandUndetected();
      return;
    }

    var pinchStarted = grabHandState.pinch && !_prevPinch;
    var pinchEnded = !grabHandState.pinch && _prevPinch;
    _prevPinch = grabHandState.pinch;
    _lastResolvedGrabHand = grabControlHand;

    if (pinchStarted && !_grabInteractor.HasGrabbedBody)
    {
      _activeGrabHand = grabControlHand;
      _grabInteractor.TryBeginGrab(handTarget);
    }

    if (_grabInteractor.HasGrabbedBody && grabHandState.pinch)
    {
      _grabInteractor.UpdateGrabbedObject(handTarget);
    }

    if (pinchEnded)
    {
      _grabInteractor.ReleaseGrabbedObject();
      _activeGrabHand = GrabControlHand.None;
      _lastResolvedGrabHand = GrabControlHand.None;
      _rotationGestureDetector?.Reset();
    }
  }

  private void UpdateRotationInteraction()
  {
    if (_rotationGestureDetector == null
        || _grabInteractor == null
        || !_grabInteractor.HasGrabbedBody
        || _activeGrabHand != GrabControlHand.Right
        || !_mediaPipeReceiver.HasFreshHand(MediaPipeReceiver.TrackedHandType.Left)
        || !_mediaPipeReceiver.HasFreshHand(MediaPipeReceiver.TrackedHandType.Right))
    {
      _rotationGestureDetector?.Reset();
      return;
    }

    if (!_leftHandState.pinch)
    {
      _rotationGestureDetector.Reset();
      return;
    }

    if (_rotationGestureDetector.TryDetectSwipe(_leftHandState, out var rotationCommand))
    {
      _grabInteractor.RotateGrabbedObject(rotationCommand.worldAxis, rotationCommand.angleDegrees);
    }
  }

  private bool TryResolveGrabHandState(out HandState handState, out GrabControlHand grabControlHand)
  {
    handState = default;
    grabControlHand = GrabControlHand.None;

    if (_grabInteractor != null && _grabInteractor.HasGrabbedBody)
    {
      if (_activeGrabHand == GrabControlHand.Right && _rightHandState.detected)
      {
        handState = _rightHandState;
        grabControlHand = GrabControlHand.Right;
        return true;
      }

      if (_activeGrabHand == GrabControlHand.Fallback && _fallbackHandState.detected)
      {
        handState = _fallbackHandState;
        grabControlHand = GrabControlHand.Fallback;
        return true;
      }

      return false;
    }

    if (_rightHandState.detected)
    {
      handState = _rightHandState;
      grabControlHand = GrabControlHand.Right;
      return true;
    }

    if (_fallbackHandState.detected)
    {
      handState = _fallbackHandState;
      grabControlHand = GrabControlHand.Fallback;
      return true;
    }

    return false;
  }

  // grab 側の手を見失った状態として内部状態を更新する。
  private void SetGrabHandUndetected()
  {
    _fallbackHandStateCalculator?.SetUndetected(ref _fallbackHandState);
    _rightHandStateCalculator?.SetUndetected(ref _rightHandState);
    _handCursorController?.ResetTrackingCache();
    _handCursorController?.HideCursor();
    _grabInteractor?.ReleaseGrabbedObject();
    _rotationGestureDetector?.Reset();
    _activeGrabHand = GrabControlHand.None;
    _lastResolvedGrabHand = GrabControlHand.None;
    _prevPinch = false;
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
