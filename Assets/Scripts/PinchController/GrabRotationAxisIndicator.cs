using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// 掴み中オブジェクトの回転方向を示す表示。
// 矢印(円柱の軸 + 円錐の矢じり)を円周に沿って曲げた立体を回転軸のまわりに置き、
// 回転方向へ一定速度で回し続ける。軸方向から見るとリトライボタンのような円弧矢印になる。
//
// 大きさは掴んだブロックを構成する立方体 1 マスの大きさに比例させる(referenceCubeSize のとき Shape の数値どおり)。
// 矢印が立方体に近づいた部分は、シェーダー側でノイズ付きの発光 Dissolve で消す。
// そのために、構成する立方体の位置・向き・大きさ(GrabTargetShape)を毎フレーム MaterialPropertyBlock で渡す。
//
// メッシュはローカル +Y を回転軸として生成し、ローカル +Y まわりの正の回転方向へ矢印が進む。
// 表示時に +Y をワールドの回転軸へ向けるので、GrabInteractor.RotateGrabbedObject(軸, 正の角度) と同じ向きを指す。
//
// 表示レイヤーは 3D 空間を描くカメラに映る World3D を既定にする(Default はステージのカメラに映らない)。
// 影の計算(PlanarProjectedShadowInstaller)は Grabbable / handCursor レイヤーだけが対象だが、
// ライトの影は全レイヤーから壁へ落ちるため、Renderer の影も必ず切って足場に影響しないようにしている。
[DisallowMultipleComponent]
public sealed class GrabRotationAxisIndicator : MonoBehaviour
{
  private const string IndicatorShaderName = "PinchWitch/RotationAxisIndicator";

  private static readonly int BoxCountId = Shader.PropertyToID("_IndicatorBoxCount");
  private static readonly int BoxWorldToLocalId = Shader.PropertyToID("_IndicatorBoxWorldToLocal");
  private static readonly int BoxHalfExtentsId = Shader.PropertyToID("_IndicatorBoxHalfExtents");
  private static readonly int ScaleId = Shader.PropertyToID("_IndicatorScale");

  [Header("Size")]
  // 掴んだブロックの立方体 1 マスがこの大きさのとき、下の Shape の数値どおりの大きさで出す。
  // 立方体がこれより大きければ矢印全体も同じ比率で大きくなる。
  [SerializeField] private float referenceCubeSize = 1.2f;

  [Header("Shape")]
  // 矢印を巻きつける円周の半径(referenceCubeSize の立方体に対する値)。
  [SerializeField] private float ringRadius = 1.5f;
  // 矢印の軸の太さ(断面の半径)。
  [SerializeField] private float shaftRadius = 0.06f;
  // 矢じり根元の太さ(断面の半径)。
  [SerializeField] private float headRadius = 0.16f;
  // 矢印 1 本が円周上で占める角度(矢じり込み)。
  [SerializeField, Range(10f, 350f)] private float arcDegrees = 270f;
  // そのうち矢じりが占める角度。
  [SerializeField, Range(5f, 90f)] private float headArcDegrees = 30f;
  // 円周上に等間隔で並べる矢印の本数。
  [SerializeField, Range(1, 4)] private int arrowCount = 1;
  // 断面の円の分割数。
  [SerializeField, Range(3, 32)] private int sidesPerRing = 12;
  // 円周方向の分割間隔(度)。
  [SerializeField, Range(1f, 30f)] private float segmentDegrees = 6f;

  [Header("Motion")]
  // 回転方向へ回し続ける速さ(度/秒)。
  [SerializeField] private float spinDegreesPerSecond = 60f;

  [Header("Look")]
  // 表示に使うレイヤー名。見つからなければ自身のレイヤーのまま。
  [SerializeField] private string displayLayerName = "World3D";
  // 使用するマテリアル(通常は PinchWitch/RotationAxisIndicator シェーダーのもの)。
  // 未指定なら同じシェーダーで実行時に作り、それも無ければ半透明の URP Lit で代用する。
  [SerializeField] private Material material;
  // URP Lit で代用するときの色。
  [SerializeField] private Color fallbackColor = new Color(1f, 1f, 1f, 0.35f);

  private MeshFilter _meshFilter;
  private MeshRenderer _meshRenderer;
  private Mesh _mesh;
  private Material _runtimeMaterial;
  private MaterialPropertyBlock _propertyBlock;
  private readonly GrabTargetShape _targetShape = new GrabTargetShape();
  private Transform _target;
  private Vector3 _center;
  private float _scale = 1f;
  private Quaternion _axisRotation = Quaternion.identity;
  private float _spinAngle;
  private bool _isShown;

  // 回転方向の表示を出す(毎フレーム呼んで対象と軸を更新する)。
  // target は掴んでいるオブジェクト。大きさ・中心・Dissolve の判定はその見た目から求める。
  public void Show(Transform target, Vector3 worldAxis)
  {
    EnsureInitialized();
    if (target == null || worldAxis.sqrMagnitude <= Mathf.Epsilon)
    {
      Hide();
      return;
    }

    var axisRotation = Quaternion.FromToRotation(Vector3.up, worldAxis.normalized);
    // 軸が切り替わったら回転位相を戻し、矢印が新しい軸の決まった位置から回り始めるようにする。
    if (!_isShown || Quaternion.Angle(axisRotation, _axisRotation) > 0.5f)
    {
      _spinAngle = 0f;
    }

    _target = target;
    _axisRotation = axisRotation;
    _isShown = true;
    _meshRenderer.enabled = true;
    RefreshFromTarget();
  }

  public void Hide()
  {
    _isShown = false;
    _target = null;
    if (_meshRenderer != null)
    {
      _meshRenderer.enabled = false;
    }
  }

  private void Awake()
  {
    EnsureInitialized();
    Hide();
  }

  private void LateUpdate()
  {
    if (!_isShown)
    {
      return;
    }

    if (_target == null)
    {
      Hide();
      return;
    }

    _spinAngle = Mathf.Repeat(_spinAngle + spinDegreesPerSecond * Time.deltaTime, 360f);
    // 掴んだオブジェクトは移動・回転し続けるので、描画直前の姿勢で中心と立方体の位置を取り直す。
    RefreshFromTarget();
  }

  // 対象の見た目から中心・大きさ・Dissolve 用の立方体情報を更新する。
  private void RefreshFromTarget()
  {
    if (_targetShape.Update(_target))
    {
      _center = _targetShape.Center;
      _scale = _targetShape.CubeSize / Mathf.Max(0.0001f, referenceCubeSize);
    }
    else
    {
      _center = _target.position;
      _scale = 1f;
    }

    ApplyTransform();
    ApplyShapeToMaterial();
  }

  private void ApplyShapeToMaterial()
  {
    _propertyBlock ??= new MaterialPropertyBlock();
    _meshRenderer.GetPropertyBlock(_propertyBlock);
    _propertyBlock.SetFloat(BoxCountId, _targetShape.BoxCount);
    // 配列の長さは最初に渡した長さで固定されるため、常に最大数ぶん渡す。
    _propertyBlock.SetMatrixArray(BoxWorldToLocalId, _targetShape.WorldToBox);
    _propertyBlock.SetVectorArray(BoxHalfExtentsId, _targetShape.HalfExtents);
    _propertyBlock.SetFloat(ScaleId, _scale);
    _meshRenderer.SetPropertyBlock(_propertyBlock);
  }

  private void OnValidate()
  {
    referenceCubeSize = Mathf.Max(0.01f, referenceCubeSize);
    ringRadius = Mathf.Max(0.01f, ringRadius);
    shaftRadius = Mathf.Max(0.001f, shaftRadius);
    headRadius = Mathf.Max(shaftRadius, headRadius);
    headArcDegrees = Mathf.Min(headArcDegrees, arcDegrees);

    // 実行中に形を調整したときは作り直して即座に反映する。
    if (Application.isPlaying && _mesh != null)
    {
      RebuildMesh();
    }
  }

  private void OnDestroy()
  {
    if (_mesh != null)
    {
      Destroy(_mesh);
    }

    if (_runtimeMaterial != null)
    {
      Destroy(_runtimeMaterial);
    }
  }

  private void ApplyTransform()
  {
    transform.SetPositionAndRotation(_center, _axisRotation * Quaternion.AngleAxis(_spinAngle, Vector3.up));
    ApplyWorldScale(_scale);
  }

  // プレハブの子として置かれても、親の拡大率に関係なくワールドで worldScale 倍の大きさにする。
  private void ApplyWorldScale(float worldScale)
  {
    var parent = transform.parent;
    var parentScale = parent != null ? parent.lossyScale : Vector3.one;
    transform.localScale = new Vector3(
      worldScale * SafeInverse(parentScale.x),
      worldScale * SafeInverse(parentScale.y),
      worldScale * SafeInverse(parentScale.z));
  }

  private static float SafeInverse(float value)
  {
    return Mathf.Abs(value) > 0.0001f ? 1f / value : 1f;
  }

  private void EnsureInitialized()
  {
    if (_meshRenderer != null)
    {
      return;
    }

    _meshFilter = GetComponent<MeshFilter>();
    if (_meshFilter == null)
    {
      _meshFilter = gameObject.AddComponent<MeshFilter>();
    }

    _meshRenderer = GetComponent<MeshRenderer>();
    if (_meshRenderer == null)
    {
      _meshRenderer = gameObject.AddComponent<MeshRenderer>();
    }

    var displayLayer = LayerMask.NameToLayer(displayLayerName);
    if (displayLayer >= 0)
    {
      gameObject.layer = displayLayer;
    }

    _meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
    _meshRenderer.receiveShadows = false;
    _meshRenderer.lightProbeUsage = LightProbeUsage.Off;
    _meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    _meshRenderer.sharedMaterial = material != null ? material : CreateRuntimeMaterial();

    _mesh = new Mesh { name = "GrabRotationAxisIndicator" };
    _mesh.MarkDynamic();
    _meshFilter.sharedMesh = _mesh;
    RebuildMesh();
  }

  private Material CreateRuntimeMaterial()
  {
    var indicatorShader = Shader.Find(IndicatorShaderName);
    if (indicatorShader != null)
    {
      _runtimeMaterial = new Material(indicatorShader) { name = "GrabRotationAxisIndicator (Runtime)" };
      return _runtimeMaterial;
    }

    var shader = Shader.Find("Universal Render Pipeline/Lit");
    if (shader == null)
    {
      shader = Shader.Find("Sprites/Default");
    }

    _runtimeMaterial = new Material(shader) { name = "GrabRotationAxisIndicator (Runtime)" };

    // URP Lit を半透明にする設定。
    _runtimeMaterial.SetFloat("_Surface", 1f);
    _runtimeMaterial.SetFloat("_Blend", 0f);
    _runtimeMaterial.SetOverrideTag("RenderType", "Transparent");
    _runtimeMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
    _runtimeMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
    _runtimeMaterial.SetFloat("_ZWrite", 0f);
    _runtimeMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
    _runtimeMaterial.renderQueue = (int)RenderQueue.Transparent;
    _runtimeMaterial.SetColor("_BaseColor", fallbackColor);
    _runtimeMaterial.color = fallbackColor;
    return _runtimeMaterial;
  }

  //=========================================================
  // メッシュ生成
  //=========================================================
  // 円周上の位置 θ(度)での局所座標系。
  //   radial  : 円の中心から外向き
  //   tangent : θ が増える向き(= +Y まわりの正の回転方向)
  //   binormal: 回転軸(+Y)
  // Unity の左手系では AngleAxis(90, up) * forward = right なので、θ=0 を forward、θ=90 を right に置く。
  private static Vector3 Radial(float thetaRadians)
  {
    return new Vector3(Mathf.Sin(thetaRadians), 0f, Mathf.Cos(thetaRadians));
  }

  private static Vector3 Tangent(float thetaRadians)
  {
    return new Vector3(Mathf.Cos(thetaRadians), 0f, -Mathf.Sin(thetaRadians));
  }

  private void RebuildMesh()
  {
    var vertices = new List<Vector3>();
    var normals = new List<Vector3>();
    var triangles = new List<int>();

    var spacing = 360f / Mathf.Max(1, arrowCount);
    var arrowArc = Mathf.Min(arcDegrees, spacing - 1f);
    var headArc = Mathf.Min(headArcDegrees, arrowArc);

    for (var i = 0; i < arrowCount; i++)
    {
      var start = i * spacing;
      var headStart = start + arrowArc - headArc;
      var end = start + arrowArc;

      // 尾の平らな蓋 → 軸 → 矢じりの裏面(段差) → 矢じり(先端で半径 0)
      AddCap(vertices, normals, triangles, start, shaftRadius);
      AddSweep(vertices, normals, triangles, start, headStart, shaftRadius, shaftRadius);
      AddAnnulus(vertices, normals, triangles, headStart, shaftRadius, headRadius);
      AddSweep(vertices, normals, triangles, headStart, end, headRadius, 0f);
    }

    _mesh.Clear();
    _mesh.SetVertices(vertices);
    _mesh.SetNormals(normals);
    _mesh.SetTriangles(triangles, 0);
    _mesh.RecalculateBounds();
  }

  // θ0 から θ1 まで、断面半径を r0 から r1 へ変えながら円周に沿って管を伸ばす。
  private void AddSweep(List<Vector3> vertices, List<Vector3> normals, List<int> triangles,
    float theta0Degrees, float theta1Degrees, float r0, float r1)
  {
    var arc = theta1Degrees - theta0Degrees;
    if (arc <= 0f)
    {
      return;
    }

    var ringCount = Mathf.Max(1, Mathf.CeilToInt(arc / segmentDegrees));
    var arcLength = ringRadius * arc * Mathf.Deg2Rad;
    // 先細りの分だけ法線を進行方向へ傾ける(円錐の側面の向き)。
    var slope = (r0 - r1) / Mathf.Max(0.0001f, arcLength);
    var baseIndex = vertices.Count;

    for (var k = 0; k <= ringCount; k++)
    {
      var t = (float)k / ringCount;
      var theta = Mathf.Lerp(theta0Degrees, theta1Degrees, t) * Mathf.Deg2Rad;
      var radius = Mathf.Lerp(r0, r1, t);
      var radial = Radial(theta);
      var tangent = Tangent(theta);
      var center = radial * ringRadius;

      for (var s = 0; s < sidesPerRing; s++)
      {
        var phi = 2f * Mathf.PI * s / sidesPerRing;
        var direction = radial * Mathf.Cos(phi) + Vector3.up * Mathf.Sin(phi);
        vertices.Add(center + direction * radius);
        normals.Add((direction + tangent * slope).normalized);
      }
    }

    for (var k = 0; k < ringCount; k++)
    {
      for (var s = 0; s < sidesPerRing; s++)
      {
        var next = (s + 1) % sidesPerRing;
        var a = baseIndex + k * sidesPerRing + s;
        var b = baseIndex + (k + 1) * sidesPerRing + s;
        var c = baseIndex + (k + 1) * sidesPerRing + next;
        var d = baseIndex + k * sidesPerRing + next;
        triangles.Add(a);
        triangles.Add(b);
        triangles.Add(d);
        triangles.Add(b);
        triangles.Add(c);
        triangles.Add(d);
      }
    }
  }

  // θ の位置に、進行方向と逆向きの平らな円板を置く(矢印の尾)。
  private void AddCap(List<Vector3> vertices, List<Vector3> normals, List<int> triangles,
    float thetaDegrees, float radius)
  {
    var theta = thetaDegrees * Mathf.Deg2Rad;
    var radial = Radial(theta);
    var normal = -Tangent(theta);
    var center = radial * ringRadius;
    var centerIndex = vertices.Count;

    vertices.Add(center);
    normals.Add(normal);
    for (var s = 0; s < sidesPerRing; s++)
    {
      var phi = 2f * Mathf.PI * s / sidesPerRing;
      vertices.Add(center + (radial * Mathf.Cos(phi) + Vector3.up * Mathf.Sin(phi)) * radius);
      normals.Add(normal);
    }

    for (var s = 0; s < sidesPerRing; s++)
    {
      triangles.Add(centerIndex);
      triangles.Add(centerIndex + 1 + s);
      triangles.Add(centerIndex + 1 + (s + 1) % sidesPerRing);
    }
  }

  // θ の位置に、進行方向と逆向きのドーナツ状の面を置く(矢じりの裏側の段差)。
  private void AddAnnulus(List<Vector3> vertices, List<Vector3> normals, List<int> triangles,
    float thetaDegrees, float innerRadius, float outerRadius)
  {
    var theta = thetaDegrees * Mathf.Deg2Rad;
    var radial = Radial(theta);
    var normal = -Tangent(theta);
    var center = radial * ringRadius;
    var innerIndex = vertices.Count;
    var outerIndex = innerIndex + sidesPerRing;

    for (var s = 0; s < sidesPerRing; s++)
    {
      var phi = 2f * Mathf.PI * s / sidesPerRing;
      vertices.Add(center + (radial * Mathf.Cos(phi) + Vector3.up * Mathf.Sin(phi)) * innerRadius);
      normals.Add(normal);
    }

    for (var s = 0; s < sidesPerRing; s++)
    {
      var phi = 2f * Mathf.PI * s / sidesPerRing;
      vertices.Add(center + (radial * Mathf.Cos(phi) + Vector3.up * Mathf.Sin(phi)) * outerRadius);
      normals.Add(normal);
    }

    for (var s = 0; s < sidesPerRing; s++)
    {
      var next = (s + 1) % sidesPerRing;
      triangles.Add(innerIndex + s);
      triangles.Add(outerIndex + s);
      triangles.Add(outerIndex + next);
      triangles.Add(innerIndex + s);
      triangles.Add(outerIndex + next);
      triangles.Add(innerIndex + next);
    }
  }
}
