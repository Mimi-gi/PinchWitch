using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

// ステージエディタのパレット。配置できる Prefab と塗れるタイルを登録する。
// 「フォルダから自動登録」で既定のフォルダを走査して中身を作り直せる。
[CreateAssetMenu(menuName = "PinchWitch/Stage Palette", fileName = "StagePalette")]
public sealed class StagePaletteAsset : ScriptableObject
{
    public enum Category
    {
        Enemy,
        Gimmick,
        Block,
    }

    [Serializable]
    public sealed class Entry
    {
        public GameObject prefab;
        public Category category;
    }

    public const string DefaultAssetPath = "Assets/Editor/StageEditor/StagePalette.asset";

    private static readonly (string folder, Category category)[] AutoFillFolders =
    {
        ("Assets/Prefabs/Enemy", Category.Enemy),
        ("Assets/Prefabs/Gimmick", Category.Gimmick),
        ("Assets/Prefabs/Grabbable_Object", Category.Block),
    };

    private static readonly (string path, Category category)[] AutoFillPrefabs =
    {
        ("Assets/Prefabs/Goal.prefab", Category.Gimmick),
        ("Assets/Prefabs/LightBeamEmitter.prefab", Category.Gimmick),
    };

    private const string AutoFillTileFolder = "Assets/Tilemap";

    public List<Entry> entries = new List<Entry>();
    public List<TileBase> tiles = new List<TileBase>();

    public static StagePaletteAsset LoadOrCreateDefault()
    {
        var palette = AssetDatabase.LoadAssetAtPath<StagePaletteAsset>(DefaultAssetPath);
        if (palette != null)
        {
            return palette;
        }

        palette = CreateInstance<StagePaletteAsset>();
        AssetDatabase.CreateAsset(palette, DefaultAssetPath);
        palette.AutoFill();
        return palette;
    }

    public void AutoFill()
    {
        entries.Clear();
        foreach (var (folder, category) in AutoFillFolders)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                continue;
            }

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
            {
                AddEntry(AssetDatabase.GUIDToAssetPath(guid), category);
            }
        }

        foreach (var (path, category) in AutoFillPrefabs)
        {
            AddEntry(path, category);
        }

        tiles.Clear();
        if (AssetDatabase.IsValidFolder(AutoFillTileFolder))
        {
            foreach (var guid in AssetDatabase.FindAssets("t:TileBase", new[] { AutoFillTileFolder }))
            {
                var tile = AssetDatabase.LoadAssetAtPath<TileBase>(AssetDatabase.GUIDToAssetPath(guid));
                if (tile != null)
                {
                    tiles.Add(tile);
                }
            }
        }

        EditorUtility.SetDirty(this);
        AssetDatabase.SaveAssets();
    }

    private void AddEntry(string path, Category category)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null || entries.Exists(entry => entry.prefab == prefab))
        {
            return;
        }

        entries.Add(new Entry { prefab = prefab, category = category });
    }
}
