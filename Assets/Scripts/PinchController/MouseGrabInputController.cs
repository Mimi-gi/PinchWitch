using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(50)]
public sealed class MouseGrabInputController : MonoBehaviour
{
  [Header("Source")]
  // grab 共有依存を提供する手操作コントローラー。
  [SerializeField] private HandPinchController handPinchController;

  [Header("Mouse Input")]
  // マウスによる grab 操作を有効にするかどうか。
  [SerializeField] private bool enableMouseInput = true;
  // マウス判定に使うカメラ。未指定なら HandPinchController の worldCamera または Camera.main を使う。
  [SerializeField] private Camera mouseCamera;
  // マウスで掴んだときにオブジェクトを持ち上げる高さ。
  [SerializeField] private float mouseLiftHeight = 2f;
  // マウス Raycast の最大距離。
  [SerializeField] private float mouseRaycastDistance = 100f;
  // ホイール 1 ステップあたりの回転量に掛ける倍率。
  [SerializeField] private float mouseWheelRotationScale = 1f;

  private MouseGrabController _mouseGrabController;
  private GrabInputContext _grabInputContext;
  private bool _hasGrabInputContext;

  private void Reset()
  {
    EnsureReferences();
    NormalizeSerializedValues();
  }

  private void Awake()
  {
    EnsureReferences();
    NormalizeSerializedValues();
  }

  private void OnValidate()
  {
    NormalizeSerializedValues();
    if (_hasGrabInputContext)
    {
      _mouseGrabController?.ApplyConfiguration(BuildMouseConfig(_grabInputContext));
    }
  }

  private void OnDisable()
  {
    ResetInteractionState();
  }

  private void OnDestroy()
  {
    ResetInteractionState();
  }

  private void Update()
  {
    if (!TryPrepareController())
    {
      return;
    }

    _mouseGrabController.Process();
  }

  private bool TryPrepareController()
  {
    EnsureReferences();
    if (handPinchController == null || !handPinchController.TryGetGrabInputContext(out var context))
    {
      ResetInteractionState();
      return false;
    }

    _grabInputContext = context;
    _hasGrabInputContext = true;
    if (_mouseGrabController == null)
    {
      _mouseGrabController = new MouseGrabController(
        context.GrabInteractor,
        context.OutlineController,
        context.InputCoordinator,
        BuildMouseConfig(context));
    }
    else
    {
      _mouseGrabController.UpdateDependencies(context.GrabInteractor, context.OutlineController, context.InputCoordinator);
      _mouseGrabController.ApplyConfiguration(BuildMouseConfig(context));
    }

    return true;
  }

  private void EnsureReferences()
  {
    if (handPinchController == null)
    {
      handPinchController = GetComponent<HandPinchController>();
    }

    if (handPinchController == null)
    {
      handPinchController = FindFirstObjectByType<HandPinchController>();
    }
  }

  private MouseGrabController.Config BuildMouseConfig(GrabInputContext context)
  {
    return new MouseGrabController.Config
    {
      enabled = enableMouseInput,
      mouseCamera = mouseCamera,
      fallbackWorldCamera = context.FallbackWorldCamera,
      grabbableLayerMask = context.GrabbableLayerMask,
      liftHeight = mouseLiftHeight,
      raycastDistance = mouseRaycastDistance,
      dragPlaneY = context.DragPlaneY,
      xRotationDegrees = context.XRotationDegrees,
      yRotationDegrees = context.YRotationDegrees,
      wheelRotationScale = mouseWheelRotationScale,
    };
  }

  private void ResetInteractionState()
  {
    _mouseGrabController?.Reset();
    if (_hasGrabInputContext)
    {
      _grabInputContext.InputCoordinator?.Release(GrabInputSource.Pointer);
    }
  }

  private void NormalizeSerializedValues()
  {
    mouseLiftHeight = Mathf.Max(0f, mouseLiftHeight);
    mouseRaycastDistance = Mathf.Max(0.01f, mouseRaycastDistance);
    mouseWheelRotationScale = Mathf.Max(0f, mouseWheelRotationScale);
  }
}
