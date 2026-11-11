using UnityEngine;

// 掴み中のステップ回転で使う回転軸の選択状態。
// マウス操作と VR(Stepped)操作で共通に使い、Y → X → Z → Y の順に切り替える。
// 軸はワールド座標の固定軸で、掴んだオブジェクト自身の向きには依存しない。
public sealed class GrabRotationAxisSelector
{
  public enum Axis
  {
    Y,
    X,
    Z,
  }

  // 1 回ぶんの回転角度(度)。
  public struct StepDegrees
  {
    public float x;
    public float y;
    public float z;
  }

  public Axis CurrentAxis { get; private set; } = Axis.Y;

  // 現在の軸のワールド方向。回転はこの軸まわりに正の角度で行う。
  public Vector3 CurrentWorldAxis => ToWorldAxis(CurrentAxis);

  // 掴み直しや中断のときに初期軸(Y)へ戻す。
  public void Reset()
  {
    CurrentAxis = Axis.Y;
  }

  // 次の軸へ切り替える。
  public void Cycle()
  {
    CurrentAxis = CurrentAxis switch
    {
      Axis.Y => Axis.X,
      Axis.X => Axis.Z,
      _ => Axis.Y,
    };
  }

  // 現在の軸で 1 回ぶん回すときの軸と角度を返す。
  public void ResolveStep(StepDegrees degrees, out Vector3 worldAxis, out float angleDegrees)
  {
    worldAxis = CurrentWorldAxis;
    angleDegrees = CurrentAxis switch
    {
      Axis.X => Mathf.Max(0f, degrees.x),
      Axis.Z => Mathf.Max(0f, degrees.z),
      _ => Mathf.Max(0f, degrees.y),
    };
  }

  private static Vector3 ToWorldAxis(Axis axis)
  {
    return axis switch
    {
      Axis.X => Vector3.right,
      Axis.Z => Vector3.forward,
      _ => Vector3.up,
    };
  }
}
