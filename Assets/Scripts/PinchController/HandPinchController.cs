using Mediapipe.Unity.HandTracking;
using UnityEngine;

[DisallowMultipleComponent]
public class HandPinchController : MonoBehaviour
{
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

  [Header("Fail Safe")]
  [SerializeField] private float handLostTimeoutSeconds = 0.25f;

  private readonly Vector3[] _frameLandmarks = new Vector3[MediaPipeReceiver.HandLandmarkCount];

  private MediaPipeReceiver _mediaPipeReceiver;
  private HandStateCalculator _handStateCalculator;
  private HandCursorController _handCursorController;
  private GrabInteractor _grabInteractor;
  private HandState _handState;
  private bool _prevPinch;

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
  }

  // 生成したリソースを破棄する。
  private void OnDestroy()
  {
    _mediaPipeReceiver?.Stop(this);
    _grabInteractor?.ReleaseGrabbedObject();
    _handCursorController?.Dispose();
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
    ProcessLatestHandFrame();
    UpdateHandInteraction();
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
    _handStateCalculator = new HandStateCalculator();
    _handState = _handStateCalculator.CreateInitialState();
    _mediaPipeReceiver = new MediaPipeReceiver(handTrackingGraph, handLostTimeoutSeconds);
    _handCursorController = new HandCursorController(transform, BuildCursorConfig());
    _grabInteractor = new GrabInteractor(BuildGrabConfig());
  }

  // 変更された設定を各責務クラスへ反映する。
  private void RefreshSystems()
  {
    ApplyHandTrackingConfiguration();
    _mediaPipeReceiver?.UpdateGraph(handTrackingGraph);
    _mediaPipeReceiver?.UpdateSettings(handLostTimeoutSeconds);
    _handCursorController?.Configure(transform, BuildCursorConfig());
    _grabInteractor?.ApplyConfiguration(BuildGrabConfig());
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
      restoreIsKinematicOnRelease = restoreIsKinematicOnRelease,
      restoreUseGravityOnRelease = restoreUseGravityOnRelease,
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
  }

  // 最新フレームを受け取り、有効なら手状態へ反映する。
  private void ProcessLatestHandFrame()
  {
    if (_mediaPipeReceiver == null || !_mediaPipeReceiver.TryGetLatestLandmarks(_frameLandmarks))
    {
      SetHandUndetected();
      return;
    }

    _handStateCalculator.UpdateHandState(_frameLandmarks, ref _handState);
  }

  // 現在の手状態に応じてカーソル表示と grab の開始・追従・解放を制御する。
  private void UpdateHandInteraction()
  {
    if (_handCursorController == null
        || _grabInteractor == null
        || !_handCursorController.TryUpdateCursor(_handState, out var handTarget))
    {
      _grabInteractor?.ReleaseGrabbedObject();
      _prevPinch = false;
      return;
    }

    var pinchStarted = _handState.pinch && !_prevPinch;
    var pinchEnded = !_handState.pinch && _prevPinch;
    _prevPinch = _handState.pinch;

    if (pinchStarted && !_grabInteractor.HasGrabbedBody)
    {
      _grabInteractor.TryBeginGrab(handTarget);
    }

    if (_grabInteractor.HasGrabbedBody && _handState.pinch)
    {
      _grabInteractor.UpdateGrabbedObject(handTarget);
    }

    if (pinchEnded)
    {
      _grabInteractor.ReleaseGrabbedObject();
    }
  }

  // 手を見失った状態として内部状態を更新する。
  private void SetHandUndetected()
  {
    _handStateCalculator?.SetUndetected(ref _handState);
    _handCursorController?.ResetTrackingCache();
    _handCursorController?.HideCursor();
    _grabInteractor?.ReleaseGrabbedObject();
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
