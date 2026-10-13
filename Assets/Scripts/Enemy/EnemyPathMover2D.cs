using UnityEngine;

// 線(折れ線)に沿って等速で動く敵。
// 経路は「配置位置からの相対座標」の点列で持つので、敵ごと動かせば経路も付いてくる。
// LineRenderer があれば経路をワールド座標で描画する(エディタ上でも表示)。
[DisallowMultipleComponent]
[ExecuteAlways]
public sealed class EnemyPathMover2D : MonoBehaviour, IEnemyMover, IRuntimeResettable
{
    public enum PathMode
    {
        Loop,
        PingPong,
    }

    [SerializeField] private PathMode pathMode = PathMode.PingPong;
    [SerializeField] private float speed = 2f;
    [Tooltip("配置位置からの相対座標。先頭の点から動き始める。")]
    [SerializeField] private Vector2[] points = { Vector2.zero, new Vector2(3f, 0f) };
    [SerializeField] private LineRenderer pathLine;

    [Header("Shadow")]
    [SerializeField] private bool stopWhileShadowCrushed = true;
    [SerializeField] private bool reverseWhenTouchingShadowEdge;
    [SerializeField] private Wall2DPlayerCrushDetector shadowCrushDetector;

    private Vector3 _origin;
    private bool _hasOrigin;
    private float _distance;
    private float _travelDirection = 1f;
    private float _externalSpeedMultiplier = 1f;

    public int ResetOrder => 100;
    public PathMode Mode => pathMode;
    public Vector2[] Points => points;

    /// <summary>
    /// 経路の基準位置。実行中は開始時の位置、エディタでは現在の位置。
    /// </summary>
    public Vector3 Origin => Application.isPlaying && _hasOrigin ? _origin : transform.position;

    public void SetExternalSpeedMultiplier(float multiplier)
    {
        _externalSpeedMultiplier = Mathf.Max(0f, multiplier);
    }

    private void Awake()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        _origin = transform.position;
        _hasOrigin = true;
        if (shadowCrushDetector == null)
        {
            shadowCrushDetector = GetComponent<Wall2DPlayerCrushDetector>();
        }

        ResetRuntimeState();
    }

    private void OnValidate()
    {
        speed = Mathf.Max(0f, speed);
        if (points == null || points.Length < 2)
        {
            points = new[] { Vector2.zero, new Vector2(3f, 0f) };
        }

        RefreshLine();
    }

    private void Update()
    {
        if (!Application.isPlaying)
        {
            RefreshLine();
            return;
        }

        if (stopWhileShadowCrushed && shadowCrushDetector != null && shadowCrushDetector.IsCrushed)
        {
            return;
        }

        var step = speed * _externalSpeedMultiplier * Time.deltaTime;
        if (step <= 0f)
        {
            return;
        }

        if (reverseWhenTouchingShadowEdge
            && shadowCrushDetector != null
            && shadowCrushDetector.IsShadowBlockingDirection(GetCurrentDirection()))
        {
            _travelDirection = -_travelDirection;
        }

        Advance(step);
        transform.position = EvaluatePosition(_distance);
    }

    public void ResetRuntimeState()
    {
        _distance = 0f;
        _travelDirection = 1f;
        _externalSpeedMultiplier = 1f;
        if (_hasOrigin)
        {
            transform.position = EvaluatePosition(0f);
        }

        RefreshLine();
    }

    private void Advance(float step)
    {
        var length = GetPathLength();
        if (length <= 0f)
        {
            return;
        }

        _distance += step * _travelDirection;

        if (pathMode == PathMode.Loop)
        {
            _distance = Mathf.Repeat(_distance, length);
            return;
        }

        // PingPong: 端で折り返す。
        while (_distance < 0f || _distance > length)
        {
            if (_distance > length)
            {
                _distance = length - (_distance - length);
                _travelDirection = -1f;
            }
            else
            {
                _distance = -_distance;
                _travelDirection = 1f;
            }
        }
    }

    private Vector3 EvaluatePosition(float distance)
    {
        var origin = Origin;
        var count = GetSegmentCount();
        for (var i = 0; i < count; i++)
        {
            var from = GetPoint(i);
            var to = GetPoint(i + 1);
            var segmentLength = Vector2.Distance(from, to);
            if (distance <= segmentLength || i == count - 1)
            {
                var t = segmentLength > 0f ? Mathf.Clamp01(distance / segmentLength) : 0f;
                var point = Vector2.Lerp(from, to, t);
                return new Vector3(origin.x + point.x, origin.y + point.y, transform.position.z);
            }

            distance -= segmentLength;
        }

        return transform.position;
    }

    private Vector2 GetCurrentDirection()
    {
        var ahead = EvaluatePosition(Mathf.Clamp(_distance + 0.01f * _travelDirection, 0f, GetPathLength()));
        var direction = (Vector2)(ahead - transform.position);
        return direction.sqrMagnitude > 0f ? direction.normalized : Vector2.zero;
    }

    private float GetPathLength()
    {
        var length = 0f;
        var count = GetSegmentCount();
        for (var i = 0; i < count; i++)
        {
            length += Vector2.Distance(GetPoint(i), GetPoint(i + 1));
        }

        return length;
    }

    // Loop は最後の点から先頭へ戻る区間を含む。
    private int GetSegmentCount()
    {
        if (points == null || points.Length < 2)
        {
            return 0;
        }

        return pathMode == PathMode.Loop ? points.Length : points.Length - 1;
    }

    private Vector2 GetPoint(int index)
    {
        return points[index % points.Length];
    }

    private void RefreshLine()
    {
        if (pathLine == null || points == null || points.Length < 2)
        {
            return;
        }

        var origin = Origin;
        pathLine.useWorldSpace = true;
        pathLine.loop = pathMode == PathMode.Loop;
        pathLine.positionCount = points.Length;
        for (var i = 0; i < points.Length; i++)
        {
            pathLine.SetPosition(i, new Vector3(origin.x + points[i].x, origin.y + points[i].y, origin.z));
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (points == null || points.Length < 2)
        {
            return;
        }

        var origin = Origin;
        Gizmos.color = Color.cyan;
        var count = GetSegmentCount();
        for (var i = 0; i < count; i++)
        {
            var from = GetPoint(i);
            var to = GetPoint(i + 1);
            Gizmos.DrawLine(origin + (Vector3)from, origin + (Vector3)to);
        }
    }
}
