using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Mediapipe;
using Mediapipe.Unity;
using Mediapipe.Unity.HandTracking;
using UnityEngine;

public sealed class MediaPipeReceiver
{
  public enum TrackedHandType
  {
    Left,
    Right,
  }

  public const int InteractionHandCount = 2;
  public const int HandLandmarkCount = 21;

  private readonly object _frameLock = new object();
  private readonly Vector3[] _latestAnyLandmarks = new Vector3[HandLandmarkCount];
  private readonly Vector3[] _latestLeftLandmarks = new Vector3[HandLandmarkCount];
  private readonly Vector3[] _latestRightLandmarks = new Vector3[HandLandmarkCount];
  private readonly TrackedHandType?[] _latestHandednessByIndex = new TrackedHandType?[InteractionHandCount];

  private HandTrackingGraph _handTrackingGraph;
  private float _handLostTimeoutSeconds;
  private bool _hasAnyHandOnLatestFrame;
  private bool _hasLeftHandOnLatestFrame;
  private bool _hasRightHandOnLatestFrame;
  private long _latestAnyHandTicks;
  private long _latestLeftHandTicks;
  private long _latestRightHandTicks;
  private bool _isSubscribed;
  private Coroutine _subscribeCoroutine;

  public MediaPipeReceiver(HandTrackingGraph handTrackingGraph, float handLostTimeoutSeconds)
  {
    UpdateGraph(handTrackingGraph);
    UpdateSettings(handLostTimeoutSeconds);
  }

  public void UpdateGraph(HandTrackingGraph handTrackingGraph)
  {
    _handTrackingGraph = handTrackingGraph;
    if (_handTrackingGraph != null)
    {
      _handTrackingGraph.maxNumHands = InteractionHandCount;
    }
  }

  public void UpdateSettings(float handLostTimeoutSeconds)
  {
    _handLostTimeoutSeconds = Mathf.Max(0.01f, handLostTimeoutSeconds);
  }

  public void Start(MonoBehaviour host)
  {
    if (host == null || _subscribeCoroutine != null)
    {
      return;
    }

    _subscribeCoroutine = host.StartCoroutine(SubscribeWhenGraphIsReady(host));
  }

  public void Stop(MonoBehaviour host)
  {
    if (_subscribeCoroutine != null && host != null)
    {
      host.StopCoroutine(_subscribeCoroutine);
    }

    _subscribeCoroutine = null;
    UnsubscribeFromGraph();
  }

  public bool TryGetLatestLandmarks(Vector3[] frameLandmarks)
  {
    if (TryGetLatestLandmarks(TrackedHandType.Right, frameLandmarks))
    {
      return true;
    }

    bool hasHand;
    long frameTicks;
    lock (_frameLock)
    {
      hasHand = _hasAnyHandOnLatestFrame;
      frameTicks = _latestAnyHandTicks;
      if (hasHand)
      {
        System.Array.Copy(_latestAnyLandmarks, frameLandmarks, HandLandmarkCount);
      }
    }

    return IsHandFresh(hasHand, frameTicks);
  }

  public bool TryGetLatestLandmarks(TrackedHandType handType, Vector3[] frameLandmarks)
  {
    bool hasHand;
    long frameTicks;

    lock (_frameLock)
    {
      switch (handType)
      {
        case TrackedHandType.Left:
          hasHand = _hasLeftHandOnLatestFrame;
          frameTicks = _latestLeftHandTicks;
          if (hasHand)
          {
            System.Array.Copy(_latestLeftLandmarks, frameLandmarks, HandLandmarkCount);
          }
          break;
        case TrackedHandType.Right:
          hasHand = _hasRightHandOnLatestFrame;
          frameTicks = _latestRightHandTicks;
          if (hasHand)
          {
            System.Array.Copy(_latestRightLandmarks, frameLandmarks, HandLandmarkCount);
          }
          break;
        default:
          hasHand = false;
          frameTicks = 0;
          break;
      }
    }

    return IsHandFresh(hasHand, frameTicks);
  }

  public bool HasFreshHand(TrackedHandType handType)
  {
    bool hasHand;
    long frameTicks;

    lock (_frameLock)
    {
      switch (handType)
      {
        case TrackedHandType.Left:
          hasHand = _hasLeftHandOnLatestFrame;
          frameTicks = _latestLeftHandTicks;
          break;
        case TrackedHandType.Right:
          hasHand = _hasRightHandOnLatestFrame;
          frameTicks = _latestRightHandTicks;
          break;
        default:
          hasHand = false;
          frameTicks = 0;
          break;
      }
    }

    return IsHandFresh(hasHand, frameTicks);
  }

  private bool IsHandFresh(bool hasHand, long frameTicks)
  {
    if (!hasHand)
    {
      return false;
    }

    var elapsedSeconds = (Stopwatch.GetTimestamp() - frameTicks) / (double)Stopwatch.Frequency;
    return elapsedSeconds <= _handLostTimeoutSeconds;
  }

  private IEnumerator SubscribeWhenGraphIsReady(MonoBehaviour host)
  {
    while (!_isSubscribed && host != null && host.isActiveAndEnabled)
    {
      SubscribeToGraph();
      if (_isSubscribed)
      {
        yield break;
      }

      yield return null;
    }
  }

  private void SubscribeToGraph()
  {
    if (_isSubscribed || _handTrackingGraph == null)
    {
      return;
    }

    try
    {
      _handTrackingGraph.OnHandLandmarksOutput += OnHandLandmarksOutput;
      _handTrackingGraph.OnHandednessOutput += OnHandednessOutput;
      _isSubscribed = true;
    }
    catch (System.NullReferenceException)
    {
      _isSubscribed = false;
    }
  }

  private void UnsubscribeFromGraph()
  {
    if (!_isSubscribed || _handTrackingGraph == null)
    {
      return;
    }

    try
    {
      _handTrackingGraph.OnHandLandmarksOutput -= OnHandLandmarksOutput;
      _handTrackingGraph.OnHandednessOutput -= OnHandednessOutput;
    }
    catch (System.NullReferenceException)
    {
      // Graph is already disposed/stopped.
    }

    ResetFrameCache();
    _isSubscribed = false;
  }

  private void OnHandLandmarksOutput(object stream, OutputEventArgs<List<NormalizedLandmarkList>> eventArgs)
  {
    var handLandmarks = eventArgs == null ? null : eventArgs.value;

    lock (_frameLock)
    {
      var timestampTicks = Stopwatch.GetTimestamp();
      _hasAnyHandOnLatestFrame = false;
      _hasLeftHandOnLatestFrame = false;
      _hasRightHandOnLatestFrame = false;

      if (handLandmarks == null || handLandmarks.Count == 0)
      {
        return;
      }

      for (var i = 0; i < Mathf.Min(handLandmarks.Count, InteractionHandCount); i++)
      {
        var candidate = handLandmarks[i];
        if (!IsValidHand(candidate))
        {
          continue;
        }

        if (!_hasAnyHandOnLatestFrame)
        {
          CopyLandmarks(candidate, _latestAnyLandmarks);
          _hasAnyHandOnLatestFrame = true;
          _latestAnyHandTicks = timestampTicks;
        }

        var handedness = ResolveHandednessForIndex(i);
        if (!handedness.HasValue)
        {
          continue;
        }

        if (handedness.Value == TrackedHandType.Left)
        {
          CopyLandmarks(candidate, _latestLeftLandmarks);
          _hasLeftHandOnLatestFrame = true;
          _latestLeftHandTicks = timestampTicks;
        }
        else
        {
          CopyLandmarks(candidate, _latestRightLandmarks);
          _hasRightHandOnLatestFrame = true;
          _latestRightHandTicks = timestampTicks;
        }
      }
    }
  }

  private void OnHandednessOutput(object stream, OutputEventArgs<List<ClassificationList>> eventArgs)
  {
    lock (_frameLock)
    {
      for (var i = 0; i < _latestHandednessByIndex.Length; i++)
      {
        _latestHandednessByIndex[i] = null;
      }

      var handednessLists = eventArgs == null ? null : eventArgs.value;
      if (handednessLists == null)
      {
        return;
      }

      for (var i = 0; i < Mathf.Min(handednessLists.Count, _latestHandednessByIndex.Length); i++)
      {
        _latestHandednessByIndex[i] = ParseHandedness(handednessLists[i]);
      }
    }
  }

  private void ResetFrameCache()
  {
    lock (_frameLock)
    {
      _hasAnyHandOnLatestFrame = false;
      _hasLeftHandOnLatestFrame = false;
      _hasRightHandOnLatestFrame = false;

      for (var i = 0; i < _latestHandednessByIndex.Length; i++)
      {
        _latestHandednessByIndex[i] = null;
      }
    }
  }

  private TrackedHandType? ResolveHandednessForIndex(int handIndex)
  {
    if (handIndex < 0 || handIndex >= _latestHandednessByIndex.Length)
    {
      return null;
    }

    if (_latestHandednessByIndex[handIndex].HasValue)
    {
      return _latestHandednessByIndex[handIndex];
    }

    return null;
  }

  private static TrackedHandType? ParseHandedness(ClassificationList handedness)
  {
    if (handedness == null || handedness.Classification == null || handedness.Classification.Count == 0)
    {
      return null;
    }

    var label = handedness.Classification[0].Label;
    if (string.Equals(label, "Left", System.StringComparison.OrdinalIgnoreCase))
    {
      return TrackedHandType.Left;
    }

    if (string.Equals(label, "Right", System.StringComparison.OrdinalIgnoreCase))
    {
      return TrackedHandType.Right;
    }

    return null;
  }

  private static bool IsValidHand(NormalizedLandmarkList handLandmarks)
  {
    return handLandmarks != null
           && handLandmarks.Landmark != null
           && handLandmarks.Landmark.Count >= HandLandmarkCount;
  }

  private static void CopyLandmarks(NormalizedLandmarkList source, Vector3[] destination)
  {
    for (var i = 0; i < HandLandmarkCount; i++)
    {
      var landmark = source.Landmark[i];
      destination[i] = new Vector3(landmark.X, landmark.Y, landmark.Z);
    }
  }
}
