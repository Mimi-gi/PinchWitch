using UnityEditor;
using UnityEngine;

// 線移動の敵の経路点を SceneView でドラッグ編集できるようにする。点の追加・削除はインスペクタのボタンで行う。
[CustomEditor(typeof(EnemyPathMover2D))]
public sealed class EnemyPathMover2DEditor : Editor
{
    private const float SnapStep = 0.25f;

    private SerializedProperty _points;

    private void OnEnable()
    {
        _points = serializedObject.FindProperty("points");
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        serializedObject.Update();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("点を追加"))
            {
                var count = _points.arraySize;
                var last = count > 0 ? _points.GetArrayElementAtIndex(count - 1).vector2Value : Vector2.zero;
                _points.InsertArrayElementAtIndex(count);
                _points.GetArrayElementAtIndex(count).vector2Value = last + Vector2.right;
            }

            using (new EditorGUI.DisabledScope(_points.arraySize <= 2))
            {
                if (GUILayout.Button("最後の点を削除"))
                {
                    _points.DeleteArrayElementAtIndex(_points.arraySize - 1);
                }
            }
        }

        serializedObject.ApplyModifiedProperties();
        EditorGUILayout.HelpBox("SceneView で点をドラッグして経路を編集できます(0.25 単位でスナップ)。", MessageType.None);
    }

    private void OnSceneGUI()
    {
        if (Application.isPlaying)
        {
            return;
        }

        var mover = (EnemyPathMover2D)target;
        var points = mover.Points;
        if (points == null)
        {
            return;
        }

        var origin = mover.Origin;
        Handles.color = Color.cyan;
        for (var i = 0; i < points.Length; i++)
        {
            var worldPoint = origin + (Vector3)points[i];
            Handles.Label(worldPoint + Vector3.up * HandleUtility.GetHandleSize(worldPoint) * 0.2f, i.ToString());

            EditorGUI.BeginChangeCheck();
            var size = HandleUtility.GetHandleSize(worldPoint) * 0.08f;
            var moved = Handles.FreeMoveHandle(worldPoint, size, Vector3.one * SnapStep, Handles.DotHandleCap);
            if (!EditorGUI.EndChangeCheck())
            {
                continue;
            }

            var local = (Vector2)(moved - origin);
            local = new Vector2(StageEditorUtility.Snap(local.x, SnapStep), StageEditorUtility.Snap(local.y, SnapStep));
            Undo.RecordObject(mover, "Move Path Point");
            serializedObject.Update();
            _points.GetArrayElementAtIndex(i).vector2Value = local;
            serializedObject.ApplyModifiedProperties();
        }
    }
}
