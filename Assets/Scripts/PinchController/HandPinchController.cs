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

  [Header("Mouse Input")]
  // マウスによる grab 操作を有効にするかどうか。
  [SerializeField] private bool enableMouseInput = true;
  // マウス判定に使うカメラ。未指定なら worldCamera または Camera.main を使う。
  [SerializeField] private Camera mouseCamera;
  // マウスで掴んだときにオブジェクトを持ち上げる高さ。
  [SerializeField] private float mouseLiftHeight = 2f;
  // マウス Raycast の最大距離。
  [SerializeField] private float mouseRaycastDistance = 100f;

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
  // 前回適用済みの左右役割入れ替え設定。
  private bool _lastAppliedSwapLeftRightRoles;
  // マウス Raycast の結果を受ける使い回しバッファ。
  private readonly RaycastHit[] _mouseRaycastHits = new RaycastHit[32];
  // 現在マウス操作で grab 中かどうか。
  private bool _isMouseDragging;

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
    ResetMouseInteractionState();
    _handInteractionProcessor?.ResetInteractionState();
  }

  // 受信停止後に操作状態と生成リソースを破棄する。
  private void OnDestroy()
  {
    _mediaPipeReceiver?.Stop(this);
    ResetMouseInteractionState();
    _handInteractionProcessor?.ResetInteractionState();
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
    if (ProcessMouseInteraction())
    {
      _rotationGestureDetector?.Reset();
      return;
    }

    UpdateMovementInteraction(handRoles);
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
    mouseLiftHeight = Mathf.Max(0f, mouseLiftHeight);
    mouseRaycastDistance = Mathf.Max(0.01f, mouseRaycastDistance);
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

  // マウスによる grab 開始・追従・解放を処理する。
  private bool ProcessMouseInteraction()
  {
    if (!enableMouseInput)
    {
      if (_isMouseDragging)
      {
        EndMouseGrab();
      }

      return false;
    }

    if (_grabInteractor == null)
    {
      _isMouseDragging = false;
      return false;
    }

    if (_isMouseDragging)
    {
      if (Input.GetMouseButtonUp(0) || !Input.GetMouseButton(0))
      {
        EndMouseGrab();
        return true;
      }

      UpdateMouseGrab();
      return true;
    }

    if (TryGetMouseGrabbableBody(out var hoverBody, out _))
    {
      _outlineController?.ShowHover(hoverBody);
      if (Input.GetMouseButtonDown(0))
      {
        TryBeginMouseGrab(hoverBody);
      }

      return true;
    }

    if (Input.GetMouseButtonDown(0))
    {
      _outlineController?.ClearCurrentOutline();
    }

    return false;
  }

  // マウスカーソル直下の掴める Rigidbody を選択して grab を開始する。
  private void TryBeginMouseGrab(Rigidbody targetBody)
  {
    if (_grabInteractor.HasGrabbedBody || targetBody == null)
    {
      return;
    }

    if (!TryGetMouseGrabTarget(out var grabTarget))
    {
      grabTarget = new Vector3(targetBody.position.x, ResolveMouseLiftHeight(), targetBody.position.z);
    }

    if (!_grabInteractor.TryBeginGrab(targetBody, grabTarget))
    {
      return;
    }

    _grabInteractor.UpdateGrabbedObject(grabTarget, false);
    _isMouseDragging = true;
    UpdateMouseGrab();
  }

  // マウス位置から掴み中オブジェクトの目標位置を更新する。
  private void UpdateMouseGrab()
  {
    if (!_grabInteractor.HasGrabbedBody)
    {
      _isMouseDragging = false;
      return;
    }

    if (TryGetMouseGrabTarget(out var grabTarget))
    {
      _grabInteractor.UpdateGrabbedObject(grabTarget, false);
    }

    UpdateMouseGrabbedOutline();
  }

  // マウス grab を終了して物理挙動へ戻す。
  private void EndMouseGrab()
  {
    _isMouseDragging = false;
    _grabInteractor?.ReleaseGrabbedObject();
    _outlineController?.ClearCurrentOutline();
  }

  // マウス操作の一時状態を破棄する。
  private void ResetMouseInteractionState()
  {
    _isMouseDragging = false;
  }

  // マウスカーソル直下にある掴める Rigidbody を取得する。
  private bool TryGetMouseGrabbableBody(out Rigidbody targetBody, out RaycastHit targetHit)
  {
    targetBody = null;
    targetHit = default;
    if (!TryGetMouseRay(out var ray))
    {
      return false;
    }

    var hitCount = Physics.RaycastNonAlloc(
      ray,
      _mouseRaycastHits,
      mouseRaycastDistance,
      ~0,
      QueryTriggerInteraction.Ignore);
    var nearestDistance = float.MaxValue;

    for (var i = 0; i < hitCount; i++)
    {
      var hit = _mouseRaycastHits[i];
      var body = ResolveGrabbableBody(hit.collider);
      if (body == null || hit.distance >= nearestDistance)
      {
        continue;
      }

      nearestDistance = hit.distance;
      targetBody = body;
      targetHit = hit;
    }

    return targetBody != null;
  }

  // 現在のマウス位置を、床面基準の XZ と固定高さ Y の grab 目標へ変換する。
  private bool TryGetMouseGrabTarget(out Vector3 grabTarget)
  {
    grabTarget = Vector3.zero;
    if (!TryGetMouseRay(out var ray))
    {
      return false;
    }

    var dragPlane = new Plane(Vector3.up, new Vector3(0f, cursorPlaneY, 0f));
    if (!dragPlane.Raycast(ray, out var distance) || distance < 0f)
    {
      return false;
    }

    var planePoint = ray.GetPoint(distance);
    grabTarget = new Vector3(planePoint.x, ResolveMouseLiftHeight(), planePoint.z);
    return true;
  }

  // マウス位置から Ray を作る。
  private bool TryGetMouseRay(out Ray ray)
  {
    ray = default;
    var inputCamera = ResolveMouseCamera();
    if (inputCamera == null)
    {
      return false;
    }

    ray = inputCamera.ScreenPointToRay(Input.mousePosition);
    return true;
  }

  // マウス入力に使うカメラを解決する。
  private Camera ResolveMouseCamera()
  {
    if (mouseCamera != null && mouseCamera.enabled)
    {
      return mouseCamera;
    }

    if (worldCamera != null && worldCamera.enabled)
    {
      return worldCamera;
    }

    var mainCamera = Camera.main;
    if (mainCamera != null && mainCamera.enabled)
    {
      return mainCamera;
    }

    var cameras = Camera.allCameras;
    for (var i = 0; i < cameras.Length; i++)
    {
      if (cameras[i] != null && cameras[i].enabled)
      {
        return cameras[i];
      }
    }

    return null;
  }

  // Collider から掴める Rigidbody を解決する。
  private Rigidbody ResolveGrabbableBody(Collider targetCollider)
  {
    if (targetCollider == null)
    {
      return null;
    }

    var body = targetCollider.attachedRigidbody;
    if (body == null)
    {
      return null;
    }

    return (grabbableLayerMask.value & (1 << body.gameObject.layer)) != 0 ? body : null;
  }

  // マウス操作中のアウトラインを更新する。
  private void UpdateMouseGrabbedOutline()
  {
    if (_outlineController == null || !_grabInteractor.HasGrabbedBody)
    {
      return;
    }

    if (_grabInteractor.IsGrabbedObjectOverlapping)
    {
      _outlineController.ShowError(_grabInteractor.GrabbedBody);
    }
    else
    {
      _outlineController.ShowGrabbed(_grabInteractor.GrabbedBody);
    }
  }

  // マウスで持ち上げる高さを返す。
  private float ResolveMouseLiftHeight()
  {
    return Mathf.Max(0f, mouseLiftHeight);
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
