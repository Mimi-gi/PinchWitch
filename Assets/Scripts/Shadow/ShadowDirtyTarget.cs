
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
    [SerializeField] private bool rebuildAfterMotion = true;
    [SerializeField] private float motionPositionThreshold = 0.002f;
    [SerializeField] private float motionRotationThresholdDegrees = 0.2f;
    [SerializeField] private float settleVelocityThreshold = 0.02f;
    [SerializeField] private float settleAngularVelocityThreshold = 1f;
    [SerializeField] private float settleDuration = 0.15f;

    private DirtyState _state;
    private float _settleTimer;
    private bool _hasWarnedAboutBuilderReplacement;
    private Vector3 _lastCleanPosition;
    private Quaternion _lastCleanRotation = Quaternion.identity;
    private Vector3 _lastSettlePosition;
    private Quaternion _lastSettleRotation = Quaternion.identity;
    private bool _hasRecordedCleanTransform;
    private bool _hasRecordedSettleTransform;

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
        RecordCleanTransformState();
    }

    private void OnValidate()
    {
        AutoAssignMissingReferences();
        ClampSerializedValues();
    }

    private void FixedUpdate()
    {
        if (_state == DirtyState.Clean && HasMovedSinceLastCleanState())
        {
            BeginMotionDirty();
            return;
        }

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
        _hasRecordedSettleTransform = false;
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
        RecordSettleTransformState();

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

        if (_state == DirtyState.Queued && !IsSettled())
        {
            _state = DirtyState.WaitingForSettle;
            _settleTimer = 0f;
            return;
        }

        if (builder == null)
        {
            _state = DirtyState.Clean;
            _settleTimer = 0f;
            RecordCleanTransformState();
            return;
        }

        builder.MarkCacheInvalid();
        builder.BuildNow();
        builder.SetOutputEnabled(builder.IsShadowActive);
        _state = DirtyState.Clean;
        _settleTimer = 0f;
        RecordCleanTransformState();
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
        if (!_hasRecordedCleanTransform)
        {
            RecordCleanTransformState();
        }
    }

    private void QueueDirtyBuild()
    {
        AutoAssignMissingReferences();
        _state = DirtyState.Queued;
        _settleTimer = 0f;

        if (registry != null)
        {
            registry.EnqueueDirtyTarget(this);
            return;
        }

        BuildDirtyNow();
    }

    private void BeginMotionDirty()
    {
        AutoAssignMissingReferences();
        _state = DirtyState.WaitingForSettle;
        _settleTimer = 0f;
        RecordSettleTransformState();
        if (builder != null)
        {
            builder.MarkCacheInvalid();
        }

        if (disableOutputWhileDirty && builder != null)
        {
            builder.SetOutputEnabled(false);
        }
    }

    private bool HasMovedSinceLastCleanState()
    {
        if (!rebuildAfterMotion)
        {
            return false;
        }

        if (!_hasRecordedCleanTransform)
        {
            RecordCleanTransformState();
            return false;
        }

        return HasActiveRigidbodyMotion()
            || HasTransformMovedFrom(_lastCleanPosition, _lastCleanRotation, motionPositionThreshold, motionRotationThresholdDegrees);
    }

    private bool IsSettled()
    {
        if (targetRigidbody == null)
        {
            return IsTransformSettled();
        }

        if (!targetRigidbody.IsSleeping())
        {
            var linearVelocityThresholdSquared = settleVelocityThreshold * settleVelocityThreshold;
            var angularVelocityThresholdSquared = settleAngularVelocityThreshold * settleAngularVelocityThreshold;

            if (targetRigidbody.linearVelocity.sqrMagnitude > linearVelocityThresholdSquared
                || targetRigidbody.angularVelocity.sqrMagnitude > angularVelocityThresholdSquared)
            {
                return false;
            }
        }

        return IsTransformSettled();
    }

    private bool HasActiveRigidbodyMotion()
    {
        if (targetRigidbody == null)
        {
            return false;
        }

        if (targetRigidbody.IsSleeping())
        {
            return false;
        }

        var linearVelocityThresholdSquared = settleVelocityThreshold * settleVelocityThreshold;
        var angularVelocityThresholdSquared = settleAngularVelocityThreshold * settleAngularVelocityThreshold;
        return targetRigidbody.linearVelocity.sqrMagnitude > linearVelocityThresholdSquared
            || targetRigidbody.angularVelocity.sqrMagnitude > angularVelocityThresholdSquared;
    }

    private bool IsTransformSettled()
    {
        if (!_hasRecordedSettleTransform)
        {
            RecordSettleTransformState();
            return false;
        }

        var isSettled = !HasTransformMovedFrom(
            _lastSettlePosition,
            _lastSettleRotation,
            motionPositionThreshold,
            motionRotationThresholdDegrees);

        RecordSettleTransformState();
        return isSettled;
    }

    private bool HasTransformMovedFrom(
        Vector3 referencePosition,
        Quaternion referenceRotation,
        float positionThreshold,
        float rotationThresholdDegrees)
    {
        var observedTransform = ResolveObservedTransform();
        if (observedTransform == null)
        {
            return false;
        }

        var positionThresholdSquared = positionThreshold * positionThreshold;
        return (observedTransform.position - referencePosition).sqrMagnitude > positionThresholdSquared
            || Quaternion.Angle(observedTransform.rotation, referenceRotation) > rotationThresholdDegrees;
    }

    private void RecordCleanTransformState()
    {
        var observedTransform = ResolveObservedTransform();
        if (observedTransform == null)
        {
            _hasRecordedCleanTransform = false;
            return;
        }

        _lastCleanPosition = observedTransform.position;
        _lastCleanRotation = observedTransform.rotation;
        _hasRecordedCleanTransform = true;
    }

    private void RecordSettleTransformState()
    {
        var observedTransform = ResolveObservedTransform();
        if (observedTransform == null)
        {
            _hasRecordedSettleTransform = false;
            return;
        }

        _lastSettlePosition = observedTransform.position;
        _lastSettleRotation = observedTransform.rotation;
        _hasRecordedSettleTransform = true;
    }

    private Transform ResolveObservedTransform()
    {
        return targetRigidbody != null ? targetRigidbody.transform : transform;
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
        motionPositionThreshold = Mathf.Max(0f, motionPositionThreshold);
        motionRotationThresholdDegrees = Mathf.Max(0f, motionRotationThresholdDegrees);
    }
}
