using System.Collections.Generic;
using UnityEngine;

public static class ShadowMathUtility
{
    /// <summary>
    /// 3Dの頂点群を、点光源から特定の平面へ投影し、平面上のワールド座標として返す
    /// </summary>
    public static List<Vector3> ProjectVerticesToPlane(Vector3 lightPos, Vector3[] worldVertices, Transform plane)
    {
        List<Vector3> projectedPoints = new List<Vector3>();

        foreach (Vector3 vertex in worldVertices)
        {
            if (TryProjectPointToPlane(lightPos, vertex, plane, out var projectedPos3D))
            {
                projectedPoints.Add(projectedPos3D);
            }
        }

        return projectedPoints;
    }

    /// <summary>
    /// 1頂点を点光源から平面へ投影する。
    /// </summary>
    public static bool TryProjectPointToPlane(
        Vector3 lightPos,
        Vector3 worldPoint,
        Transform plane,
        out Vector3 projectedWorldPoint)
    {
        projectedWorldPoint = default;

        if (plane == null)
        {
            return false;
        }

        Vector3 planePos = plane.position;
        Vector3 planeNormal = plane.up;
        Vector3 rayDir = worldPoint - lightPos;
        float denominator = Vector3.Dot(rayDir, planeNormal);

        // 光が平面と平行、または裏側から当たっている場合は除外
        if (denominator >= -Mathf.Epsilon)
        {
            return false;
        }

        float t = Vector3.Dot(planePos - lightPos, planeNormal) / denominator;
        if (t <= 0f)
        {
            return false;
        }

        projectedWorldPoint = lightPos + rayDir * t;
        return true;
    }

    /// <summary>
    /// 平面上のワールド座標を、平面に沿った2D座標へ変換する
    /// </summary>
    public static Vector2 WorldPointToPlane2D(Vector3 worldPoint, Transform plane)
    {
        Vector3 localPos = plane.InverseTransformPoint(worldPoint);

        // UnityのPlaneはローカルXZ平面なので、XYとして使うためにx/zを抽出する
        return new Vector2(localPos.x, localPos.z);
    }

    /// <summary>
    /// 三角形の3頂点を投影し、平面ローカルの2D三角形として返す。
    /// </summary>
    public static bool TryProjectTriangleToPlane2D(
        Vector3 lightPos,
        Vector3 worldVertex0,
        Vector3 worldVertex1,
        Vector3 worldVertex2,
        Transform plane,
        out Vector2[] projectedTriangle)
    {
        projectedTriangle = null;

        if (!TryProjectPointToPlane(lightPos, worldVertex0, plane, out var projectedWorldVertex0)
            || !TryProjectPointToPlane(lightPos, worldVertex1, plane, out var projectedWorldVertex1)
            || !TryProjectPointToPlane(lightPos, worldVertex2, plane, out var projectedWorldVertex2))
        {
            return false;
        }

        projectedTriangle = new[]
        {
            WorldPointToPlane2D(projectedWorldVertex0, plane),
            WorldPointToPlane2D(projectedWorldVertex1, plane),
            WorldPointToPlane2D(projectedWorldVertex2, plane),
        };

        return true;
    }

    /// <summary>
    /// Monotone Chain (Andrew's algorithm) による2D凸包の計算
    /// </summary>
    public static Vector2[] GetConvexHull(List<Vector2> points)
    {
        if (points.Count <= 2) return points.ToArray();

        // X座標優先、次いでY座標でソート
        points.Sort((a, b) => a.x == b.x ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));

        List<Vector2> lower = new List<Vector2>();
        foreach (var p in points)
        {
            while (lower.Count >= 2 && CrossProduct(lower[lower.Count - 2], lower[lower.Count - 1], p) <= 0)
                lower.RemoveAt(lower.Count - 1);
            lower.Add(p);
        }

        List<Vector2> upper = new List<Vector2>();
        for (int i = points.Count - 1; i >= 0; i--)
        {
            var p = points[i];
            while (upper.Count >= 2 && CrossProduct(upper[upper.Count - 2], upper[upper.Count - 1], p) <= 0)
                upper.RemoveAt(upper.Count - 1);
            upper.Add(p);
        }

        // 始点と終点の重複を削除して結合
        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);

        return lower.ToArray();
    }

    // 2D外積（Z成分）: 正なら左回り、負なら右回り
    private static float CrossProduct(Vector2 o, Vector2 a, Vector2 b)
    {
        return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
    }

    public static float GetSignedTriangleArea(Vector2 a, Vector2 b, Vector2 c)
    {
        return CrossProduct(a, b, c) * 0.5f;
    }
}
