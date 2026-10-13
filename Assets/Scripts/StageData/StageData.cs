using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

// 1 ステージ分の内容。共通シーン(Stage_Runtime)が実行時にこれを読み込んでステージを組み立てる。
// 作成は Stage_Authoring シーン + ステージエディタの「ステージデータ」タブで行う。
[CreateAssetMenu(menuName = "PinchWitch/Stage Data", fileName = "Stage_")]
public sealed class StageData : ScriptableObject
{
    [Serializable]
    public sealed class TilemapLayer
    {
        public string tilemapName;
        public List<Vector3Int> positions = new List<Vector3Int>();
        public List<TileBase> tiles = new List<TileBase>();
    }

    // Prefab 上の値から変更されたコンポーネントの値(JsonUtility 形式。オブジェクト参照は含まない)。
    [Serializable]
    public sealed class ComponentOverride
    {
        public string componentType;
        public int componentIndex;
        public string json;
    }

    [Serializable]
    public sealed class PlacedObject
    {
        public GameObject prefab;
        public string name;
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;
        public Vector3 scale = Vector3.one;
        public bool active = true;
        public List<ComponentOverride> overrides = new List<ComponentOverride>();
    }

    public string displayName;

    [Header("Environment")]
    public Vector3 gridPosition = new Vector3(0f, 0f, 5f);
    public Vector3 playerStartPosition = new Vector3(-8.4f, 2f, 5f);
    public Vector3 spawnPointPosition = new Vector3(-4f, 7.51f, 4.9f);

    [Header("Content")]
    public List<TilemapLayer> tilemaps = new List<TilemapLayer>();
    public List<PlacedObject> objects = new List<PlacedObject>();
}
