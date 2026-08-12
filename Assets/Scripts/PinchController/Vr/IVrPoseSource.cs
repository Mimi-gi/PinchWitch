using UnityEngine;

// VR コントローラーの姿勢とボタン入力を供給する抽象。
// 実機(OpenVR)とモック(マウス・キーボード)を差し替えるための境界であり、
// 実装側は「姿勢とボタンを返す」以外の責務を持たない。
//
// 位置と回転はトラッキング空間の値をそのまま返す。
// ワールド空間への変換は VrTrackingSpaceMapper が担当する。
public interface IVrPoseSource
{
  // 姿勢が取得できる状態かどうか。
  bool IsAvailable { get; }

  // 掴み入力がこのフレームで押されたか。
  bool GrabPressedThisFrame { get; }

  // 掴み入力が押され続けているか。
  bool GrabHeld { get; }

  // ステップ回転入力がこのフレームで押されたか。
  bool RotatePressedThisFrame { get; }

  // 回転軸切り替え入力がこのフレームで押されたか。
  bool AxisTogglePressedThisFrame { get; }

  // フレーム先頭で呼び、内部の入力状態を更新する。
  void Poll();

  // トラッキング空間での姿勢を取得する。
  bool TryGetPose(out Vector3 position, out Quaternion rotation);

  // 一時状態を初期化する。
  void ResetState();
}
