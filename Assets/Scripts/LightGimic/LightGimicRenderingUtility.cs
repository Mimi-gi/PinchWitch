using UnityEngine;
using UnityEngine.Rendering;

// 光ギミック用の半透明マテリアルを、シェーダー未指定時に自動生成するユーティリティ。
public static class LightGimicRenderingUtility
{
    private static Shader _cachedUnlitShader;

    public static Material CreateDefaultTransparentMaterial(Color color)
    {
        var shader = GetUnlitShader();
        if (shader == null)
        {
            return null;
        }

        var material = new Material(shader) { hideFlags = HideFlags.DontSave };
        ConfigureTransparent(material);
        SetColor(material, color);
        return material;
    }

    public static void SetColor(Material material, Color color)
    {
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        else if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
    }

    private static Shader GetUnlitShader()
    {
        if (_cachedUnlitShader == null)
        {
            _cachedUnlitShader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        return _cachedUnlitShader;
    }

    private static void ConfigureTransparent(Material material)
    {
        material.SetOverrideTag("RenderType", "Transparent");
        material.SetFloat("_Surface", 1f); // 0:Opaque 1:Transparent
        material.SetFloat("_Blend", 0f); // Alpha Blend
        material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        // 半透明のままだが、他の不透明3Dオブジェクトと同様に前後関係を深度バッファへ反映させるためZWriteはOnにする。
        material.SetInt("_ZWrite", 1);
        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)RenderQueue.Transparent;
    }
}
