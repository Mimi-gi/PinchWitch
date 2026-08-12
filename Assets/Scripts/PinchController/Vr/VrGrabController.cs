using UnityEngine;

// 掴み入力の受け取り方。
public enum VrGrabTriggerMode
{
  // 押すたびに掴む/離すを切り替える(マウス操作と同じ)。
  Toggle,
  // 押している間だけ掴む。
  Hold,
}

// 掴み中の回転の決め方。
public enum VrRotationMode
{
  // ボタン入力で 1 ステップずつ回す(マウス操作と同じ)。
  Stepped,
  // コントローラーの向きをそのまま反映する。
  Wrist,
}

// 掴む対象の探し方。
public enum VrTargetingMode
{
  // 手の周囲の球で探す。VR コントローラーで直接掴む挙動。
  Proximity,
  // コントローラー前方への Ray で探す。離れた対象を指して掴む挙動。
  Ray,
}

// 掴み中の位置の決め方。
public enum VrPositionMappingMode
{
  // 掴んだ瞬間からの移動量だけを反映する。掴み始めに位置が飛ばず、持ち直しも効く。
  Relative,
  // トラッキング空間の絶対位置をワールド位置へ対応させる。
  Absolute,
}

// VR コントローラーによる掴み・移動・回転・解放を処理する。
// MouseGrabController と対になる入力層で、GrabInteractor への依存の仕方も同じ。
public sealed class VrGrabController
{
  private enum RotationAxisMode
  {
    Y,
    X,
  }

  // コントローラー姿勢をワールド空間へ変換した 1 フレームぶんのサンプル。
  public readonly struct PoseSample
  {
    // 絶対マッピングしたワールド位置。
    public readonly Vector3 WorldPosition;
    // ワールド空間での向き。
    public readonly Quaternion WorldRotation;
    // 前フレームからのワールド移動量(倍率適用済み)。
    public readonly Vector3 WorldPositionDelta;

    public PoseSample(Vector3 worldPosition, Quaternion worldRotation, Vector3 worldPositionDelta)
    {
      WorldPosition = worldPosition;
      WorldRotation = worldRotation;
      WorldPositionDelta = worldPositionDelta;
    }
  }

  public struct Config
  {
    // VR による grab 操作を有効にするかどうか。
    public bool enabled;
    // 掴み入力の受け取り方。
    public VrGrabTriggerMode triggerMode;
    // 掴み中の回転の決め方。
    public VrRotationMode rotationMode;
    // 掴み中の位置の決め方。
    public VrPositionMappingMode positionMappingMode;
    // 掴む対象の探し方。
    public VrTargetingMode targetingMode;
    // Proximity: 掴める範囲の半径。
    public float grabRadius;
    // Proximity: 射程外ホバー表示を出す範囲の半径。
    public float hoverRadius;
    // Ray: コントローラー前方へ飛ばす Ray の最大距離。
    public float rayDistance;
    // X 軸ステップ回転 1 回ぶんの角度。
    public float xRotationDegrees;
    // Y 軸ステップ回転 1 回ぶんの角度。
    public float yRotationDegrees;
  }

  // Ray の結果を受ける使い回しバッファ。
  private readonly RaycastHit[] _raycastHits = new RaycastHit[32];
  // Proximity 判定の結果を受ける使い回しバッファ。
  private readonly Collider[] _overlapResults = new Collider[32];

  // 掴み・追従・解放を担当する制御クラス。
  private GrabInteractor _grabInteractor;
  // アウトラインの表示制御を担当するクラス。
  private OutlineController _outlineController;
  // 同時入力を避けるための共有制御。
  private GrabInputCoordinator _inputCoordinator;
  // VR 操作設定。
  private Config _config;
  // 現在 VR 操作で grab 中かどうか。
  private bool _isGrabbing;
  // 回転完了後に release する保留状態かどうか(ステップ回転時のみ使う)。
  private bool _releasePendingAfterRotation;
  // 現在選択中のステップ回転軸。
  private RotationAxisMode _rotationAxisMode = RotationAxisMode.Y;
  // Relative マッピングで積み上げている目標位置。
  private Vector3 _relativeTargetPosition;
  // 掴み開始時のコントローラー回転(Wrist モード用)。
  private Quaternion _grabStartControllerRotation = Quaternion.identity;
  // 掴み開始時のオブジェクト回転(Wrist モード用)。
  private Quaternion _grabStartBodyRotation = Quaternion.identity;

  // 直近フレームで Ray が掴める対象に当たったかどうか(デバッグ表示用)。
  public bool HasAimHit { get; private set; }
  // 直近フレームで Ray が当たった位置(デバッグ表示用)。
  public Vector3 AimHitPoint { get; private set; }
  // 直近フレームで Ray が当たるまでの距離(未ヒット時は rayDistance)。
  public float AimDistance { get; private set; }

  public VrGrabController(
    GrabInteractor grabInteractor,
    OutlineController outlineController,
    GrabInputCoordinator inputCoordinator,
    Config config)
  {
    UpdateDependencies(grabInteractor, outlineController, inputCoordinator);
    ApplyConfiguration(config);
  }

  // grab 操作に必要な共有依存を更新する。
  public void UpdateDependencies(GrabInteractor grabInteractor, OutlineController outlineController, GrabInputCoordinator inputCoordinator)
  {
    _grabInteractor = grabInteractor;
    _outlineController = outlineController;
    _inputCoordinator = inputCoordinator;
  }

  // VR 操作設定を更新する。
  public void ApplyConfiguration(Config config)
  {
    _config = config;
  }

  // VR コントローラーによる grab 開始・追従・解放を処理する。
  public bool Process(IVrPoseSource poseSource, PoseSample pose)
  {
    if (!_config.enabled || poseSource == null || !poseSource.IsAvailable)
    {
      if (_isGrabbing)
      {
        CancelGrabImmediately();
      }

      return false;
    }

    if (_grabInteractor == null)
    {
      ResetInputState();
      return false;
    }

    if (_inputCoordinator != null && _inputCoordinator.IsBlocked(GrabInputSource.Vr))
    {
      if (_isGrabbing)
      {
        CancelGrabImmediately();
      }

      return false;
    }

    if (_isGrabbing)
    {
      return ProcessWhileGrabbing(poseSource, pose);
    }

    return ProcessWhileIdle(poseSource, pose);
  }

  // VR 操作の一時状態を破棄する。
  public void ResetController()
  {
    if (_isGrabbing)
    {
      CancelGrabImmediately();
      return;
    }

    ResetInputState();
  }

  // grab 中フレームの処理。
  private bool ProcessWhileGrabbing(IVrPoseSource poseSource, PoseSample pose)
  {
    // 掴み中はホバー判定をしないため、照準は「当たり無し」として更新しておく。
    RecordAim(pose, ResolveIdleAimEnd(pose), false);
    UpdateDeferredRelease();
    if (!_isGrabbing)
    {
      return true;
    }

    if (_releasePendingAfterRotation)
    {
      UpdateGrabbedOutline();
      return true;
    }

    ProcessRotationInput(poseSource, pose);

    if (IsReleaseRequested(poseSource))
    {
      RequestRelease();
      return true;
    }

    UpdateGrabbedTransform(pose);
    return true;
  }

  // 非 grab 中フレームの処理。ホバー表示と掴み開始を扱う。
  private bool ProcessWhileIdle(IVrPoseSource poseSource, PoseSample pose)
  {
    if (TryGetGrabbableBodyInGrabRange(pose, out var hoverBody, out var hoverPoint))
    {
      RecordAim(pose, hoverPoint, true);
      _outlineController?.ShowHover(hoverBody);
      if (poseSource.GrabPressedThisFrame)
      {
        TryBeginGrab(hoverBody, pose);
      }

      return true;
    }

    // 射程外の Grabbable には「掴めないが対象ではある」ことを示す細いアウトラインだけ出す。
    // 掴み操作は受け付けないため、入力は消費しない。
    if (TryGetGrabbableBodyInHoverRange(pose, out var outOfRangeBody, out _))
    {
      RecordAim(pose, ResolveIdleAimEnd(pose), false);
      _outlineController?.ShowHoverOutOfRange(outOfRangeBody);
      return false;
    }

    RecordAim(pose, ResolveIdleAimEnd(pose), false);
    _outlineController?.ClearCurrentOutline();
    return false;
  }

  // デバッグ表示用に、このフレームの照準結果を記録する。
  private void RecordAim(PoseSample pose, Vector3 endPoint, bool hasHit)
  {
    HasAimHit = hasHit;
    AimHitPoint = endPoint;
    AimDistance = Vector3.Distance(pose.WorldPosition, endPoint);
  }

  // 対象を捉えていないときの照準の終点。
  // Proximity は方向の概念が無いため手の位置を返し、結果として線を描画させない。
  private Vector3 ResolveIdleAimEnd(PoseSample pose)
  {
    return _config.targetingMode == VrTargetingMode.Proximity
      ? pose.WorldPosition
      : pose.WorldPosition + pose.WorldRotation * Vector3.forward * _config.rayDistance;
  }

  // 対象の Rigidbody の grab を開始する。
  private void TryBeginGrab(Rigidbody targetBody, PoseSample pose)
  {
    if (_grabInteractor.HasGrabbedBody || targetBody == null)
    {
      return;
    }

    if (_inputCoordinator != null && !_inputCoordinator.TryClaim(GrabInputSource.Vr))
    {
      return;
    }

    // Relative では掴み始めに位置が飛ばないよう、オブジェクトの現在位置を基準にする。
    // Absolute ではコントローラー位置を基準にするため、GrabInteractor 側で水平オフセットが保存される。
    var grabTarget = _config.positionMappingMode == VrPositionMappingMode.Relative
      ? targetBody.position
      : pose.WorldPosition;

    if (!_grabInteractor.TryBeginGrab(targetBody, grabTarget))
    {
      _inputCoordinator?.Release(GrabInputSource.Vr);
      return;
    }

    BeginGrabState(targetBody, pose);
    UpdateGrabbedTransform(pose);
  }

  // 掴み中オブジェクトの目標位置と目標回転を更新する。
  private void UpdateGrabbedTransform(PoseSample pose)
  {
    if (!_grabInteractor.HasGrabbedBody)
    {
      ResetInputState();
      return;
    }

    if (_config.positionMappingMode == VrPositionMappingMode.Relative)
    {
      _relativeTargetPosition += pose.WorldPositionDelta;
      _grabInteractor.UpdateGrabbedObject(_relativeTargetPosition);
    }
    else
    {
      _grabInteractor.UpdateGrabbedObject(pose.WorldPosition);
    }

    if (_config.rotationMode == VrRotationMode.Wrist)
    {
      var controllerDelta = pose.WorldRotation * Quaternion.Inverse(_grabStartControllerRotation);
      _grabInteractor.SetGrabbedObjectRotation(controllerDelta * _grabStartBodyRotation);
    }

    UpdateGrabbedOutline();
  }

  // 内部状態を grab 開始状態へ切り替える。
  private void BeginGrabState(Rigidbody targetBody, PoseSample pose)
  {
    _releasePendingAfterRotation = false;
    _rotationAxisMode = RotationAxisMode.Y;
    _isGrabbing = true;
    _relativeTargetPosition = targetBody.position;
    _grabStartControllerRotation = pose.WorldRotation;
    _grabStartBodyRotation = targetBody.rotation;
  }

  // VR 操作の一時状態を初期化する。
  private void ResetInputState()
  {
    _isGrabbing = false;
    _releasePendingAfterRotation = false;
    HasAimHit = false;
    _outlineController?.ClearCurrentOutline();
    _inputCoordinator?.Release(GrabInputSource.Vr);
    _rotationAxisMode = RotationAxisMode.Y;
  }

  // 入力無効化や Reset などの中断時は、回転完了を待たずに grab を即座に終了する。
  private void CancelGrabImmediately()
  {
    ReleaseGrabbedObjectAndResetInputState();
  }

  // 掴み入力の設定に応じて、解放が要求されたかを判定する。
  private bool IsReleaseRequested(IVrPoseSource poseSource)
  {
    return _config.triggerMode == VrGrabTriggerMode.Hold
      ? !poseSource.GrabHeld
      : poseSource.GrabPressedThisFrame;
  }

  // 解放要求を処理する。ステップ回転中の場合のみ完了まで保留する。
  private void RequestRelease()
  {
    if (_grabInteractor == null || !_grabInteractor.HasGrabbedBody)
    {
      ResetInputState();
      return;
    }

    // Wrist モードでは手ブレで目標回転が動き続け、追従完了の判定が立たない。
    // 保留するとリリースできなくなるため、ステップ回転のときだけ待つ。
    if (_config.rotationMode == VrRotationMode.Stepped && _grabInteractor.IsGrabbedObjectRotating)
    {
      _releasePendingAfterRotation = true;
      return;
    }

    ReleaseGrabbedObjectAndResetInputState();
  }

  // 回転完了待ちの release を更新する。
  private void UpdateDeferredRelease()
  {
    if (!_releasePendingAfterRotation)
    {
      return;
    }

    if (_grabInteractor == null || !_grabInteractor.HasGrabbedBody)
    {
      ResetInputState();
      return;
    }

    if (_grabInteractor.IsGrabbedObjectRotating)
    {
      return;
    }

    ReleaseGrabbedObjectAndResetInputState();
  }

  // grab 中オブジェクトを物理挙動へ戻し、VR 操作状態を初期化する。
  private void ReleaseGrabbedObjectAndResetInputState()
  {
    _grabInteractor?.ReleaseGrabbedObject();
    ResetInputState();
  }

  // ステップ回転の実行と回転軸の切り替えを処理する。
  private void ProcessRotationInput(IVrPoseSource poseSource, PoseSample pose)
  {
    if (_config.rotationMode != VrRotationMode.Stepped)
    {
      return;
    }

    if (_grabInteractor == null || !_grabInteractor.HasGrabbedBody)
    {
      return;
    }

    if (poseSource.AxisTogglePressedThisFrame)
    {
      ToggleRotationAxis();
    }

    if (!poseSource.RotatePressedThisFrame || _grabInteractor.IsGrabbedObjectRotating)
    {
      return;
    }

    ResolveRotationStep(out var worldAxis, out var angleDegrees);
    _grabInteractor.RotateGrabbedObject(worldAxis, angleDegrees);
  }

  // 現在の回転軸設定に基づいて 1 回ぶんの回転量を解決する。
  private void ResolveRotationStep(out Vector3 worldAxis, out float angleDegrees)
  {
    switch (_rotationAxisMode)
    {
      case RotationAxisMode.X:
        worldAxis = Vector3.right;
        angleDegrees = Mathf.Max(0f, _config.xRotationDegrees);
        return;
      default:
        worldAxis = Vector3.up;
        angleDegrees = Mathf.Max(0f, _config.yRotationDegrees);
        return;
    }
  }

  // 入力ごとに回転軸を X/Y で切り替える。
  private void ToggleRotationAxis()
  {
    _rotationAxisMode = _rotationAxisMode switch
    {
      RotationAxisMode.Y => RotationAxisMode.X,
      _ => RotationAxisMode.Y,
    };
  }

  // 掴める範囲にある対象を、現在の判定方式で探す。
  private bool TryGetGrabbableBodyInGrabRange(PoseSample pose, out Rigidbody targetBody, out Vector3 targetPoint)
  {
    return _config.targetingMode == VrTargetingMode.Proximity
      ? TryGetNearestGrabbableBody(pose, _config.grabRadius, out targetBody, out targetPoint)
      : TryGetGrabbableBodyOnRay(pose, _config.rayDistance, out targetBody, out targetPoint);
  }

  // 掴めないが対象ではあることを示すための、一段広い範囲の対象を探す。
  private bool TryGetGrabbableBodyInHoverRange(PoseSample pose, out Rigidbody targetBody, out Vector3 targetPoint)
  {
    return _config.targetingMode == VrTargetingMode.Proximity
      ? TryGetNearestGrabbableBody(pose, Mathf.Max(_config.grabRadius, _config.hoverRadius), out targetBody, out targetPoint)
      : TryGetGrabbableBodyOnRay(pose, Mathf.Infinity, out targetBody, out targetPoint);
  }

  // 手の周囲にある最も近い掴める Rigidbody を取得する。
  private bool TryGetNearestGrabbableBody(PoseSample pose, float radius, out Rigidbody targetBody, out Vector3 targetPoint)
  {
    targetBody = null;
    targetPoint = pose.WorldPosition;
    if (radius <= 0f)
    {
      return false;
    }

    var handPosition = pose.WorldPosition;
    var hitCount = Physics.OverlapSphereNonAlloc(
      handPosition,
      radius,
      _overlapResults,
      ~0,
      QueryTriggerInteraction.Ignore);
    var nearestDistance = float.MaxValue;

    for (var i = 0; i < hitCount; i++)
    {
      var body = ResolveGrabbableBody(_overlapResults[i]);
      if (body == null)
      {
        continue;
      }

      // Collider.ClosestPoint は非凸 MeshCollider で入力点をそのまま返すため使わない。
      // bounds 基準なら形状によらず定義され、距離が 0 に潰れる事故を避けられる。
      var closestPoint = body.ClosestPointOnBounds(handPosition);
      var distance = Vector3.Distance(handPosition, closestPoint);
      if (distance >= nearestDistance)
      {
        continue;
      }

      nearestDistance = distance;
      targetBody = body;
      targetPoint = closestPoint;
    }

    return targetBody != null;
  }

  // コントローラー前方の Ray 上にある掴める Rigidbody を取得する。
  private bool TryGetGrabbableBodyOnRay(PoseSample pose, float maxDistance, out Rigidbody targetBody, out Vector3 targetPoint)
  {
    targetBody = null;
    targetPoint = pose.WorldPosition;
    var ray = new Ray(pose.WorldPosition, pose.WorldRotation * Vector3.forward);
    var hitCount = Physics.RaycastNonAlloc(
      ray,
      _raycastHits,
      maxDistance,
      ~0,
      QueryTriggerInteraction.Ignore);
    var nearestDistance = float.MaxValue;

    for (var i = 0; i < hitCount; i++)
    {
      var hit = _raycastHits[i];
      var body = ResolveGrabbableBody(hit.collider);
      if (body == null || hit.distance >= nearestDistance)
      {
        continue;
      }

      nearestDistance = hit.distance;
      targetBody = body;
      targetPoint = hit.point;
    }

    return targetBody != null;
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

    return _grabInteractor != null && _grabInteractor.CanGrabBody(body) ? body : null;
  }

  // VR 操作中のアウトラインを更新する。
  private void UpdateGrabbedOutline()
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
}
