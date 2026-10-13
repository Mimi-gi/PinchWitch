using UnityEngine;
using UnityEngine.Tilemaps;

// 共通シーン(Stage_Runtime / Stage_Authoring)で、ステージデータの読み書き先になる参照をまとめる。
[DisallowMultipleComponent]
public sealed class StageSceneAnchors : MonoBehaviour
{
    [SerializeField] private Grid grid;
    [SerializeField] private Transform player;
    [SerializeField] private Transform spawnPoint;
    [Tooltip("配置物(敵・ブロック・ゴール等)の親。読み込み時にここへ生成し、保存時はシーン全体から配置物を集める。")]
    [SerializeField] private Transform contentRoot;

    public Grid Grid => grid;
    public Transform Player => player;
    public Transform SpawnPoint => spawnPoint;
    public Transform ContentRoot => contentRoot;

    public void Configure(Grid targetGrid, Transform targetPlayer, Transform targetSpawnPoint, Transform targetContentRoot)
    {
        grid = targetGrid;
        player = targetPlayer;
        spawnPoint = targetSpawnPoint;
        contentRoot = targetContentRoot;
    }

    public Tilemap FindTilemap(string tilemapName)
    {
        if (grid == null)
        {
            return null;
        }

        foreach (var tilemap in grid.GetComponentsInChildren<Tilemap>(true))
        {
            if (tilemap.name == tilemapName)
            {
                return tilemap;
            }
        }

        return null;
    }
}
