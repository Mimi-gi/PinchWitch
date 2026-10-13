using System.IO;
using UnityEditor;
using UnityEngine;

// ブロック組み立てタブ。正面から見たマス目を「空/通常/ガラス」で塗り、掴めるブロックの Prefab を生成する。
// 生成処理は GrabbableBlockFactory に任せる。
public sealed class StageBlockBuilderTab : IStageEditorTab
{
    private const int MaxGridSize = 8;
    private const float CellButtonSize = 34f;

    private static readonly string[] BrushLabels = { "空", "通常", "ガラス" };
    private static readonly Color SolidColor = new Color(0.85f, 0.75f, 0.6f);
    private static readonly Color GlassColor = new Color(0.6f, 0.9f, 1f);
    private static readonly Color EmptyColor = new Color(0.3f, 0.3f, 0.3f);

    private readonly StageEditorWindow _window;
    private readonly GrabbableBlockLayout _layout = new GrabbableBlockLayout();
    private BlockCellType _brush = BlockCellType.Solid;
    private string _prefabName = "Grabbable_Custom";
    private Vector2 _scroll;

    public StageBlockBuilderTab(StageEditorWindow window)
    {
        _window = window;
    }

    public string Title => "ブロック組み立て";

    public void OnEnable()
    {
        _layout.EnsureSize();
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

        var oldWidth = _layout.width;
        var oldHeight = _layout.height;
        EditorGUI.BeginChangeCheck();
        _layout.width = EditorGUILayout.IntSlider("横マス数", _layout.width, 1, MaxGridSize);
        _layout.height = EditorGUILayout.IntSlider("縦マス数", _layout.height, 1, MaxGridSize);
        if (EditorGUI.EndChangeCheck())
        {
            ResizeKeepingCells(oldWidth, oldHeight);
        }

        _layout.cellSize = Mathf.Max(0.1f, EditorGUILayout.FloatField("1マスの大きさ", _layout.cellSize));
        _layout.depth = Mathf.Max(0.1f, EditorGUILayout.FloatField("奥行き", _layout.depth));

        EditorGUILayout.Space();
        _brush = (BlockCellType)GUILayout.Toolbar((int)_brush, BrushLabels);
        EditorGUILayout.HelpBox("マスをクリックして塗ります。ガラスのマスは影を落とさず、光も通します。", MessageType.None);
        DrawGrid();

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("すべて通常"))
            {
                Fill(BlockCellType.Solid);
            }

            if (GUILayout.Button("すべてガラス"))
            {
                Fill(BlockCellType.Glass);
            }

            if (GUILayout.Button("クリア"))
            {
                Fill(BlockCellType.Empty);
            }
        }

        EditorGUILayout.Space();
        _prefabName = EditorGUILayout.TextField("Prefab 名", _prefabName);
        var path = $"{GrabbableBlockFactory.DefaultOutputFolder}/{_prefabName}.prefab";
        EditorGUILayout.LabelField("保存先", path, EditorStyles.miniLabel);

        using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_prefabName)))
        {
            if (GUILayout.Button("Prefab を生成", GUILayout.Height(28f)))
            {
                Generate(path);
            }
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawGrid()
    {
        var previousColor = GUI.backgroundColor;
        // 上の行ほど y が大きい(見た目と配置を一致させる)。
        for (var y = _layout.height - 1; y >= 0; y--)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                for (var x = 0; x < _layout.width; x++)
                {
                    var cell = _layout.Get(x, y);
                    GUI.backgroundColor = cell switch
                    {
                        BlockCellType.Solid => SolidColor,
                        BlockCellType.Glass => GlassColor,
                        _ => EmptyColor,
                    };

                    if (GUILayout.Button(cell == BlockCellType.Empty ? string.Empty : BrushLabels[(int)cell],
                            GUILayout.Width(CellButtonSize), GUILayout.Height(CellButtonSize)))
                    {
                        _layout.Set(x, y, _brush);
                    }
                }

                GUILayout.FlexibleSpace();
            }
        }

        GUI.backgroundColor = previousColor;
    }

    // 幅が変わると一次元配列の並びが崩れるので、(x, y) を保ったまま詰め替える。
    private void ResizeKeepingCells(int oldWidth, int oldHeight)
    {
        var oldCells = _layout.cells;
        var resized = new BlockCellType[_layout.width * _layout.height];
        if (oldCells != null && oldCells.Length == oldWidth * oldHeight)
        {
            for (var y = 0; y < Mathf.Min(oldHeight, _layout.height); y++)
            {
                for (var x = 0; x < Mathf.Min(oldWidth, _layout.width); x++)
                {
                    resized[y * _layout.width + x] = oldCells[y * oldWidth + x];
                }
            }
        }

        _layout.cells = resized;
    }

    private void Fill(BlockCellType type)
    {
        _layout.EnsureSize();
        for (var i = 0; i < _layout.cells.Length; i++)
        {
            _layout.cells[i] = type;
        }
    }

    private void Generate(string path)
    {
        if (File.Exists(path) && !EditorUtility.DisplayDialog("上書き確認", $"{path} は既にあります。上書きしますか?", "上書き", "キャンセル"))
        {
            return;
        }

        var prefab = GrabbableBlockFactory.CreatePrefab(_layout, path);
        if (prefab == null)
        {
            return;
        }

        _window.Palette?.AutoFill();
        EditorGUIUtility.PingObject(prefab);
        Debug.Log($"掴めるブロックを生成しました: {path}");
    }
}
