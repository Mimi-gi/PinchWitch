using UnityEngine;

public sealed class HandCursorController
{
  private static readonly Color UncalibratedCursorColor = Color.red;

  public struct Config
  {
    public Transform cursorSphere;
    public Camera worldCamera;
    public float cursorSphereDiameter;
    public Color cursorIdleColor;
    public Color cursorPinchColor;
    public float cursorPlaneY;
    public Vector2 xRange;
    public Vector2 yRange;
    public Vector2 zRange;
    public float cursorLerpX;
    public float cursorLerpZ;
    public float horizontalSensitivity;
    public float depthSensitivity;
  }

  private Transform _ownerTransform;
  private Transform _cursorSphere;
  private Camera _worldCamera;
  private float _cursorSphereDiameter;
  private Color _cursorIdleColor;
  private Color _cursorPinchColor;
  private float _cursorPlaneY;
  private Vector2 _xRange;
  private Vector2 _yRange;
  private Vector2 _zRange;
  private float _cursorLerpX;
  private float _cursorLerpZ;
  private float _horizontalSensitivity;
  private float _depthSensitivity;

  private Renderer _cursorRenderer;
  private Material _cursorMaterial;
  private bool _createdCursorAtRuntime;
  private Vector3 _smoothedCursorPoint;
  private bool _hasSmoothedCursorPoint;
  private int _cachedCursorFrame = -1;
  private Vector3 _cachedCursorPoint;

  // カーソル制御を設定付きで生成する。
  public HandCursorController(Transform ownerTransform, Config config)
  {
    Configure(ownerTransform, config);
  }

  // カーソル表示に使う参照と設定を更新する。
  public void Configure(Transform ownerTransform, Config config)
  {
    _ownerTransform = ownerTransform;
    _worldCamera = config.worldCamera;
    _cursorSphereDiameter = config.cursorSphereDiameter;
    _cursorIdleColor = config.cursorIdleColor;
    _cursorPinchColor = config.cursorPinchColor;
    _cursorPlaneY = config.cursorPlaneY;
    _xRange = config.xRange;
    _yRange = config.yRange;
    _zRange = config.zRange;
    _cursorLerpX = config.cursorLerpX;
    _cursorLerpZ = config.cursorLerpZ;
    _horizontalSensitivity = config.horizontalSensitivity;
    _depthSensitivity = config.depthSensitivity;
    _cursorSphere = config.cursorSphere != null ? config.cursorSphere : _cursorSphere;

    EnsureCursorSphere();
  }

  // 手状態からカーソル位置を更新し、追従先座標を返す。
  public bool TryUpdateCursor(HandState handState, out Vector3 handTarget)
  {
    handTarget = Vector3.zero;
    if (_worldCamera == null || !handState.detected)
    {
      ShowStandbyCursor();
      return false;
    }

    if (!handState.depthReady)
    {
      ResetTrackingCache();
    }

    var cursorPoint = GetCursorPointOnPlane(handState);
    ShowCursor(cursorPoint, handState.pinch, handState.depthReady);
    handTarget = GetHandTargetPoint(handState, cursorPoint);
    return true;
  }

  // カーソル表示を非表示にする。
  public void HideCursor()
  {
    ResetTrackingCache();
    ShowStandbyCursor();
  }

  // 同一フレーム内のキャッシュを破棄する。
  public void ResetTrackingCache()
  {
    _cachedCursorFrame = -1;
    _hasSmoothedCursorPoint = false;
  }

  // 動的生成したリソースを破棄する。
  public void Dispose()
  {
    if (_cursorMaterial != null)
    {
      Object.Destroy(_cursorMaterial);
      _cursorMaterial = null;
    }

    if (_createdCursorAtRuntime && _cursorSphere != null)
    {
      Object.Destroy(_cursorSphere.gameObject);
      _cursorSphere = null;
    }
  }

  // カーソル球の生成と見た目の初期化を行う。
  private void EnsureCursorSphere()
  {
    if (_cursorSphere == null)
    {
      var cursorObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
      cursorObject.name = "HandCursorSphere";
      cursorObject.transform.SetParent(_ownerTransform, false);
      var collider = cursorObject.GetComponent<Collider>();
      if (collider != null)
      {
        Object.Destroy(collider);
      }

      _cursorSphere = cursorObject.transform;
      _createdCursorAtRuntime = true;
    }

    _cursorSphere.localScale = Vector3.one * _cursorSphereDiameter;
    if (_worldCamera != null)
    {
      _cursorSphere.position = GetCursorPointOnPlane(new HandState
      {
        detected = true,
        x = 0.5f,
        y = 0.5f,
        depth = 1f,
        depthReady = false,
        pinch = false,
      });
    }

    var cursorRenderer = _cursorSphere.GetComponent<Renderer>();
    if (_cursorRenderer != cursorRenderer)
    {
      if (_cursorMaterial != null)
      {
        Object.Destroy(_cursorMaterial);
      }

      _cursorRenderer = cursorRenderer;
      _cursorMaterial = _cursorRenderer != null ? _cursorRenderer.material : null;
    }

    if (_cursorMaterial != null)
    {
      ApplyCursorColor(false, false);
    }

    _cursorSphere.gameObject.hideFlags = HideFlags.None;
    ResetTrackingCache();
    ShowStandbyCursor();
  }

  // 指定位置にカーソルを表示して色を更新する。
  private void ShowCursor(Vector3 position, bool isPinching, bool isDepthReady)
  {
    if (_cursorSphere == null)
    {
      return;
    }

    _cursorSphere.position = position;
    _cursorSphere.localScale = Vector3.one * _cursorSphereDiameter;
    ApplyCursorColor(isPinching, isDepthReady);
    SetCursorVisible(true);
  }

  // 待機中のカーソルを中央の手前位置へ表示する。
  private void ShowStandbyCursor()
  {
    if (_cursorSphere == null || _worldCamera == null)
    {
      SetCursorVisible(false);
      return;
    }

    var standbyPoint = GetCursorPointOnPlane(new HandState
    {
      detected = true,
      x = 0.5f,
      y = 0.5f,
      depth = 1f,
      depthReady = false,
      pinch = false,
    });
    ShowCursor(standbyPoint, false, false);
  }

  // 手状態から床面上のカーソル位置を求める。
  private Vector3 GetCursorPointOnPlane(HandState handState)
  {
    if (_cachedCursorFrame == Time.frameCount)
    {
      return _cachedCursorPoint;
    }

    var rawPoint = ComputeRawCursorPointOnPlane(handState);
    if (!_hasSmoothedCursorPoint)
    {
      _smoothedCursorPoint = rawPoint;
      _hasSmoothedCursorPoint = true;
    }
    else
    {
      _smoothedCursorPoint.x = Mathf.Lerp(_smoothedCursorPoint.x, rawPoint.x, _cursorLerpX);
      _smoothedCursorPoint.z = Mathf.Lerp(_smoothedCursorPoint.z, rawPoint.z, _cursorLerpZ);
      _smoothedCursorPoint.y = _cursorPlaneY;
    }

    _cachedCursorPoint = _smoothedCursorPoint;
    _cachedCursorFrame = Time.frameCount;
    return _cachedCursorPoint;
  }

  // 手状態から未平滑の床面カーソル位置を計算する。
  private Vector3 ComputeRawCursorPointOnPlane(HandState handState)
  {
    var depthZ = Mathf.Clamp(GetHandDepthWorldZ(handState), _zRange.x, _zRange.y);
    var xNorm = ApplySensitivity(Mathf.Clamp01(handState.x), _horizontalSensitivity);
    var point = new Vector3(Mathf.Lerp(_xRange.x, _xRange.y, xNorm), _cursorPlaneY, depthZ);

    var viewportX = xNorm;
    if (TryProjectToFloorPlane(viewportX, 0.5f, out var floorPoint)
        || TryProjectToFloorPlane(viewportX, 0.45f, out floorPoint)
        || TryProjectToFloorPlane(viewportX, 0.55f, out floorPoint))
    {
      point.x = Mathf.Clamp(floorPoint.x, _xRange.x, _xRange.y);
    }

    var xCenter = (_xRange.x + _xRange.y) * 0.5f;
    point.x = Mathf.Lerp(xCenter, point.x, Mathf.Clamp01(_horizontalSensitivity));
    return point;
  }

  // カーソル位置と手の高さから 3D 追従位置を作る。
  private Vector3 GetHandTargetPoint(HandState handState, Vector3 cursorPoint)
  {
    var handY = Mathf.Lerp(_yRange.x, _yRange.y, 1f - Mathf.Clamp01(handState.y));
    return new Vector3(cursorPoint.x, Mathf.Clamp(handY, _yRange.x, _yRange.y), cursorPoint.z);
  }

  // 正規化 depth をワールド座標の Z に変換する。
  private float GetHandDepthWorldZ(HandState handState)
  {
    var normalizedDepth = ApplyDepthSensitivityFromFront(Mathf.Clamp01(handState.depth), _depthSensitivity);
    ResolveVisualDepthRange(out var frontWorldZ, out var backWorldZ);
    return Mathf.Lerp(backWorldZ, frontWorldZ, normalizedDepth);
  }

  // カーソルオブジェクトの表示状態を切り替える。
  private void SetCursorVisible(bool visible)
  {
    if (_cursorSphere != null && _cursorSphere.gameObject.activeSelf != visible)
    {
      _cursorSphere.gameObject.SetActive(visible);
    }
  }

  // pinch 状態に応じてカーソル色を更新する。
  private void ApplyCursorColor(bool isPinching, bool isDepthReady)
  {
    if (_cursorMaterial == null)
    {
      return;
    }

    var color = !isDepthReady
      ? UncalibratedCursorColor
      : isPinching ? _cursorPinchColor : _cursorIdleColor;
    _cursorMaterial.color = color;
    if (_cursorMaterial.HasProperty("_EmissionColor"))
    {
      _cursorMaterial.SetColor("_EmissionColor", color * 0.2f);
    }
  }

  // 入力感度を中心基準で正規化値へ反映する。
  private static float ApplySensitivity(float normalizedValue, float sensitivity)
  {
    return Mathf.Clamp01(0.5f + (normalizedValue - 0.5f) * sensitivity);
  }

  // 手前アンカーは固定し、奥方向へ離れる量だけを感度で伸縮する。
  private static float ApplyDepthSensitivityFromFront(float normalizedDepth, float sensitivity)
  {
    return Mathf.Clamp01(1f - (1f - normalizedDepth) * sensitivity);
  }

  // `HandState.depth` は見た目の手前/奥で表現されるため、
  // カメラ基準で実際に手前側へ見える Z 端を毎フレーム解決してから割り当てる。
  private void ResolveVisualDepthRange(out float frontWorldZ, out float backWorldZ)
  {
    var centerX = (_xRange.x + _xRange.y) * 0.5f;
    var minPoint = new Vector3(centerX, _cursorPlaneY, _zRange.x);
    var maxPoint = new Vector3(centerX, _cursorPlaneY, _zRange.y);
    var cameraTransform = _worldCamera.transform;
    var minDepth = Vector3.Dot(minPoint - cameraTransform.position, cameraTransform.forward);
    var maxDepth = Vector3.Dot(maxPoint - cameraTransform.position, cameraTransform.forward);

    if (minDepth <= maxDepth)
    {
      frontWorldZ = _zRange.x;
      backWorldZ = _zRange.y;
      return;
    }

    frontWorldZ = _zRange.y;
    backWorldZ = _zRange.x;
  }

  // ビューポート座標を床面へ射影する。
  private bool TryProjectToFloorPlane(float viewportX, float viewportY, out Vector3 hitPoint)
  {
    hitPoint = Vector3.zero;
    var ray = _worldCamera.ViewportPointToRay(new Vector3(viewportX, viewportY, 0f));
    var floorPlane = new Plane(Vector3.up, new Vector3(0f, _cursorPlaneY, 0f));
    if (!floorPlane.Raycast(ray, out var distance))
    {
      return false;
    }

    hitPoint = ray.GetPoint(distance);
    return true;
  }
}
