using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// ステージデータタブ。作成用シーン(Stage_Authoring)の内容を StageData(SO) に保存・読み込みし、StageCatalog に登録する。
public sealed class StageDataTab : IStageEditorTab
{
    private readonly StageEditorWindow _window;
    private StageData _stage;
    private int _stageNumber = 11;
    private Vector2 _scroll;

    public StageDataTab(StageEditorWindow window)
    {
        _window = window;
    }

    public string Title => "ステージデータ";

    public void OnEnable()
    {
    }

    public void OnDisable()
    {
    }

    public void OnSceneGUI(SceneView sceneView)
    {
    }

    public void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        DrawSceneSection();
        EditorGUILayout.Space();
        DrawStageSection();
        EditorGUILayout.Space();
        DrawCatalogSection();
        EditorGUILayout.EndScrollView();
    }

    private void DrawSceneSection()
    {
        EditorGUILayout.LabelField("シーン", EditorStyles.boldLabel);
        var hasAuthoring = File.Exists(StageSceneSetup.AuthoringScenePath);
        var hasRuntime = File.Exists(StageSceneSetup.RuntimeScenePath);
        if (!hasAuthoring || !hasRuntime)
        {
            EditorGUILayout.HelpBox("ステージ作成用シーン(Stage_Authoring)と共通シーン(Stage_Runtime)がまだありません。", MessageType.Info);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(hasAuthoring ? "シーンを作り直す" : "シーンを作成"))
            {
                StageSceneSetup.CreateStageScenes();
            }

            using (new EditorGUI.DisabledScope(!hasAuthoring))
            {
                if (GUILayout.Button("作成用シーンを開く") && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    EditorSceneManager.OpenScene(StageSceneSetup.AuthoringScenePath, OpenSceneMode.Single);
                }
            }
        }
    }

    private void DrawStageSection()
    {
        EditorGUILayout.LabelField("ステージデータ", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            _stage = (StageData)EditorGUILayout.ObjectField(_stage, typeof(StageData), false);
            if (GUILayout.Button("新規", GUILayout.Width(50f)))
            {
                CreateNewStage();
            }
        }

        var scene = SceneManager.GetActiveScene();
        var anchors = StageDataSerializer.FindAnchors(scene);
        if (anchors == null)
        {
            EditorGUILayout.HelpBox("保存・読み込みは Stage_Authoring シーンを開いて行ってください。", MessageType.Warning);
        }

        using (new EditorGUI.DisabledScope(_stage == null || anchors == null || EditorApplication.isPlaying))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("シーン → SO に保存", GUILayout.Height(26f)))
                {
                    Save(scene);
                }

                if (GUILayout.Button("SO → シーンに読み込み", GUILayout.Height(26f))
                    && EditorUtility.DisplayDialog("読み込み確認", "シーン上の配置物とタイルを置き換えます。よろしいですか?(Undo できます)", "読み込む", "キャンセル"))
                {
                    StageDataSerializer.LoadIntoScene(_stage, scene);
                }
            }
        }

        if (_stage != null)
        {
            EditorGUILayout.LabelField($"配置物 {_stage.objects.Count} 個 / タイルレイヤー {_stage.tilemaps.Count}", EditorStyles.miniLabel);
        }
    }

    private void DrawCatalogSection()
    {
        EditorGUILayout.LabelField("ステージカタログ(ゲームで読み込まれるステージ)", EditorStyles.boldLabel);
        var catalog = AssetDatabase.LoadAssetAtPath<StageCatalog>(StageSceneSetup.CatalogPath);

        using (new EditorGUILayout.HorizontalScope())
        {
            _stageNumber = Mathf.Max(1, EditorGUILayout.IntField("ステージ番号", _stageNumber));
            using (new EditorGUI.DisabledScope(_stage == null))
            {
                if (GUILayout.Button("この番号で登録", GUILayout.Width(110f)))
                {
                    catalog = StageSceneSetup.LoadOrCreateCatalog();
                    Undo.RecordObject(catalog, "Register Stage");
                    catalog.SetStage(_stageNumber, _stage);
                    EditorUtility.SetDirty(catalog);
                    AssetDatabase.SaveAssetIfDirty(catalog);
                }
            }
        }

        if (catalog != null)
        {
            StageCatalog.Entry removeEntry = null;
            foreach (var entry in catalog.entries)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"Stage {entry.stageNumber}", GUILayout.Width(70f));
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.ObjectField(entry.stage, typeof(StageData), false);
                    }

                    if (GUILayout.Button("選択", EditorStyles.miniButtonLeft, GUILayout.Width(40f)))
                    {
                        _stage = entry.stage;
                        _stageNumber = entry.stageNumber;
                    }

                    if (GUILayout.Button("解除", EditorStyles.miniButtonRight, GUILayout.Width(40f)))
                    {
                        removeEntry = entry;
                    }
                }
            }

            if (removeEntry != null)
            {
                Undo.RecordObject(catalog, "Unregister Stage");
                catalog.entries.Remove(removeEntry);
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssetIfDirty(catalog);
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            $"既存の Stage_{StageSceneSetup.ConvertFirstStage}〜{StageSceneSetup.ConvertLastStage} のシーンを読み取り、{StageSceneSetup.StageDataFolder} に SO を作ってカタログに登録します。元のシーンは変更しません。",
            MessageType.None);
        if (GUILayout.Button("既存ステージを一括変換")
            && EditorUtility.DisplayDialog("一括変換", "既存ステージを SO に変換してカタログに登録します。同名の SO は上書きされます。", "変換", "キャンセル"))
        {
            StageSceneSetup.ConvertExistingStages();
        }
    }

    private void Save(Scene scene)
    {
        var report = new List<string>();
        StageDataSerializer.Capture(scene, _stage, report);
        var message = $"{_stage.name} に保存しました(配置物 {_stage.objects.Count} 個)";
        if (report.Count > 0)
        {
            message += "\n" + string.Join("\n", report);
            Debug.LogWarning(message, _stage);
        }
        else
        {
            Debug.Log(message, _stage);
        }

        _window.ShowNotification(new GUIContent("保存しました"));
    }

    private void CreateNewStage()
    {
        GrabbableBlockFactory.EnsureFolder(StageSceneSetup.StageDataFolder);
        var path = EditorUtility.SaveFilePanelInProject("ステージデータを作成", "Stage_New", "asset", "保存先を選んでください", StageSceneSetup.StageDataFolder);
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        _stage = StageSceneSetup.LoadOrCreateStageData(path);
        _stage.displayName = Path.GetFileNameWithoutExtension(path);
        EditorUtility.SetDirty(_stage);
        AssetDatabase.SaveAssetIfDirty(_stage);
        EditorGUIUtility.PingObject(_stage);
    }
}
