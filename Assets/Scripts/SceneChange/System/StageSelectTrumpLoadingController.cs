using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

public sealed class StageSelectTrumpLoadingController : MonoBehaviour
{
  private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
  private static readonly int ColorId = Shader.PropertyToID("_Color");
  private static readonly int TintColorId = Shader.PropertyToID("_TintColor");
  private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
  private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

  [Header("UI")]
  [SerializeField] private Canvas loadingCanvas;
  [SerializeField] private CanvasGroup loadingRoot;
  [SerializeField] private RectTransform loadingTrump;
  [SerializeField] private Image loadingTrumpImage;
  [SerializeField] private Sprite[] loadingTrumpSprites = new Sprite[11];
  [SerializeField] private int loadingCanvasSortingOrder = 32000;

  [Header("3D Fade")]
  [SerializeField] private Material transparentFadeMaterial;
  [SerializeField] private float fadeOutDuration = 0.3f;
  [SerializeField] private Ease fadeOutEase = Ease.OutQuad;

  [Header("UI Motion")]
  [SerializeField] private Vector2 startAnchoredPosition = new Vector2(0f, -700f);
  [SerializeField] private Vector2 stopAnchoredPosition = new Vector2(0f, 0f);
  [SerializeField] private float riseDuration = 0.45f;
  [SerializeField] private Ease riseEase = Ease.OutCubic;
  [SerializeField] private float holdDuration = 0.2f;

  private readonly List<Material> fadingMaterials = new List<Material>();
  private Sequence loadingSequence;

  private void Awake()
  {
    ConfigureCanvas();
    HideLoadingTrump();
  }

  private void OnDisable()
  {
    KillSequence();
  }

  public void Play(StageSelectGrabTarget selectedTarget, int destinationStageNum, Action<int> onReadyToLoad)
  {
    if (selectedTarget == null)
    {
      onReadyToLoad?.Invoke(destinationStageNum);
      return;
    }

    KillSequence();
    PrepareLoadingTrump(destinationStageNum);

    loadingSequence = DOTween.Sequence();
    loadingSequence.Append(CreateFadeOutTween(selectedTarget.transform));
    loadingSequence.AppendCallback(ShowLoadingTrump);

    if (loadingTrump != null)
    {
      loadingSequence.Append(loadingTrump.DOAnchorPos(stopAnchoredPosition, riseDuration).SetEase(riseEase));
    }
    else if (riseDuration > 0f)
    {
      loadingSequence.AppendInterval(riseDuration);
    }

    if (holdDuration > 0f)
    {
      loadingSequence.AppendInterval(holdDuration);
    }

    loadingSequence.AppendCallback(() => onReadyToLoad?.Invoke(destinationStageNum));
    loadingSequence.SetLink(gameObject);
  }

  private void ConfigureCanvas()
  {
    if (loadingCanvas == null)
    {
      return;
    }

    loadingCanvas.overrideSorting = true;
    loadingCanvas.sortingOrder = loadingCanvasSortingOrder;
  }

  private void PrepareLoadingTrump(int destinationStageNum)
  {
    HideLoadingTrump();

    if (loadingTrump != null)
    {
      loadingTrump.anchoredPosition = startAnchoredPosition;
    }

    ApplyLoadingTrumpSprite(destinationStageNum);
  }

  private void ShowLoadingTrump()
  {
    if (loadingRoot != null)
    {
      loadingRoot.gameObject.SetActive(true);
      loadingRoot.alpha = 1f;
      loadingRoot.blocksRaycasts = false;
      loadingRoot.interactable = false;
    }

    if (loadingTrump != null)
    {
      loadingTrump.gameObject.SetActive(true);
      loadingTrump.anchoredPosition = startAnchoredPosition;
    }

    if (loadingTrumpImage != null)
    {
      loadingTrumpImage.gameObject.SetActive(true);
    }
  }

  private void HideLoadingTrump()
  {
    if (loadingRoot != null)
    {
      loadingRoot.alpha = 0f;
      loadingRoot.blocksRaycasts = false;
      loadingRoot.interactable = false;
      loadingRoot.gameObject.SetActive(false);
    }

    if (loadingTrump != null)
    {
      loadingTrump.gameObject.SetActive(false);
      loadingTrump.anchoredPosition = startAnchoredPosition;
    }
  }

  private Tween CreateFadeOutTween(Transform selectedTrumpRoot)
  {
    fadingMaterials.Clear();

    Renderer[] renderers = selectedTrumpRoot.GetComponentsInChildren<Renderer>(true);
    for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
    {
      Renderer targetRenderer = renderers[rendererIndex];
      if (targetRenderer == null)
      {
        continue;
      }

      Material[] sourceMaterials = targetRenderer.sharedMaterials;
      Material[] fadeMaterials = new Material[sourceMaterials.Length];
      for (int materialIndex = 0; materialIndex < sourceMaterials.Length; materialIndex++)
      {
        Material sourceMaterial = sourceMaterials[materialIndex];
        Material fadeMaterial = CreateFadeMaterial(sourceMaterial);
        fadeMaterials[materialIndex] = fadeMaterial;

        if (fadeMaterial != null)
        {
          SetMaterialAlpha(fadeMaterial, 1f);
          fadingMaterials.Add(fadeMaterial);
        }
      }

      targetRenderer.materials = fadeMaterials;
    }

    if (fadingMaterials.Count == 0 || fadeOutDuration <= 0f)
    {
      SetFadingMaterialsAlpha(0f);
      return DOTween.Sequence();
    }

    return DOTween.To(() => 1f, SetFadingMaterialsAlpha, 0f, fadeOutDuration).SetEase(fadeOutEase);
  }

  private Material CreateFadeMaterial(Material sourceMaterial)
  {
    if (sourceMaterial == null && transparentFadeMaterial == null)
    {
      return null;
    }

    Material fadeMaterial = new Material(transparentFadeMaterial != null ? transparentFadeMaterial : sourceMaterial);
    if (sourceMaterial != null)
    {
      CopyVisualProperties(sourceMaterial, fadeMaterial);
    }

    return fadeMaterial;
  }

  private void CopyVisualProperties(Material sourceMaterial, Material targetMaterial)
  {
    CopyTexture(sourceMaterial, targetMaterial, BaseMapId);
    CopyTexture(sourceMaterial, targetMaterial, MainTexId);

    if (TryGetColor(sourceMaterial, out Color sourceColor))
    {
      SetMaterialColor(targetMaterial, sourceColor);
    }
  }

  private static void CopyTexture(Material sourceMaterial, Material targetMaterial, int propertyId)
  {
    if (!sourceMaterial.HasProperty(propertyId) || !targetMaterial.HasProperty(propertyId))
    {
      return;
    }

    targetMaterial.SetTexture(propertyId, sourceMaterial.GetTexture(propertyId));
    targetMaterial.SetTextureScale(propertyId, sourceMaterial.GetTextureScale(propertyId));
    targetMaterial.SetTextureOffset(propertyId, sourceMaterial.GetTextureOffset(propertyId));
  }

  private static bool TryGetColor(Material material, out Color color)
  {
    if (material.HasProperty(BaseColorId))
    {
      color = material.GetColor(BaseColorId);
      return true;
    }

    if (material.HasProperty(ColorId))
    {
      color = material.GetColor(ColorId);
      return true;
    }

    if (material.HasProperty(TintColorId))
    {
      color = material.GetColor(TintColorId);
      return true;
    }

    color = Color.white;
    return false;
  }

  private static void SetMaterialColor(Material material, Color color)
  {
    if (material.HasProperty(BaseColorId))
    {
      material.SetColor(BaseColorId, color);
    }

    if (material.HasProperty(ColorId))
    {
      material.SetColor(ColorId, color);
    }

    if (material.HasProperty(TintColorId))
    {
      material.SetColor(TintColorId, color);
    }
  }

  private void SetFadingMaterialsAlpha(float alpha)
  {
    for (int i = 0; i < fadingMaterials.Count; i++)
    {
      SetMaterialAlpha(fadingMaterials[i], alpha);
    }
  }

  private static void SetMaterialAlpha(Material material, float alpha)
  {
    if (material == null)
    {
      return;
    }

    if (!TryGetColor(material, out Color color))
    {
      return;
    }

    color.a = Mathf.Clamp01(alpha);
    SetMaterialColor(material, color);
  }

  private void ApplyLoadingTrumpSprite(int stageNum)
  {
    if (loadingTrumpImage == null)
    {
      return;
    }

    loadingTrumpImage.sprite = ResolveLoadingTrumpSprite(stageNum);

    Color imageColor = loadingTrumpImage.color;
    imageColor.a = 1f;
    loadingTrumpImage.color = imageColor;
  }

  private Sprite ResolveLoadingTrumpSprite(int stageNum)
  {
    if (loadingTrumpSprites == null || loadingTrumpSprites.Length == 0)
    {
      return null;
    }

    if (stageNum >= 1 && stageNum < loadingTrumpSprites.Length && loadingTrumpSprites[stageNum] != null)
    {
      return loadingTrumpSprites[stageNum];
    }

    return loadingTrumpSprites[0];
  }

  private void KillSequence()
  {
    if (loadingSequence == null)
    {
      return;
    }

    loadingSequence.Kill();
    loadingSequence = null;
  }

  private void OnValidate()
  {
    if (loadingTrumpSprites == null || loadingTrumpSprites.Length != 11)
    {
      Array.Resize(ref loadingTrumpSprites, 11);
    }

    loadingCanvasSortingOrder = Mathf.Max(0, loadingCanvasSortingOrder);
    fadeOutDuration = Mathf.Max(0f, fadeOutDuration);
    riseDuration = Mathf.Max(0f, riseDuration);
    holdDuration = Mathf.Max(0f, holdDuration);
  }
}
