using System;
using UnityEngine;
using UnityEngine.Serialization;

// 掴み・アウトライン表示・入力調停の共有ハブ。
// マウス操作（MouseGrabInputController）はこのクラスが所有する
// GrabInteractor / OutlineController / GrabInputCoordinator を経由して動作する。
[DisallowMultipleComponent]
public class HandPinchController : MonoBehaviour
{
  private const string GrabbableLayerName = "Grabbable";
  private const string GlassLayerName = "Glass";

  public event Action<Rigidbody> GrabStarted;
  public event Action<Rigidbody> GrabReleased;

  [Header("Camera")]
  // マウス操作のフォールバックカメラとして使う基準カメラ。
  [SerializeField] private Camera worldCamera;

  [Header("Grabbing")]
  // 掴めるオブジェクトを探す対象レイヤー。
  [SerializeField] private LayerMask grabbableLayerMask;
  // 掴み対象が動ける X 範囲。
  [SerializeField] private Vector2 xRange = new Vector2(-4.5f, 4.5f);
  // 掴み対象が動ける Y 範囲。
  [SerializeField] private Vector2 yRange = new Vector2(0f, 9f);
  // 掴み対象が動ける Z 範囲。
  [SerializeField] private Vector2 zRange = new Vector2(-4.5f, 4.5f);
  // 解放時に元の isKinematic を復元するかどうか。
  [SerializeField] private bool restoreIsKinematicOnRelease;
  // 解放時に元の useGravity を復元するかどうか。
  [SerializeField] private bool restoreUseGravityOnRelease;

  [Header("Rotation")]
  // 掴み中オブジェクトが回転目標へ追従する速度。
  [SerializeField] private float rotationDegreesPerSecond = 180f;
  [FormerlySerializedAs("xSwipeRotationDegrees")]
  // X 軸回転 1 回ぶんの角度。
  [SerializeField] private float xRotationDegrees = 90f;
  [FormerlySerializedAs("ySwipeRotationDegrees")]
  // Y 軸回転 1 回ぶんの角度。
  [SerializeField] private float yRotationDegrees = 45f;

  [Header("Drag Plane")]
  [FormerlySerializedAs("cursorPlaneY")]
  // マウス追従位置を求める基準平面の Y 座標。
  [SerializeField] private float dragPlaneY;

  // 掴み・追従・解放を担当する制御クラス。
  private GrabInteractor _grabInteractor;
  // アウトラインの表示制御を担当するクラス。
  private OutlineController _outlineController;
  // 複数入力が同時に grab を操作しないようにする共有制御。
  private GrabInputCoordinator _grabInputCoordinator;

  private bool _interactionEnabled = true;

  private void Reset()
  {
    EnsureReferences();
    if (grabbableLayerMask.value == 0)
    {
      grabbableLayerMask = LayerMask.GetMask(GrabbableLayerName, GlassLayerName);
    }

    NormalizeSerializedValues();
  }

  private void Awake()
  {
    EnsureReferences();
    if (grabbableLayerMask.value == 0)
    {
      grabbableLayerMask = LayerMask.GetMask(GrabbableLayerName, GlassLayerName);
    }

    NormalizeSerializedValues();
    InitializeSystems();
    EnsureShadowDirtyRegistryExists();
  }

  private void OnDisable()
  {
    ResetInteractionStateForControllerShutdown();
  }

  private void OnDestroy()
  {
    ResetInteractionStateForControllerShutdown();
    UnsubscribeFromGrabInteractor();
  }

  // Inspector から変更された設定値を反映する。
  private void OnValidate()
  {
    NormalizeSerializedValues();
    RefreshSystems();
  }

  // 物理オブジェクトの追従を FixedUpdate で更新する。
  private void FixedUpdate()
  {
    if (!_interactionEnabled)
    {
      return;
    }

    _grabInteractor?.FixedTick(Time.fixedDeltaTime);
  }

  // 必要なコンポーネント参照を補完する。
  private void EnsureReferences()
  {
    if (worldCamera == null)
    {
      worldCamera = Camera.main;
    }
  }

  private void EnsureShadowDirtyRegistryExists()
  {
    var existingRegistry = FindFirstObjectByType<ShadowDirtyRegistry>(FindObjectsInactive.Include);
    if (existingRegistry != null)
    {
      return;
    }

    var registryObject = new GameObject("ShadowDirtyRegistry");
    registryObject.AddComponent<ShadowDirtyRegistry>();
  }

  // 分割した各責務クラスを初期化する。
  private void InitializeSystems()
  {
    _grabInteractor = new GrabInteractor(BuildGrabConfig());
    _outlineController = new OutlineController();
    _grabInputCoordinator = new GrabInputCoordinator();
    SubscribeToGrabInteractor();
  }

  // 変更された設定を各責務クラスへ反映する。
  private void RefreshSystems()
  {
    _grabInteractor?.ApplyConfiguration(BuildGrabConfig());
  }

  private void SubscribeToGrabInteractor()
  {
    if (_grabInteractor == null)
    {
      return;
    }

    _grabInteractor.GrabStarted -= HandleGrabStarted;
    _grabInteractor.GrabReleased -= HandleGrabReleased;
    _grabInteractor.ReleasedBodyLanded -= HandleReleasedBodyLanded;
    _grabInteractor.GrabStarted += HandleGrabStarted;
    _grabInteractor.GrabReleased += HandleGrabReleased;
    _grabInteractor.ReleasedBodyLanded += HandleReleasedBodyLanded;
  }

  private void UnsubscribeFromGrabInteractor()
  {
    if (_grabInteractor == null)
    {
      return;
    }

    _grabInteractor.GrabStarted -= HandleGrabStarted;
    _grabInteractor.GrabReleased -= HandleGrabReleased;
    _grabInteractor.ReleasedBodyLanded -= HandleReleasedBodyLanded;
  }

  private void HandleGrabStarted(Rigidbody grabbedBody)
  {
    AudioManager.Instance.PlaySE(AudioManager.Instance.SE_Pinch);

    GrabStarted?.Invoke(grabbedBody);
  }

  private void HandleGrabReleased(Rigidbody releasedBody)
  {
    GrabReleased?.Invoke(releasedBody);
  }

  private void HandleReleasedBodyLanded(Rigidbody releasedBody)
  {
    AudioManager.Instance.PlaySE(AudioManager.Instance.SE_Release);
  }

  // 他入力コンポーネントへ grab 操作に必要な共有依存を渡す。
  public bool TryGetGrabInputContext(out GrabInputContext context)
  {
    context = default;
    if (!isActiveAndEnabled || !_interactionEnabled || _grabInteractor == null || _outlineController == null || _grabInputCoordinator == null)
    {
      return false;
    }

    context = new GrabInputContext(
      _grabInteractor,
      _outlineController,
      _grabInputCoordinator,
      worldCamera,
      grabbableLayerMask,
      dragPlaneY,
      xRotationDegrees,
      yRotationDegrees);
    return true;
  }

  public void SetInteractionEnabled(bool isEnabled)
  {
    if (_interactionEnabled == isEnabled)
    {
      return;
    }

    _interactionEnabled = isEnabled;
    if (!isEnabled)
    {
      ResetInteractionStateForControllerShutdown();
    }
  }

  // grab 制御に渡す設定を構築する。
  private GrabInteractor.Config BuildGrabConfig()
  {
    return new GrabInteractor.Config
    {
      grabbableLayerMask = grabbableLayerMask,
      xRange = xRange,
      yRange = yRange,
      zRange = zRange,
      rotationDegreesPerSecond = rotationDegreesPerSecond,
      restoreIsKinematicOnRelease = restoreIsKinematicOnRelease,
      restoreUseGravityOnRelease = restoreUseGravityOnRelease,
    };
  }

  // Inspector から編集された値を安全な範囲に正規化する。
  private void NormalizeSerializedValues()
  {
    NormalizeRange(ref xRange);
    NormalizeRange(ref yRange);
    NormalizeRange(ref zRange);
    rotationDegreesPerSecond = Mathf.Clamp(rotationDegreesPerSecond, 1f, 720f);
    xRotationDegrees = Mathf.Max(0f, xRotationDegrees);
    yRotationDegrees = Mathf.Max(0f, yRotationDegrees);
  }

  // lifecycle 停止時など、grab 中の一時状態を復元可能な状態へ畳む。
  private void ResetInteractionStateForControllerShutdown()
  {
    _grabInteractor?.ForceFinishVerticalDrops();
    _outlineController?.Dispose();
  }

  // Vector2 の最小値と最大値の順序が逆なら入れ替える。
  private static void NormalizeRange(ref Vector2 range)
  {
    if (range.x > range.y)
    {
      var temp = range.x;
      range.x = range.y;
      range.y = temp;
    }
  }
}
