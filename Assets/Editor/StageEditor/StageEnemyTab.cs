using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// 敵パラメータタブ。シーン内の敵(IEnemyMover を持つ GameObject)を一覧し、
// 選んだ敵の移動・スタン・被光コンポーネントをまとめて編集する(複数選択で一括編集)。
// シーン上のハンドル(巡回範囲・経路点)は各コンポーネントのカスタム Editor が描く。
public sealed class StageEnemyTab : IStageEditorTab
{
    private static readonly Type[] EditableComponentTypes =
    {
        typeof(EnemyPatrolMover2D),
        typeof(EnemyWalker2D),
        typeof(EnemyPathMover2D),
        typeof(EnemyStun2D),
        typeof(EnemyLightReaction2D),
        typeof(EnemyHealth2D),
    };

    private readonly StageEditorWindow _window;
    private readonly List<GameObject> _enemies = new List<GameObject>();
    private readonly Dictionary<Type, Editor> _editors = new Dictionary<Type, Editor>();
    private Vector2 _listScroll;
    private Vector2 _inspectorScroll;

    public StageEnemyTab(StageEditorWindow window)
    {
        _window = window;
    }

    public string Title => "敵パラメータ";

    public void OnEnable()
    {
        RefreshEnemies();
        Selection.selectionChanged += HandleSelectionChanged;
        EditorApplication.hierarchyChanged += RefreshEnemies;
    }

    public void OnDisable()
    {
        Selection.selectionChanged -= HandleSelectionChanged;
        EditorApplication.hierarchyChanged -= RefreshEnemies;
        ClearEditors();
    }

    public void OnSceneGUI(SceneView sceneView)
    {
    }

    public void OnGUI()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField($"シーン内の敵: {_enemies.Count}", EditorStyles.boldLabel);
            if (GUILayout.Button("再取得", GUILayout.Width(60f)))
            {
                RefreshEnemies();
            }
        }

        _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.Height(Mathf.Min(220f, 22f * _enemies.Count + 8f)));
        foreach (var group in _enemies.GroupBy(GetMoverLabel))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(group.Key, EditorStyles.miniBoldLabel);
                if (GUILayout.Button("この種類をすべて選択", EditorStyles.miniButton, GUILayout.Width(120f)))
                {
                    Selection.objects = group.Cast<Object>().ToArray();
                }
            }

            foreach (var enemy in group)
            {
                DrawEnemyRow(enemy);
            }
        }

        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();
        DrawSelectedInspectors();
    }

    private void DrawEnemyRow(GameObject enemy)
    {
        if (enemy == null)
        {
            return;
        }

        var isSelected = Selection.gameObjects.Contains(enemy);
        using (new EditorGUILayout.HorizontalScope())
        {
            var toggled = GUILayout.Toggle(isSelected, enemy.name, EditorStyles.miniButtonLeft);
            if (toggled != isSelected)
            {
                var additive = Event.current.control || Event.current.command || Event.current.shift;
                if (additive)
                {
                    var selection = Selection.objects.ToList();
                    if (toggled) selection.Add(enemy); else selection.Remove(enemy);
                    Selection.objects = selection.ToArray();
                }
                else
                {
                    Selection.activeGameObject = enemy;
                }
            }

            if (GUILayout.Button("表示", EditorStyles.miniButtonRight, GUILayout.Width(40f)))
            {
                Selection.activeGameObject = enemy;
                SceneView.lastActiveSceneView?.FrameSelected();
            }
        }
    }

    private void DrawSelectedInspectors()
    {
        var selectedEnemies = Selection.gameObjects.Where(_enemies.Contains).ToArray();
        if (selectedEnemies.Length == 0)
        {
            EditorGUILayout.HelpBox("敵を選ぶとパラメータを編集できます(Ctrl/Shift+クリックで複数選択)。", MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField(selectedEnemies.Length == 1 ? selectedEnemies[0].name : $"{selectedEnemies.Length} 体を一括編集", EditorStyles.boldLabel);
        _inspectorScroll = EditorGUILayout.BeginScrollView(_inspectorScroll);
        foreach (var componentType in EditableComponentTypes)
        {
            var targets = selectedEnemies
                .Select(enemy => enemy.GetComponent(componentType))
                .Where(component => component != null)
                .ToArray();

            // 複数選択時は、選んだ全員が持つコンポーネントだけを一括編集の対象にする。
            if (targets.Length == 0 || targets.Length != selectedEnemies.Length)
            {
                continue;
            }

            var editor = GetEditor(componentType, targets);
            EditorGUILayout.LabelField(ObjectNames.NicifyVariableName(componentType.Name), EditorStyles.miniBoldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                editor.OnInspectorGUI();
            }

            EditorGUILayout.Space();
        }

        EditorGUILayout.EndScrollView();
    }

    private Editor GetEditor(Type componentType, Object[] targets)
    {
        _editors.TryGetValue(componentType, out var editor);
        if (editor == null || !editor.targets.SequenceEqual(targets))
        {
            Editor.CreateCachedEditor(targets, null, ref editor);
            _editors[componentType] = editor;
        }

        return editor;
    }

    private void HandleSelectionChanged()
    {
        _window.Repaint();
    }

    private void RefreshEnemies()
    {
        _enemies.Clear();
        foreach (var behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (behaviour is IEnemyMover && !_enemies.Contains(behaviour.gameObject) && !EditorUtility.IsPersistent(behaviour))
            {
                _enemies.Add(behaviour.gameObject);
            }
        }

        _enemies.Sort((left, right) => string.CompareOrdinal(left.name, right.name));
        _window.Repaint();
    }

    private static string GetMoverLabel(GameObject enemy)
    {
        if (enemy.TryGetComponent<EnemyWalker2D>(out var walker))
        {
            return walker.Ledge == EnemyWalker2D.LedgeBehavior.TurnAtLedge ? "トランプ兵(赤: 崖で反転)" : "トランプ兵(緑: 崖から落ちる)";
        }

        if (enemy.TryGetComponent<EnemyPathMover2D>(out _))
        {
            return "線に沿って動く敵";
        }

        if (enemy.TryGetComponent<EnemyPatrolMover2D>(out var patrol))
        {
            return patrol.Axis == EnemyPatrolMover2D.PatrolAxis.Horizontal ? "巡回(左右・地形すり抜け)" : "巡回(上下・地形すり抜け)";
        }

        return "その他";
    }

    private void ClearEditors()
    {
        foreach (var editor in _editors.Values)
        {
            if (editor != null)
            {
                Object.DestroyImmediate(editor);
            }
        }

        _editors.Clear();
    }
}
