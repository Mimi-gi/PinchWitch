using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

// ステージデータ用のシーン作成と、既存ステージシーンの一括変換。
public static class StageSceneSetup
{
    public const string TemplateScenePath = "Assets/Scenes/Stage_10.unity";
    public const string RuntimeScenePath = "Assets/Scenes/Stage_Runtime.unity";
    public const string AuthoringScenePath = "Assets/Scenes/Stage_Authoring.unity";
    public const string StageDataFolder = "Assets/StageData";
    public const string CatalogPath = "Assets/Resources/StageCatalog.asset";

    private const string ContentRootName = "Stage";
    private const string SystemObjectName = "StageSystem";

    // 既存ステージのうち SO に変換する範囲(stage_1 はチュートリアル専用の仕組みがあるのでシーンのまま)。
    public const int ConvertFirstStage = 2;
    public const int ConvertLastStage = 10;

    public static StageCatalog LoadOrCreateCatalog()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<StageCatalog>(CatalogPath);
        if (catalog != null)
        {
            return catalog;
        }

        GrabbableBlockFactory.EnsureFolder(Path.GetDirectoryName(CatalogPath)?.Replace('\\', '/'));
        catalog = ScriptableObject.CreateInstance<StageCatalog>();
        AssetDatabase.CreateAsset(catalog, CatalogPath);
        return catalog;
    }

    public static StageData LoadOrCreateStageData(string path)
    {
        var stage = AssetDatabase.LoadAssetAtPath<StageData>(path);
        if (stage != null)
        {
            return stage;
        }

        GrabbableBlockFactory.EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));
        stage = ScriptableObject.CreateInstance<StageData>();
        AssetDatabase.CreateAsset(stage, path);
        return stage;
    }

    /// <summary>
    /// Stage_10 を雛形に、ステージ固有の配置物とタイルを取り除いた共通シーンを作る。
    /// </summary>
    [MenuItem("Tools/PinchWitch/Stage Data/Create Runtime And Authoring Scenes")]
    public static void CreateStageScenes()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        CreateSceneFromTemplate(RuntimeScenePath, isRuntime: true);
        CreateSceneFromTemplate(AuthoringScenePath, isRuntime: false);
        AddToBuildSettings(RuntimeScenePath);
        EditorSceneManager.OpenScene(AuthoringScenePath, OpenSceneMode.Single);
        Debug.Log($"ステージ用シーンを作成しました: {RuntimeScenePath}, {AuthoringScenePath}");
    }

    private static void CreateSceneFromTemplate(string path, bool isRuntime)
    {
        if (File.Exists(path) && !EditorUtility.DisplayDialog("上書き確認", $"{path} は既にあります。作り直しますか?", "作り直す", "スキップ"))
        {
            return;
        }

        AssetDatabase.DeleteAsset(path);
        if (!AssetDatabase.CopyAsset(TemplateScenePath, path))
        {
            Debug.LogError($"シーンのコピーに失敗しました: {TemplateScenePath} → {path}");
            return;
        }

        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        var refs = StageDataSerializer.ResolveRefs(scene);

        StageDataSerializer.ClearContent(scene);
        if (refs.grid != null)
        {
            foreach (var tilemap in refs.grid.GetComponentsInChildren<Tilemap>(true))
            {
                tilemap.ClearAllTiles();
            }
        }

        var contentRoot = new GameObject(ContentRootName);
        SceneManager.MoveGameObjectToScene(contentRoot, scene);

        var system = new GameObject(SystemObjectName);
        SceneManager.MoveGameObjectToScene(system, scene);
        var anchors = system.AddComponent<StageSceneAnchors>();
        anchors.Configure(refs.grid, refs.player, refs.spawnPoint, contentRoot.transform);

        if (isRuntime)
        {
            system.AddComponent<StageRuntimeLoader>();
        }
        else
        {
            system.AddComponent<StageAuthoringMarker>();
        }

        EditorSceneManager.SaveScene(scene);
    }

    private static void AddToBuildSettings(string path)
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        if (scenes.Any(scene => scene.path == path))
        {
            return;
        }

        scenes.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    /// <summary>
    /// 既存の Stage_2〜10 を StageData に変換し、StageCatalog に登録する。元のシーンは変更しない。
    /// </summary>
    [MenuItem("Tools/PinchWitch/Stage Data/Convert Existing Stages")]
    public static void ConvertExistingStages()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        var catalog = LoadOrCreateCatalog();
        var report = new List<string>();
        for (var stageNumber = ConvertFirstStage; stageNumber <= ConvertLastStage; stageNumber++)
        {
            var scenePath = $"Assets/Scenes/Stage_{stageNumber}.unity";
            if (!File.Exists(scenePath))
            {
                continue;
            }

            var alreadyOpen = SceneManager.GetSceneByPath(scenePath);
            var scene = alreadyOpen.IsValid() && alreadyOpen.isLoaded
                ? alreadyOpen
                : EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

            var stage = LoadOrCreateStageData($"{StageDataFolder}/Stage_{stageNumber}.asset");
            stage.displayName = $"Stage {stageNumber}";
            var stageReport = new List<string>();
            StageDataSerializer.Capture(scene, stage, stageReport);
            catalog.SetStage(stageNumber, stage);

            report.Add($"Stage_{stageNumber}: 配置物 {stage.objects.Count} 個, タイル {stage.tilemaps.Sum(layer => layer.positions.Count)} 枚");
            report.AddRange(stageReport.Select(line => $"    {line}"));

            if (scene != alreadyOpen)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        Debug.Log("既存ステージの変換結果:\n" + string.Join("\n", report));
    }
}
