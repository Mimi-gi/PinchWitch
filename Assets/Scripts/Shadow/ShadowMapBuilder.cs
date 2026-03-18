using UnityEngine;
using System.Collections.Generic;

public class ShadowColliderBuilder : MonoBehaviour
{
    [Header("3D空間の参照")]
    public Transform pointLight;
    public MeshFilter targetCube;
    public Transform projectionWall; // 影を落とす「壁」（Quadなどを想定）

    [Header("2D空間（ShadowRealm）の参照")]
    public PolygonCollider2D shadowRealmCollider; // 別次元の(0,0,0)に置いたコライダー→自動生成にした方がいいかもしれない。

    public bool Shadow;

    void Update()
    {
        // ⓪ 影モード切替インプット
        if (Shadow)
        {
            BuildShadowMap();
        }
    }

    public void BuildShadowMap()
    {
        if (targetCube == null || pointLight == null || projectionWall == null || shadowRealmCollider == null)
        {
            Debug.LogError("参照が設定されておらぬ！切腹せよ！");
            return;
        }

        // ワールド座標の頂点配列を取得
        Vector3[] localVertices = targetCube.mesh.vertices;
        Vector3[] worldVertices = new Vector3[localVertices.Length];
        for (int i = 0; i < localVertices.Length; i++)
        {
            worldVertices[i] = targetCube.transform.TransformPoint(localVertices[i]);
        }

        // ① 投影アルゴリズムの実行
        List<Vector2> projected2D = ShadowMathUtility.ProjectVerticesToPlane(pointLight.position, worldVertices, projectionWall);

        if (projected2D.Count < 3)
        {
            Debug.LogWarning("影が落ちておらぬ！");
            return;
        }

        // ② 凸包アルゴリズムの実行
        Vector2[] convexHull = ShadowMathUtility.GetConvexHull(projected2D);

        // ③ 影オブジェクトのコライダーを決定（別次元のマップを更新）
        shadowRealmCollider.SetPath(0, convexHull);
        
        Debug.Log("見事！別次元の2Dマップ構築が完了した！");
        // ※ここでモード切替のフラグを立て、3Dキャラクターを消し、2Dキャラクターをアクティブにする処理を呼ぶのだ！
    }
}