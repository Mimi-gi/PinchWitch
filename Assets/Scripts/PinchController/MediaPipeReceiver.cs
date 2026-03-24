using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Mediapipe;
using Mediapipe.Unity;
using Mediapipe.Unity.HandTracking;
using UnityEngine;

public sealed class MediaPipeReceiver
{
  private const double HandednessPairingToleranceSeconds = 0.02d;

  public enum TrackedHandType
  {
    Left,
    Right,
  }

  public const int InteractionHandCount = 2;
  public const int HandLandmarkCount = 21;

  private readonly object _frameLock = new object();
  private readonly Vector3[] _latestLeftLandmarks = new Vector3[HandLandmarkCount];
  private readonly Vector3[] _latestRightLandmarks = new Vector3[HandLandmarkCount];
  private readonly TrackedHandType?[] _latestHandednessByIndex = new TrackedHandType?[InteractionHandCount];

  private HandTrackingGraph _handTrackingGraph;
  private float _handLostTimeoutSeconds;
  private bool _hasLeftHandOnLatestFrame;
  private bool _hasRightHandOnLatestFrame;
  private long _latestLeftHandTicks;
  private long _latestRightHandTicks;
  private long _latestHandednessTicks;
  private bool _isSubscribed;
  private Coroutine _subscribeCoroutine;
  private MonoBehaviour _subscriptionHost;

  // 受信対象の graph とタイムアウト設定を初期化する。
  public MediaPipeReceiver(HandTrackingGraph handTrackingGraph, float handLostTimeoutSeconds)
  {
    UpdateGraph(handTrackingGraph);
    UpdateSettings(handLostTimeoutSeconds);
  }

  // 対象の HandTrackingGraph を更新する。
  public void UpdateGraph(HandTrackingGraph handTrackingGraph)
  {
    if (ReferenceEquals(_handTrackingGraph, handTrackingGraph))
    {
      if (_handTrackingGraph != null)
      {
        _handTrackingGraph.maxNumHands = InteractionHandCount;
      }

      return;
    }

    var shouldResumeSubscription = _isSubscribed || _subscribeCoroutine != null;
    StopPendingSubscription(_subscriptionHost);
    if (_isSubscribed)
    {
      DetachFromGraph(_handTrackingGraph);
      _isSubscribed = false;
    }

    _handTrackingGraph = handTrackingGraph;
    ResetFrameCache();
    if (_handTrackingGraph != null)
    {
      _handTrackingGraph.maxNumHands = InteractionHandCount;
    }

    if (shouldResumeSubscription)
    {
      RestartSubscription();
    }
  }

  // 手を見失ったとみなす猶予時間を更新する。
  public void UpdateSettings(float handLostTimeoutSeconds)
  {
    _handLostTimeoutSeconds = Mathf.Max(0.01f, handLostTimeoutSeconds);
  }

  // graph の出力購読を開始する。
  public void Start(MonoBehaviour host)
  {
    if (host == null)
    {
      return;
    }

    _subscriptionHost = host;
    if (_isSubscribed)
    {
      return;
    }

    RestartSubscription();
  }

  // graph の出力購読を停止する。
  public void Stop(MonoBehaviour host)
  {
    StopPendingSubscription(host != null ? host : _subscriptionHost);
    _subscriptionHost = null;
    UnsubscribeFromGraph();
  }

  // 指定した左右の手の最新ランドマークを取得する。
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

  // 指定した左右の手が新鮮なフレームを持つか確認する。
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

  // 保持中のフレームがまだ有効かを判定する。
  private bool IsHandFresh(bool hasHand, long frameTicks)
  {
    if (!hasHand)
    {
      return false;
    }

    var elapsedSeconds = (Stopwatch.GetTimestamp() - frameTicks) / (double)Stopwatch.Frequency;
    return elapsedSeconds <= _handLostTimeoutSeconds;
  }

  // graph が準備できるまで購読開始を待つ。
  private IEnumerator SubscribeWhenGraphIsReady(MonoBehaviour host)
  {
    while (!_isSubscribed && host != null && host.isActiveAndEnabled)
    {
      SubscribeToGraph();
      if (_isSubscribed)
      {
        _subscribeCoroutine = null;
        yield break;
      }

      yield return null;
    }

    _subscribeCoroutine = null;
  }

  // graph の出力イベントに購読する。
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

  // 購読待機中の coroutine を停止し、必要なら即時再購読を試みる。
  private void RestartSubscription()
  {
    if (_subscriptionHost == null || !_subscriptionHost.isActiveAndEnabled)
    {
      return;
    }

    StopPendingSubscription(_subscriptionHost);
    SubscribeToGraph();
    if (!_isSubscribed)
    {
      _subscribeCoroutine = _subscriptionHost.StartCoroutine(SubscribeWhenGraphIsReady(_subscriptionHost));
    }
  }

  // 購読開始待ちの coroutine を停止する。
  private void StopPendingSubscription(MonoBehaviour host)
  {
    if (_subscribeCoroutine != null && host != null)
    {
      host.StopCoroutine(_subscribeCoroutine);
    }

    _subscribeCoroutine = null;
  }

  // graph の出力イベント購読を解除する。
  private void UnsubscribeFromGraph()
  {
    if (_isSubscribed)
    {
      DetachFromGraph(_handTrackingGraph);
      _isSubscribed = false;
    }

    ResetFrameCache();
  }

  // 指定 graph から出力イベント購読を解除する。
  private void DetachFromGraph(HandTrackingGraph handTrackingGraph)
  {
    if (handTrackingGraph == null)
    {
      return;
    }

    try
    {
      handTrackingGraph.OnHandLandmarksOutput -= OnHandLandmarksOutput;
      handTrackingGraph.OnHandednessOutput -= OnHandednessOutput;
    }
    catch (System.NullReferenceException)
    {
      // Graph is already disposed/stopped.
    }
  }

  // ランドマーク出力を左右の手キャッシュへ反映する。
  private void OnHandLandmarksOutput(object stream, OutputEventArgs<List<NormalizedLandmarkList>> eventArgs)
  {
    var handLandmarks = eventArgs == null ? null : eventArgs.value;

    lock (_frameLock)
    {
      if (handLandmarks == null || handLandmarks.Count == 0)
      {
        return;
      }

      var timestampTicks = Stopwatch.GetTimestamp();
      var handCount = Mathf.Min(handLandmarks.Count, InteractionHandCount);
      var handednessByIndex = new TrackedHandType?[InteractionHandCount];
      if (!TryGetSynchronizedHandedness(handCount, timestampTicks, handednessByIndex))
      {
        return;
      }

      for (var i = 0; i < handCount; i++)
      {
        var candidate = handLandmarks[i];
        if (!IsValidHand(candidate))
        {
          continue;
        }

        var handedness = handednessByIndex[i].Value;
        if (handedness == TrackedHandType.Left)
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

  // handedness 出力をインデックスごとの左右情報として保持する。
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

      _latestHandednessTicks = Stopwatch.GetTimestamp();
    }
  }

  // 保持している左右手のキャッシュをクリアする。
  private void ResetFrameCache()
  {
    lock (_frameLock)
    {
      _hasLeftHandOnLatestFrame = false;
      _hasRightHandOnLatestFrame = false;
      _latestHandednessTicks = 0;

      for (var i = 0; i < _latestHandednessByIndex.Length; i++)
      {
        _latestHandednessByIndex[i] = null;
      }
    }
  }

  // 同数・一意の handedness が揃っているフレームだけ左右へ反映する。
  private bool TryGetSynchronizedHandedness(int handCount, long timestampTicks, TrackedHandType?[] handednessByIndex)
  {
    if (handCount <= 0
        || handednessByIndex == null
        || handednessByIndex.Length < InteractionHandCount
        || GetKnownHandednessCount() != handCount
        || !IsHandednessFresh(timestampTicks))
    {
      return false;
    }

    var hasLeft = false;
    var hasRight = false;
    for (var i = 0; i < handCount; i++)
    {
      var handedness = _latestHandednessByIndex[i];
      if (!handedness.HasValue)
      {
        return false;
      }

      if (handedness.Value == TrackedHandType.Left)
      {
        if (hasLeft)
        {
          return false;
        }

        hasLeft = true;
      }
      else
      {
        if (hasRight)
        {
          return false;
        }

        hasRight = true;
      }

      handednessByIndex[i] = handedness;
    }

    return true;
  }

  // 現在保持している handedness の件数を返す。
  private int GetKnownHandednessCount()
  {
    var count = 0;
    for (var i = 0; i < _latestHandednessByIndex.Length; i++)
    {
      if (_latestHandednessByIndex[i].HasValue)
      {
        count++;
      }
    }

    return count;
  }

  // 同一フレームとみなせる新しい handedness か確認する。
  private bool IsHandednessFresh(long timestampTicks)
  {
    if (_latestHandednessTicks <= 0)
    {
      return false;
    }

    var elapsedSeconds = (timestampTicks - _latestHandednessTicks) / (double)Stopwatch.Frequency;
    return elapsedSeconds >= 0d && elapsedSeconds <= HandednessPairingToleranceSeconds;
  }

  // MediaPipe の handedness ラベルを内部 enum に変換する。
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

  // ランドマーク配列が有効な手データか確認する。
  private static bool IsValidHand(NormalizedLandmarkList handLandmarks)
  {
    return handLandmarks != null
           && handLandmarks.Landmark != null
           && handLandmarks.Landmark.Count >= HandLandmarkCount;
  }

  // MediaPipe のランドマークを Unity 用の配列へコピーする。
  private static void CopyLandmarks(NormalizedLandmarkList source, Vector3[] destination)
  {
    for (var i = 0; i < HandLandmarkCount; i++)
    {
      var landmark = source.Landmark[i];
      destination[i] = new Vector3(landmark.X, landmark.Y, landmark.Z);
    }
  }
}
