using UnityEngine;

public sealed class HandCursorController
{
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
    public float xSensitivity;
    public float zSensitivity;
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
  private float _xSensitivity;
  private float _zSensitivity;

  private Renderer _cursorRenderer;
  private Material _cursorMaterial;
  private bool _createdCursorAtRuntime;
  private Vector3 _smoothedCursorPoint;
  private bool _hasSmoothedCursorPoint;
  private int _cachedCursorFrame = -1;
  private Vector3 _cachedCursorPoint;

  public HandCursorController(Transform ownerTransform, Config config)
  {
    Configure(ownerTransform, config);
  }

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
    _xSensitivity = config.xSensitivity;
    _zSensitivity = config.zSensitivity;
    _cursorSphere = config.cursorSphere != null ? config.cursorSphere : _cursorSphere;

    EnsureCursorSphere();
  }

  public bool TryUpdateCursor(HandState handState, out Vector3 handTarget)
  {
    handTarget = Vector3.zero;
    if (_worldCamera == null || !handState.detected)
    {
      HideCursor();
      return false;
    }

    var cursorPoint = GetCursorPointOnPlane(handState);
    ShowCursor(cursorPoint, handState.pinch);
    handTarget = GetHandTargetPoint(handState, cursorPoint);
    return true;
  }

  public void HideCursor()
  {
    SetCursorVisible(false);
  }

  public void ResetTrackingCache()
  {
    _cachedCursorFrame = -1;
  }

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
        depth = 0.5f,
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
      ApplyCursorColor(false);
    }

    _cursorSphere.gameObject.hideFlags = HideFlags.None;
    SetCursorVisible(false);
  }

  private void ShowCursor(Vector3 position, bool isPinching)
  {
    if (_cursorSphere == null)
    {
      return;
    }

    _cursorSphere.position = position;
    _cursorSphere.localScale = Vector3.one * _cursorSphereDiameter;
    ApplyCursorColor(isPinching);
    SetCursorVisible(true);
  }

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

  private Vector3 ComputeRawCursorPointOnPlane(HandState handState)
  {
    var depthZ = Mathf.Clamp(GetHandDepthWorldZ(handState), _zRange.x, _zRange.y);
    var xNorm = ApplySensitivity(Mathf.Clamp01(handState.x), _xSensitivity);
    var point = new Vector3(Mathf.Lerp(_xRange.x, _xRange.y, xNorm), _cursorPlaneY, depthZ);

    var viewportX = xNorm;
    if (TryProjectToFloorPlane(viewportX, 0.5f, out var floorPoint)
        || TryProjectToFloorPlane(viewportX, 0.45f, out floorPoint)
        || TryProjectToFloorPlane(viewportX, 0.55f, out floorPoint))
    {
      point.x = Mathf.Clamp(floorPoint.x, _xRange.x, _xRange.y);
    }

    var xCenter = (_xRange.x + _xRange.y) * 0.5f;
    point.x = Mathf.Lerp(xCenter, point.x, Mathf.Clamp01(_xSensitivity));
    return point;
  }

  private Vector3 GetHandTargetPoint(HandState handState, Vector3 cursorPoint)
  {
    var handY = Mathf.Lerp(_yRange.x, _yRange.y, 1f - Mathf.Clamp01(handState.y));
    return new Vector3(cursorPoint.x, Mathf.Clamp(handY, _yRange.x, _yRange.y), cursorPoint.z);
  }

  private float GetHandDepthWorldZ(HandState handState)
  {
    var normalizedDepth = ApplySensitivity(Mathf.Clamp01(handState.depth), _zSensitivity);
    return Mathf.Lerp(_zRange.x, _zRange.y, normalizedDepth);
  }

  private void SetCursorVisible(bool visible)
  {
    if (_cursorSphere != null && _cursorSphere.gameObject.activeSelf != visible)
    {
      _cursorSphere.gameObject.SetActive(visible);
    }
  }

  private void ApplyCursorColor(bool isPinching)
  {
    if (_cursorMaterial == null)
    {
      return;
    }

    var color = isPinching ? _cursorPinchColor : _cursorIdleColor;
    _cursorMaterial.color = color;
    if (_cursorMaterial.HasProperty("_EmissionColor"))
    {
      _cursorMaterial.SetColor("_EmissionColor", color * 0.2f);
    }
  }

  private static float ApplySensitivity(float normalizedValue, float sensitivity)
  {
    return Mathf.Clamp01(0.5f + (normalizedValue - 0.5f) * sensitivity);
  }

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
