using UnityEngine;

public sealed class GoodGestureDetector
{
  private const int WristIndex = 0;
  private const int ThumbMcpIndex = 2;
  private const int ThumbIpIndex = 3;
  private const int ThumbTipIndex = 4;
  private const int IndexMcpIndex = 5;
  private const int IndexPipIndex = 6;
  private const int IndexTipIndex = 8;
  private const int MiddleMcpIndex = 9;
  private const int MiddlePipIndex = 10;
  private const int MiddleTipIndex = 12;
  private const int RingMcpIndex = 13;
  private const int RingPipIndex = 14;
  private const int RingTipIndex = 16;
  private const int PinkyMcpIndex = 17;
  private const int PinkyPipIndex = 18;
  private const int PinkyTipIndex = 20;

  private const float MinThumbExtensionRatio = 0.45f;
  private const float ThumbVerticalDominance = 1.1f;
  private const float ThumbTipAboveFingerMcpRatio = 0.25f;
  private const float FingerFoldToleranceRatio = 0.12f;
  private const float FoldedTipToMcpDistanceMultiplier = 1.25f;

  private readonly Vector3[] _landmarks = new Vector3[MediaPipeReceiver.HandLandmarkCount];

  public bool IsDetected(MediaPipeReceiver mediaPipeReceiver, MediaPipeReceiver.TrackedHandType handType)
  {
    if (mediaPipeReceiver == null)
    {
      return false;
    }

    return mediaPipeReceiver.TryGetLatestLandmarks(handType, _landmarks) && IsThumbsUp(_landmarks);
  }

  private static bool IsThumbsUp(Vector3[] landmarks)
  {
    if (landmarks == null || landmarks.Length < MediaPipeReceiver.HandLandmarkCount)
    {
      return false;
    }

    var palmScale = GetPalmScale(landmarks);
    if (palmScale <= Mathf.Epsilon)
    {
      return false;
    }

    var thumbMcp = landmarks[ThumbMcpIndex];
    var thumbIp = landmarks[ThumbIpIndex];
    var thumbTip = landmarks[ThumbTipIndex];
    var thumbDelta = thumbTip - thumbMcp;
    var thumbUpDistance = thumbMcp.y - thumbTip.y;

    if (thumbUpDistance < palmScale * MinThumbExtensionRatio)
    {
      return false;
    }

    if (thumbUpDistance < Mathf.Abs(thumbDelta.x) * ThumbVerticalDominance)
    {
      return false;
    }

    if (thumbTip.y >= thumbIp.y || thumbTip.y >= thumbMcp.y)
    {
      return false;
    }

    var highestFingerMcpY = Mathf.Min(
      Mathf.Min(landmarks[IndexMcpIndex].y, landmarks[MiddleMcpIndex].y),
      Mathf.Min(landmarks[RingMcpIndex].y, landmarks[PinkyMcpIndex].y));

    if (thumbTip.y > highestFingerMcpY - (palmScale * ThumbTipAboveFingerMcpRatio))
    {
      return false;
    }

    return IsFingerFolded(landmarks, IndexMcpIndex, IndexPipIndex, IndexTipIndex, palmScale)
           && IsFingerFolded(landmarks, MiddleMcpIndex, MiddlePipIndex, MiddleTipIndex, palmScale)
           && IsFingerFolded(landmarks, RingMcpIndex, RingPipIndex, RingTipIndex, palmScale)
           && IsFingerFolded(landmarks, PinkyMcpIndex, PinkyPipIndex, PinkyTipIndex, palmScale);
  }

  private static bool IsFingerFolded(Vector3[] landmarks, int mcpIndex, int pipIndex, int tipIndex, float palmScale)
  {
    var mcp = landmarks[mcpIndex];
    var pip = landmarks[pipIndex];
    var tip = landmarks[tipIndex];
    var foldTolerance = palmScale * FingerFoldToleranceRatio;

    if (tip.y >= pip.y - foldTolerance)
    {
      return true;
    }

    var pipDistance = Distance2D(pip, mcp);
    var tipDistance = Distance2D(tip, mcp);
    return tipDistance <= pipDistance * FoldedTipToMcpDistanceMultiplier;
  }

  private static float GetPalmScale(Vector3[] landmarks)
  {
    var wrist = landmarks[WristIndex];
    var indexMcp = landmarks[IndexMcpIndex];
    var middleMcp = landmarks[MiddleMcpIndex];
    var pinkyMcp = landmarks[PinkyMcpIndex];

    var palmWidth = Distance2D(indexMcp, pinkyMcp);
    var palmHeight = Distance2D(wrist, middleMcp);
    return Mathf.Max(palmWidth, palmHeight);
  }

  private static float Distance2D(Vector3 a, Vector3 b)
  {
    var dx = a.x - b.x;
    var dy = a.y - b.y;
    return Mathf.Sqrt(dx * dx + dy * dy);
  }
}
