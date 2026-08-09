using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// シーン内の全 ShadowColliderOutput をまとめて扱う静的レジストリ。
/// 光ギミック(LightPassablePlatform2D)がコライダーに開けた穴を無視して、
/// 「本来の影形状」に対する点内包判定を提供する。
/// </summary>
public static class ShadowGeometryRegistry
{
  private static readonly List<ShadowColliderOutput> Outputs = new List<ShadowColliderOutput>();

  public static void Register(ShadowColliderOutput output)
  {
    if (output == null || Outputs.Contains(output))
    {
      return;
    }

    Outputs.Add(output);
  }

  public static void Unregister(ShadowColliderOutput output)
  {
    if (output == null)
    {
      return;
    }

    Outputs.Remove(output);
  }

  /// <summary>
  /// 光の穴あけを無視した影形状に、指定のワールド座標が含まれるか。
  /// </summary>
  public static bool ContainsWorldPoint(Vector2 worldPoint)
  {
    for (var i = 0; i < Outputs.Count; i++)
    {
      var output = Outputs[i];
      if (output != null && output.ContainsWorldPointIgnoringLightHoles(worldPoint))
      {
        return true;
      }
    }

    return false;
  }
}
