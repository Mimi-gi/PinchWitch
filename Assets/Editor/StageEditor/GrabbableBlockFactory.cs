using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public enum BlockCellType
{
    Empty,
    Solid,
    Glass,
}

// 正面から見たマス目(左下が原点)で掴めるブロックの形を表す。
[Serializable]
public sealed class GrabbableBlockLayout
{
    public int width = 1;
    public int height = 2;
    public float cellSize = 1.2f;
    public float depth = 1.2f;
    public BlockCellType[] cells = { BlockCellType.Solid, BlockCellType.Solid };

    public BlockCellType Get(int x, int y)
    {
        var index = y * width + x;
        return cells != null && index >= 0 && index < cells.Length ? cells[index] : BlockCellType.Empty;
    }

    public void Set(int x, int y, BlockCellType type)
    {
        EnsureSize();
        cells[y * width + x] = type;
    }

    public void EnsureSize()
    {
        width = Mathf.Max(1, width);
        height = Mathf.Max(1, height);
        if (cells != null && cells.Length == width * height)
        {
            return;
        }

        var resized = new BlockCellType[width * height];
        if (cells != null)
        {
            Array.Copy(cells, resized, Mathf.Min(cells.Length, resized.Length));
        }

        cells = resized;
    }

    public bool HasAnyCell(BlockCellType type)
    {
        if (cells == null)
        {
            return false;
        }

        foreach (var cell in cells)
        {
            if (cell == type)
            {
                return true;
            }
        }

        return false;
    }
}

// 既存の掴めるブロック Prefab を雛形に、マス目レイアウトから複合ブロック(通常+ガラス)の Prefab を生成する。
// 雛形から Rigidbody / ShadowColliderBuilder / ShadowOnlyProxy / ShadowDirtyTarget の設定を引き継ぎ、
// 見た目とコライダーをマスごとの子キューブに置き換える。ガラスのマスはプロキシに複製しないので影足場にならない。
public static class GrabbableBlockFactory
{
    public const string TemplatePrefabPath = "Assets/Prefabs/Grabbable_Object/Grabbable_2Block_Tate.prefab";
    public const string DefaultOutputFolder = "Assets/Prefabs/Grabbable_Object/Custom";
    public const string SolidMaterialPath = "Assets/Materials/Block_Solid.mat";
    public const string GlassMaterialPath = "Assets/Materials/Block_Glass.mat";

    private const string GrabbableLayerName = "Grabbable";
    private const string GlassLayerName = "Glass";
    private const string CellNamePrefix = "Cell_";

    public static GameObject CreatePrefab(GrabbableBlockLayout layout, string assetPath, Material solidMaterial = null, Material glassMaterial = null)
    {
        if (layout == null)
        {
            throw new ArgumentNullException(nameof(layout));
        }

        layout.EnsureSize();
        if (!layout.HasAnyCell(BlockCellType.Solid) && !layout.HasAnyCell(BlockCellType.Glass))
        {
            Debug.LogWarning("GrabbableBlockFactory: レイアウトにマスがありません。");
            return null;
        }

        var template = AssetDatabase.LoadAssetAtPath<GameObject>(TemplatePrefabPath);
        if (template == null)
        {
            Debug.LogError($"GrabbableBlockFactory: 雛形 Prefab が見つかりません: {TemplatePrefabPath}");
            return null;
        }

        solidMaterial = solidMaterial != null ? solidMaterial : EnsureSolidMaterial();
        glassMaterial = glassMaterial != null ? glassMaterial : EnsureGlassMaterial();

        var root = UnityEngine.Object.Instantiate(template);
        try
        {
            root.name = Path.GetFileNameWithoutExtension(assetPath);
            var builder = root.GetComponent<ShadowColliderBuilder>();
            var proxy = root.GetComponentInChildren<ShadowOnlyProxy>(true);
            if (builder == null || proxy == null)
            {
                Debug.LogError("GrabbableBlockFactory: 雛形に ShadowColliderBuilder / ShadowOnlyProxy がありません。");
                return null;
            }

            StripTemplateVisuals(root, builder, proxy);
            proxy.gameObject.name = $"{root.name}_ShadowProxy";
            root.transform.localScale = Vector3.one;
            proxy.transform.localScale = Vector3.one;

            // 雛形と同じく、底面が床(y=0)に接する高さに置く。
            var rootPosition = root.transform.localPosition;
            root.transform.localPosition = new Vector3(rootPosition.x, layout.height * layout.cellSize * 0.5f, rootPosition.z);

            BuildCells(layout, root.transform, proxy.transform, solidMaterial, glassMaterial);

            EnsureFolder(Path.GetDirectoryName(assetPath)?.Replace('\\', '/'));
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, assetPath, out var success);
            if (!success)
            {
                Debug.LogError($"GrabbableBlockFactory: Prefab の保存に失敗しました: {assetPath}");
                return null;
            }

            return prefab;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void StripTemplateVisuals(GameObject root, ShadowColliderBuilder builder, ShadowOnlyProxy proxy)
    {
        // 雛形の装飾モデル(子)を削除する。プロキシだけ残す。
        for (var i = root.transform.childCount - 1; i >= 0; i--)
        {
            var child = root.transform.GetChild(i);
            if (child != proxy.transform)
            {
                UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
        }

        foreach (Transform proxyChild in proxy.transform)
        {
            UnityEngine.Object.DestroyImmediate(proxyChild.gameObject);
        }

        // ルートと プロキシ ルートの単体キューブは使わない(形はマスの子キューブが担う)。
        DestroyIfExists<Collider>(root);
        DestroyIfExists<MeshRenderer>(root);
        DestroyIfExists<MeshFilter>(root);
        DestroyIfExists<MeshRenderer>(proxy.gameObject);
        DestroyIfExists<MeshFilter>(proxy.gameObject);

        var serializedBuilder = new SerializedObject(builder);
        serializedBuilder.FindProperty("targetCube").objectReferenceValue = null;
        serializedBuilder.FindProperty("sourceRoot").objectReferenceValue = root.transform;
        serializedBuilder.FindProperty("shadowProxyRoot").objectReferenceValue = proxy.transform;
        serializedBuilder.FindProperty("shadowOnlyProxy").objectReferenceValue = proxy;
        serializedBuilder.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void BuildCells(GrabbableBlockLayout layout, Transform root, Transform proxyRoot, Material solidMaterial, Material glassMaterial)
    {
        var cubeMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        var grabbableLayer = LayerMask.NameToLayer(GrabbableLayerName);
        var glassLayer = LayerMask.NameToLayer(GlassLayerName);
        var cellScale = new Vector3(layout.cellSize, layout.cellSize, layout.depth);

        // ルート原点がブロック全体の中心になるようにずらす。
        var origin = new Vector2(
            -(layout.width - 1) * layout.cellSize * 0.5f,
            -(layout.height - 1) * layout.cellSize * 0.5f);

        for (var y = 0; y < layout.height; y++)
        {
            for (var x = 0; x < layout.width; x++)
            {
                var type = layout.Get(x, y);
                if (type == BlockCellType.Empty)
                {
                    continue;
                }

                var localPosition = new Vector3(origin.x + x * layout.cellSize, origin.y + y * layout.cellSize, 0f);
                var isGlass = type == BlockCellType.Glass;
                var cellName = $"{CellNamePrefix}{x}_{y}_{type}";

                var cell = CreateCube(cellName, root, localPosition, cellScale, cubeMesh, isGlass ? glassMaterial : solidMaterial);
                cell.layer = isGlass ? glassLayer : grabbableLayer;
                cell.AddComponent<BoxCollider>();

                var cellRenderer = cell.GetComponent<MeshRenderer>();
                if (isGlass)
                {
                    cell.AddComponent<ShadowExcludedPart>();
                    cellRenderer.shadowCastingMode = ShadowCastingMode.Off;
                    continue;
                }

                // 通常マスだけプロキシに複製する(名前一致で ShadowOnlyProxy が位置同期する)。
                var proxyCell = CreateCube(cellName, proxyRoot, localPosition, cellScale, cubeMesh, solidMaterial);
                proxyCell.layer = proxyRoot.gameObject.layer;
                var proxyRenderer = proxyCell.GetComponent<MeshRenderer>();
                proxyRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                proxyRenderer.receiveShadows = false;
            }
        }
    }

    private static GameObject CreateCube(string name, Transform parent, Vector3 localPosition, Vector3 localScale, Mesh mesh, Material material)
    {
        var cube = new GameObject(name);
        cube.transform.SetParent(parent, false);
        cube.transform.localPosition = localPosition;
        cube.transform.localRotation = Quaternion.identity;
        cube.transform.localScale = localScale;
        cube.AddComponent<MeshFilter>().sharedMesh = mesh;
        cube.AddComponent<MeshRenderer>().sharedMaterial = material;
        return cube;
    }

    private static void DestroyIfExists<T>(GameObject target) where T : Component
    {
        var component = target.GetComponent<T>();
        if (component != null)
        {
            UnityEngine.Object.DestroyImmediate(component);
        }
    }

    public static Material EnsureSolidMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(SolidMaterialPath);
        if (material != null)
        {
            return material;
        }

        material = new Material(FindLitShader());
        material.SetColor("_BaseColor", new Color(0.85f, 0.82f, 0.78f, 1f));
        return CreateMaterialAsset(material, SolidMaterialPath);
    }

    public static Material EnsureGlassMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(GlassMaterialPath);
        if (material != null)
        {
            return material;
        }

        material = new Material(FindLitShader());
        material.SetColor("_BaseColor", new Color(0.75f, 0.92f, 1f, 0.25f));
        material.SetFloat("_Smoothness", 0.95f);

        // URP Lit を半透明(Alpha ブレンド)に設定する。
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = (int)RenderQueue.Transparent;
        material.SetShaderPassEnabled("ShadowCaster", false);
        return CreateMaterialAsset(material, GlassMaterialPath);
    }

    private static Shader FindLitShader()
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        return shader != null ? shader : Shader.Find("Standard");
    }

    private static Material CreateMaterialAsset(Material material, string path)
    {
        EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));
        AssetDatabase.CreateAsset(material, path);
        AssetDatabase.SaveAssets();
        return material;
    }

    public static void EnsureFolder(string folder)
    {
        if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
        {
            return;
        }

        var parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }

    [MenuItem("Tools/PinchWitch/Blocks/Generate Glass Block Presets")]
    private static void GenerateGlassPresets()
    {
        // 縦2マスすべてガラス
        var glass = new GrabbableBlockLayout { width = 1, height = 2, cells = new[] { BlockCellType.Glass, BlockCellType.Glass } };
        // 縦2マス: 下が通常、上がガラス
        var halfGlass = new GrabbableBlockLayout { width = 1, height = 2, cells = new[] { BlockCellType.Solid, BlockCellType.Glass } };
        // 横2マス: 左が通常、右がガラス
        var halfGlassYoko = new GrabbableBlockLayout { width = 2, height = 1, cells = new[] { BlockCellType.Solid, BlockCellType.Glass } };

        CreatePrefab(glass, $"{DefaultOutputFolder}/Grabbable_Glass.prefab");
        CreatePrefab(halfGlass, $"{DefaultOutputFolder}/Grabbable_HalfGlass.prefab");
        CreatePrefab(halfGlassYoko, $"{DefaultOutputFolder}/Grabbable_HalfGlass_Yoko.prefab");
        AssetDatabase.SaveAssets();
        Debug.Log($"ガラスブロックのプリセットを生成しました: {DefaultOutputFolder}");
    }
}
