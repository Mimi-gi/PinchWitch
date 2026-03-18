using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Mediapipe;
using Mediapipe.Unity;
using Mediapipe.Unity.HandTracking;
using UnityEngine;

public sealed class MediaPipeReceiver
{
  public const int InteractionHandCount = 1;
  public const int HandLandmarkCount = 21;

  private readonly object _frameLock = new object();
  private readonly Vector3[] _latestLandmarks = new Vector3[HandLandmarkCount];

  private HandTrackingGraph _handTrackingGraph;
  private float _handLostTimeoutSeconds;
  private bool _hasFrame;
  private bool _hasHandOnLatestFrame;
  private long _latestFrameTicks;
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
    bool hasFrame;
    bool hasHand;
    long frameTicks;

    lock (_frameLock)
    {
      hasFrame = _hasFrame;
      hasHand = _hasHandOnLatestFrame;
      frameTicks = _latestFrameTicks;

      if (hasFrame && hasHand)
      {
        System.Array.Copy(_latestLandmarks, frameLandmarks, HandLandmarkCount);
      }
    }

    if (!hasFrame)
    {
      return false;
    }

    var elapsedSeconds = (Stopwatch.GetTimestamp() - frameTicks) / (double)Stopwatch.Frequency;
    return elapsedSeconds <= _handLostTimeoutSeconds && hasHand;
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
    }
    catch (System.NullReferenceException)
    {
      // Graph is already disposed/stopped.
    }

    _isSubscribed = false;
  }

  private void OnHandLandmarksOutput(object stream, OutputEventArgs<List<NormalizedLandmarkList>> eventArgs)
  {
    var hasHand = TrySelectInteractionHand(eventArgs == null ? null : eventArgs.value, out var handLandmarks);

    lock (_frameLock)
    {
      _hasFrame = true;
      _latestFrameTicks = Stopwatch.GetTimestamp();
      _hasHandOnLatestFrame = hasHand;

      if (!hasHand)
      {
        return;
      }

      for (var i = 0; i < HandLandmarkCount; i++)
      {
        var landmark = handLandmarks.Landmark[i];
        _latestLandmarks[i] = new Vector3(landmark.X, landmark.Y, landmark.Z);
      }
    }
  }

  private static bool TrySelectInteractionHand(IList<NormalizedLandmarkList> handLandmarks, out NormalizedLandmarkList selectedHand)
  {
    selectedHand = null;
    if (handLandmarks == null || handLandmarks.Count == 0)
    {
      return false;
    }

    for (var i = 0; i < Mathf.Min(handLandmarks.Count, InteractionHandCount); i++)
    {
      var candidate = handLandmarks[i];
      if (candidate != null && candidate.Landmark != null && candidate.Landmark.Count >= HandLandmarkCount)
      {
        selectedHand = candidate;
        return true;
      }
    }

    return false;
  }
}
