using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class ShadowColliderBuilder : MonoBehaviour
{
    [Header("Legacy References")]
    public Transform pointLight;
    public MeshFilter targetCube;
    public Transform projectionWall;
    public PolygonCollider2D shadowRealmCollider;

    [Header("Runtime Toggle")]
    public bool Shadow = true;

    [Header("Auto Setup")]
    [SerializeField] private ShadowProjectionContext context;
    [SerializeField] private Transform sourceRoot;
    [SerializeField] private Transform shadowProxyRoot;
    [SerializeField] private ShadowColliderOutput output;
    [SerializeField] private ShadowOnlyProxy shadowOnlyProxy;
    [SerializeField] private ShadowDirtyTarget shadowDirtyTarget;
    [SerializeField] private bool includeInactiveMeshFilters = true;
    [SerializeField] private bool autoCreateOutput = true;
    [SerializeField] private bool autoCreateShadowOnlyProxy = true;
    [SerializeField] private bool autoCreateDirtyTargetForRigidbodies = true;
    [SerializeField] private bool buildOnEnable = true;
    [SerializeField] private bool rebuildContinuously = true;
    [SerializeField] private bool allowAutomaticBuildOnEnable = false;
    [SerializeField] private bool allowContinuousRuntimeRebuild = false;
    [SerializeField] private float outputWorldZ = 5f;
    [SerializeField] private float minimumTriangleArea = 0.0001f;

    private readonly List<MeshFilter> _meshFilters = new List<MeshFilter>();
    private readonly List<Vector2[]> _trianglePaths = new List<Vector2[]>();
    private int _lastBuildStateHash = int.MinValue;

    public bool IsShadowActive => Shadow;
    public bool HasValidOutput => output != null && output.HasGeometry;
    public bool HasBuiltThisSession => _lastBuildStateHash != int.MinValue;

    public bool NeedsRebuild()
    {
        AutoAssignDefaults();

        if (!HasValidOutput || _lastBuildStateHash == int.MinValue)
        {
            return true;
        }

        if (!TryResolveProjectionReferences(out var resolvedLight, out var resolvedWall))
        {
            return false;
        }

        return ComputeBuildStateHash(resolvedLight, resolvedWall) != _lastBuildStateHash;
    }

    private void Reset()
    {
        AutoAssignDefaults();
        SetupShadow();
    }

    private void Awake()
    {
        AutoAssignDefaults();
        EnsureSetup(autoCreateOutput, false);
        EnsureDirtyTarget();
        RegisterToContext();
    }

    private void OnEnable()
    {
        RegisterToContext();

        if (buildOnEnable && allowAutomaticBuildOnEnable && Shadow)
        {
            BuildNow();
        }
    }

    private void OnDisable()
    {
        UnregisterFromContext();
    }

    private void OnValidate()
    {
        AutoAssignDefaults();

        if (!Application.isPlaying)
        {
            return;
        }

        EnsureSetup(autoCreateOutput, false);

        if (Shadow)
        {
            BuildNow();
        }
    }

    private void LateUpdate()
    {
        if (!allowContinuousRuntimeRebuild || !Shadow || !rebuildContinuously)
        {
            return;
        }

        if (context != null && !context.ShadowEnabled)
        {
            return;
        }

        if (HasBuildDependenciesChanged())
        {
            BuildNow();
        }
    }

    [ContextMenu("Setup Shadow")]
    public void SetupShadow()
    {
        AutoAssignDefaults();
        EnsureSetup(true, autoCreateShadowOnlyProxy);
        EnsureDirtyTarget();

        if (Shadow)
        {
            BuildNow();
        }
    }

    [ContextMenu("Build Shadow Now")]
    public void BuildNow()
    {
        AutoAssignDefaults();

        if (!EnsureSetup(autoCreateOutput, false))
        {
            return;
        }

        if (!TryResolveProjectionReferences(out var resolvedLight, out var resolvedWall))
        {
            Debug.LogWarning("ShadowColliderBuilder requires a point light and projection wall.", this);
            output.Clear();
            return;
        }

        SyncShadowProxyIfNeeded();
        CollectSourceMeshes(_meshFilters);
        if (_meshFilters.Count == 0)
        {
            Debug.LogWarning("ShadowColliderBuilder could not find any MeshFilter to project.", this);
            output.Clear();
            _lastBuildStateHash = ComputeBuildStateHash(resolvedLight, resolvedWall);
            return;
        }

        _trianglePaths.Clear();
        var readableMeshFound = false;
        foreach (var meshFilter in _meshFilters)
        {
            if (meshFilter == null)
            {
                continue;
            }

            var mesh = meshFilter.sharedMesh;
            if (mesh == null)
            {
                continue;
            }

            if (!mesh.isReadable)
            {
                Debug.LogWarning(
                    $"ShadowColliderBuilder skipped mesh '{mesh.name}' because Read/Write is disabled.",
                    meshFilter);
                continue;
            }

            readableMeshFound = true;
            AddProjectedTriangles(meshFilter, mesh, resolvedLight, resolvedWall, output.transform, _trianglePaths);
        }

        if (!readableMeshFound)
        {
            output.Clear();
            Debug.LogWarning("ShadowColliderBuilder could not read any source mesh. Enable Read/Write or assign a shadow proxy mesh.", this);
            _lastBuildStateHash = ComputeBuildStateHash(resolvedLight, resolvedWall);
            return;
        }

        if (_trianglePaths.Count == 0)
        {
            output.Clear();
            _lastBuildStateHash = ComputeBuildStateHash(resolvedLight, resolvedWall);
            return;
        }

        output.ApplyTrianglePaths(_trianglePaths);
        shadowRealmCollider = output.SourcePolygon;
        _lastBuildStateHash = ComputeBuildStateHash(resolvedLight, resolvedWall);
        SetOutputEnabled(Shadow);
    }

    [ContextMenu("Create Shadow Only Proxy")]
    public void CreateShadowOnlyProxy()
    {
        AutoAssignDefaults();
        EnsureSetup(autoCreateOutput, true);
    }

    [ContextMenu("Setup Dirty Tracking")]
    public void SetupDirtyTracking()
    {
        AutoAssignDefaults();
        EnsureDirtyTarget();
    }

    public void SetShadowActive(bool isActive)
    {
        Shadow = isActive;
        SetOutputEnabled(isActive);
    }

    public void SetOutputEnabled(bool isEnabled)
    {
        AutoAssignDefaults();

        if (!EnsureSetup(false, false))
        {
            return;
        }

        if (output == null)
        {
            return;
        }

        var shouldEnableGeometry = isEnabled && output.HasGeometry;
        output.SetGeometryEnabled(shouldEnableGeometry);
        shadowRealmCollider = output.SourcePolygon;
    }

    public void MarkCacheInvalid()
    {
        _lastBuildStateHash = int.MinValue;
    }

    private bool EnsureSetup(bool allowCreateOutput, bool allowCreateShadowProxy)
    {
        if (sourceRoot == null)
        {
            sourceRoot = targetCube != null ? targetCube.transform : transform;
        }

        if (output == null && shadowRealmCollider != null)
        {
            output = shadowRealmCollider.GetComponent<ShadowColliderOutput>();
            if (output == null)
            {
                output = shadowRealmCollider.gameObject.AddComponent<ShadowColliderOutput>();
            }
        }

        if (output == null)
        {
            output = GetComponentInChildren<ShadowColliderOutput>(true);
        }

        if (output == null && allowCreateOutput)
        {
            output = CreateOutputObject();
        }

        if (!TryClaimOutput(output))
        {
            output = null;
        }

        if (output == null && allowCreateOutput)
        {
            output = CreateOutputObject();
        }

        if (output == null)
        {
            return false;
        }

        EnsureShadowProxyConfigured();
        EnsureOutputLivesInGeneratedRoot();
        output.EnsureSetup();
        SyncOutputTransformPosition();
        shadowRealmCollider = output.SourcePolygon;

        if (shadowOnlyProxy == null)
        {
            shadowOnlyProxy = GetComponentInChildren<ShadowOnlyProxy>(true);
        }

        if (shadowDirtyTarget == null)
        {
            shadowDirtyTarget = GetComponent<ShadowDirtyTarget>();
            if (shadowDirtyTarget == null)
            {
                shadowDirtyTarget = GetComponentInParent<ShadowDirtyTarget>();
            }
        }

        if (allowCreateShadowProxy && autoCreateShadowOnlyProxy && shadowOnlyProxy == null)
        {
            shadowOnlyProxy = CreateShadowOnlyProxyHierarchy();
            if (shadowOnlyProxy != null)
            {
                shadowProxyRoot = shadowOnlyProxy.transform;
            }
        }

        return true;
    }

    private void EnsureOutputLivesInGeneratedRoot()
    {
        if (output == null || context == null)
        {
            if (output != null)
            {
                output.gameObject.SetActive(true);
            }

            return;
        }

        var outputRoot = context.EnsureGeneratedOutputRoot();
        if (outputRoot != null && output.transform.parent != outputRoot)
        {
            output.transform.SetParent(outputRoot, true);
        }

        output.gameObject.name = ResolveOutputObjectName(outputRoot);
        output.gameObject.SetActive(true);
    }

    private void AutoAssignDefaults()
    {
        if (context == null)
        {
            context = FindFirstObjectByType<ShadowProjectionContext>(FindObjectsInactive.Include);
        }

        if (sourceRoot == null)
        {
            sourceRoot = targetCube != null ? targetCube.transform : transform;
        }

        if (shadowProxyRoot == null && shadowOnlyProxy != null)
        {
            shadowProxyRoot = shadowOnlyProxy.transform;
        }

        if (shadowOnlyProxy == null && shadowProxyRoot != null)
        {
            shadowOnlyProxy = shadowProxyRoot.GetComponent<ShadowOnlyProxy>();
        }

        if (targetCube == null && sourceRoot != null)
        {
            targetCube = sourceRoot.GetComponent<MeshFilter>();
            if (targetCube == null)
            {
                targetCube = sourceRoot.GetComponentInChildren<MeshFilter>(true);
            }
        }

        if (shadowDirtyTarget == null)
        {
            shadowDirtyTarget = GetComponent<ShadowDirtyTarget>();
            if (shadowDirtyTarget == null)
            {
                shadowDirtyTarget = GetComponentInParent<ShadowDirtyTarget>();
            }
        }
    }

    private void RegisterToContext()
    {
        if (context == null)
        {
            return;
        }

        context.Register(this);
    }

    private void UnregisterFromContext()
    {
        if (context == null)
        {
            return;
        }

        context.Unregister(this);
    }

    private bool TryResolveProjectionReferences(out Transform resolvedLight, out Transform resolvedWall)
    {
        resolvedLight = null;
        resolvedWall = null;

        if (context != null && context.TryGetProjectionReferences(out resolvedLight, out resolvedWall))
        {
            return true;
        }

        resolvedLight = pointLight;
        resolvedWall = projectionWall;
        return resolvedLight != null && resolvedWall != null;
    }

    private void CollectSourceMeshes(List<MeshFilter> buffer)
    {
        buffer.Clear();

        Transform root = shadowProxyRoot != null ? shadowProxyRoot : sourceRoot;
        if (root == null)
        {
            if (targetCube != null)
            {
                buffer.Add(targetCube);
            }

            return;
        }

        var meshFilters = root.GetComponentsInChildren<MeshFilter>(includeInactiveMeshFilters);
        foreach (var meshFilter in meshFilters)
        {
            if (meshFilter != null)
            {
                buffer.Add(meshFilter);
            }
        }
    }

    private void AddProjectedTriangles(
        MeshFilter meshFilter,
        Mesh mesh,
        Transform resolvedLight,
        Transform resolvedWall,
        Transform outputTransform,
        List<Vector2[]> triangleBuffer)
    {
        var vertices = mesh.vertices;
        var triangles = mesh.triangles;
        var localToWorld = meshFilter.transform.localToWorldMatrix;

        for (var triangleIndex = 0; triangleIndex <= triangles.Length - 3; triangleIndex += 3)
        {
            var worldVertex0 = localToWorld.MultiplyPoint3x4(vertices[triangles[triangleIndex]]);
            var worldVertex1 = localToWorld.MultiplyPoint3x4(vertices[triangles[triangleIndex + 1]]);
            var worldVertex2 = localToWorld.MultiplyPoint3x4(vertices[triangles[triangleIndex + 2]]);

            if (!ShadowMathUtility.TryProjectTriangleToPlane2D(
                    resolvedLight.position,
                    worldVertex0,
                    worldVertex1,
                    worldVertex2,
                    resolvedWall,
                    out var projectedTriangle))
            {
                continue;
            }

            var signedArea = ShadowMathUtility.GetSignedTriangleArea(
                projectedTriangle[0],
                projectedTriangle[1],
                projectedTriangle[2]);

            if (Mathf.Abs(signedArea) < minimumTriangleArea)
            {
                continue;
            }

            var colliderTriangle = new Vector2[3];
            for (var pointIndex = 0; pointIndex < projectedTriangle.Length; pointIndex++)
            {
                colliderTriangle[pointIndex] = ConvertPlanePointToOutputLocal(
                    projectedTriangle[pointIndex],
                    resolvedWall,
                    outputTransform);
            }

            // Composite への入力三角形は巻き方向を揃えておく。
            EnsureCounterClockwiseWinding(colliderTriangle);
            triangleBuffer.Add(colliderTriangle);
        }
    }

    private Vector2 ConvertPlanePointToOutputLocal(Vector2 projectedPlanePoint, Transform resolvedWall, Transform outputTransform)
    {
        var worldPointOnPlane = resolvedWall.TransformPoint(new Vector3(projectedPlanePoint.x, 0f, projectedPlanePoint.y));
        var colliderLocalPoint = outputTransform.InverseTransformPoint(worldPointOnPlane);
        return new Vector2(colliderLocalPoint.x, colliderLocalPoint.y);
    }

    private ShadowColliderOutput CreateOutputObject()
    {
        var outputName = $"{ResolveSourceLabel()}_Shadow2DOutput";
        var outputRoot = context != null ? context.EnsureGeneratedOutputRoot() : null;
        var existingOwnedOutput = FindOwnedOutput(outputRoot);
        if (existingOwnedOutput != null)
        {
            return existingOwnedOutput;
        }

        if (outputRoot != null)
        {
            var existingOutputTransform = outputRoot.Find(outputName);
            if (existingOutputTransform != null)
            {
                var existingOutput = existingOutputTransform.GetComponent<ShadowColliderOutput>();
                if (TryClaimOutput(existingOutput))
                {
                    return existingOutput;
                }

                outputName = $"{outputName}_{GetInstanceID()}";
            }
        }

        var outputObject = new GameObject(outputName);
        if (outputRoot != null)
        {
            outputObject.transform.SetParent(outputRoot, false);
        }

        outputObject.transform.position = Vector3.zero;
        outputObject.transform.rotation = Quaternion.identity;
        outputObject.transform.localScale = Vector3.one;

        var createdOutput = outputObject.AddComponent<ShadowColliderOutput>();
        createdOutput.EnsureSetup();
        createdOutput.TryAssignOwner(this);
        SyncOutputTransformPosition(createdOutput.transform);
        return createdOutput;
    }

    private ShadowColliderOutput FindOwnedOutput(Transform outputRoot)
    {
        if (outputRoot == null)
        {
            return null;
        }

        var outputs = outputRoot.GetComponentsInChildren<ShadowColliderOutput>(true);
        foreach (var candidate in outputs)
        {
            if (candidate != null && candidate.OwnerBuilder == this)
            {
                return candidate;
            }
        }

        return null;
    }

    private string ResolveOutputObjectName(Transform outputRoot)
    {
        var baseName = $"{ResolveSourceLabel()}_Shadow2DOutput";
        if (outputRoot == null || output == null)
        {
            return baseName;
        }

        foreach (Transform child in outputRoot)
        {
            if (child == null || child == output.transform || child.name != baseName)
            {
                continue;
            }

            var siblingOutput = child.GetComponent<ShadowColliderOutput>();
            if (siblingOutput == null || siblingOutput.OwnerBuilder == this)
            {
                continue;
            }

            return $"{baseName}_{GetInstanceID()}";
        }

        return baseName;
    }

    private void EnsureShadowProxyConfigured()
    {
        if (shadowProxyRoot == null || shadowProxyRoot == sourceRoot)
        {
            return;
        }

        if (shadowOnlyProxy == null || shadowOnlyProxy.transform != shadowProxyRoot)
        {
            shadowOnlyProxy = shadowProxyRoot.GetComponent<ShadowOnlyProxy>();
        }

        if (shadowOnlyProxy == null)
        {
            shadowOnlyProxy = shadowProxyRoot.gameObject.AddComponent<ShadowOnlyProxy>();
        }

        shadowOnlyProxy.SetupFromSource(sourceRoot, ResolveShadowOnlyProxyLayer());
    }

    private void SyncOutputTransformPosition()
    {
        SyncOutputTransformPosition(output != null ? output.transform : null);
    }

    private void SyncOutputTransformPosition(Transform outputTransform)
    {
        if (outputTransform == null)
        {
            return;
        }

        var currentPosition = outputTransform.position;
        if (Mathf.Approximately(currentPosition.z, outputWorldZ))
        {
            return;
        }

        outputTransform.position = new Vector3(currentPosition.x, currentPosition.y, outputWorldZ);
    }

    private ShadowOnlyProxy CreateShadowOnlyProxyHierarchy()
    {
        if (sourceRoot == null)
        {
            return null;
        }

        var proxyObject = new GameObject($"{ResolveSourceLabel()}_ShadowProxy");
        var proxyTransform = proxyObject.transform;

        if (sourceRoot.parent != null)
        {
            proxyTransform.SetParent(sourceRoot.parent, false);
        }

        proxyTransform.localPosition = sourceRoot.localPosition;
        proxyTransform.localRotation = sourceRoot.localRotation;
        proxyTransform.localScale = sourceRoot.localScale;

        CloneRenderableHierarchy(sourceRoot, proxyTransform, true);

        var createdProxy = proxyObject.AddComponent<ShadowOnlyProxy>();
        createdProxy.SetupFromSource(sourceRoot, ResolveShadowOnlyProxyLayer());
        return createdProxy;
    }

    private void EnsureDirtyTarget()
    {
        if (!autoCreateDirtyTargetForRigidbodies)
        {
            return;
        }

        var targetBody = GetComponent<Rigidbody>();
        if (targetBody == null)
        {
            targetBody = GetComponentInParent<Rigidbody>();
        }

        if (targetBody == null)
        {
            return;
        }

        if (shadowDirtyTarget == null)
        {
            shadowDirtyTarget = targetBody.GetComponent<ShadowDirtyTarget>();
        }

        if (shadowDirtyTarget == null)
        {
            shadowDirtyTarget = targetBody.gameObject.AddComponent<ShadowDirtyTarget>();
        }

        ShadowDirtyRegistry registry = null;
        if (Application.isPlaying)
        {
            registry = ShadowDirtyRegistry.Instance;
        }

        if (registry == null)
        {
            registry = FindFirstObjectByType<ShadowDirtyRegistry>(FindObjectsInactive.Include);
        }

        shadowDirtyTarget.Configure(this, targetBody, registry);
    }

    private void SyncShadowProxyIfNeeded()
    {
        if (shadowOnlyProxy != null)
        {
            shadowOnlyProxy.SyncNow();
            return;
        }

        if (shadowProxyRoot == null)
        {
            return;
        }

        var shadowProxyComponent = shadowProxyRoot.GetComponent<ShadowOnlyProxy>();
        if (shadowProxyComponent != null)
        {
            shadowProxyComponent.SyncNow();
        }
    }

    private void CloneRenderableHierarchy(Transform source, Transform destination, bool isRoot)
    {
        if (!isRoot)
        {
            destination.localPosition = source.localPosition;
            destination.localRotation = source.localRotation;
            destination.localScale = source.localScale;
        }

        destination.gameObject.SetActive(source.gameObject.activeSelf);
        CopyRenderableComponents(source, destination.gameObject);

        foreach (Transform child in source)
        {
            var childClone = new GameObject(child.name).transform;
            childClone.SetParent(destination, false);
            CloneRenderableHierarchy(child, childClone, false);
        }
    }

    private static void CopyRenderableComponents(Transform source, GameObject destination)
    {
        var sourceMeshFilter = source.GetComponent<MeshFilter>();
        if (sourceMeshFilter != null)
        {
            var destinationMeshFilter = destination.GetComponent<MeshFilter>();
            if (destinationMeshFilter == null)
            {
                destinationMeshFilter = destination.AddComponent<MeshFilter>();
            }

            destinationMeshFilter.sharedMesh = sourceMeshFilter.sharedMesh;
        }

        var sourceMeshRenderer = source.GetComponent<MeshRenderer>();
        if (sourceMeshRenderer != null)
        {
            var destinationMeshRenderer = destination.GetComponent<MeshRenderer>();
            if (destinationMeshRenderer == null)
            {
                destinationMeshRenderer = destination.AddComponent<MeshRenderer>();
            }

            destinationMeshRenderer.sharedMaterials = sourceMeshRenderer.sharedMaterials;
            destinationMeshRenderer.shadowCastingMode = sourceMeshRenderer.shadowCastingMode;
            destinationMeshRenderer.receiveShadows = sourceMeshRenderer.receiveShadows;
        }
    }

    private bool HasBuildDependenciesChanged()
    {
        if (!TryResolveProjectionReferences(out var resolvedLight, out var resolvedWall))
        {
            return false;
        }

        return ComputeBuildStateHash(resolvedLight, resolvedWall) != _lastBuildStateHash;
    }

    private int ComputeBuildStateHash(Transform resolvedLight, Transform resolvedWall)
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + GetTransformHash(resolvedLight);
            hash = hash * 31 + GetTransformHash(resolvedWall);

            CollectSourceMeshes(_meshFilters);
            hash = hash * 31 + _meshFilters.Count;
            foreach (var meshFilter in _meshFilters)
            {
                if (meshFilter == null)
                {
                    continue;
                }

                hash = hash * 31 + GetTransformHash(meshFilter.transform);
                hash = hash * 31 + (meshFilter.sharedMesh != null ? meshFilter.sharedMesh.GetInstanceID() : 0);
            }

            return hash;
        }
    }

    private static int GetTransformHash(Transform target)
    {
        if (target == null)
        {
            return 0;
        }

        unchecked
        {
            var matrix = target.localToWorldMatrix;
            var hash = 17;
            for (var row = 0; row < 4; row++)
            {
                for (var column = 0; column < 4; column++)
                {
                    hash = hash * 31 + matrix[row, column].GetHashCode();
                }
            }

            return hash;
        }
    }

    private string ResolveSourceLabel()
    {
        if (sourceRoot != null)
        {
            return sourceRoot.name;
        }

        if (targetCube != null)
        {
            return targetCube.name;
        }

        return name;
    }

    private static void EnsureCounterClockwiseWinding(Vector2[] triangle)
    {
        if (triangle == null || triangle.Length < 3)
        {
            return;
        }

        if (ShadowMathUtility.GetSignedTriangleArea(triangle[0], triangle[1], triangle[2]) >= 0f)
        {
            return;
        }

        (triangle[1], triangle[2]) = (triangle[2], triangle[1]);
    }

    private bool TryClaimOutput(ShadowColliderOutput candidate)
    {
        if (candidate == null)
        {
            return false;
        }

        if (candidate.TryAssignOwner(this))
        {
            return true;
        }

        Debug.LogWarning(
            $"ShadowColliderBuilder on '{name}' skipped output '{candidate.name}' because it is already owned by '{candidate.OwnerBuilder.name}'.",
            this);
        return false;
    }

    private int ResolveShadowOnlyProxyLayer()
    {
        if (context != null)
        {
            return context.ShadowOnlyProxyLayer;
        }

        var layer = LayerMask.NameToLayer("ShadowCaster2D");
        return layer >= 0 ? layer : gameObject.layer;
    }
}
