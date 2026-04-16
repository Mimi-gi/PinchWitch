using UnityEngine;

/// <summary>
/// 掴み中のオブジェクトに動的にアタッチされ、
/// 指定レイヤーの他コライダーとの重なりをカウントする。
/// GrabInteractor が AddComponent / Destroy で管理するため、
/// 掴まれる側のオブジェクトに事前アタッチする必要はない。
/// </summary>
public sealed class ColliderDetection : MonoBehaviour
{
  // 重なり検知の対象レイヤーマスク。
  private LayerMask _detectionMask;
  // 現在重なっているコライダーの数。
  private int _overlapCount;

  /// <summary>
  /// 他のコライダーと重なっているかどうかを返す。
  /// </summary>
  public bool IsOverlapping => _overlapCount > 0;

  /// <summary>
  /// 検知対象のレイヤーマスクを設定して初期化する。
  /// </summary>
  public void Initialize(LayerMask detectionMask)
  {
    _detectionMask = detectionMask;
    _overlapCount = 0;
  }

  private void OnTriggerEnter(Collider other)
  {
    if (IsInDetectionMask(other))
    {
      _overlapCount++;
    }
  }

  private void OnTriggerExit(Collider other)
  {
    if (IsInDetectionMask(other))
    {
      _overlapCount--;
    }
  }

  /// <summary>
  /// 対象コライダーが検知レイヤーに含まれるか、または特定のオブジェクトであるかを判定する。
  /// </summary>
  private bool IsInDetectionMask(Collider other)
  {
    // レイヤーマスクに含まれているかチェック
    if ((_detectionMask.value & (1 << other.gameObject.layer)) != 0)
    {
      return true;
    }

    // オブジェクト名が "Plane" の場合も検知対象とする
    return other.gameObject.name == "Plane";
  }
}
