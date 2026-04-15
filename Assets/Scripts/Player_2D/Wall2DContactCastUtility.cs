using System;
using UnityEngine;

/// <summary>
/// 2Dプレイヤー周辺で使う Collider2D.Cast の共通処理をまとめた補助クラス。
/// 地面判定や圧死判定で同じ Cast 処理を何度も書かなくて済むようにし、
/// 呼び出し側は「どの法線や向きなら有効か」だけを決めればよいようにする。
/// </summary>
public static class Wall2DContactCastUtility
{
  // 触れてほしくない Trigger を除外し、必要なレイヤーだけに絞ったフィルタを作る。
  public static ContactFilter2D CreateContactFilter(LayerMask layerMask)
  {
    return new ContactFilter2D
    {
      useLayerMask = true,
      layerMask = layerMask,
      useTriggers = false,
    };
  }

  // 指定方向へ Cast して、上向き成分が十分ある接触を「地面候補」として扱う。
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

  // 指定方向へ Cast して、進行方向を塞ぐ障害物があるかを調べる。
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

  // Cast 結果の中から、指定条件を満たす接触が 1 つでもあるかを返す。
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
