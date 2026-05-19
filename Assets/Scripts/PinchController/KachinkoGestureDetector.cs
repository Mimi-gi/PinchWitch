using UnityEngine;

public sealed class KachinkoGestureDetector
{
  private const int WristIndex = 0;
  private const int IndexMcpIndex = 5;
  private const int MiddleMcpIndex = 9;
  private const int RingMcpIndex = 13;
  private const int PinkyMcpIndex = 17;

  private const int MaxSamples = 16;
  private const float DetectionWindowSeconds = 0.45f;
  private const float MinDownwardDistanceRatio = 0.55f;
  private const float MinDownwardSpeed = 0.45f;
  private const float MinClosingDistanceRatio = 0.35f;
  private const float ContactDistanceRatio = 1.65f;
  private const float StartAboveRightHandRatio = 0.25f;
  private const float MaxRightHandMovementRatio = 0.9f;
  private const float TriggerLatchSeconds = 0.18f;
  private const float TriggerCooldownSeconds = 0.7f;

  private readonly Vector3[] _leftLandmarks = new Vector3[MediaPipeReceiver.HandLandmarkCount];
  private readonly Vector3[] _rightLandmarks = new Vector3[MediaPipeReceiver.HandLandmarkCount];
  private readonly FrameSample[] _samples = new FrameSample[MaxSamples];

  private int _sampleCount;
  private int _nextSampleIndex;
  private int _lastEvaluatedFrame = -1;
  private float _lastDetectedTime = float.NegativeInfinity;

  public bool IsDetected(MediaPipeReceiver mediaPipeReceiver)
  {
    var now = Time.unscaledTime;
    if (_lastEvaluatedFrame == Time.frameCount)
    {
      return IsTriggerLatched(now);
    }

    _lastEvaluatedFrame = Time.frameCount;
    if (mediaPipeReceiver == null
        || !mediaPipeReceiver.TryGetLatestLandmarks(MediaPipeReceiver.TrackedHandType.Left, _leftLandmarks)
        || !mediaPipeReceiver.TryGetLatestLandmarks(MediaPipeReceiver.TrackedHandType.Right, _rightLandmarks))
    {
      ResetSamples();
      return IsTriggerLatched(now);
    }

    var leftPalm = GetPalmCenter(_leftLandmarks);
    var rightPalm = GetPalmCenter(_rightLandmarks);
    var combinedPalmScale = Mathf.Max(GetPalmScale(_leftLandmarks), GetPalmScale(_rightLandmarks));
    if (combinedPalmScale <= Mathf.Epsilon)
    {
      ResetSamples();
      return IsTriggerLatched(now);
    }

    var current = new FrameSample(now, leftPalm, rightPalm, combinedPalmScale);
    AddSample(current);
    if (CanTrigger(now) && HasKachinkoStrike(current))
    {
      _lastDetectedTime = now;
      ResetSamples();
    }

    return IsTriggerLatched(now);
  }

  private bool CanTrigger(float now)
  {
    return now - _lastDetectedTime >= TriggerCooldownSeconds;
  }

  private bool IsTriggerLatched(float now)
  {
    return now - _lastDetectedTime <= TriggerLatchSeconds;
  }

  private bool HasKachinkoStrike(FrameSample current)
  {
    for (var i = 0; i < _sampleCount; i++)
    {
      var start = _samples[GetSampleIndexFromOldest(i)];
      var elapsed = current.time - start.time;
      if (elapsed <= Mathf.Epsilon || elapsed > DetectionWindowSeconds)
      {
        continue;
      }

      var scale = Mathf.Max(start.combinedPalmScale, current.combinedPalmScale);
      var downwardDistance = current.leftPalm.y - start.leftPalm.y;
      if (downwardDistance < scale * MinDownwardDistanceRatio)
      {
        continue;
      }

      if (downwardDistance / elapsed < MinDownwardSpeed)
      {
        continue;
      }

      var closingDistance = start.handDistance - current.handDistance;
      if (closingDistance < scale * MinClosingDistanceRatio)
      {
        continue;
      }

      if (current.handDistance > scale * ContactDistanceRatio)
      {
        continue;
      }

      if (start.leftPalm.y > start.rightPalm.y - (scale * StartAboveRightHandRatio))
      {
        continue;
      }

      if (Vector2.Distance(start.rightPalm, current.rightPalm) > scale * MaxRightHandMovementRatio)
      {
        continue;
      }

      return true;
    }

    return false;
  }

  private void AddSample(FrameSample sample)
  {
    _samples[_nextSampleIndex] = sample;
    _nextSampleIndex = (_nextSampleIndex + 1) % MaxSamples;
    _sampleCount = Mathf.Min(_sampleCount + 1, MaxSamples);
    RemoveExpiredSamples(sample.time);
  }

  private void RemoveExpiredSamples(float now)
  {
    while (_sampleCount > 0)
    {
      var oldest = _samples[GetSampleIndexFromOldest(0)];
      if (now - oldest.time <= DetectionWindowSeconds)
      {
        return;
      }

      _sampleCount--;
    }
  }

  private int GetSampleIndexFromOldest(int offset)
  {
    var oldestIndex = (_nextSampleIndex - _sampleCount + MaxSamples) % MaxSamples;
    return (oldestIndex + offset) % MaxSamples;
  }

  private void ResetSamples()
  {
    _sampleCount = 0;
    _nextSampleIndex = 0;
  }

  private static Vector2 GetPalmCenter(Vector3[] landmarks)
  {
    var sum = landmarks[WristIndex]
              + landmarks[IndexMcpIndex]
              + landmarks[MiddleMcpIndex]
              + landmarks[RingMcpIndex]
              + landmarks[PinkyMcpIndex];
    return new Vector2(sum.x / 5f, sum.y / 5f);
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

  private readonly struct FrameSample
  {
    public readonly float time;
    public readonly Vector2 leftPalm;
    public readonly Vector2 rightPalm;
    public readonly float combinedPalmScale;
    public readonly float handDistance;

    public FrameSample(float time, Vector2 leftPalm, Vector2 rightPalm, float combinedPalmScale)
    {
      this.time = time;
      this.leftPalm = leftPalm;
      this.rightPalm = rightPalm;
      this.combinedPalmScale = combinedPalmScale;
      handDistance = Vector2.Distance(leftPalm, rightPalm);
    }
  }
}
