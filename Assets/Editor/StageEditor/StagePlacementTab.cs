using UnityEditor;
using UnityEngine;

// 配置タブ。パレットで選んだ Prefab を SceneView のクリックで置く。
// - 3D Rigidbody を持つ Prefab(掴めるブロック等)は床(y=0)上に、Prefab 本来の高さを保って置く
// - それ以外(敵・鍵・ゴール等)は 2D 壁面(z=WallZ)上に置く
// 左クリックで配置、Esc または右クリックで選択解除。Undo 対応。
public sealed class StagePlacementTab : IStageEditorTab
{
    private const float ThumbnailSize = 72f;

    private static readonly string[] FilterLabels = { "すべて", "敵", "ギミック", "ブロック" };

    private readonly StageEditorWindow _window;
    private GameObject _selectedPrefab;
    private StagePaletteAsset.Category _selectedCategory;
    private int _filter;
    private Vector2 _scroll;
    private bool _keepSelection = true;

    public StagePlacementTab(StageEditorWindow window)
    {
        _window = window;
    }

    public string Title => "配置";

    public void OnEnable()
    {
    }

    public void OnDisable()
    {
        _selectedPrefab = null;
    }

    public void OnGUI()
    {
        var palette = _window.Palette;
        if (palette == null)
        {
            EditorGUILayout.HelpBox("パレットが設定されていません。", MessageType.Warning);
            return;
        }

        _filter = GUILayout.Toolbar(_filter, FilterLabels);
        _keepSelection = EditorGUILayout.ToggleLeft("配置後も選択を維持する(連続配置)", _keepSelection);

        if (_selectedPrefab != null)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"配置中: {_selectedPrefab.name}", EditorStyles.boldLabel);
                if (GUILayout.Button("解除", GUILayout.Width(60f)))
                {
                    ClearSelection();
                }
            }
        }
        else
        {
            EditorGUILayout.HelpBox("パレットから選んで、SceneView を左クリックで配置します。", MessageType.Info);
        }

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        DrawPaletteGrid(palette);
        EditorGUILayout.EndScrollView();
    }

    private void DrawPaletteGrid(StagePaletteAsset palette)
    {
        var columns = Mathf.Max(1, Mathf.FloorToInt((_window.position.width - 24f) / (ThumbnailSize + 8f)));
        var column = 0;
        EditorGUILayout.BeginHorizontal();
        foreach (var entry in palette.entries)
        {
            if (entry.prefab == null || (_filter > 0 && (int)entry.category != _filter - 1))
            {
                continue;
            }

            if (column == columns)
            {
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                column = 0;
            }

            DrawPaletteButton(entry);
            column++;
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawPaletteButton(StagePaletteAsset.Entry entry)
    {
        var preview = AssetPreview.GetAssetPreview(entry.prefab);
        if (preview == null)
        {
            preview = AssetPreview.GetMiniThumbnail(entry.prefab);
            _window.Repaint();
        }

        var isSelected = _selectedPrefab == entry.prefab;
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(ThumbnailSize + 4f)))
        {
            var previousColor = GUI.backgroundColor;
            if (isSelected)
            {
                GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
            }

            if (GUILayout.Button(preview, GUILayout.Width(ThumbnailSize), GUILayout.Height(ThumbnailSize)))
            {
                if (isSelected)
                {
                    ClearSelection();
                }
                else
                {
                    _selectedPrefab = entry.prefab;
                    _selectedCategory = entry.category;
                    SceneView.lastActiveSceneView?.Focus();
                }
            }

            GUI.backgroundColor = previousColor;
            GUILayout.Label(entry.prefab.name, EditorStyles.miniLabel, GUILayout.Width(ThumbnailSize));
        }
    }

    public void OnSceneGUI(SceneView sceneView)
    {
        if (_selectedPrefab == null)
        {
            return;
        }

        var currentEvent = Event.current;
        var controlId = GUIUtility.GetControlID(FocusType.Passive);
        HandleUtility.AddDefaultControl(controlId);

        if ((currentEvent.type == EventType.KeyDown && currentEvent.keyCode == KeyCode.Escape)
            || (currentEvent.type == EventType.MouseDown && currentEvent.button == 1))
        {
            ClearSelection();
            currentEvent.Use();
            return;
        }

        if (!TryResolvePlacement(currentEvent.mousePosition, out var position))
        {
            return;
        }

        if (currentEvent.type == EventType.Repaint)
        {
            DrawPlacementPreview(position);
        }
        else if (currentEvent.type == EventType.MouseMove)
        {
            sceneView.Repaint();
        }
        else if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && !currentEvent.alt)
        {
            Place(position);
            currentEvent.Use();
            if (!_keepSelection)
            {
                ClearSelection();
            }
        }
    }

    private bool TryResolvePlacement(Vector2 guiPosition, out Vector3 position)
    {
        var snap = _window.Snap;
        if (IsFloorObject(_selectedPrefab))
        {
            if (!StageEditorUtility.TryGetMousePointOnPlane(StageEditorUtility.GetFloorPlane(), guiPosition, out var floorPoint))
            {
                position = default;
                return false;
            }

            position = new Vector3(
                StageEditorUtility.Snap(floorPoint.x, snap),
                _selectedPrefab.transform.position.y,
                StageEditorUtility.Snap(floorPoint.z, snap));
            return true;
        }

        if (!StageEditorUtility.TryGetMousePointOnPlane(StageEditorUtility.GetWallPlane(_window.WallZ), guiPosition, out var wallPoint))
        {
            position = default;
            return false;
        }

        position = new Vector3(StageEditorUtility.Snap(wallPoint.x, snap), StageEditorUtility.Snap(wallPoint.y, snap), _window.WallZ);
        return true;
    }

    private void DrawPlacementPreview(Vector3 position)
    {
        var isFloor = IsFloorObject(_selectedPrefab);
        Handles.color = isFloor ? new Color(1f, 0.8f, 0.2f, 0.9f) : new Color(0.3f, 0.9f, 1f, 0.9f);
        var size = HandleUtility.GetHandleSize(position) * 0.15f;
        Handles.DrawWireDisc(position, isFloor ? Vector3.up : Vector3.forward, size * 2f);
        Handles.DrawLine(position - Vector3.right * size, position + Vector3.right * size);
        Handles.DrawLine(position - (isFloor ? Vector3.forward : Vector3.up) * size, position + (isFloor ? Vector3.forward : Vector3.up) * size);
        Handles.Label(position + Vector3.up * size * 2.5f, _selectedPrefab.name);
    }

    private void Place(Vector3 position)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(_selectedPrefab);
        if (instance == null)
        {
            return;
        }

        Undo.RegisterCreatedObjectUndo(instance, $"Place {_selectedPrefab.name}");
        var parent = StageEditorUtility.EnsureParent(GetParentPath(_selectedCategory));
        Undo.SetTransformParent(instance.transform, parent, "Place Parent");
        instance.transform.position = position;
        Selection.activeGameObject = instance;
    }

    private static string GetParentPath(StagePaletteAsset.Category category)
    {
        return category switch
        {
            StagePaletteAsset.Category.Enemy => "Stage/Enemies",
            StagePaletteAsset.Category.Block => "Stage/Blocks",
            _ => "Stage/Gimmicks",
        };
    }

    private static bool IsFloorObject(GameObject prefab)
    {
        return prefab != null && prefab.GetComponentInChildren<Rigidbody>(true) != null;
    }

    private void ClearSelection()
    {
        _selectedPrefab = null;
        _window.Repaint();
        SceneView.RepaintAll();
    }
}
