using UnityEditor;
using UnityEngine;

// ステージエディタ本体。Tools > PinchWitch > Stage Editor から開く。
// タブ: 配置 / 敵パラメータ / ブロック組み立て / 地形 / ステージデータ
public sealed class StageEditorWindow : EditorWindow
{
    private const string WallZPrefKey = "PinchWitch.StageEditor.WallZ";
    private const string SnapPrefKey = "PinchWitch.StageEditor.Snap";
    private const string TabPrefKey = "PinchWitch.StageEditor.Tab";

    private IStageEditorTab[] _tabs;
    private string[] _tabTitles;
    private int _activeTab;

    public StagePaletteAsset Palette { get; private set; }
    public float WallZ { get; private set; } = StageEditorUtility.DefaultWallZ;
    public float Snap { get; private set; } = 0.5f;

    [MenuItem("Tools/PinchWitch/Stage Editor")]
    public static void Open()
    {
        var window = GetWindow<StageEditorWindow>();
        window.titleContent = new GUIContent("Stage Editor");
        window.minSize = new Vector2(320f, 360f);
        window.Show();
    }

    private void OnEnable()
    {
        Palette = StagePaletteAsset.LoadOrCreateDefault();
        WallZ = EditorPrefs.GetFloat(WallZPrefKey, StageEditorUtility.DefaultWallZ);
        Snap = EditorPrefs.GetFloat(SnapPrefKey, 0.5f);

        _tabs = new IStageEditorTab[]
        {
            new StagePlacementTab(this),
            new StageEnemyTab(this),
            new StageBlockBuilderTab(this),
            new StageTerrainTab(this),
            new StageDataTab(this),
        };
        _tabTitles = new string[_tabs.Length];
        for (var i = 0; i < _tabs.Length; i++)
        {
            _tabTitles[i] = _tabs[i].Title;
        }

        _activeTab = Mathf.Clamp(EditorPrefs.GetInt(TabPrefKey, 0), 0, _tabs.Length - 1);
        _tabs[_activeTab].OnEnable();
        SceneView.duringSceneGui += HandleSceneGUI;
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= HandleSceneGUI;
        _tabs?[_activeTab].OnDisable();
    }

    private void OnGUI()
    {
        DrawCommonSettings();

        var nextTab = GUILayout.Toolbar(_activeTab, _tabTitles);
        if (nextTab != _activeTab)
        {
            _tabs[_activeTab].OnDisable();
            _activeTab = nextTab;
            EditorPrefs.SetInt(TabPrefKey, _activeTab);
            _tabs[_activeTab].OnEnable();
            SceneView.RepaintAll();
        }

        EditorGUILayout.Space();
        _tabs[_activeTab].OnGUI();
    }

    private void DrawCommonSettings()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            Palette = (StagePaletteAsset)EditorGUILayout.ObjectField(Palette, typeof(StagePaletteAsset), false);
            if (GUILayout.Button("フォルダから自動登録", GUILayout.Width(130f)) && Palette != null)
            {
                Palette.AutoFill();
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginChangeCheck();
            WallZ = EditorGUILayout.FloatField(new GUIContent("2D 壁面 Z", "敵・鍵など 2D オブジェクトを置く平面の z"), WallZ);
            Snap = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("スナップ", "0 でスナップなし"), Snap));
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetFloat(WallZPrefKey, WallZ);
                EditorPrefs.SetFloat(SnapPrefKey, Snap);
            }
        }
    }

    private void HandleSceneGUI(SceneView sceneView)
    {
        if (_tabs == null)
        {
            return;
        }

        _tabs[_activeTab].OnSceneGUI(sceneView);
    }
}
