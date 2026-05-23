using UnityEngine;

public sealed class HandCursorController
{
  private const string CursorLayerName = "handCursor";

  // depth 未確定時に使うカーソル色。
  private static readonly Color UncalibratedCursorColor = Color.red;

  public struct Config
  {
    // 表示に使うカーソル球の Transform。
    public Transform cursorSphere;
    // 画面座標をワールド座標へ変換するカメラ。
    public Camera worldCamera;
    // カーソル球の直径。
    public float cursorSphereDiameter;
    // 通常時のカーソル色。
    public Color cursorIdleColor;
    // pinch 中のカーソル色。
    public Color cursorPinchColor;
    // カーソルを置く平面の Y 座標。
    public float cursorPlaneY;
    // 掴み中オブジェクトの固定 Y 座標。
    public float grabLiftHeight;
    // カーソルが動ける X 範囲。
    public Vector2 xRange;
    // 掴み対象に渡す Y 範囲。
    public Vector2 yRange;
    // カーソルが動ける Z 範囲。
    public Vector2 zRange;
    // 左右操作の感度。
    public float horizontalSensitivity;
    // 前後操作の感度。
    public float depthSensitivity;
  }

  // 動的生成したカーソルをぶら下げる親 Transform。
  private Transform _ownerTransform;
  // 実際に表示するカーソル球。
  private Transform _cursorSphere;
  // 座標変換に使うワールドカメラ。
  private Camera _worldCamera;
  // カーソル球の直径設定。
  private float _cursorSphereDiameter;
  // 通常時のカーソル色設定。
  private Color _cursorIdleColor;
  // pinch 中のカーソル色設定。
  private Color _cursorPinchColor;
  // カーソルを動かす平面の Y 座標設定。
  private float _cursorPlaneY;
  // 掴み中オブジェクトの固定 Y 座標設定。
  private float _grabLiftHeight;
  // カーソルの X 可動範囲設定。
  private Vector2 _xRange;
  // 手ターゲットの Y 可動範囲設定。
  private Vector2 _yRange;
  // カーソルの Z 可動範囲設定。
  private Vector2 _zRange;
  // 左右感度設定。
  private float _horizontalSensitivity;
  // depth 感度設定。
  private float _depthSensitivity;

  // カーソル球の Renderer 参照。
  private Renderer _cursorRenderer;
  // カーソル色変更に使う Material 参照。
  private Material _cursorMaterial;
  // カーソル球をコードから生成したかどうか。
  private bool _createdCursorAtRuntime;
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
    _grabLiftHeight = config.grabLiftHeight;
    _xRange = config.xRange;
    _yRange = config.yRange;
    _zRange = config.zRange;
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

    var cursorPoint = GetCursorPointOnPlane(handState);
    ShowCursor(cursorPoint, handState.pinch, handState.depthReady);
    handTarget = GetHandTargetPoint(cursorPoint);
    return true;
  }

  // 追跡状態を破棄し、カーソル表示を待機中の見た目へ戻す。
  public void HideCursor()
  {
    ShowStandbyCursor();
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
      if (cursorObject.TryGetComponent<Collider>(out var collider))
      {
        Object.Destroy(collider);
      }

      _cursorSphere = cursorObject.transform;
      _createdCursorAtRuntime = true;
    }

    ApplyCursorLayer();
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
    ApplyCursorLayer();
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
    return ComputeRawCursorPointOnPlane(handState);
  }

  // 手状態から未平滑の床面カーソル位置を計算する。
  private Vector3 ComputeRawCursorPointOnPlane(HandState handState)
  {
    var depthZ = Mathf.Clamp(GetHandDepthWorldZ(handState), _zRange.x, _zRange.y);
    var xNorm = ApplyHorizontalSensitivity(Mathf.Clamp01(handState.x), _horizontalSensitivity);
    return new Vector3(Mathf.Lerp(_xRange.x, _xRange.y, xNorm), _cursorPlaneY, depthZ);
  }

  // カーソル表示と同じ X/Z、固定の持ち上げ高さ Y から 3D 追従位置を作る。
  private Vector3 GetHandTargetPoint(Vector3 cursorPoint)
  {
    return new Vector3(cursorPoint.x, Mathf.Clamp(_grabLiftHeight, _yRange.x, _yRange.y), cursorPoint.z);
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

  // 左右移動は端での動き出しを優先する。
  // 1 以下では線形のまま使い、1 を超えたときだけ中心からの変位を増幅する。
  private static float ApplyHorizontalSensitivity(float normalizedValue, float sensitivity)
  {
    var clampedValue = Mathf.Clamp01(normalizedValue);
    if (sensitivity <= 1f)
    {
      return clampedValue;
    }

    return Mathf.Clamp01(0.5f + ((clampedValue - 0.5f) * sensitivity));
  }

  // 前後移動の中心を保ったまま、Inspector の depth 感度で反応量を調整する。
  private static float ApplyDepthSensitivityFromFront(float normalizedDepth, float depthSensitivity)
  {
    var clampedDepth = Mathf.Clamp01(normalizedDepth);
    var safeSensitivity = Mathf.Max(0f, depthSensitivity);
    return Mathf.Clamp01(0.5f + ((clampedDepth - 0.5f) * safeSensitivity));
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

  // カーソルを描画対象の 3D ワールドレイヤーに揃える。
  private void ApplyCursorLayer()
  {
    if (_cursorSphere == null)
    {
      return;
    }

    var cursorLayer = LayerMask.NameToLayer(CursorLayerName);
    if (cursorLayer < 0)
    {
      return;
    }

    SetLayerRecursively(_cursorSphere, cursorLayer);
  }

  private static void SetLayerRecursively(Transform target, int layer)
  {
    target.gameObject.layer = layer;
    for (var i = 0; i < target.childCount; i++)
    {
      SetLayerRecursively(target.GetChild(i), layer);
    }
  }
}
