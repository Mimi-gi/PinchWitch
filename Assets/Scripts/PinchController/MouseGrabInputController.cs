using System;
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(50)]
public sealed class MouseGrabInputController : MonoBehaviour
{
  public event Action<Rigidbody> GrabStarted;
  public event Action<Rigidbody> GrabReleased;

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

  private MouseGrabController _mouseGrabController;
  private GrabInputContext _grabInputContext;
  private bool _hasGrabInputContext;
  private bool _interactionEnabled = true;
  private GrabInteractor _subscribedGrabInteractor;

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
    UnsubscribeFromGrabInteractor();
  }

  private void OnDestroy()
  {
    ResetInteractionState();
    UnsubscribeFromGrabInteractor();
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
    SubscribeToGrabInteractor(context.GrabInteractor);
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
      enabled = _interactionEnabled && enableMouseInput,
      mouseCamera = mouseCamera,
      fallbackWorldCamera = context.FallbackWorldCamera,
      grabbableLayerMask = context.GrabbableLayerMask,
      liftHeight = mouseLiftHeight,
      raycastDistance = mouseRaycastDistance,
      dragPlaneY = context.DragPlaneY,
      xRotationDegrees = context.XRotationDegrees,
      yRotationDegrees = context.YRotationDegrees,
    };
  }

  public void SetInteractionEnabled(bool isEnabled)
  {
    if (_interactionEnabled == isEnabled)
    {
      return;
    }

    _interactionEnabled = isEnabled;
    if (!_interactionEnabled)
    {
      ResetInteractionState();
    }

    if (_hasGrabInputContext)
    {
      _mouseGrabController?.ApplyConfiguration(BuildMouseConfig(_grabInputContext));
    }
  }

  private void ResetInteractionState()
  {
    _mouseGrabController?.Reset();
    if (_hasGrabInputContext)
    {
      _grabInputContext.InputCoordinator?.Release(GrabInputSource.Pointer);
    }
  }

  private void SubscribeToGrabInteractor(GrabInteractor grabInteractor)
  {
    if (_subscribedGrabInteractor == grabInteractor)
    {
      return;
    }

    UnsubscribeFromGrabInteractor();
    _subscribedGrabInteractor = grabInteractor;
    if (_subscribedGrabInteractor == null)
    {
      return;
    }

    _subscribedGrabInteractor.GrabStarted += HandleGrabStarted;
    _subscribedGrabInteractor.GrabReleased += HandleGrabReleased;
  }

  private void UnsubscribeFromGrabInteractor()
  {
    if (_subscribedGrabInteractor == null)
    {
      return;
    }

    _subscribedGrabInteractor.GrabStarted -= HandleGrabStarted;
    _subscribedGrabInteractor.GrabReleased -= HandleGrabReleased;
    _subscribedGrabInteractor = null;
  }

  private void HandleGrabStarted(Rigidbody grabbedBody)
  {
    GrabStarted?.Invoke(grabbedBody);
  }

  private void HandleGrabReleased(Rigidbody releasedBody)
  {
    GrabReleased?.Invoke(releasedBody);
  }

  private void NormalizeSerializedValues()
  {
    mouseLiftHeight = Mathf.Max(0f, mouseLiftHeight);
    mouseRaycastDistance = Mathf.Max(0.01f, mouseRaycastDistance);
  }
}
