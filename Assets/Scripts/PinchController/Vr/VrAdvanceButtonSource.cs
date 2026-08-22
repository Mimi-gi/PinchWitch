using System.Runtime.InteropServices;
using UnityEngine;
using Valve.VR;

// UI 進行(テキスト送り・決定操作など)に使う VR ボタン入力。
//
// 掴み操作の入力(OpenVrPoseSource)とは別系統にしてある。
// 掴み操作は「現在アクティブな片手」だけを見るが、UI 進行はどちらの手で押しても反応してほしいため、
// 接続中の両手のコントローラーを毎フレーム確認する。
public sealed class VrAdvanceButtonSource
{
  // GetControllerState へ渡す構造体サイズ。
  private static readonly uint ControllerStateSize = (uint)Marshal.SizeOf(typeof(VRControllerState_t));

  // SteamVR への接続の仕方。
  private readonly OpenVrApplicationMode _applicationMode;
  // 対象とするボタン群から変換済みのマスク。
  private readonly ulong _buttonMask;

  // 現在フレームで、いずれかの手の対象ボタンが押されているかどうか。
  private bool _isCurrentlyHeld;
  // 前フレームの状態。押した瞬間の判定に使う。
  private bool _wasHeldLastFrame;

  public VrAdvanceButtonSource(OpenVrApplicationMode applicationMode, VrControllerButton buttons)
  {
    _applicationMode = applicationMode;
    _buttonMask = buttons.ToOpenVrButtonMask();
  }

  // このフレームで新たに押されたかどうか。
  public bool PressedThisFrame { get; private set; }

  // 押され続けているかどうか。
  public bool Held => _isCurrentlyHeld;

  // フレーム先頭で呼び、両手ぶんのボタン状態を更新する。
  public void Poll()
  {
    _wasHeldLastFrame = _isCurrentlyHeld;
    _isCurrentlyHeld = false;

    var system = OpenVrRuntime.TryGetSystem(_applicationMode);
    if (system != null)
    {
      _isCurrentlyHeld =
        IsButtonHeld(system, system.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.LeftHand))
        || IsButtonHeld(system, system.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.RightHand));
    }

    PressedThisFrame = _isCurrentlyHeld && !_wasHeldLastFrame;
  }

  // 一時状態を初期化する。
  public void ResetState()
  {
    _isCurrentlyHeld = false;
    _wasHeldLastFrame = false;
    PressedThisFrame = false;
  }

  private bool IsButtonHeld(CVRSystem system, uint deviceIndex)
  {
    if (deviceIndex >= OpenVR.k_unMaxTrackedDeviceCount
        || system.GetTrackedDeviceClass(deviceIndex) != ETrackedDeviceClass.Controller
        || !system.IsTrackedDeviceConnected(deviceIndex))
    {
      return false;
    }

    var state = default(VRControllerState_t);
    if (!system.GetControllerState(deviceIndex, ref state, ControllerStateSize))
    {
      return false;
    }

    return (state.ulButtonPressed & _buttonMask) != 0;
  }
}
