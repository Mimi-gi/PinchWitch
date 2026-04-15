using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class ShadowDirtyRegistry : MonoBehaviour
{
    [SerializeField] private HandPinchController handPinchController;
    [SerializeField] private int maxBuildsPerFrame = 1;

    private readonly Queue<ShadowDirtyTarget> _buildQueue = new Queue<ShadowDirtyTarget>();
    private readonly HashSet<ShadowDirtyTarget> _queuedTargets = new HashSet<ShadowDirtyTarget>();

    public static ShadowDirtyRegistry Instance { get; private set; }

    private void Reset()
    {
        // 参照と設定値を、エディタ上で分かりやすい初期状態に戻す。
        AutoAssignMissingReferences();
        ClampSerializedValues();
    }

    private void Awake()
    {
        // シングルトン的に扱うので、同じ種類の登録先が複数あっても最後に有効なものを使う。
        if (Instance == null || Instance == this)
        {
            Instance = this;
        }

        AutoAssignMissingReferences();
        ClampSerializedValues();
    }

    private void OnEnable()
    {
        AutoAssignMissingReferences();
        SubscribeToGrabEvents();
    }

    private void OnDisable()
    {
        UnsubscribeFromGrabEvents();

        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnValidate()
    {
        AutoAssignMissingReferences();
        ClampSerializedValues();
    }

    private void Update()
    {
        if (handPinchController == null)
        {
            AutoAssignMissingReferences();
            SubscribeToGrabEvents();
        }

        // 1 フレームに処理しすぎないよう、上限数を守りながらキューを消化する。
        var buildCount = 0;
        while (buildCount < maxBuildsPerFrame && _buildQueue.Count > 0)
        {
            var target = _buildQueue.Dequeue();
            if (target == null || !_queuedTargets.Remove(target))
            {
                continue;
            }

            target.BuildDirtyNow();
            buildCount++;
        }
    }

    public void EnqueueDirtyTarget(ShadowDirtyTarget target)
    {
        if (target == null || !_queuedTargets.Add(target))
        {
            return;
        }

        // すでに登録済みの対象は重複投入しない。
        _buildQueue.Enqueue(target);
    }

    public void CancelQueuedBuild(ShadowDirtyTarget target)
    {
        if (target == null)
        {
            return;
        }

        _queuedTargets.Remove(target);
    }

    private void AutoAssignMissingReferences()
    {
        if (handPinchController == null)
        {
            handPinchController = FindFirstObjectByType<HandPinchController>(FindObjectsInactive.Include);
        }
    }

    private void ClampSerializedValues()
    {
        maxBuildsPerFrame = Mathf.Max(1, maxBuildsPerFrame);
    }

    private void SubscribeToGrabEvents()
    {
        if (handPinchController == null)
        {
            return;
        }

        // 再購読前に一度外しておくと、再有効化時の二重登録を避けやすい。
        handPinchController.GrabStarted -= HandleGrabStarted;
        handPinchController.GrabReleased -= HandleGrabReleased;
        handPinchController.GrabStarted += HandleGrabStarted;
        handPinchController.GrabReleased += HandleGrabReleased;
    }

    private void UnsubscribeFromGrabEvents()
    {
        if (handPinchController == null)
        {
            return;
        }

        handPinchController.GrabStarted -= HandleGrabStarted;
        handPinchController.GrabReleased -= HandleGrabReleased;
    }

    private void HandleGrabStarted(Rigidbody grabbedBody)
    {
        // 掴んだ対象が影の再構築対象なら、操作開始時点で待ち行列から外す。
        var target = ResolveTarget(grabbedBody);
        if (target == null)
        {
            return;
        }

        CancelQueuedBuild(target);
        target.BeginManipulation();
    }

    private void HandleGrabReleased(Rigidbody releasedBody)
    {
        // 離したタイミングで、必要なら再構築のきっかけを渡す。
        var target = ResolveTarget(releasedBody);
        if (target == null)
        {
            return;
        }

        target.NotifyReleased();
    }

    private static ShadowDirtyTarget ResolveTarget(Rigidbody body)
    {
        if (body == null)
        {
            return null;
        }

        var directTarget = body.GetComponent<ShadowDirtyTarget>();
        if (directTarget != null)
        {
            return directTarget;
        }

        return body.GetComponentInParent<ShadowDirtyTarget>();
    }
}
