using UnityEngine;
using System.Collections.Generic;

public class ShadowColliderBuilder : MonoBehaviour
{
    [Header("3D空間の参照")]
    public Transform pointLight;
    public MeshFilter targetCube;
    public Transform projectionWall; // 影を落とす「壁」（Quadなどを想定）

    [Header("2D空間（ShadowRealm）の参照")]
    public PolygonCollider2D shadowRealmCollider; // 配置済みのコライダーに対してローカル座標へ変換して書き込む

    public bool Shadow;

    void Update()
    {
        // ⓪ 影モード切替インプット
        if (Shadow)
        {
            BuildShadowMap();
        }
    }

    void SyncShadowRealmDepth()
    {
        Transform colliderTransform = shadowRealmCollider.transform;
        Vector3 colliderPosition = colliderTransform.position;
        colliderPosition.z = projectionWall.position.z;
        colliderTransform.position = colliderPosition;
    }

    public void BuildShadowMap()
    {
        if (targetCube == null || pointLight == null || projectionWall == null || shadowRealmCollider == null)
        {
            Debug.LogError("参照が設定されておらぬ！切腹せよ！");
            return;
        }

        // 2D世界の実体化位置は projectionWall の奥行きに揃える
        SyncShadowRealmDepth();

        // ワールド座標の頂点配列を取得
        Vector3[] localVertices = targetCube.mesh.vertices;
        Vector3[] worldVertices = new Vector3[localVertices.Length];
        for (int i = 0; i < localVertices.Length; i++)
        {
            worldVertices[i] = targetCube.transform.TransformPoint(localVertices[i]);
        }

        // ① 投影アルゴリズムの実行
        List<Vector3> projectedWorldPoints = ShadowMathUtility.ProjectVerticesToPlane(pointLight.position, worldVertices, projectionWall);

        if (projectedWorldPoints.Count < 3)
        {
            Debug.LogWarning("影が落ちておらぬ！");
            return;
        }

        List<Vector2> projectedPlane2D = new List<Vector2>(projectedWorldPoints.Count);
        foreach (Vector3 projectedWorldPoint in projectedWorldPoints)
        {
            projectedPlane2D.Add(ShadowMathUtility.WorldPointToPlane2D(projectedWorldPoint, projectionWall));
        }

        // ② 凸包アルゴリズムの実行
        Vector2[] convexHullOnPlane = ShadowMathUtility.GetConvexHull(projectedPlane2D);

        Vector2[] colliderLocalPath = new Vector2[convexHullOnPlane.Length];
        for (int i = 0; i < convexHullOnPlane.Length; i++)
        {
            Vector3 worldPointOnPlane = projectionWall.TransformPoint(new Vector3(convexHullOnPlane[i].x, 0f, convexHullOnPlane[i].y));
            Vector3 colliderLocalPoint = shadowRealmCollider.transform.InverseTransformPoint(worldPointOnPlane);
            colliderLocalPath[i] = new Vector2(colliderLocalPoint.x, colliderLocalPoint.y);
        }

        // ③ 影オブジェクトのコライダーを決定（別次元のマップを更新）
        shadowRealmCollider.SetPath(0, colliderLocalPath);
        
        Debug.Log("見事！別次元の2Dマップ構築が完了した！");
        // ※ここでモード切替のフラグを立て、3Dキャラクターを消し、2Dキャラクターをアクティブにする処理を呼ぶのだ！
    }
}
