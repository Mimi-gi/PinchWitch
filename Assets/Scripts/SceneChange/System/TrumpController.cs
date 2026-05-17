using System.Collections;
using UnityEngine;

public sealed class TrumpController : MonoBehaviour
{
  [Header("Trump References")]
  [SerializeField] private Transform trump1;
  [SerializeField] private Transform trump2;
  [SerializeField] private Transform trump3;
  [SerializeField] private Transform trump4;
  [SerializeField] private Transform trump5;
  [SerializeField] private Transform trump6;
  [SerializeField] private Transform trump7;
  [SerializeField] private Transform trump8;
  [SerializeField] private Transform trump9;
  [SerializeField] private Transform trump10;

  [Header("Face Rotation")]
  [SerializeField] private float frontZRotation = 0f;
  [SerializeField] private float backZRotation = 180f;

  [Header("Clear Animation")]
  [SerializeField] private float liftHeight = 0.5f;
  [SerializeField] private float liftDuration = 0.25f;
  [SerializeField] private float flipDuration = 0.45f;
  [SerializeField] private float returnDuration = 0.25f;
  [SerializeField] private AnimationCurve moveCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
  [SerializeField] private AnimationCurve rotationCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

  private Coroutine clearAnimationCoroutine;

  private void Start()
  {
    int newlyClearedStageNum = StageProgressManager.ConsumeLastNewlyClearedStageNum();
    bool shouldAnimate = IsValidStageNum(newlyClearedStageNum)
                         && GetTrump(newlyClearedStageNum) != null
                         && StageProgressManager.IsCleared(newlyClearedStageNum);

    ApplyCurrentProgress(shouldAnimate ? newlyClearedStageNum : 0);

    if (shouldAnimate)
    {
      clearAnimationCoroutine = StartCoroutine(PlayClearAnimation(GetTrump(newlyClearedStageNum)));
    }
  }

  public void Refresh()
  {
    if (clearAnimationCoroutine != null)
    {
      StopCoroutine(clearAnimationCoroutine);
      clearAnimationCoroutine = null;
    }

    ApplyCurrentProgress(0);
  }

  private void ApplyCurrentProgress(int keepBackStageNum)
  {
    for (int stageNum = 1; stageNum <= StageProgressManager.TotalStageCount; stageNum++)
    {
      Transform trump = GetTrump(stageNum);
      if (trump == null)
      {
        continue;
      }

      bool isFaceUp = StageProgressManager.IsCleared(stageNum) && stageNum != keepBackStageNum;
      SetFace(trump, isFaceUp);
    }
  }

  private IEnumerator PlayClearAnimation(Transform trump)
  {
    Vector3 originalPosition = trump.localPosition;
    Vector3 liftedPosition = originalPosition + Vector3.up * liftHeight;

    SetFace(trump, false);
    yield return AnimateLocalPosition(trump, originalPosition, liftedPosition, liftDuration);
    yield return AnimateLocalZRotation(trump, backZRotation, frontZRotation, flipDuration);
    yield return AnimateLocalPosition(trump, liftedPosition, originalPosition, returnDuration);

    trump.localPosition = originalPosition;
    SetFace(trump, true);
    clearAnimationCoroutine = null;
  }

  private IEnumerator AnimateLocalPosition(Transform trump, Vector3 from, Vector3 to, float duration)
  {
    if (duration <= 0f)
    {
      trump.localPosition = to;
      yield break;
    }

    float elapsed = 0f;
    while (elapsed < duration)
    {
      elapsed += Time.deltaTime;
      float t = Mathf.Clamp01(elapsed / duration);
      trump.localPosition = Vector3.LerpUnclamped(from, to, Evaluate(moveCurve, t));
      yield return null;
    }

    trump.localPosition = to;
  }

  private IEnumerator AnimateLocalZRotation(Transform trump, float fromZ, float toZ, float duration)
  {
    if (duration <= 0f)
    {
      SetLocalZRotation(trump, toZ);
      yield break;
    }

    float elapsed = 0f;
    while (elapsed < duration)
    {
      elapsed += Time.deltaTime;
      float t = Mathf.Clamp01(elapsed / duration);
      float z = Mathf.LerpAngle(fromZ, toZ, Evaluate(rotationCurve, t));
      SetLocalZRotation(trump, z);
      yield return null;
    }

    SetLocalZRotation(trump, toZ);
  }

  private void SetFace(Transform trump, bool isFaceUp)
  {
    SetLocalZRotation(trump, isFaceUp ? frontZRotation : backZRotation);
  }

  private static void SetLocalZRotation(Transform trump, float zRotation)
  {
    Vector3 eulerAngles = trump.localEulerAngles;
    eulerAngles.z = zRotation;
    trump.localEulerAngles = eulerAngles;
  }

  private static float Evaluate(AnimationCurve curve, float t)
  {
    return curve != null ? curve.Evaluate(t) : t;
  }

  private Transform GetTrump(int stageNum)
  {
    switch (stageNum)
    {
      case 1:
        return trump1;
      case 2:
        return trump2;
      case 3:
        return trump3;
      case 4:
        return trump4;
      case 5:
        return trump5;
      case 6:
        return trump6;
      case 7:
        return trump7;
      case 8:
        return trump8;
      case 9:
        return trump9;
      case 10:
        return trump10;
      default:
        return null;
    }
  }

  private static bool IsValidStageNum(int stageNum)
  {
    return stageNum >= 1 && stageNum <= 10;
  }

  private void OnValidate()
  {
    liftHeight = Mathf.Max(0f, liftHeight);
    liftDuration = Mathf.Max(0f, liftDuration);
    flipDuration = Mathf.Max(0f, flipDuration);
    returnDuration = Mathf.Max(0f, returnDuration);
  }
}
