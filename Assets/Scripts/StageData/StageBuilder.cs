using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

// StageData をシーンへ反映する処理。実行時(StageRuntimeLoader)とエディタ(Stage_Authoring への読み込み)で共有する。
// 生成方法だけが異なるので、Prefab の生成は呼び出し側から渡す。
public static class StageBuilder
{
    public delegate GameObject InstantiateFunc(GameObject prefab, Transform parent);

    public static void ApplyEnvironment(StageData stage, StageSceneAnchors anchors)
    {
        if (anchors.Grid != null)
        {
            anchors.Grid.transform.position = stage.gridPosition;
        }

        if (anchors.Player != null)
        {
            anchors.Player.position = stage.playerStartPosition;
        }

        if (anchors.SpawnPoint != null)
        {
            anchors.SpawnPoint.position = stage.spawnPointPosition;
        }
    }

    public static void ApplyTilemaps(StageData stage, StageSceneAnchors anchors)
    {
        foreach (var layer in stage.tilemaps)
        {
            var tilemap = anchors.FindTilemap(layer.tilemapName);
            if (tilemap == null)
            {
                Debug.LogWarning($"[StageBuilder] Tilemap '{layer.tilemapName}' が見つかりません。", anchors);
                continue;
            }

            tilemap.ClearAllTiles();
            var count = Mathf.Min(layer.positions.Count, layer.tiles.Count);
            if (count == 0)
            {
                continue;
            }

            tilemap.SetTiles(layer.positions.GetRange(0, count).ToArray(), layer.tiles.GetRange(0, count).ToArray());
        }
    }

    /// <summary>
    /// 配置物を 1 つ生成し、Transform と変更値を反映する。parent が非アクティブなら Awake 前に値を入れられる。
    /// </summary>
    public static GameObject Place(StageData.PlacedObject placed, Transform parent, InstantiateFunc instantiate)
    {
        if (placed.prefab == null)
        {
            return null;
        }

        var instance = instantiate(placed.prefab, parent);
        if (instance == null)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(placed.name))
        {
            instance.name = placed.name;
        }

        var instanceTransform = instance.transform;
        instanceTransform.SetPositionAndRotation(placed.position, placed.rotation);
        instanceTransform.localScale = placed.scale;
        ApplyOverrides(instance, placed.overrides);
        instance.SetActive(placed.active);
        return instance;
    }

    public static void ApplyOverrides(GameObject instance, List<StageData.ComponentOverride> overrides)
    {
        if (overrides == null)
        {
            return;
        }

        foreach (var componentOverride in overrides)
        {
            var type = ResolveType(componentOverride.componentType);
            if (type == null)
            {
                Debug.LogWarning($"[StageBuilder] 型 '{componentOverride.componentType}' が見つかりません。", instance);
                continue;
            }

            var components = instance.GetComponents(type);
            Component target;
            if (componentOverride.componentIndex < components.Length)
            {
                target = components[componentOverride.componentIndex];
            }
            else
            {
                // Prefab に無く、シーン上で追加されていたコンポーネント。
                target = instance.AddComponent(type);
            }

            if (target != null && !string.IsNullOrEmpty(componentOverride.json))
            {
                JsonUtility.FromJsonOverwrite(componentOverride.json, target);
            }
        }
    }

    /// <summary>
    /// 実行時の組み立て。非アクティブな仮親の下で生成して値を入れてから、コンテンツルートへ移す(Awake 前に値を反映するため)。
    /// </summary>
    public static void BuildRuntime(StageData stage, StageSceneAnchors anchors)
    {
        ApplyEnvironment(stage, anchors);
        ApplyTilemaps(stage, anchors);

        var staging = new GameObject("StageStaging");
        staging.SetActive(false);
        var contentRoot = anchors.ContentRoot != null ? anchors.ContentRoot : anchors.transform;
        var placedInstances = new List<GameObject>(stage.objects.Count);
        foreach (var placed in stage.objects)
        {
            var instance = Place(placed, staging.transform, (prefab, parent) => Object.Instantiate(prefab, parent));
            if (instance != null)
            {
                placedInstances.Add(instance);
            }
        }

        foreach (var instance in placedInstances)
        {
            instance.transform.SetParent(contentRoot, true);
        }

        Object.Destroy(staging);
    }

    public static string GetTypeName(Type type)
    {
        return $"{type.FullName}, {type.Assembly.GetName().Name}";
    }

    private static Type ResolveType(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
        {
            return null;
        }

        var type = Type.GetType(typeName);
        if (type != null)
        {
            return type;
        }

        // アセンブリ名が変わった場合の保険として、型名だけで探す。
        var shortName = typeName.Split(',')[0].Trim();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = assembly.GetType(shortName);
            if (type != null)
            {
                return type;
            }
        }

        return null;
    }
}
