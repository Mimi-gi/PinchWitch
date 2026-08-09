using System;
using System.Collections.Generic;
using UnityEngine;

// シーン内の全 LightRegionOutput(=各ライトの光領域)をまとめて扱う静的レジストリ。
// 複数ライトは同時動作するが、穴あけ・被光判定は全ライトの合算で行う。
public static class LightRegionRegistry
{
    private static readonly List<LightRegionOutput> Regions = new List<LightRegionOutput>();

    public static event Action Changed;

    public static void Register(LightRegionOutput region)
    {
        if (region == null || Regions.Contains(region))
        {
            return;
        }

        Regions.Add(region);
        NotifyChanged();
    }

    public static void Unregister(LightRegionOutput region)
    {
        if (region == null)
        {
            return;
        }

        if (Regions.Remove(region))
        {
            NotifyChanged();
        }
    }

    // 各 LightRegionOutput が光領域を再計算する度に呼び、購読者(足場の穴あけ等)へ変更を知らせる。
    public static void NotifyChanged()
    {
        Changed?.Invoke();
    }

    public static bool IsPointLit(Vector2 worldPoint)
    {
        for (var i = 0; i < Regions.Count; i++)
        {
            var region = Regions[i];
            if (region != null && region.ContainsWorldPoint(worldPoint))
            {
                return true;
            }
        }

        return false;
    }

    public static void CollectAllQuads(List<Vector2[]> buffer)
    {
        buffer.Clear();
        for (var i = 0; i < Regions.Count; i++)
        {
            Regions[i]?.AppendQuads(buffer);
        }
    }
}
