using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

// 地形タブ。パレットのタイルでシーン内の Tilemap を塗る。
// 左クリック/ドラッグで塗る・消す、矩形モードはドラッグで範囲指定。Undo 対応。
public sealed class StageTerrainTab : IStageEditorTab
{
    private enum PaintMode
    {
        Paint,
        Erase,
        RectPaint,
        RectErase,
    }

    private const float ThumbnailSize = 48f;

    private static readonly string[] ModeLabels = { "塗る", "消す", "矩形で塗る", "矩形で消す" };

    private readonly StageEditorWindow _window;
    private Tilemap[] _tilemaps = new Tilemap[0];
    private int _tilemapIndex;
    private TileBase _selectedTile;
    private PaintMode _mode;
    private bool _isEditing = true;
    private bool _isDragging;
    private Vector3Int _dragStart;
    private Vector2 _scroll;

    public StageTerrainTab(StageEditorWindow window)
    {
        _window = window;
    }

    public string Title => "地形";

    private Tilemap TargetTilemap => _tilemapIndex >= 0 && _tilemapIndex < _tilemaps.Length ? _tilemaps[_tilemapIndex] : null;

    public void OnEnable()
    {
        RefreshTilemaps();
        EditorApplication.hierarchyChanged += RefreshTilemaps;
    }

    public void OnDisable()
    {
        EditorApplication.hierarchyChanged -= RefreshTilemaps;
        _isDragging = false;
    }

    public void OnGUI()
    {
        if (_tilemaps.Length == 0)
        {
            EditorGUILayout.HelpBox("シーンに Tilemap がありません(Grid_2DWorld を置いてください)。", MessageType.Warning);
            return;
        }

        _tilemapIndex = EditorGUILayout.Popup("対象 Tilemap", _tilemapIndex, _tilemaps.Select(GetTilemapLabel).ToArray());
        _mode = (PaintMode)GUILayout.Toolbar((int)_mode, ModeLabels);
        _isEditing = EditorGUILayout.ToggleLeft("SceneView で編集する", _isEditing);

        if (GUILayout.Button("Tile Palette を開く"))
        {
            EditorApplication.ExecuteMenuItem("Window/2D/Tile Palette");
        }

        EditorGUILayout.Space();
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        DrawTileGrid();
        EditorGUILayout.EndScrollView();
    }

    private void DrawTileGrid()
    {
        var palette = _window.Palette;
        if (palette == null || palette.tiles.Count == 0)
        {
            EditorGUILayout.HelpBox("パレットにタイルがありません。「フォルダから自動登録」を押してください。", MessageType.Info);
            return;
        }

        var columns = Mathf.Max(1, Mathf.FloorToInt((_window.position.width - 24f) / (ThumbnailSize + 6f)));
        var column = 0;
        var previousColor = GUI.backgroundColor;
        EditorGUILayout.BeginHorizontal();
        foreach (var tile in palette.tiles)
        {
            if (tile == null)
            {
                continue;
            }

            if (column == columns)
            {
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                column = 0;
            }

            GUI.backgroundColor = tile == _selectedTile ? new Color(0.4f, 0.8f, 1f) : previousColor;
            var content = new GUIContent(GetTilePreview(tile), tile.name);
            if (GUILayout.Button(content, GUILayout.Width(ThumbnailSize), GUILayout.Height(ThumbnailSize)))
            {
                _selectedTile = tile;
                if (_mode == PaintMode.Erase || _mode == PaintMode.RectErase)
                {
                    _mode = PaintMode.Paint;
                }
            }

            column++;
        }

        GUI.backgroundColor = previousColor;
        EditorGUILayout.EndHorizontal();
    }

    public void OnSceneGUI(SceneView sceneView)
    {
        var tilemap = TargetTilemap;
        if (!_isEditing || tilemap == null)
        {
            return;
        }

        var currentEvent = Event.current;
        var controlId = GUIUtility.GetControlID(FocusType.Passive);
        HandleUtility.AddDefaultControl(controlId);

        var plane = new Plane(Vector3.forward, tilemap.transform.position);
        if (!StageEditorUtility.TryGetMousePointOnPlane(plane, currentEvent.mousePosition, out var point))
        {
            return;
        }

        var cell = tilemap.WorldToCell(point);
        switch (currentEvent.type)
        {
            case EventType.Repaint:
                DrawCellHighlight(tilemap, _isDragging && IsRectMode() ? _dragStart : cell, cell);
                break;
            case EventType.MouseMove:
                sceneView.Repaint();
                break;
            case EventType.MouseDown when currentEvent.button == 0 && !currentEvent.alt:
                GUIUtility.hotControl = controlId;
                _isDragging = true;
                _dragStart = cell;
                if (!IsRectMode())
                {
                    Undo.RegisterCompleteObjectUndo(tilemap, "Paint Tiles");
                    ApplyToCell(tilemap, cell);
                }

                currentEvent.Use();
                break;
            case EventType.MouseDrag when _isDragging && GUIUtility.hotControl == controlId:
                if (!IsRectMode())
                {
                    ApplyToCell(tilemap, cell);
                }

                sceneView.Repaint();
                currentEvent.Use();
                break;
            case EventType.MouseUp when _isDragging && GUIUtility.hotControl == controlId:
                if (IsRectMode())
                {
                    Undo.RegisterCompleteObjectUndo(tilemap, "Fill Tiles");
                    ApplyToRect(tilemap, _dragStart, cell);
                }

                _isDragging = false;
                GUIUtility.hotControl = 0;
                currentEvent.Use();
                break;
        }
    }

    private bool IsRectMode()
    {
        return _mode == PaintMode.RectPaint || _mode == PaintMode.RectErase;
    }

    private TileBase ResolveTileToSet()
    {
        return _mode == PaintMode.Erase || _mode == PaintMode.RectErase ? null : _selectedTile;
    }

    private void ApplyToCell(Tilemap tilemap, Vector3Int cell)
    {
        var tile = ResolveTileToSet();
        if (tile == null && (_mode == PaintMode.Paint || _mode == PaintMode.RectPaint))
        {
            return;
        }

        if (tilemap.GetTile(cell) != tile)
        {
            tilemap.SetTile(cell, tile);
        }
    }

    private void ApplyToRect(Tilemap tilemap, Vector3Int from, Vector3Int to)
    {
        var min = Vector3Int.Min(from, to);
        var max = Vector3Int.Max(from, to);
        for (var y = min.y; y <= max.y; y++)
        {
            for (var x = min.x; x <= max.x; x++)
            {
                ApplyToCell(tilemap, new Vector3Int(x, y, from.z));
            }
        }
    }

    private void DrawCellHighlight(Tilemap tilemap, Vector3Int from, Vector3Int to)
    {
        var min = Vector3Int.Min(from, to);
        var max = Vector3Int.Max(from, to) + new Vector3Int(1, 1, 0);
        var corners = new[]
        {
            tilemap.CellToWorld(new Vector3Int(min.x, min.y, 0)),
            tilemap.CellToWorld(new Vector3Int(max.x, min.y, 0)),
            tilemap.CellToWorld(new Vector3Int(max.x, max.y, 0)),
            tilemap.CellToWorld(new Vector3Int(min.x, max.y, 0)),
        };

        var isErase = _mode == PaintMode.Erase || _mode == PaintMode.RectErase;
        var outline = isErase ? new Color(1f, 0.4f, 0.4f, 1f) : new Color(0.4f, 1f, 0.6f, 1f);
        Handles.DrawSolidRectangleWithOutline(corners, new Color(outline.r, outline.g, outline.b, 0.15f), outline);
    }

    private void RefreshTilemaps()
    {
        var previous = TargetTilemap;
        _tilemaps = Object.FindObjectsByType<Tilemap>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .Where(tilemap => !EditorUtility.IsPersistent(tilemap))
            .OrderBy(tilemap => tilemap.name)
            .ToArray();

        var index = previous != null ? System.Array.IndexOf(_tilemaps, previous) : -1;
        if (index < 0)
        {
            // 当たり判定のある Tilemap(地面)を優先して選ぶ。
            index = System.Array.FindIndex(_tilemaps, tilemap => tilemap.GetComponent<TilemapCollider2D>() != null);
        }

        _tilemapIndex = Mathf.Max(0, index);
        _window.Repaint();
    }

    private static string GetTilemapLabel(Tilemap tilemap)
    {
        var suffix = tilemap.GetComponent<TilemapCollider2D>() != null ? " (当たり判定あり)" : string.Empty;
        return $"{tilemap.name}{suffix}";
    }

    private static Texture GetTilePreview(TileBase tile)
    {
        if (tile is Tile spriteTile && spriteTile.sprite != null)
        {
            var spritePreview = AssetPreview.GetAssetPreview(spriteTile.sprite);
            if (spritePreview != null)
            {
                return spritePreview;
            }
        }

        var preview = AssetPreview.GetAssetPreview(tile);
        return preview != null ? preview : AssetPreview.GetMiniThumbnail(tile);
    }
}
