using UnityEditor;
using UnityEngine;

// 巡回敵の移動範囲を SceneView に表示し、端点のドラッグで moveRange を調整できるようにする。
[CustomEditor(typeof(EnemyPatrolMover2D))]
[CanEditMultipleObjects]
public sealed class EnemyPatrolMover2DEditor : Editor
{
    private void OnSceneGUI()
    {
        // 実行中は Transform が動くので、配置時の範囲表示はしない。
        if (Application.isPlaying)
        {
            return;
        }

        var mover = (EnemyPatrolMover2D)target;
        var moverTransform = mover.transform;
        // 複数選択時も対象ごとに処理するため、target 単体の SerializedObject を使う。
        var serializedMover = new SerializedObject(mover);
        var axis = (EnemyPatrolMover2D.PatrolAxis)serializedMover.FindProperty("patrolAxis").enumValueIndex;
        var reference = (EnemyPatrolMover2D.InitialPositionReference)serializedMover.FindProperty("initialPositionReference").enumValueIndex;
        var rangeProperty = serializedMover.FindProperty("moveRange");
        var range = rangeProperty.floatValue;

        var localAxis = axis == EnemyPatrolMover2D.PatrolAxis.Horizontal ? Vector3.right : Vector3.up;
        var localPosition = moverTransform.localPosition;

        // EnemyPatrolMover2D.GetPatrolCenterLocalPosition と同じ規則で中心を求める。
        var centerOffset = reference switch
        {
            EnemyPatrolMover2D.InitialPositionReference.LowerOrLeftEnd => range,
            EnemyPatrolMover2D.InitialPositionReference.UpperOrRightEnd => -range,
            _ => 0f,
        };
        var localCenter = localPosition + localAxis * centerOffset;
        var localMin = localCenter - localAxis * range;
        var localMax = localCenter + localAxis * range;

        var parent = moverTransform.parent;
        var worldMin = parent != null ? parent.TransformPoint(localMin) : localMin;
        var worldMax = parent != null ? parent.TransformPoint(localMax) : localMax;
        var worldAxis = (parent != null ? parent.TransformDirection(localAxis) : localAxis).normalized;

        Handles.color = new Color(1f, 0.5f, 0.2f, 1f);
        Handles.DrawLine(worldMin, worldMax, 2f);

        // 動かせる端点: Center/下・左基準なら + 側、上・右基準なら - 側。
        var draggingMax = reference != EnemyPatrolMover2D.InitialPositionReference.UpperOrRightEnd;
        var handlePosition = draggingMax ? worldMax : worldMin;
        var fixedPosition = draggingMax ? worldMin : worldMax;
        Handles.DrawWireDisc(fixedPosition, Vector3.forward, HandleUtility.GetHandleSize(fixedPosition) * 0.06f);

        EditorGUI.BeginChangeCheck();
        var size = HandleUtility.GetHandleSize(handlePosition) * 0.08f;
        var moved = Handles.Slider(handlePosition, worldAxis, size, Handles.DotHandleCap, 0f);
        if (!EditorGUI.EndChangeCheck())
        {
            return;
        }

        var worldPosition = moverTransform.position;
        var distance = Vector3.Dot(moved - worldPosition, worldAxis);
        var newRange = reference == EnemyPatrolMover2D.InitialPositionReference.Center
            ? Mathf.Abs(distance)
            : Mathf.Abs(distance) * 0.5f;

        rangeProperty.floatValue = newRange;
        serializedMover.ApplyModifiedProperties();
    }
}
