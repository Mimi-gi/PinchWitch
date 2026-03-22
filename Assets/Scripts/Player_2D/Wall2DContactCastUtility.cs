using System;
using UnityEngine;

/// <summary>
/// 2Dプレイヤー周辺で使う Collider2D.Cast の共通処理をまとめた補助クラス。
/// 地面判定や圧死判定で似たロジックを何度も書かないようにし、
/// 呼び出し側では「どの条件を満たしたら有効な接触か」だけを意識できるようにする。
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

  // 指定方向に Cast して、移動を塞ぐ接触があるか調べる。
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

  // Cast 結果の中から、指定条件を満たす接触があるかを返す共通処理。
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
