using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// ステージエディタ共通の処理(配置平面の解決、親オブジェクトの用意、スナップ)。
public static class StageEditorUtility
{
    // 既存ステージでは 2D 世界(敵・影足場)は z=5 の壁面上にある。
    public const float DefaultWallZ = 5f;
    public const float FloorY = 0f;

    /// <summary>
    /// 2D オブジェクトを置く壁面の平面(影足場の出力 z と揃える。既定は ShadowColliderBuilder.outputWorldZ と同じ 5)。
    /// </summary>
    public static Plane GetWallPlane(float wallZ)
    {
        return new Plane(Vector3.forward, new Vector3(0f, 0f, wallZ));
    }

    public static Plane GetFloorPlane()
    {
        return new Plane(Vector3.up, new Vector3(0f, FloorY, 0f));
    }

    public static bool TryGetMousePointOnPlane(Plane plane, Vector2 guiPosition, out Vector3 point)
    {
        var ray = HandleUtility.GUIPointToWorldRay(guiPosition);
        if (plane.Raycast(ray, out var distance))
        {
            point = ray.GetPoint(distance);
            return true;
        }

        point = default;
        return false;
    }

    public static float Snap(float value, float step)
    {
        return step > 0f ? Mathf.Round(value / step) * step : value;
    }

    /// <summary>
    /// "Stage/Enemies" のようなパスの親オブジェクトをアクティブシーンに用意して返す。
    /// </summary>
    public static Transform EnsureParent(string path)
    {
        Transform current = null;
        foreach (var segment in path.Split('/'))
        {
            var next = current != null ? current.Find(segment) : FindRoot(segment);
            if (next == null)
            {
                var created = new GameObject(segment);
                Undo.RegisterCreatedObjectUndo(created, "Create Stage Parent");
                if (current != null)
                {
                    created.transform.SetParent(current, false);
                }

                next = created.transform;
            }

            current = next;
        }

        return current;
    }

    private static Transform FindRoot(string name)
    {
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name == name)
            {
                return root.transform;
            }
        }

        return null;
    }
}
