using UnityEngine;

[DisallowMultipleComponent]
public sealed class ShadowDirtyTarget : MonoBehaviour
{
    private enum DirtyState
    {
        Clean,
        Held,
        WaitingForSettle,
        Queued,
    }

    [SerializeField] private ShadowColliderBuilder builder;
    [SerializeField] private Rigidbody targetRigidbody;
    [SerializeField] private ShadowDirtyRegistry registry;
    [SerializeField] private bool disableOutputWhileDirty = true;
    [SerializeField] private float settleVelocityThreshold = 0.02f;
    [SerializeField] private float settleAngularVelocityThreshold = 1f;
    [SerializeField] private float settleDuration = 0.15f;

    private DirtyState _state;
    private float _settleTimer;
    private bool _hasWarnedAboutBuilderReplacement;

    public bool IsDirty => _state != DirtyState.Clean;

    private void Reset()
    {
        AutoAssignMissingReferences();
        ClampSerializedValues();
    }

    private void Awake()
    {
        AutoAssignMissingReferences();
        ClampSerializedValues();
    }

    private void OnValidate()
    {
        AutoAssignMissingReferences();
        ClampSerializedValues();
    }

    private void FixedUpdate()
    {
        if (_state != DirtyState.WaitingForSettle)
        {
            return;
        }

        if (!IsSettled())
        {
            _settleTimer = 0f;
            return;
        }

        _settleTimer += Time.fixedDeltaTime;
        if (_settleTimer < settleDuration)
        {
            return;
        }

        QueueDirtyBuild();
    }

    public void BeginManipulation()
    {
        AutoAssignMissingReferences();
        registry?.CancelQueuedBuild(this);
        _state = DirtyState.Held;
        _settleTimer = 0f;
        builder?.MarkCacheInvalid();

        if (disableOutputWhileDirty && builder != null)
        {
            builder.SetOutputEnabled(false);
        }
    }

    public void NotifyReleased()
    {
        AutoAssignMissingReferences();
        _settleTimer = 0f;

        if (targetRigidbody == null)
        {
            QueueDirtyBuild();
            return;
        }

        _state = DirtyState.WaitingForSettle;
    }

    public void BuildDirtyNow()
    {
        AutoAssignMissingReferences();

        if (builder == null)
        {
            _state = DirtyState.Clean;
            _settleTimer = 0f;
            return;
        }

        builder.MarkCacheInvalid();
        builder.BuildNow();
        builder.SetOutputEnabled(builder.IsShadowActive);
        _state = DirtyState.Clean;
        _settleTimer = 0f;
    }

    public void Configure(ShadowColliderBuilder assignedBuilder, Rigidbody assignedRigidbody, ShadowDirtyRegistry assignedRegistry = null)
    {
        if (assignedBuilder != null)
        {
            if (builder != null && builder != assignedBuilder && !_hasWarnedAboutBuilderReplacement)
            {
                Debug.LogWarning(
                    $"ShadowDirtyTarget on '{name}' is being reassigned from '{builder.name}' to '{assignedBuilder.name}'. Only the last builder will be rebuilt by dirty tracking.",
                    this);
                _hasWarnedAboutBuilderReplacement = true;
            }

            builder = assignedBuilder;
        }

        if (assignedRigidbody != null)
        {
            targetRigidbody = assignedRigidbody;
        }

        if (assignedRegistry != null)
        {
            registry = assignedRegistry;
        }

        AutoAssignMissingReferences();
        ClampSerializedValues();
    }

    private void QueueDirtyBuild()
    {
        _state = DirtyState.Queued;
        _settleTimer = 0f;

        if (registry != null)
        {
            registry.EnqueueDirtyTarget(this);
            return;
        }

        BuildDirtyNow();
    }

    private bool IsSettled()
    {
        if (targetRigidbody == null)
        {
            return true;
        }

        if (targetRigidbody.IsSleeping())
        {
            return true;
        }

        var linearVelocityThresholdSquared = settleVelocityThreshold * settleVelocityThreshold;
        var angularVelocityThresholdSquared = settleAngularVelocityThreshold * settleAngularVelocityThreshold;

        return targetRigidbody.linearVelocity.sqrMagnitude <= linearVelocityThresholdSquared
            && targetRigidbody.angularVelocity.sqrMagnitude <= angularVelocityThresholdSquared;
    }

    private void AutoAssignMissingReferences()
    {
        if (builder == null)
        {
            builder = GetComponent<ShadowColliderBuilder>();
            if (builder == null)
            {
                builder = GetComponentInParent<ShadowColliderBuilder>();
            }
        }

        if (targetRigidbody == null)
        {
            targetRigidbody = GetComponent<Rigidbody>();
            if (targetRigidbody == null)
            {
                targetRigidbody = GetComponentInParent<Rigidbody>();
            }
        }

        if (registry == null)
        {
            registry = ShadowDirtyRegistry.Instance;
            if (registry == null)
            {
                registry = FindFirstObjectByType<ShadowDirtyRegistry>(FindObjectsInactive.Include);
            }
        }
    }

    private void ClampSerializedValues()
    {
        settleVelocityThreshold = Mathf.Max(0f, settleVelocityThreshold);
        settleAngularVelocityThreshold = Mathf.Max(0f, settleAngularVelocityThreshold);
        settleDuration = Mathf.Max(0f, settleDuration);
    }
}
