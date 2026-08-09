using UnityEngine;

// 光が壁面(2D側)に届いている矩形領域。
// このプロジェクトの2D物理は常にワールドXY平面上で動くため、四隅はワールド座標のX/Yをそのまま使う。
public readonly struct LightLitQuad
{
    public readonly Vector2 A;
    public readonly Vector2 B;
    public readonly Vector2 C;
    public readonly Vector2 D;

    public LightLitQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        A = a;
        B = b;
        C = c;
        D = d;
    }

    public bool ContainsWorldPoint(Vector2 point)
    {
        return PointInTriangle(point, A, B, C) || PointInTriangle(point, A, C, D);
    }

    public Vector2[] ToArray()
    {
        return new[] { A, B, C, D };
    }

    private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        var d1 = Cross(p, a, b);
        var d2 = Cross(p, b, c);
        var d3 = Cross(p, c, a);

        var hasNegative = d1 < 0f || d2 < 0f || d3 < 0f;
        var hasPositive = d1 > 0f || d2 > 0f || d3 > 0f;

        return !(hasNegative && hasPositive);
    }

    private static float Cross(Vector2 p, Vector2 a, Vector2 b)
    {
        return (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
    }
}
