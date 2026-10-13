using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

// シーン ⇔ StageData の変換(エディタ専用)。
// - Capture: シーンのタイル・プレイヤー位置・配置物(ステージ用 Prefab のインスタンス)を StageData に書き出す
// - LoadIntoScene: StageData の内容をシーン(Stage_Authoring)に Prefab リンクを保ったまま展開する
// 配置物の「Prefab から変更した値」は JsonUtility で保存する。オブジェクト参照は保存できないので除外する。
public static class StageDataSerializer
{
    // ステージ固有の配置物とみなす Prefab。これ以外(Solution, Audio, Player 等)は共通シーン側の持ち物。
    private static readonly string[] ContentFolders =
    {
        "Assets/Prefabs/Enemy/",
        "Assets/Prefabs/Gimmick/",
        "Assets/Prefabs/Grabbable_Object/",
    };

    private static readonly string[] ContentPrefabs =
    {
        "Assets/Prefabs/Goal.prefab",
        "Assets/Prefabs/LightBeamEmitter.prefab",
    };

    // 共通シーンにある Prefab(変換時に「未対応」として報告しない)。
    private static readonly string[] EnvironmentPrefabs =
    {
        "Assets/Prefabs/Solution.prefab",
        "Assets/Prefabs/Audio.prefab",
        "Assets/Prefabs/Player.prefab",
        "Assets/Prefabs/Grid_2DWorld.prefab",
        "Assets/Prefabs/Background_2DWorld.prefab",
    };

    // 配置物として扱うべきなのに Prefab でないオブジェクトを見つけるための目印。
    private static readonly Type[] GameplayComponentTypes =
    {
        typeof(ShadowColliderBuilder),
        typeof(GoalController),
        typeof(IEnemyMover),
        typeof(StageKey2D),
        typeof(KeyLockedBlock2D),
        typeof(LightBeamEmitter),
    };

    private const string ConvertedBlockFolder = "Assets/Prefabs/Grabbable_Object/Converted";
    private const string ConvertedGimmickFolder = "Assets/Prefabs/Gimmick/Converted";

    private static readonly Regex ObjectReferenceField = new Regex(@"""[^""]+"":\{""instanceID"":-?\d+\}");
    private static readonly Regex ObjectReferenceArrayField = new Regex(@"""[^""]+"":\[(\{""instanceID"":-?\d+\},?)*\]");

    public sealed class SceneRefs
    {
        public Grid grid;
        public Transform player;
        public Transform spawnPoint;
        public Transform contentRoot;
    }

    public static bool IsStageContentPrefab(GameObject prefabAsset)
    {
        var path = AssetDatabase.GetAssetPath(prefabAsset);
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        foreach (var folder in ContentFolders)
        {
            if (path.StartsWith(folder, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return Array.IndexOf(ContentPrefabs, path) >= 0;
    }

    /// <summary>
    /// StageSceneAnchors があればそれを、無ければ既存ステージシーンの構成から推測して参照を集める。
    /// </summary>
    public static SceneRefs ResolveRefs(Scene scene)
    {
        var refs = new SceneRefs();
        foreach (var root in scene.GetRootGameObjects())
        {
            var anchors = root.GetComponentInChildren<StageSceneAnchors>(true);
            if (anchors != null)
            {
                refs.grid = anchors.Grid;
                refs.player = anchors.Player;
                refs.spawnPoint = anchors.SpawnPoint;
                refs.contentRoot = anchors.ContentRoot;
                return refs;
            }
        }

        foreach (var root in scene.GetRootGameObjects())
        {
            if (refs.grid == null)
            {
                refs.grid = root.GetComponentInChildren<Grid>(true);
            }

            if (refs.player == null && root.TryGetComponent<Wall2DPlayerController>(out var playerController))
            {
                refs.player = playerController.transform;
            }

            if (refs.spawnPoint == null && root.TryGetComponent<Wall2DPlayerRespawner>(out var respawner))
            {
                refs.spawnPoint = new SerializedObject(respawner).FindProperty("spawnPoint").objectReferenceValue as Transform;
            }
        }

        return refs;
    }

    public static void Capture(Scene scene, StageData target, List<string> report)
    {
        var refs = ResolveRefs(scene);
        Undo.RecordObject(target, "Save Stage Data");

        if (refs.grid != null) target.gridPosition = refs.grid.transform.position;
        else report.Add("Grid が見つかりません(タイルは保存されません)");
        if (refs.player != null) target.playerStartPosition = refs.player.position;
        else report.Add("Player が見つかりません");
        if (refs.spawnPoint != null) target.spawnPointPosition = refs.spawnPoint.position;

        CaptureTilemaps(refs.grid, target);
        CaptureObjects(scene, target, report);

        EditorUtility.SetDirty(target);
        AssetDatabase.SaveAssetIfDirty(target);
    }

    private static void CaptureTilemaps(Grid grid, StageData target)
    {
        target.tilemaps.Clear();
        if (grid == null)
        {
            return;
        }

        foreach (var tilemap in grid.GetComponentsInChildren<Tilemap>(true))
        {
            tilemap.CompressBounds();
            var layer = new StageData.TilemapLayer { tilemapName = tilemap.name };
            foreach (var position in tilemap.cellBounds.allPositionsWithin)
            {
                var tile = tilemap.GetTile(position);
                if (tile != null)
                {
                    layer.positions.Add(position);
                    layer.tiles.Add(tile);
                }
            }

            target.tilemaps.Add(layer);
        }
    }

    private static void CaptureObjects(Scene scene, StageData target, List<string> report)
    {
        target.objects.Clear();
        foreach (var root in scene.GetRootGameObjects())
        {
            CaptureRecursive(root.transform, scene, target, report);
        }
    }

    private static void CaptureRecursive(Transform transform, Scene scene, StageData target, List<string> report)
    {
        var gameObject = transform.gameObject;
        if (PrefabUtility.IsOutermostPrefabInstanceRoot(gameObject))
        {
            CaptureIfContent(gameObject, target, report);
            return;
        }

        if (!PrefabUtility.IsPartOfPrefabInstance(gameObject) && gameObject.activeInHierarchy && HasGameplayComponent(gameObject))
        {
            // シーン上で手作りされた配置物(ブロック等)は、Prefab として書き出してから参照する。元のシーンは変更しない。
            var prefab = SaveAsConvertedPrefab(gameObject, scene);
            if (prefab != null)
            {
                target.objects.Add(CreatePlacedObject(gameObject, prefab));
                report.Add($"Prefab 化: '{GetPath(transform)}' → {AssetDatabase.GetAssetPath(prefab)}");
            }

            return;
        }

        foreach (Transform child in transform)
        {
            CaptureRecursive(child, scene, target, report);
        }
    }

    private static GameObject SaveAsConvertedPrefab(GameObject source, Scene scene)
    {
        var folder = source.GetComponent<ShadowColliderBuilder>() != null ? ConvertedBlockFolder : ConvertedGimmickFolder;
        GrabbableBlockFactory.EnsureFolder(folder);
        var path = $"{folder}/{scene.name}_{source.name}.prefab";

        // SaveAsPrefabAsset は元オブジェクトを Prefab に接続しないので、シーン側は変わらない。
        var prefab = PrefabUtility.SaveAsPrefabAsset(source, path, out var success);
        if (!success)
        {
            Debug.LogError($"Prefab 化に失敗しました: {path}", source);
            return null;
        }

        return prefab;
    }

    private static void CaptureIfContent(GameObject instance, StageData target, List<string> report)
    {
        var prefab = PrefabUtility.GetCorrespondingObjectFromSource(instance);
        if (prefab == null)
        {
            return;
        }

        if (!IsStageContentPrefab(prefab))
        {
            var path = AssetDatabase.GetAssetPath(prefab);
            if (Array.IndexOf(EnvironmentPrefabs, path) < 0 && path.StartsWith("Assets/Prefabs/", StringComparison.Ordinal))
            {
                report.Add($"未対応: '{instance.name}' ({path}) はステージ用 Prefab ではないため保存されません");
            }

            return;
        }

        var placed = CreatePlacedObject(instance, prefab);
        CaptureOverrides(instance, placed.overrides);
        target.objects.Add(placed);
    }

    private static StageData.PlacedObject CreatePlacedObject(GameObject instance, GameObject prefab)
    {
        var transform = instance.transform;
        return new StageData.PlacedObject
        {
            prefab = prefab,
            name = instance.name,
            position = transform.position,
            rotation = transform.rotation,
            // 生成先(コンテンツルート)は原点・等倍なので、ワールドの拡大率をそのまま使う。
            scale = transform.lossyScale,
            active = instance.activeSelf,
        };
    }

    private static void CaptureOverrides(GameObject instance, List<StageData.ComponentOverride> overrides)
    {
        var typeCounts = new Dictionary<Type, int>();
        foreach (var component in instance.GetComponents<MonoBehaviour>())
        {
            if (component == null)
            {
                continue;
            }

            var type = component.GetType();
            typeCounts.TryGetValue(type, out var index);
            typeCounts[type] = index + 1;

            var json = StripObjectReferences(JsonUtility.ToJson(component));
            var source = PrefabUtility.GetCorrespondingObjectFromSource(component);
            if (source != null && StripObjectReferences(JsonUtility.ToJson(source)) == json)
            {
                continue;
            }

            overrides.Add(new StageData.ComponentOverride
            {
                componentType = StageBuilder.GetTypeName(type),
                componentIndex = index,
                json = json,
            });
        }
    }

    public static void LoadIntoScene(StageData stage, Scene scene)
    {
        var refs = ResolveRefs(scene);
        var anchors = FindAnchors(scene);
        if (anchors == null)
        {
            Debug.LogError("StageSceneAnchors がシーンにありません。Stage_Authoring シーンで実行してください。");
            return;
        }

        Undo.SetCurrentGroupName($"Load Stage {stage.name}");
        var undoGroup = Undo.GetCurrentGroup();

        ClearContent(scene);

        if (refs.grid != null) Undo.RecordObject(refs.grid.transform, "Load Stage");
        if (refs.player != null) Undo.RecordObject(refs.player, "Load Stage");
        if (refs.spawnPoint != null) Undo.RecordObject(refs.spawnPoint, "Load Stage");
        StageBuilder.ApplyEnvironment(stage, anchors);

        if (refs.grid != null)
        {
            foreach (var tilemap in refs.grid.GetComponentsInChildren<Tilemap>(true))
            {
                Undo.RegisterCompleteObjectUndo(tilemap, "Load Stage");
                tilemap.ClearAllTiles();
            }
        }

        StageBuilder.ApplyTilemaps(stage, anchors);

        var contentRoot = anchors.ContentRoot != null ? anchors.ContentRoot : anchors.transform;
        foreach (var placed in stage.objects)
        {
            var instance = StageBuilder.Place(placed, contentRoot, InstantiateLinked);
            if (instance == null)
            {
                continue;
            }

            foreach (var component in instance.GetComponents<MonoBehaviour>())
            {
                if (component != null)
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                }
            }
        }

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    /// <summary>
    /// シーン上のステージ用配置物をすべて削除する(Undo 可)。
    /// </summary>
    public static void ClearContent(Scene scene)
    {
        var toDelete = new List<GameObject>();
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                var gameObject = transform.gameObject;
                if (!PrefabUtility.IsOutermostPrefabInstanceRoot(gameObject))
                {
                    continue;
                }

                var prefab = PrefabUtility.GetCorrespondingObjectFromSource(gameObject);
                if (prefab != null && IsStageContentPrefab(prefab))
                {
                    toDelete.Add(gameObject);
                }
            }
        }

        foreach (var gameObject in toDelete)
        {
            if (gameObject != null)
            {
                Undo.DestroyObjectImmediate(gameObject);
            }
        }

        RemoveOrphanedShadowOutputs(scene);
    }

    /// <summary>
    /// 持ち主のブロックが無くなった影足場(ShadowColliderOutput)を削除する。残すと見えない足場になる。
    /// </summary>
    public static void RemoveOrphanedShadowOutputs(Scene scene)
    {
        var orphans = new List<GameObject>();
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var output in root.GetComponentsInChildren<ShadowColliderOutput>(true))
            {
                if (!output.HasOwner)
                {
                    orphans.Add(output.gameObject);
                }
            }
        }

        foreach (var orphan in orphans)
        {
            if (orphan != null)
            {
                Undo.DestroyObjectImmediate(orphan);
            }
        }
    }

    public static StageSceneAnchors FindAnchors(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var anchors = root.GetComponentInChildren<StageSceneAnchors>(true);
            if (anchors != null)
            {
                return anchors;
            }
        }

        return null;
    }

    private static GameObject InstantiateLinked(GameObject prefab, Transform parent)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        Undo.RegisterCreatedObjectUndo(instance, "Load Stage");
        return instance;
    }

    /// <summary>
    /// JsonUtility の出力からオブジェクト参照({"instanceID":n})のフィールドを取り除く。
    /// 参照はセッションをまたいで復元できないため、Prefab 側の参照をそのまま使わせる。
    /// </summary>
    public static string StripObjectReferences(string json)
    {
        json = ObjectReferenceArrayField.Replace(json, string.Empty);
        json = ObjectReferenceField.Replace(json, string.Empty);

        // 取り除いた跡のカンマを整える。
        string previous;
        do
        {
            previous = json;
            json = json.Replace(",,", ",").Replace("{,", "{").Replace(",}", "}").Replace("[,", "[").Replace(",]", "]");
        }
        while (json != previous);

        return json;
    }

    private static bool HasGameplayComponent(GameObject gameObject)
    {
        foreach (var type in GameplayComponentTypes)
        {
            if (gameObject.GetComponent(type) != null)
            {
                return true;
            }
        }

        return false;
    }

    private static string GetPath(Transform transform)
    {
        var path = transform.name;
        for (var parent = transform.parent; parent != null; parent = parent.parent)
        {
            path = $"{parent.name}/{path}";
        }

        return path;
    }
}
