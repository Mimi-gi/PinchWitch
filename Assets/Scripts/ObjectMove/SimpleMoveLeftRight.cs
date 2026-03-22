using UnityEngine;

public class SimpleMoveLeftRight : MonoBehaviour
{
    public float speed = 2f;      // 動く速さ
    public float moveRange = 3f;  // 中心からどこまで動くか

    private Vector3 startPos;

    void Start()
    {
        startPos = transform.position;
    }

    void Update()
    {
        float x = Mathf.Sin(Time.time * speed) * moveRange;
        transform.position = new Vector3(startPos.x + x, startPos.y, startPos.z);
    }
}