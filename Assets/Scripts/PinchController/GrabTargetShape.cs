using UnityEngine;
using UnityEngine.Rendering;

// 掴み対象を構成する立方体(見た目の箱)の一覧。
// 回転方向表示(GrabRotationAxisIndicator)の大きさ合わせと、矢印が立方体に近い部分の Dissolve 判定に使う。
//
// ブロックは 1 マスぶんのモデル(box_spade 等)や Cell_* の立方体が並んでできているので、
// 表示されている MeshRenderer それぞれのメッシュ境界を「向き付きの箱」として扱う。
// 影専用(ShadowsOnly)の代理メッシュや厚みの無い板(影用の Plane)は除く。
public sealed class GrabTargetShape
{
  // シェーダーへ渡せる箱の最大数。シェーダー側の MAX_BOXES と合わせる。
  public const int MaxBoxes = 16;

  // これより薄い辺を持つメッシュは立方体ではない(影用の板など)とみなす。
  private const float MinBoxThickness = 0.01f;

  // ワールド座標から各箱の中心・向き基準の座標(拡大なし)へ変換する行列。
  public readonly Matrix4x4[] WorldToBox = new Matrix4x4[MaxBoxes];
  // 各箱の半分の大きさ(ワールド単位、xyz のみ使う)。
  public readonly Vector4[] HalfExtents = new Vector4[MaxBoxes];

  public int BoxCount { get; private set; }
  // 表示されている箱全体の中心。
  public Vector3 Center { get; private set; }
  // 構成する立方体 1 マスの大きさ(最も小さい箱の最大辺)。箱が無ければ 0。
  public float CubeSize { get; private set; }

  private Transform _target;
  private MeshRenderer[] _renderers = new MeshRenderer[0];

  // 対象の現在の姿勢で一覧を作り直す。箱が 1 つも無ければ false。
  public bool Update(Transform target)
  {
    if (target != _target)
    {
      _target = target;
      _renderers = target != null ? target.GetComponentsInChildren<MeshRenderer>(true) : new MeshRenderer[0];
    }

    BoxCount = 0;
    CubeSize = 0f;
    Center = target != null ? target.position : Vector3.zero;
    if (target == null)
    {
      return false;
    }

    var hasBounds = false;
    var worldBounds = default(Bounds);
    var smallestCube = float.MaxValue;

    foreach (var meshRenderer in _renderers)
    {
      if (!TryGetBox(meshRenderer, out var mesh))
      {
        continue;
      }

      var boxTransform = meshRenderer.transform;
      var meshBounds = mesh.bounds;
      var lossyScale = boxTransform.lossyScale;
      var halfExtents = new Vector3(
        Mathf.Abs(meshBounds.extents.x * lossyScale.x),
        Mathf.Abs(meshBounds.extents.y * lossyScale.y),
        Mathf.Abs(meshBounds.extents.z * lossyScale.z));
      if (Mathf.Min(halfExtents.x, Mathf.Min(halfExtents.y, halfExtents.z)) * 2f < MinBoxThickness)
      {
        continue;
      }

      var worldCenter = boxTransform.TransformPoint(meshBounds.center);
      if (BoxCount < MaxBoxes)
      {
        WorldToBox[BoxCount] = Matrix4x4.TRS(worldCenter, boxTransform.rotation, Vector3.one).inverse;
        HalfExtents[BoxCount] = halfExtents;
        BoxCount++;
      }

      smallestCube = Mathf.Min(smallestCube, Mathf.Max(halfExtents.x, Mathf.Max(halfExtents.y, halfExtents.z)) * 2f);

      if (hasBounds)
      {
        worldBounds.Encapsulate(meshRenderer.bounds);
      }
      else
      {
        worldBounds = meshRenderer.bounds;
        hasBounds = true;
      }
    }

    if (!hasBounds)
    {
      return false;
    }

    Center = worldBounds.center;
    CubeSize = smallestCube;
    return BoxCount > 0;
  }

  private static bool TryGetBox(MeshRenderer meshRenderer, out Mesh mesh)
  {
    mesh = null;
    if (meshRenderer == null
        || !meshRenderer.enabled
        || !meshRenderer.gameObject.activeInHierarchy
        || meshRenderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly)
    {
      return false;
    }

    var meshFilter = meshRenderer.GetComponent<MeshFilter>();
    mesh = meshFilter != null ? meshFilter.sharedMesh : null;
    return mesh != null;
  }
}
