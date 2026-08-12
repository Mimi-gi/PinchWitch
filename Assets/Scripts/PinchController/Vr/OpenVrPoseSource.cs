using System.Runtime.InteropServices;
using UnityEngine;
using Valve.VR;

// Vr モードで優先的に使うコントローラーの手。
public enum OpenVrControllerHand
{
  // 右手のコントローラーを優先する。
  Right,
  // 左手のコントローラーを優先する。
  Left,
}

// 実機コントローラーの姿勢とボタン入力を OpenVR (SteamVR) 経由で供給するソース。
// Meta Quest は Quest Link で PC に接続して SteamVR を経由することで、
// Lighthouse 系(Vive / Index)と同じ SteamVR コントローラーとして扱える。
//
// ボタン割り当て(SteamVR レガシー入力。Quest Touch コントローラーの場合):
//   トリガー / グリップ : 掴み入力
//   A / X ボタン        : ステップ回転
//   B / Y ボタン        : 回転軸切り替え
public sealed class OpenVrPoseSource : IVrPoseSource
{
  public struct Config
  {
    // 優先的に使う手。未接続なら逆の手、それも無ければ接続中の任意のコントローラーを使う。
    public OpenVrControllerHand preferredHand;
    // SteamVR への接続の仕方。
    public OpenVrApplicationMode applicationMode;
  }

  // 掴み入力として扱うボタン群。トリガーでもグリップでも掴めるようにしてある。
  private const ulong GrabButtonMask =
    (1ul << (int)EVRButtonId.k_EButton_SteamVR_Trigger) | (1ul << (int)EVRButtonId.k_EButton_Grip);
  // ステップ回転に割り当てるボタン(Quest Touch の A / X)。
  private const ulong RotateButtonMask = 1ul << (int)EVRButtonId.k_EButton_A;
  // 回転軸切り替えに割り当てるボタン(Quest Touch の B / Y)。
  private const ulong AxisToggleButtonMask = 1ul << (int)EVRButtonId.k_EButton_ApplicationMenu;

  // GetControllerState へ渡す構造体サイズ。
  private static readonly uint ControllerStateSize = (uint)Marshal.SizeOf(typeof(VRControllerState_t));

  // 全トラッキングデバイスの姿勢を受ける使い回しバッファ。
  private readonly TrackedDevicePose_t[] _poses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];

  // 接続設定。
  private Config _config;
  // 現在フレームのボタン押下マスク。
  private ulong _currentButtonMask;
  // 前フレームのボタン押下マスク。押した瞬間の判定に使う。
  private ulong _previousButtonMask;
  // 最後に取得できた姿勢(トラッキング空間)。
  private Vector3 _position;
  private Quaternion _rotation = Quaternion.identity;
  // 一度でも有効な姿勢を取得できたかどうか。
  private bool _hasPose;
  // 現在コントローラーが接続されているかどうか。
  private bool _isControllerConnected;
  // 現在値を取っているデバイス番号。未接続なら k_unTrackedDeviceIndexInvalid。
  private uint _deviceIndex = OpenVR.k_unTrackedDeviceIndexInvalid;
  // 現在値を取っているデバイスの手。
  private ETrackedControllerRole _deviceRole = ETrackedControllerRole.Invalid;

  public OpenVrPoseSource(Config config)
  {
    ApplyConfiguration(config);
  }

  // トラッキングが一瞬途切れたフレーム(遮蔽など)は直前の姿勢を返し続け、
  // 掴み中のオブジェクトが即座に手放されるのを避ける。切断されたときだけ利用不可にする。
  public bool IsAvailable => _isControllerConnected && _hasPose;

  public string SourceDescription
  {
    get
    {
      if (!_isControllerConnected)
      {
        return "OpenVR (コントローラー未検出)";
      }

      return $"OpenVR {DescribeRole(_deviceRole)} #{_deviceIndex}";
    }
  }

  public bool GrabPressedThisFrame => IsPressedThisFrame(GrabButtonMask);
  public bool GrabHeld => (_currentButtonMask & GrabButtonMask) != 0;
  public bool RotatePressedThisFrame => IsPressedThisFrame(RotateButtonMask);
  public bool AxisTogglePressedThisFrame => IsPressedThisFrame(AxisToggleButtonMask);

  // 接続設定を更新する。
  public void ApplyConfiguration(Config config)
  {
    _config = config;
  }

  public void Poll()
  {
    _previousButtonMask = _currentButtonMask;

    var system = OpenVrRuntime.TryGetSystem(_config.applicationMode);
    if (system == null)
    {
      ClearDeviceState();
      return;
    }

    var deviceIndex = ResolveControllerIndex(system);
    if (deviceIndex == OpenVR.k_unTrackedDeviceIndexInvalid)
    {
      ClearDeviceState();
      return;
    }

    _isControllerConnected = true;
    _deviceIndex = deviceIndex;
    _deviceRole = system.GetControllerRoleForTrackedDeviceIndex(deviceIndex);
    UpdatePose(system, deviceIndex);
    UpdateButtons(system, deviceIndex);
  }

  public bool TryGetPose(out Vector3 position, out Quaternion rotation)
  {
    position = _position;
    rotation = _rotation;
    return IsAvailable;
  }

  // ボタンの一時状態だけを初期化する。姿勢キャッシュは維持する。
  public void ResetState()
  {
    _currentButtonMask = 0;
    _previousButtonMask = 0;
  }

  // 指定したボタン群のいずれかが、このフレームで新たに押されたかどうか。
  private bool IsPressedThisFrame(ulong buttonMask)
  {
    return (_currentButtonMask & buttonMask) != 0 && (_previousButtonMask & buttonMask) == 0;
  }

  // SteamVR 未接続・コントローラー未接続のフレームの状態。
  private void ClearDeviceState()
  {
    _isControllerConnected = false;
    _currentButtonMask = 0;
    _deviceIndex = OpenVR.k_unTrackedDeviceIndexInvalid;
    _deviceRole = ETrackedControllerRole.Invalid;
  }

  // 診断ログ用に手の名前を短く表す。
  private static string DescribeRole(ETrackedControllerRole role)
  {
    switch (role)
    {
      case ETrackedControllerRole.LeftHand:
        return "左手";
      case ETrackedControllerRole.RightHand:
        return "右手";
      default:
        return "手不明";
    }
  }

  // 使用するコントローラーのデバイス番号を解決する。
  // 優先する手 → 逆の手 → 接続中の任意のコントローラー、の順で探す。
  private uint ResolveControllerIndex(CVRSystem system)
  {
    var preferredRole = _config.preferredHand == OpenVrControllerHand.Left
      ? ETrackedControllerRole.LeftHand
      : ETrackedControllerRole.RightHand;
    var fallbackRole = preferredRole == ETrackedControllerRole.LeftHand
      ? ETrackedControllerRole.RightHand
      : ETrackedControllerRole.LeftHand;

    var preferredIndex = system.GetTrackedDeviceIndexForControllerRole(preferredRole);
    if (IsConnectedController(system, preferredIndex))
    {
      return preferredIndex;
    }

    var fallbackIndex = system.GetTrackedDeviceIndexForControllerRole(fallbackRole);
    if (IsConnectedController(system, fallbackIndex))
    {
      return fallbackIndex;
    }

    for (var deviceIndex = 0u; deviceIndex < OpenVR.k_unMaxTrackedDeviceCount; deviceIndex++)
    {
      if (IsConnectedController(system, deviceIndex))
      {
        return deviceIndex;
      }
    }

    return OpenVR.k_unTrackedDeviceIndexInvalid;
  }

  private static bool IsConnectedController(CVRSystem system, uint deviceIndex)
  {
    return deviceIndex < OpenVR.k_unMaxTrackedDeviceCount
      && system.GetTrackedDeviceClass(deviceIndex) == ETrackedDeviceClass.Controller
      && system.IsTrackedDeviceConnected(deviceIndex);
  }

  // 姿勢を更新する。座標変換(右手系→左手系)は openvr_api.cs 同梱のヘルパーに任せる。
  private void UpdatePose(CVRSystem system, uint deviceIndex)
  {
    system.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, 0f, _poses);
    var pose = _poses[deviceIndex];
    if (!pose.bPoseIsValid)
    {
      return;
    }

    _position = pose.mDeviceToAbsoluteTracking.GetPosition();
    _rotation = pose.mDeviceToAbsoluteTracking.GetRotation();
    _hasPose = true;
  }

  // ボタン押下状態を更新する。
  // 他のシーンアプリが入力フォーカスを持っていると取得に失敗するため、そのときは全ボタン解放として扱う。
  private void UpdateButtons(CVRSystem system, uint deviceIndex)
  {
    var state = default(VRControllerState_t);
    _currentButtonMask = system.GetControllerState(deviceIndex, ref state, ControllerStateSize)
      ? state.ulButtonPressed
      : 0ul;
  }
}
