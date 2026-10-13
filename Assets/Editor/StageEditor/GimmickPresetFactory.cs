using UnityEditor;
using UnityEngine;

// 新しい敵・鍵ギミックの Prefab を既存の敵 Prefab から生成する。
// 生成後は Prefab を直接編集して見た目や数値を調整してよい(再生成すると上書きされる)。
public static class GimmickPresetFactory
{
    public const string EnemyTemplatePath = "Assets/Prefabs/Enemy/Enemy_MovingLeftRight.prefab";
    public const string EnemyFolder = "Assets/Prefabs/Enemy";
    public const string GimmickFolder = "Assets/Prefabs/Gimmick";

    private const string TrumpLayerName = "trump";
    private const string World2DLayerName = "World2D";

    [MenuItem("Tools/PinchWitch/Gimmicks/Generate Enemy And Key Presets")]
    public static void GenerateAll()
    {
        GrabbableBlockFactory.EnsureFolder(EnemyFolder);
        GrabbableBlockFactory.EnsureFolder(GimmickFolder);

        CreateWalker("Enemy_Trump_Red", EnemyWalker2D.LedgeBehavior.TurnAtLedge, new Color(1f, 0.55f, 0.55f, 1f));
        CreateWalker("Enemy_Trump_Green", EnemyWalker2D.LedgeBehavior.FallOff, new Color(0.6f, 1f, 0.6f, 1f));
        CreateGhost();
        CreatePathEnemy();
        CreateKey();
        CreateKeyLockedBlock();

        AssetDatabase.SaveAssets();
        Debug.Log($"敵・鍵の Prefab を生成しました: {EnemyFolder}, {GimmickFolder}");
    }

    private static void CreateWalker(string prefabName, EnemyWalker2D.LedgeBehavior ledge, Color tint)
    {
        var enemy = InstantiateEnemyTemplate(prefabName);
        if (enemy == null)
        {
            return;
        }

        try
        {
            RemoveComponent<EnemyPatrolMover2D>(enemy);
            SetLayerRecursively(enemy, LayerMask.NameToLayer(TrumpLayerName));
            TintSprites(enemy, tint);

            var body = enemy.GetComponent<Rigidbody2D>();
            if (body == null)
            {
                body = enemy.AddComponent<Rigidbody2D>();
            }

            body.bodyType = RigidbodyType2D.Kinematic;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.freezeRotation = true;

            var walker = enemy.AddComponent<EnemyWalker2D>();
            var serializedWalker = new SerializedObject(walker);
            serializedWalker.FindProperty("ledgeBehavior").enumValueIndex = (int)ledge;
            serializedWalker.ApplyModifiedPropertiesWithoutUndo();

            EnsureStun(enemy);
            Save(enemy, $"{EnemyFolder}/{prefabName}.prefab");
        }
        finally
        {
            Object.DestroyImmediate(enemy);
        }
    }

    private static void CreateGhost()
    {
        const string prefabName = "Enemy_Ghost";
        var enemy = InstantiateEnemyTemplate(prefabName);
        if (enemy == null)
        {
            return;
        }

        try
        {
            // 巡回敵はもともと地形をすり抜けるので、見た目(半透明)だけ変えて区別する。
            TintSprites(enemy, new Color(0.8f, 0.85f, 1f, 0.5f));
            EnsureStun(enemy);
            Save(enemy, $"{EnemyFolder}/{prefabName}.prefab");
        }
        finally
        {
            Object.DestroyImmediate(enemy);
        }
    }

    private static void CreatePathEnemy()
    {
        const string prefabName = "Enemy_Path";
        var enemy = InstantiateEnemyTemplate(prefabName);
        if (enemy == null)
        {
            return;
        }

        try
        {
            RemoveComponent<EnemyPatrolMover2D>(enemy);

            var line = enemy.AddComponent<LineRenderer>();
            line.sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
            line.startWidth = 0.05f;
            line.endWidth = 0.05f;
            line.startColor = new Color(1f, 1f, 1f, 0.6f);
            line.endColor = new Color(1f, 1f, 1f, 0.6f);
            line.useWorldSpace = true;
            CopySorting(enemy.GetComponentInChildren<SpriteRenderer>(), line, -1);

            var mover = enemy.AddComponent<EnemyPathMover2D>();
            var serializedMover = new SerializedObject(mover);
            serializedMover.FindProperty("pathLine").objectReferenceValue = line;
            serializedMover.ApplyModifiedPropertiesWithoutUndo();

            EnsureStun(enemy);
            Save(enemy, $"{EnemyFolder}/{prefabName}.prefab");
        }
        finally
        {
            Object.DestroyImmediate(enemy);
        }
    }

    private static void CreateKey()
    {
        var key = new GameObject("Key");
        try
        {
            key.layer = LayerMask.NameToLayer(World2DLayerName);

            var spriteObject = new GameObject("Sprite");
            spriteObject.layer = key.layer;
            spriteObject.transform.SetParent(key.transform, false);
            var spriteRenderer = spriteObject.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            spriteRenderer.color = new Color(1f, 0.85f, 0.2f, 1f);
            spriteObject.transform.localScale = Vector3.one * 2.5f;
            CopySorting(LoadTemplateSprite(), spriteRenderer, 0);

            var trigger = key.AddComponent<CircleCollider2D>();
            trigger.isTrigger = true;
            trigger.radius = 0.4f;

            key.AddComponent<StageKey2D>();
            Save(key, $"{GimmickFolder}/Key.prefab");
        }
        finally
        {
            Object.DestroyImmediate(key);
        }
    }

    private static void CreateKeyLockedBlock()
    {
        var block = new GameObject("KeyLockedBlock");
        try
        {
            block.layer = LayerMask.NameToLayer(World2DLayerName);

            // 見た目は子に置き、スプライトの大きさに関わらず 1x1 ユニットに合わせる(ルートの当たり判定は 1x1)。
            var spriteObject = new GameObject("Sprite");
            spriteObject.layer = block.layer;
            spriteObject.transform.SetParent(block.transform, false);
            var spriteRenderer = spriteObject.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            spriteRenderer.color = new Color(0.95f, 0.75f, 0.25f, 1f);
            var spriteSize = spriteRenderer.sprite != null ? (Vector2)spriteRenderer.sprite.bounds.size : Vector2.one;
            spriteObject.transform.localScale = new Vector3(1f / Mathf.Max(0.0001f, spriteSize.x), 1f / Mathf.Max(0.0001f, spriteSize.y), 1f);
            CopySorting(LoadTemplateSprite(), spriteRenderer, 0);

            var blockCollider = block.AddComponent<BoxCollider2D>();
            blockCollider.size = Vector2.one;
            blockCollider.isTrigger = false;

            block.AddComponent<KeyLockedBlock2D>();
            Save(block, $"{GimmickFolder}/KeyLockedBlock.prefab");
        }
        finally
        {
            Object.DestroyImmediate(block);
        }
    }

    private static GameObject InstantiateEnemyTemplate(string name)
    {
        var template = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyTemplatePath);
        if (template == null)
        {
            Debug.LogError($"GimmickPresetFactory: 敵の雛形が見つかりません: {EnemyTemplatePath}");
            return null;
        }

        var instance = Object.Instantiate(template);
        instance.name = name;
        return instance;
    }

    private static SpriteRenderer LoadTemplateSprite()
    {
        var template = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyTemplatePath);
        return template != null ? template.GetComponentInChildren<SpriteRenderer>() : null;
    }

    private static void EnsureStun(GameObject enemy)
    {
        if (enemy.GetComponent<EnemyStun2D>() == null)
        {
            enemy.AddComponent<EnemyStun2D>();
        }
    }

    private static void TintSprites(GameObject root, Color tint)
    {
        foreach (var spriteRenderer in root.GetComponentsInChildren<SpriteRenderer>(true))
        {
            spriteRenderer.color = tint;
        }
    }

    private static void CopySorting(SpriteRenderer source, Renderer destination, int orderOffset)
    {
        if (source == null || destination == null)
        {
            return;
        }

        destination.sortingLayerID = source.sortingLayerID;
        destination.sortingOrder = source.sortingOrder + orderOffset;
    }

    private static void RemoveComponent<T>(GameObject target) where T : Component
    {
        var component = target.GetComponent<T>();
        if (component != null)
        {
            Object.DestroyImmediate(component);
        }
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        if (layer < 0)
        {
            return;
        }

        foreach (var child in root.GetComponentsInChildren<Transform>(true))
        {
            child.gameObject.layer = layer;
        }
    }

    private static void Save(GameObject root, string path)
    {
        PrefabUtility.SaveAsPrefabAsset(root, path, out var success);
        if (!success)
        {
            Debug.LogError($"GimmickPresetFactory: Prefab の保存に失敗しました: {path}");
        }
    }
}
