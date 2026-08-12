using System;
using UnityEngine;

// 操作方法の切り替え。
public enum GrabInputMode
{
  // マウス操作。
  Mouse,
  // マウスとキーボードで VR コントローラーを模擬する検証用モード。
  MouseVrMock,
  // VR コントローラー。姿勢供給元が差し込まれるまでは動作しない。
  Vr,
}

// grab 操作の入力を一本化して扱うコントローラー。
// HandPinchController から共有依存を受け取り、inputMode で選ばれた 1 つの入力だけを駆動する。
// 同時に複数の入力が grab を奪い合わないよう、モード切り替え時に非アクティブ側を必ず解除する。
//
// 実機 VR は IVrPoseSource の実装を SetPoseSource() で差し込むことで有効になる。
[DisallowMultipleComponent]
[DefaultExecutionOrder(50)]
public sealed class GrabInputController : MonoBehaviour
{
  public event Action<Rigidbody> GrabStarted;
  public event Action<Rigidbody> GrabReleased;

  [Header("Mode")]
  // 使用する操作方法。
  [SerializeField] private GrabInputMode inputMode = GrabInputMode.Mouse;

  [Header("Source")]
  // grab 共有依存を提供する手操作コントローラー。
  [SerializeField] private HandPinchController handPinchController;

  [Header("Mouse")]
  // マウス判定に使うカメラ。未指定なら HandPinchController の worldCamera または Camera.main を使う。
  [SerializeField] private Camera mouseCamera;
  // マウスで掴んだときにオブジェクトを持ち上げる高さ。
  [SerializeField] private float mouseLiftHeight = 2f;
  // マウス Raycast の最大距離。
  [SerializeField] private float mouseRaycastDistance = 100f;

  [Header("VR")]
  // 掴み入力の受け取り方。Toggle はマウス操作と同じ挙動。
  [SerializeField] private VrGrabTriggerMode vrTriggerMode = VrGrabTriggerMode.Toggle;
  // 掴み中の回転の決め方。Stepped はマウス操作と同じ挙動。
  [SerializeField] private VrRotationMode vrRotationMode = VrRotationMode.Stepped;
  // 掴み中の位置の決め方。Relative は掴み始めに位置が飛ばない。
  [SerializeField] private VrPositionMappingMode vrPositionMappingMode = VrPositionMappingMode.Relative;
  // 掴む対象の探し方。Proximity は手の周囲、Ray はコントローラー前方。
  [SerializeField] private VrTargetingMode vrTargetingMode = VrTargetingMode.Proximity;
  // Proximity: 掴める範囲の半径(ワールド単位)。
  [SerializeField] private float vrGrabRadius = 1f;
  // Proximity: 射程外ホバー表示を出す範囲の半径(ワールド単位)。grabRadius 未満なら grabRadius に丸める。
  [SerializeField] private float vrHoverRadius = 2f;
  // Ray: コントローラー前方へ飛ばす Ray の最大距離。
  [SerializeField] private float vrRayDistance = 100f;

  [Header("VR Tracking Space")]
  // トラッキング空間の原点として扱う Transform。未指定ならワールド原点。
  [SerializeField] private Transform trackingOrigin;
  // トラッキング空間 1m をワールド空間の何ユニットとして扱うか。
  [SerializeField] private float positionScale = 3f;
  // 現在のコントローラー位置を中心として記録し直すキー。
  [SerializeField] private KeyCode calibrateCenterKey = KeyCode.C;

  [Header("Hand Visual")]
  // 手(コントローラー)の想定ワールド姿勢を追従させる表示用オブジェクト。
  // Mouse モードでは手の概念が無いため、下の設定に従って自動的に隠す。
  [SerializeField] private Transform controllerVisual;
  // VR 系モード以外のとき controllerVisual を自動的に非表示にするかどうか。
  // 切っておくと、最後に居た位置に置き去りの表示が残る。
  [SerializeField] private bool hideControllerVisualWhenInactive = true;
  // 照準 Ray を Debug.DrawRay で描画するかどうか(Scene ビュー、および Gizmos 有効時の Game ビュー)。
  [SerializeField] private bool drawAimRay = true;
  // 掴める対象を捉えているときの Ray の色。
  [SerializeField] private Color aimRayHitColor = Color.green;
  // 何も捉えていないときの Ray の色。
  [SerializeField] private Color aimRayMissColor = new Color(1f, 1f, 1f, 0.7f);

  [Header("VR Mock")]
  // マウス移動 1 単位あたりの平行移動量(m)。
  [SerializeField] private float mockTranslationSensitivity = 0.02f;
  // 奥行きモード時のマウス縦移動 1 単位あたりの移動量(m)。
  [SerializeField] private float mockDepthSensitivity = 0.02f;
  // 回転モード時のマウス移動 1 単位あたりの回転量(度)。
  [SerializeField] private float mockRotationSensitivity = 2f;
  // ホイール 1 単位あたりの Roll 回転量(度)。
  [SerializeField] private float mockRollSensitivity = 150f;
  // 疑似コントローラーの初期位置(トラッキング空間)。
  [SerializeField] private Vector3 mockInitialPosition = Vector3.zero;
  // 初期位置からの可動範囲(トラッキング空間、各軸の片側)。
  [SerializeField] private Vector3 mockMovementExtents = new Vector3(1.5f, 1.5f, 1.5f);
  // 押している間、マウス縦移動を奥行きに割り当てるキー。
  [SerializeField] private KeyCode mockDepthModifierKey = KeyCode.LeftShift;
  // 押している間、マウス移動を回転に割り当てるキー。
  [SerializeField] private KeyCode mockRotationModifierKey = KeyCode.LeftAlt;
  // 掴み入力に割り当てるキー。
  [SerializeField] private KeyCode mockGrabKey = KeyCode.G;
  // ステップ回転に割り当てるキー。
  [SerializeField] private KeyCode mockRotateStepKey = KeyCode.T;
  // 回転軸切り替えに割り当てるキー。
  [SerializeField] private KeyCode mockAxisToggleKey = KeyCode.Y;
  // 疑似コントローラーの姿勢を初期状態へ戻すキー。
  [SerializeField] private KeyCode mockResetPoseKey = KeyCode.B;

  // マウス操作ロジック。
  private MouseGrabController _mouseGrabController;
  // VR 操作ロジック。MouseVrMock と Vr で共用する。
  private VrGrabController _vrGrabController;
  // トラッキング空間からワールド空間への変換。
  private VrTrackingSpaceMapper _trackingSpaceMapper;
  // 内蔵のモック姿勢供給元。
  private MockVrPoseSource _mockPoseSource;
  // 外部から差し込まれた姿勢供給元(実機用)。
  private IVrPoseSource _injectedPoseSource;
  // 前フレームのトラッキング空間位置。
  private Vector3 _previousTrackingPosition;
  // 前フレームの位置を保持済みかどうか。
  private bool _hasPreviousTrackingPosition;
  // 現在駆動しているモード。
  private GrabInputMode _activeMode;
  // 駆動中のモードを保持済みかどうか。
  private bool _hasActiveMode;
  // 姿勢供給元が無い旨の警告を出し終えたかどうか。
  private bool _hasWarnedMissingPoseSource;
  // 直近フレームの手のワールド位置(Gizmo 描画用)。
  private Vector3 _lastHandPosition;
  // 手の位置を保持済みかどうか。
  private bool _hasLastHandPosition;

  private GrabInputContext _grabInputContext;
  private bool _hasGrabInputContext;
  private bool _interactionEnabled = true;
  private GrabInteractor _subscribedGrabInteractor;

  private void Reset()
  {
    EnsureReferences();
    NormalizeSerializedValues();
  }

  private void Awake()
  {
    EnsureReferences();
    NormalizeSerializedValues();
  }

  private void OnValidate()
  {
    NormalizeSerializedValues();
    _mockPoseSource?.ApplyConfiguration(BuildMockConfig());
    _trackingSpaceMapper?.ApplyConfiguration(BuildMapperConfig());
    if (_hasGrabInputContext)
    {
      _mouseGrabController?.ApplyConfiguration(BuildMouseConfig(_grabInputContext));
      _vrGrabController?.ApplyConfiguration(BuildVrConfig(_grabInputContext));
    }
  }

  private void OnDisable()
  {
    ResetInteractionState();
    UnsubscribeFromGrabInteractor();
  }

  private void OnDestroy()
  {
    ResetInteractionState();
    UnsubscribeFromGrabInteractor();
  }

  private void Update()
  {
    if (!TryPrepareControllers())
    {
      return;
    }

    SyncActiveMode();

    switch (_activeMode)
    {
      case GrabInputMode.Mouse:
        SetControllerVisualVisible(false);
        _mouseGrabController.Process();
        return;
      case GrabInputMode.MouseVrMock:
      case GrabInputMode.Vr:
        ProcessVrInput();
        return;
    }
  }

  // 実機の姿勢供給元を差し込む。null を渡すと Vr モードは無効に戻る。
  public void SetPoseSource(IVrPoseSource poseSource)
  {
    _injectedPoseSource = poseSource;
    _hasPreviousTrackingPosition = false;
    _hasWarnedMissingPoseSource = false;
    _vrGrabController?.ResetController();
  }

  // 現在の操作方法を切り替える。
  public void SetInputMode(GrabInputMode mode)
  {
    inputMode = mode;
  }

  public void SetInteractionEnabled(bool isEnabled)
  {
    if (_interactionEnabled == isEnabled)
    {
      return;
    }

    _interactionEnabled = isEnabled;
    if (!_interactionEnabled)
    {
      ResetInteractionState();
    }

    if (_hasGrabInputContext)
    {
      _mouseGrabController?.ApplyConfiguration(BuildMouseConfig(_grabInputContext));
      _vrGrabController?.ApplyConfiguration(BuildVrConfig(_grabInputContext));
    }
  }

  // inputMode が変わったら、直前まで動かしていた入力を必ず解除してから切り替える。
  private void SyncActiveMode()
  {
    if (_hasActiveMode && _activeMode == inputMode)
    {
      return;
    }

    if (_hasActiveMode)
    {
      DeactivateMode(_activeMode);
    }

    _activeMode = inputMode;
    _hasActiveMode = true;
    _hasWarnedMissingPoseSource = false;
    _hasPreviousTrackingPosition = false;
  }

  // 指定モードが握っている grab と入力調停の権利を解放する。
  private void DeactivateMode(GrabInputMode mode)
  {
    switch (mode)
    {
      case GrabInputMode.Mouse:
        _mouseGrabController?.Reset();
        return;
      case GrabInputMode.MouseVrMock:
      case GrabInputMode.Vr:
        _vrGrabController?.ResetController();
        return;
    }
  }

  // VR 系モードの 1 フレームぶんの処理。
  private void ProcessVrInput()
  {
    var poseSource = ResolveActivePoseSource();
    if (poseSource == null)
    {
      SetControllerVisualVisible(false);
      WarnMissingPoseSourceOnce();
      return;
    }

    poseSource.Poll();
    if (!poseSource.TryGetPose(out var trackingPosition, out var trackingRotation))
    {
      SetControllerVisualVisible(false);
      _hasPreviousTrackingPosition = false;
      return;
    }

    if (Input.GetKeyDown(calibrateCenterKey))
    {
      _trackingSpaceMapper.CalibrateCenter(trackingPosition);
    }

    var trackingDelta = _hasPreviousTrackingPosition
      ? trackingPosition - _previousTrackingPosition
      : Vector3.zero;
    _previousTrackingPosition = trackingPosition;
    _hasPreviousTrackingPosition = true;

    var pose = new VrGrabController.PoseSample(
      _trackingSpaceMapper.ToWorldPosition(trackingPosition),
      _trackingSpaceMapper.ToWorldRotation(trackingRotation),
      _trackingSpaceMapper.ToWorldDelta(trackingDelta));

    _lastHandPosition = pose.WorldPosition;
    _hasLastHandPosition = true;

    UpdateControllerVisual(pose);
    _vrGrabController.Process(poseSource, pose);
    DrawAimRay(pose);
  }

  // Proximity の判定半径を Scene ビューで確認できるようにする。
  private void OnDrawGizmosSelected()
  {
    if (!_hasLastHandPosition || vrTargetingMode != VrTargetingMode.Proximity)
    {
      return;
    }

    Gizmos.color = aimRayHitColor;
    Gizmos.DrawWireSphere(_lastHandPosition, vrGrabRadius);
    Gizmos.color = aimRayMissColor;
    Gizmos.DrawWireSphere(_lastHandPosition, Mathf.Max(vrGrabRadius, vrHoverRadius));
  }

  // 現在のモードで使う姿勢供給元を解決する。
  private IVrPoseSource ResolveActivePoseSource()
  {
    if (_activeMode == GrabInputMode.MouseVrMock)
    {
      if (_mockPoseSource == null)
      {
        _mockPoseSource = new MockVrPoseSource(BuildMockConfig());
      }
      else
      {
        _mockPoseSource.ApplyConfiguration(BuildMockConfig());
      }

      return _mockPoseSource;
    }

    return _injectedPoseSource;
  }

  // 実機モードなのに姿勢供給元が無い場合、毎フレーム出力しないよう一度だけ警告する。
  private void WarnMissingPoseSourceOnce()
  {
    if (_hasWarnedMissingPoseSource)
    {
      return;
    }

    _hasWarnedMissingPoseSource = true;
    Debug.LogWarning(
      $"{nameof(GrabInputController)}: {GrabInputMode.Vr} は姿勢供給元が未設定のため動作しません。"
      + $"IVrPoseSource の実装を {nameof(SetPoseSource)}() で差し込んでください。",
      this);
  }

  private bool TryPrepareControllers()
  {
    EnsureReferences();
    if (handPinchController == null || !handPinchController.TryGetGrabInputContext(out var context))
    {
      ResetInteractionState();
      return false;
    }

    _grabInputContext = context;
    _hasGrabInputContext = true;
    SubscribeToGrabInteractor(context.GrabInteractor);

    if (_trackingSpaceMapper == null)
    {
      _trackingSpaceMapper = new VrTrackingSpaceMapper(BuildMapperConfig());
    }
    else
    {
      _trackingSpaceMapper.ApplyConfiguration(BuildMapperConfig());
    }

    if (_mouseGrabController == null)
    {
      _mouseGrabController = new MouseGrabController(
        context.GrabInteractor,
        context.OutlineController,
        context.InputCoordinator,
        BuildMouseConfig(context));
    }
    else
    {
      _mouseGrabController.UpdateDependencies(context.GrabInteractor, context.OutlineController, context.InputCoordinator);
      _mouseGrabController.ApplyConfiguration(BuildMouseConfig(context));
    }

    if (_vrGrabController == null)
    {
      _vrGrabController = new VrGrabController(
        context.GrabInteractor,
        context.OutlineController,
        context.InputCoordinator,
        BuildVrConfig(context));
    }
    else
    {
      _vrGrabController.UpdateDependencies(context.GrabInteractor, context.OutlineController, context.InputCoordinator);
      _vrGrabController.ApplyConfiguration(BuildVrConfig(context));
    }

    return true;
  }

  private void EnsureReferences()
  {
    if (handPinchController == null)
    {
      handPinchController = GetComponent<HandPinchController>();
    }

    if (handPinchController == null)
    {
      handPinchController = FindFirstObjectByType<HandPinchController>();
    }
  }

  // 表示用オブジェクトを、手があると想定しているワールド姿勢へ追従させる。
  private void UpdateControllerVisual(VrGrabController.PoseSample pose)
  {
    if (controllerVisual == null)
    {
      return;
    }

    SetControllerVisualVisible(true);
    controllerVisual.SetPositionAndRotation(pose.WorldPosition, pose.WorldRotation);
  }

  // 手の表示を出し入れする。hideControllerVisualWhenInactive が false なら消さない。
  private void SetControllerVisualVisible(bool isVisible)
  {
    if (controllerVisual == null || (!isVisible && !hideControllerVisualWhenInactive))
    {
      return;
    }

    var visualObject = controllerVisual.gameObject;
    if (visualObject.activeSelf != isVisible)
    {
      visualObject.SetActive(isVisible);
    }
  }

  // 照準 Ray を描画する。掴める対象を捉えている間は色を変え、当たった位置で止める。
  private void DrawAimRay(VrGrabController.PoseSample pose)
  {
    // Process が早期 return したフレームは照準が未記録なので描画しない。
    if (!drawAimRay || _vrGrabController == null || _vrGrabController.AimDistance <= 0f)
    {
      return;
    }

    var origin = pose.WorldPosition;
    var endPoint = _vrGrabController.AimHitPoint;
    var rayColor = _vrGrabController.HasAimHit ? aimRayHitColor : aimRayMissColor;
    // depthTest を切って、手前の物体に隠れても Ray の全長が見えるようにする。
    Debug.DrawLine(origin, endPoint, rayColor, 0f, false);
  }

  private MouseGrabController.Config BuildMouseConfig(GrabInputContext context)
  {
    return new MouseGrabController.Config
    {
      enabled = _interactionEnabled && inputMode == GrabInputMode.Mouse,
      mouseCamera = mouseCamera,
      fallbackWorldCamera = context.FallbackWorldCamera,
      grabbableLayerMask = context.GrabbableLayerMask,
      liftHeight = mouseLiftHeight,
      raycastDistance = mouseRaycastDistance,
      dragPlaneY = context.DragPlaneY,
      xRotationDegrees = context.XRotationDegrees,
      yRotationDegrees = context.YRotationDegrees,
    };
  }

  private VrGrabController.Config BuildVrConfig(GrabInputContext context)
  {
    return new VrGrabController.Config
    {
      enabled = _interactionEnabled && inputMode != GrabInputMode.Mouse,
      triggerMode = vrTriggerMode,
      rotationMode = vrRotationMode,
      positionMappingMode = vrPositionMappingMode,
      targetingMode = vrTargetingMode,
      grabRadius = vrGrabRadius,
      hoverRadius = vrHoverRadius,
      rayDistance = vrRayDistance,
      xRotationDegrees = context.XRotationDegrees,
      yRotationDegrees = context.YRotationDegrees,
    };
  }

  private VrTrackingSpaceMapper.Config BuildMapperConfig()
  {
    return new VrTrackingSpaceMapper.Config
    {
      trackingOrigin = trackingOrigin,
      positionScale = positionScale,
    };
  }

  private MockVrPoseSource.Config BuildMockConfig()
  {
    return new MockVrPoseSource.Config
    {
      translationSensitivity = mockTranslationSensitivity,
      depthSensitivity = mockDepthSensitivity,
      rotationSensitivity = mockRotationSensitivity,
      rollSensitivity = mockRollSensitivity,
      initialPosition = mockInitialPosition,
      movementExtents = mockMovementExtents,
      depthModifierKey = mockDepthModifierKey,
      rotationModifierKey = mockRotationModifierKey,
      grabKey = mockGrabKey,
      rotateStepKey = mockRotateStepKey,
      axisToggleKey = mockAxisToggleKey,
      resetPoseKey = mockResetPoseKey,
    };
  }

  private void ResetInteractionState()
  {
    _mouseGrabController?.Reset();
    _vrGrabController?.ResetController();
    _mockPoseSource?.ResetState();
    _injectedPoseSource?.ResetState();
    SetControllerVisualVisible(false);
    _hasPreviousTrackingPosition = false;
    if (_hasGrabInputContext)
    {
      _grabInputContext.InputCoordinator?.Release(GrabInputSource.Pointer);
      _grabInputContext.InputCoordinator?.Release(GrabInputSource.Vr);
    }
  }

  private void SubscribeToGrabInteractor(GrabInteractor grabInteractor)
  {
    if (_subscribedGrabInteractor == grabInteractor)
    {
      return;
    }

    UnsubscribeFromGrabInteractor();
    _subscribedGrabInteractor = grabInteractor;
    if (_subscribedGrabInteractor == null)
    {
      return;
    }

    _subscribedGrabInteractor.GrabStarted += HandleGrabStarted;
    _subscribedGrabInteractor.GrabReleased += HandleGrabReleased;
  }

  private void UnsubscribeFromGrabInteractor()
  {
    if (_subscribedGrabInteractor == null)
    {
      return;
    }

    _subscribedGrabInteractor.GrabStarted -= HandleGrabStarted;
    _subscribedGrabInteractor.GrabReleased -= HandleGrabReleased;
    _subscribedGrabInteractor = null;
  }

  private void HandleGrabStarted(Rigidbody grabbedBody)
  {
    GrabStarted?.Invoke(grabbedBody);
  }

  private void HandleGrabReleased(Rigidbody releasedBody)
  {
    GrabReleased?.Invoke(releasedBody);
  }

  private void NormalizeSerializedValues()
  {
    mouseLiftHeight = Mathf.Max(0f, mouseLiftHeight);
    mouseRaycastDistance = Mathf.Max(0.01f, mouseRaycastDistance);
    vrRayDistance = Mathf.Max(0.01f, vrRayDistance);
    vrGrabRadius = Mathf.Max(0.01f, vrGrabRadius);
    vrHoverRadius = Mathf.Max(vrGrabRadius, vrHoverRadius);
    positionScale = Mathf.Max(0.01f, positionScale);
    mockTranslationSensitivity = Mathf.Max(0f, mockTranslationSensitivity);
    mockDepthSensitivity = Mathf.Max(0f, mockDepthSensitivity);
    mockRotationSensitivity = Mathf.Max(0f, mockRotationSensitivity);
    mockRollSensitivity = Mathf.Max(0f, mockRollSensitivity);
    mockMovementExtents = new Vector3(
      Mathf.Max(0f, mockMovementExtents.x),
      Mathf.Max(0f, mockMovementExtents.y),
      Mathf.Max(0f, mockMovementExtents.z));
  }
}
