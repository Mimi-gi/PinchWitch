using UnityEngine;

// トラッキング空間の姿勢をゲームのワールド空間へ変換する。
//
// 展示ごとにベースステーションの設置位置が変わるため、
// 実行時に「現在のコントローラー位置を中心とみなす」キャリブレーションを行えるようにしてある。
// trackingOrigin の lossyScale は意図せず倍率が二重にかかるため参照せず、
// position と rotation のみを使う。
public sealed class VrTrackingSpaceMapper
{
  public struct Config
  {
    // トラッキング空間の原点として扱う Transform。未指定ならワールド原点。
    public Transform trackingOrigin;
    // トラッキング空間 1m をワールド空間の何ユニットとして扱うか。
    public float positionScale;
  }

  // 変換設定。
  private Config _config;
  // キャリブレーションで記録した中心位置(トラッキング空間)。
  private Vector3 _centerOffset;

  public VrTrackingSpaceMapper(Config config)
  {
    ApplyConfiguration(config);
  }

  // 変換設定を更新する。
  public void ApplyConfiguration(Config config)
  {
    _config = config;
  }

  // 現在のコントローラー位置をトラッキング空間の中心として記録する。
  public void CalibrateCenter(Vector3 trackingPosition)
  {
    _centerOffset = trackingPosition;
  }

  // キャリブレーション結果を破棄する。
  public void ResetCalibration()
  {
    _centerOffset = Vector3.zero;
  }

  // トラッキング空間の絶対位置をワールド空間の位置へ変換する。
  public Vector3 ToWorldPosition(Vector3 trackingPosition)
  {
    var scaled = (trackingPosition - _centerOffset) * ResolveScale();
    if (_config.trackingOrigin == null)
    {
      return scaled;
    }

    return _config.trackingOrigin.position + _config.trackingOrigin.rotation * scaled;
  }

  // トラッキング空間の移動量をワールド空間の移動量へ変換する。
  public Vector3 ToWorldDelta(Vector3 trackingDelta)
  {
    var scaled = trackingDelta * ResolveScale();
    return _config.trackingOrigin == null ? scaled : _config.trackingOrigin.rotation * scaled;
  }

  // トラッキング空間の回転をワールド空間の回転へ変換する。
  public Quaternion ToWorldRotation(Quaternion trackingRotation)
  {
    return _config.trackingOrigin == null
      ? trackingRotation
      : _config.trackingOrigin.rotation * trackingRotation;
  }

  // 倍率が 0 以下だと姿勢が潰れるため下限を設ける。
  private float ResolveScale()
  {
    return Mathf.Max(0.01f, _config.positionScale);
  }
}
