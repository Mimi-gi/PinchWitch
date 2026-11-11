using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// ステージ選択で、まだ選べないカードに暗い半透明を重ねる。
// カードの見た目の MeshRenderer それぞれにマテリアルを 1 枠足し、同じ形をもう一度暗く描く。
// 板を別に置く方法と違い、カードの形(角の丸み等)や裏返しの向きに関係なくぴったり重なる。
// カードのメッシュは絵柄より一回り大きく余白をテクスチャの透明度で消しているので、
// 同じテクスチャを重ねる側にも渡し、余白まで暗くならないようにする。
public sealed class StageCardLockOverlay
{
  private static readonly int MaskTexId = Shader.PropertyToID("_MaskTex");

  private readonly MeshRenderer[] _renderers;
  private readonly Material _overlayMaterial;
  private readonly string _cardTextureProperty;
  private readonly MaterialPropertyBlock _propertyBlock = new MaterialPropertyBlock();
  private bool _isShown;

  // cardTextureProperty: カードのマテリアルで絵柄(透明度を含む)を持つテクスチャのプロパティ名。
  public StageCardLockOverlay(StageSelectGrabTarget card, Material overlayMaterial, string cardTextureProperty)
  {
    _overlayMaterial = overlayMaterial;
    _cardTextureProperty = cardTextureProperty;
    _renderers = CollectCardRenderers(card);
  }

  public void SetShown(bool isShown)
  {
    if (_isShown == isShown || _overlayMaterial == null)
    {
      return;
    }

    _isShown = isShown;
    foreach (var cardRenderer in _renderers)
    {
      if (cardRenderer != null)
      {
        if (isShown)
        {
          PassCardTextureToOverlay(cardRenderer);
        }

        cardRenderer.sharedMaterials = isShown
          ? AppendOverlay(cardRenderer.sharedMaterials)
          : RemoveOverlay(cardRenderer.sharedMaterials);
      }
    }
  }

  // 掴み判定用の透明な箱(カードの根元のオブジェクト自身)と影専用のメッシュは除き、カードの絵柄だけを対象にする。
  private static MeshRenderer[] CollectCardRenderers(StageSelectGrabTarget card)
  {
    var renderers = new List<MeshRenderer>();
    if (card == null)
    {
      return renderers.ToArray();
    }

    foreach (var cardRenderer in card.GetComponentsInChildren<MeshRenderer>(true))
    {
      if (cardRenderer.gameObject != card.gameObject
          && cardRenderer.shadowCastingMode != ShadowCastingMode.ShadowsOnly)
      {
        renderers.Add(cardRenderer);
      }
    }

    return renderers.ToArray();
  }

  // アウトライン等ほかの値を消さないよう、既存の PropertyBlock に足す。
  private void PassCardTextureToOverlay(MeshRenderer cardRenderer)
  {
    var cardMaterial = cardRenderer.sharedMaterial;
    if (cardMaterial == null
        || string.IsNullOrEmpty(_cardTextureProperty)
        || !cardMaterial.HasTexture(_cardTextureProperty))
    {
      return;
    }

    var cardTexture = cardMaterial.GetTexture(_cardTextureProperty);
    if (cardTexture == null)
    {
      return;
    }

    cardRenderer.GetPropertyBlock(_propertyBlock);
    _propertyBlock.SetTexture(MaskTexId, cardTexture);
    cardRenderer.SetPropertyBlock(_propertyBlock);
  }

  private Material[] AppendOverlay(Material[] materials)
  {
    var result = new Material[materials.Length + 1];
    materials.CopyTo(result, 0);
    result[materials.Length] = _overlayMaterial;
    return result;
  }

  private Material[] RemoveOverlay(Material[] materials)
  {
    var result = new List<Material>(materials.Length);
    foreach (var material in materials)
    {
      if (material != _overlayMaterial)
      {
        result.Add(material);
      }
    }

    return result.ToArray();
  }
}
