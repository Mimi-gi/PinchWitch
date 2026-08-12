using System.Text;
using UnityEngine;

// VR コントローラーの 6DoF（位置 3 軸 + 回転 3 軸）を計測してログに出す。
//
// 実機の腕の可動域を測って positionScale を決めるための道具であり、常用しない前提。
// 毎フレーム出すと Console が埋まって読めなくなるため、一定間隔でまとめて 1 行に集約する。
//
// 瞬時値に加えて軸ごとの最小値・最大値を積み上げるので、
// 「楽に動かせる範囲を一往復させてログを見る」だけで実可動域が分かる。
public sealed class VrPoseDiagnostics
{
  // 出力する数値の桁。位置は mm 相当、角度は 0.1 度相当まで見れば足りる。
  //
  // 符号付き書式("+0.000;-0.000" のような 2 セクション指定)は、負の値が丸めでゼロに
  // なると "-+0.000" のような壊れた出力になる。キャリブレーション後の値はゼロ付近を
  // 常に通るため必ず踏むので、素の F3 と右詰めで桁を揃える。
  private const string PositionFormat = "F3";
  private const string AngleFormat = "F1";
  private const int PositionWidth = 7;
  private const int AngleWidth = 6;

  public struct Config
  {
    // 計測ログを出すかどうか。
    public bool enabled;
    // ログを出す間隔（秒）。
    public float logIntervalSeconds;
    // 累積している可動域を捨てて測り直すキー。
    public KeyCode resetRangeKey;
  }

  // ログ組み立て用の使い回しバッファ。毎回の文字列連結でゴミを出さないため。
  private readonly StringBuilder _builder = new StringBuilder(512);

  // 計測設定。
  private Config _config;
  // トラッキング空間で観測した各軸の最小値・最大値。
  private Vector3 _trackingMin;
  private Vector3 _trackingMax;
  // ワールド空間で観測した各軸の最小値・最大値。
  private Vector3 _worldMin;
  private Vector3 _worldMax;
  // 最小値・最大値を一度でも取り込んだかどうか。
  private bool _hasRange;
  // 次にログを出してよい時刻（Time.realtimeSinceStartup 基準）。
  private float _nextLogTime;

  public VrPoseDiagnostics(Config config)
  {
    ApplyConfiguration(config);
  }

  // 計測設定を更新する。
  public void ApplyConfiguration(Config config)
  {
    _config = config;
  }

  // このフレームの姿勢を取り込む。姿勢が取れたフレームだけ呼ぶ。
  public void Record(
    string sourceDescription,
    Vector3 trackingPosition,
    Quaternion trackingRotation,
    Vector3 worldPosition)
  {
    if (!_config.enabled)
    {
      return;
    }

    if (Input.GetKeyDown(_config.resetRangeKey))
    {
      ResetRange();
      Debug.Log("[VR 6DoF] 可動域の計測をリセットしました。");
    }

    AccumulateRange(trackingPosition, worldPosition);

    if (Time.realtimeSinceStartup < _nextLogTime)
    {
      return;
    }

    _nextLogTime = Time.realtimeSinceStartup + Mathf.Max(0.1f, _config.logIntervalSeconds);
    Debug.Log(BuildReport(sourceDescription, trackingPosition, trackingRotation, worldPosition));
  }

  // 積み上げた可動域を捨てる。キャリブレーション直後などに呼ぶ。
  public void ResetRange()
  {
    _hasRange = false;
    _trackingMin = Vector3.zero;
    _trackingMax = Vector3.zero;
    _worldMin = Vector3.zero;
    _worldMax = Vector3.zero;
  }

  // 各軸の最小値・最大値を更新する。
  private void AccumulateRange(Vector3 trackingPosition, Vector3 worldPosition)
  {
    if (!_hasRange)
    {
      _hasRange = true;
      _trackingMin = trackingPosition;
      _trackingMax = trackingPosition;
      _worldMin = worldPosition;
      _worldMax = worldPosition;
      return;
    }

    _trackingMin = Vector3.Min(_trackingMin, trackingPosition);
    _trackingMax = Vector3.Max(_trackingMax, trackingPosition);
    _worldMin = Vector3.Min(_worldMin, worldPosition);
    _worldMax = Vector3.Max(_worldMax, worldPosition);
  }

  // Console の 1 行目に要約、以降に詳細が出るよう組み立てる。
  private string BuildReport(
    string sourceDescription,
    Vector3 trackingPosition,
    Quaternion trackingRotation,
    Vector3 worldPosition)
  {
    var angles = ToSignedEulerAngles(trackingRotation);
    var trackingSpan = _trackingMax - _trackingMin;
    var worldSpan = _worldMax - _worldMin;

    _builder.Clear();
    _builder.Append("[VR 6DoF] ").Append(sourceDescription)
      .Append(" | 可動域 x").Append(Meters(trackingSpan.x))
      .Append(" y").Append(Meters(trackingSpan.y))
      .Append(" z").Append(Meters(trackingSpan.z))
      .Append(" m");

    _builder.AppendLine().Append("  位置 tracking (m)  x ").Append(Meters(trackingPosition.x))
      .Append("   y ").Append(Meters(trackingPosition.y))
      .Append("   z ").Append(Meters(trackingPosition.z));

    _builder.AppendLine().Append("  回転 (deg)     pitch ").Append(Degrees(angles.x))
      .Append("   yaw ").Append(Degrees(angles.y))
      .Append("  roll ").Append(Degrees(angles.z));

    _builder.AppendLine().Append("  位置 world         x ").Append(Meters(worldPosition.x))
      .Append("   y ").Append(Meters(worldPosition.y))
      .Append("   z ").Append(Meters(worldPosition.z));

    AppendAxisRange("  可動域 tracking (m)", _trackingMin, _trackingMax, trackingSpan);
    AppendAxisRange("  可動域 world (unit)", _worldMin, _worldMax, worldSpan);

    return _builder.ToString();
  }

  // 「x 0.834 [ -0.311 ..   0.523]」の形で 3 軸ぶんを書き出す。
  private void AppendAxisRange(string label, Vector3 min, Vector3 max, Vector3 span)
  {
    _builder.AppendLine().Append(label);
    AppendAxis(" x ", min.x, max.x, span.x);
    AppendAxis(" y ", min.y, max.y, span.y);
    AppendAxis(" z ", min.z, max.z, span.z);
  }

  private void AppendAxis(string label, float min, float max, float span)
  {
    _builder.Append(label)
      .Append(Meters(span))
      .Append(" [").Append(Meters(min))
      .Append(" ..").Append(Meters(max))
      .Append(']');
  }

  // 桁を揃えて長さを一定にする。1 秒に 1 回しか通らないため PadLeft の確保は許容する。
  private static string Meters(float value)
  {
    return value.ToString(PositionFormat).PadLeft(PositionWidth);
  }

  private static string Degrees(float value)
  {
    return value.ToString(AngleFormat).PadLeft(AngleWidth);
  }

  // Quaternion.eulerAngles は 0..360 で返るため、読みやすい -180..180 へ畳む。
  private static Vector3 ToSignedEulerAngles(Quaternion rotation)
  {
    var angles = rotation.eulerAngles;
    return new Vector3(
      ToSignedAngle(angles.x),
      ToSignedAngle(angles.y),
      ToSignedAngle(angles.z));
  }

  private static float ToSignedAngle(float degrees)
  {
    return degrees > 180f ? degrees - 360f : degrees;
  }
}
