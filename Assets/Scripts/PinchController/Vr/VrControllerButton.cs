using System;
using Valve.VR;

// Inspector から選べる VR コントローラーのボタン。
// SteamVR のレガシー入力(EVRButtonId)のうち、Quest Touch で実際に押せるものだけに絞ってある。
// 複数選択(Trigger + Grip など)を許すため [Flags] にしてあり、Inspector 上では
// チェックボックス付きのドロップダウンとして表示される。
[Flags]
public enum VrControllerButton
{
  None = 0,
  // トリガー。
  Trigger = 1 << 0,
  // グリップ(握り込み)。
  Grip = 1 << 1,
  // A / X ボタン。
  AX = 1 << 2,
  // B / Y ボタン。
  BY = 1 << 3,
  // スティック押し込み。
  ThumbstickClick = 1 << 4,
}

// VrControllerButton と OpenVR のボタンビットマスクを相互変換する。
public static class VrControllerButtonMapping
{
  // 選ばれたボタン群を、GetControllerState が返す ulButtonPressed と比較できるビットマスクへ変換する。
  public static ulong ToOpenVrButtonMask(this VrControllerButton buttons)
  {
    var mask = 0ul;
    if ((buttons & VrControllerButton.Trigger) != 0)
    {
      mask |= 1ul << (int)EVRButtonId.k_EButton_SteamVR_Trigger;
    }

    if ((buttons & VrControllerButton.Grip) != 0)
    {
      mask |= 1ul << (int)EVRButtonId.k_EButton_Grip;
    }

    if ((buttons & VrControllerButton.AX) != 0)
    {
      mask |= 1ul << (int)EVRButtonId.k_EButton_A;
    }

    if ((buttons & VrControllerButton.BY) != 0)
    {
      mask |= 1ul << (int)EVRButtonId.k_EButton_ApplicationMenu;
    }

    if ((buttons & VrControllerButton.ThumbstickClick) != 0)
    {
      mask |= 1ul << (int)EVRButtonId.k_EButton_Axis0;
    }

    return mask;
  }
}
