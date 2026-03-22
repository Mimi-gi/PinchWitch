using System;
using UnityEngine;

/// <summary>
/// 2Dプレイヤーまわりで使う Collider2D.Cast の共通処理をまとめた補助クラス。
/// 地面判定や圧死判定で似た処理を何度も書かないようにし、
/// 「どの条件で当たったら有効か」だけを呼び出し側で決められるようにしている。
/// </summary>
public static class Wall2DContactCastUtility
{
  // Trigger を無視しつつ、指定レイヤーだけを対象にする ContactFilter2D を作る。
  public static ContactFilter2D CreateContactFilter(LayerMask layerMask)
  {
    return new ContactFilter2D
    {
      useLayerMask = true,
      layerMask = layerMask,
      useTriggers = false,
    };
  }

  // 指定方向に Cast して、地面として扱える法線を持つ接触があるか調べる。
  public static bool HasHitWithMinimumNormalY(
    Collider2D collider2D,
    Vector2 direction,
    ContactFilter2D contactFilter,
    RaycastHit2D[] hitBuffer,
    float castDistance,
    float minimumNormalY)
  {
    return HasMatchingHit(
      collider2D,
      direction,
      contactFilter,
      hitBuffer,
      castDistance,
      hit => hit.normal.y >= minimumNormalY);
  }

  // 指定方向に Cast して、障害物に押し返されているとみなせる接触があるか調べる。
  public static bool HasBlockingHit(
    Collider2D collider2D,
    Vector2 direction,
    ContactFilter2D contactFilter,
    RaycastHit2D[] hitBuffer,
    float castDistance,
    float minimumContactAlignment)
  {
    var expectedNormal = -direction.normalized;

    return HasMatchingHit(
      collider2D,
      direction,
      contactFilter,
      hitBuffer,
      castDistance,
      hit => Vector2.Dot(hit.normal, expectedNormal) >= minimumContactAlignment);
  }

  // Cast した結果の中から、条件に合う接触が1つでもあるかを返す共通処理。
  private static bool HasMatchingHit(
    Collider2D collider2D,
    Vector2 direction,
    ContactFilter2D contactFilter,
    RaycastHit2D[] hitBuffer,
    float castDistance,
    Func<RaycastHit2D, bool> isMatch)
  {
    if (collider2D == null || hitBuffer == null || isMatch == null)
    {
      return false;
    }

    var hitCount = collider2D.Cast(direction, contactFilter, hitBuffer, castDistance);
    for (var i = 0; i < hitCount; i++)
    {
      var hit = hitBuffer[i];
      if (hit.collider == null)
      {
        continue;
      }

      if (isMatch(hit))
      {
        return true;
      }
    }

    return false;
  }
}
