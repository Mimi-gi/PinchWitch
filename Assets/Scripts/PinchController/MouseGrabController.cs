using UnityEngine;

public sealed class MouseGrabController
{
  public struct Config
  {
    // マウスによる grab 操作を有効にするかどうか。
    public bool enabled;
    // マウス判定に使うカメラ。
    public Camera mouseCamera;
    // mouseCamera が使えないときのフォールバックカメラ。
    public Camera fallbackWorldCamera;
    // 掴める対象を検索するレイヤーマスク。
    public LayerMask grabbableLayerMask;
    // マウスで掴んだときにオブジェクトを持ち上げる高さ。
    public float liftHeight;
    // マウス Raycast の最大距離。
    public float raycastDistance;
    // マウス追従位置を求める基準平面の Y 座標。
    public float dragPlaneY;
  }

  // マウス Raycast の結果を受ける使い回しバッファ。
  private readonly RaycastHit[] _raycastHits = new RaycastHit[32];

  // 掴み・追従・解放を担当する制御クラス。
  private GrabInteractor _grabInteractor;
  // アウトラインの表示制御を担当するクラス。
  private OutlineController _outlineController;
  // 同時入力を避けるための共有制御。
  private GrabInputCoordinator _inputCoordinator;
  // マウス操作設定。
  private Config _config;
  // 現在マウス操作で grab 中かどうか。
  private bool _isDragging;

  public MouseGrabController(GrabInteractor grabInteractor, OutlineController outlineController, GrabInputCoordinator inputCoordinator, Config config)
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

  // マウス操作設定を更新する。
  public void ApplyConfiguration(Config config)
  {
    _config = config;
  }

  // マウスによる grab 開始・追従・解放を処理する。
  public bool Process()
  {
    if (!_config.enabled)
    {
      if (_isDragging)
      {
        EndGrab();
      }

      return false;
    }

    if (_grabInteractor == null)
    {
      _isDragging = false;
      _inputCoordinator?.Release(GrabInputSource.Pointer);
      return false;
    }

    if (_inputCoordinator != null && _inputCoordinator.IsBlocked(GrabInputSource.Pointer))
    {
      if (_isDragging)
      {
        EndGrab();
      }

      return false;
    }

    if (_isDragging)
    {
      if (Input.GetMouseButtonUp(0) || !Input.GetMouseButton(0))
      {
        EndGrab();
        return true;
      }

      UpdateGrab();
      return true;
    }

    if (TryGetMouseGrabbableBody(out var hoverBody, out _))
    {
      _outlineController?.ShowHover(hoverBody);
      if (Input.GetMouseButtonDown(0))
      {
        TryBeginGrab(hoverBody);
      }

      return true;
    }

    if (Input.GetMouseButtonDown(0))
    {
      _outlineController?.ClearCurrentOutline();
    }

    return false;
  }

  // マウス操作の一時状態を破棄する。
  public void Reset()
  {
    if (_isDragging)
    {
      EndGrab();
      return;
    }

    _outlineController?.ClearCurrentOutline();
    _inputCoordinator?.Release(GrabInputSource.Pointer);
  }

  // マウスカーソル直下の掴める Rigidbody を選択して grab を開始する。
  private void TryBeginGrab(Rigidbody targetBody)
  {
    if (_grabInteractor.HasGrabbedBody || targetBody == null)
    {
      return;
    }

    if (_inputCoordinator != null && !_inputCoordinator.TryClaim(GrabInputSource.Pointer))
    {
      return;
    }

    if (!TryGetMouseGrabTarget(out var grabTarget))
    {
      grabTarget = new Vector3(targetBody.position.x, ResolveLiftHeight(), targetBody.position.z);
    }

    if (!_grabInteractor.TryBeginGrab(targetBody, grabTarget))
    {
      _inputCoordinator?.Release(GrabInputSource.Pointer);
      return;
    }

    _grabInteractor.UpdateGrabbedObjectUnclampedY(grabTarget);
    _isDragging = true;
    UpdateGrab();
  }

  // マウス位置から掴み中オブジェクトの目標位置を更新する。
  private void UpdateGrab()
  {
    if (!_grabInteractor.HasGrabbedBody)
    {
      _isDragging = false;
      return;
    }

    if (TryGetMouseGrabTarget(out var grabTarget))
    {
      _grabInteractor.UpdateGrabbedObjectUnclampedY(grabTarget);
    }

    UpdateGrabbedOutline();
  }

  // マウス grab を終了して物理挙動へ戻す。
  private void EndGrab()
  {
    _isDragging = false;
    _grabInteractor?.ReleaseGrabbedObject();
    _outlineController?.ClearCurrentOutline();
    _inputCoordinator?.Release(GrabInputSource.Pointer);
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
      _raycastHits,
      _config.raycastDistance,
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

    var dragPlane = new Plane(Vector3.up, new Vector3(0f, _config.dragPlaneY, 0f));
    if (!dragPlane.Raycast(ray, out var distance) || distance < 0f)
    {
      return false;
    }

    var planePoint = ray.GetPoint(distance);
    grabTarget = new Vector3(planePoint.x, ResolveLiftHeight(), planePoint.z);
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
    if (_config.mouseCamera != null && _config.mouseCamera.enabled)
    {
      return _config.mouseCamera;
    }

    if (_config.fallbackWorldCamera != null && _config.fallbackWorldCamera.enabled)
    {
      return _config.fallbackWorldCamera;
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

    return (_config.grabbableLayerMask.value & (1 << body.gameObject.layer)) != 0 ? body : null;
  }

  // マウス操作中のアウトラインを更新する。
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

  // マウスで持ち上げる高さを返す。
  private float ResolveLiftHeight()
  {
    return Mathf.Max(0f, _config.liftHeight);
  }
}
