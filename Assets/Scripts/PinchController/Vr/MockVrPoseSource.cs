using UnityEngine;

// 機材が無い状態で VR コントローラーの 6DoF 入力を模擬する疑似ソース。
// マウス移動で平行移動し、修飾キーとの併用で奥行きと回転を操作する。
//
// 操作割り当て(既定値):
//   マウス移動              : トラッキング空間の X/Y 平行移動
//   LeftShift + マウス移動  : X/Z 平行移動(奥行き)
//   LeftAlt   + マウス移動  : Yaw / Pitch 回転
//   ホイール                : Roll 回転
//   G                       : 掴み入力
//   T                       : ステップ回転
//   Y                       : 回転軸切り替え
//   B                       : 姿勢を初期状態へ戻す
//
// マウスの左/中/右ボタンは MouseGrabController とチュートリアル送りが使用しているため、
// 衝突を避けてキーボードへ割り当てている。
public sealed class MockVrPoseSource : IVrPoseSource
{
  private const string MouseXAxis = "Mouse X";
  private const string MouseYAxis = "Mouse Y";
  private const string MouseScrollAxis = "Mouse ScrollWheel";
  private const float MaxPitchDegrees = 89f;

  public struct Config
  {
    // マウス移動 1 単位あたりの平行移動量(m)。
    public float translationSensitivity;
    // 奥行きモード時のマウス縦移動 1 単位あたりの移動量(m)。
    public float depthSensitivity;
    // 回転モード時のマウス移動 1 単位あたりの回転量(度)。
    public float rotationSensitivity;
    // ホイール 1 単位あたりの Roll 回転量(度)。
    public float rollSensitivity;
    // 疑似コントローラーの初期位置(トラッキング空間)。
    public Vector3 initialPosition;
    // 初期位置からの可動範囲(トラッキング空間、各軸の片側)。
    public Vector3 movementExtents;
    // 押している間、マウス縦移動を奥行きに割り当てるキー。
    public KeyCode depthModifierKey;
    // 押している間、マウス移動を回転に割り当てるキー。
    public KeyCode rotationModifierKey;
    // 掴み入力に割り当てるキー。
    public KeyCode grabKey;
    // ステップ回転に割り当てるキー。
    public KeyCode rotateStepKey;
    // 回転軸切り替えに割り当てるキー。
    public KeyCode axisToggleKey;
    // 姿勢を初期状態へ戻すキー。
    public KeyCode resetPoseKey;
  }

  // 疑似コントローラーの設定。
  private Config _config;
  // トラッキング空間での現在位置。
  private Vector3 _position;
  // トラッキング空間での現在の回転(オイラー角)。
  private Vector3 _eulerAngles;

  public MockVrPoseSource(Config config)
  {
    ApplyConfiguration(config);
    ResetPose();
    ResetState();
  }

  public bool IsAvailable => true;
  public bool GrabPressedThisFrame { get; private set; }
  public bool GrabHeld { get; private set; }
  public bool RotatePressedThisFrame { get; private set; }
  public bool AxisTogglePressedThisFrame { get; private set; }

  // 疑似コントローラーの設定を更新する。
  public void ApplyConfiguration(Config config)
  {
    _config = config;
  }

  public void Poll()
  {
    GrabPressedThisFrame = Input.GetKeyDown(_config.grabKey);
    GrabHeld = Input.GetKey(_config.grabKey);
    RotatePressedThisFrame = Input.GetKeyDown(_config.rotateStepKey);
    AxisTogglePressedThisFrame = Input.GetKeyDown(_config.axisToggleKey);

    if (Input.GetKeyDown(_config.resetPoseKey))
    {
      ResetPose();
      return;
    }

    var mouseDeltaX = Input.GetAxisRaw(MouseXAxis);
    var mouseDeltaY = Input.GetAxisRaw(MouseYAxis);

    if (Input.GetKey(_config.rotationModifierKey))
    {
      _eulerAngles.y += mouseDeltaX * _config.rotationSensitivity;
      _eulerAngles.x = Mathf.Clamp(
        _eulerAngles.x - mouseDeltaY * _config.rotationSensitivity,
        -MaxPitchDegrees,
        MaxPitchDegrees);
    }
    else if (Input.GetKey(_config.depthModifierKey))
    {
      _position.x += mouseDeltaX * _config.translationSensitivity;
      _position.z += mouseDeltaY * _config.depthSensitivity;
    }
    else
    {
      _position.x += mouseDeltaX * _config.translationSensitivity;
      _position.y += mouseDeltaY * _config.translationSensitivity;
    }

    _eulerAngles.z += Input.GetAxisRaw(MouseScrollAxis) * _config.rollSensitivity;
    ClampPosition();
  }

  public bool TryGetPose(out Vector3 position, out Quaternion rotation)
  {
    position = _position;
    rotation = Quaternion.Euler(_eulerAngles);
    return true;
  }

  // ボタンの一時状態だけを初期化する。
  // 姿勢は維持する(操作中断のたびに疑似コントローラーが原点へ飛ぶのを避けるため)。
  public void ResetState()
  {
    GrabPressedThisFrame = false;
    GrabHeld = false;
    RotatePressedThisFrame = false;
    AxisTogglePressedThisFrame = false;
  }

  // 姿勢だけを初期状態へ戻す。
  private void ResetPose()
  {
    _position = _config.initialPosition;
    _eulerAngles = Vector3.zero;
  }

  // 疑似コントローラーが初期位置から離れすぎないよう可動範囲へ収める。
  private void ClampPosition()
  {
    var origin = _config.initialPosition;
    var extents = _config.movementExtents;
    _position.x = Mathf.Clamp(_position.x, origin.x - extents.x, origin.x + extents.x);
    _position.y = Mathf.Clamp(_position.y, origin.y - extents.y, origin.y + extents.y);
    _position.z = Mathf.Clamp(_position.z, origin.z - extents.z, origin.z + extents.z);
  }
}
