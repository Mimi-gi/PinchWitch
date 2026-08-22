using System;
using System.Runtime.InteropServices;
using UnityEngine;
using Valve.VR;

// ゲーム画面をヘッドセット内へ表示する仮想スクリーンの置き方。
public enum VrScreenAnchorMode
{
  World,
  HeadLocked,
}

// 同じ位置に重ねた 2 枚の OpenVR Overlay を交互に更新する。
// 表示中の旧フレームには触れず、背面へ次フレームをロードし、完了後に SortOrder を入れ替える。
public sealed class OpenVrGameViewOverlay : IDisposable
{
  private const string PrimaryOverlayKey = "pinchwitch.gameview.primary";
  private const string SecondaryOverlayKey = "pinchwitch.gameview.secondary";
  private const string PrimaryOverlayName = "PinchWitch Game View A";
  private const string SecondaryOverlayName = "PinchWitch Game View B";
  private const uint BackSortOrder = 0u;
  private const uint FrontSortOrder = 1u;
  private const float RawSubmitTimeoutSeconds = 2f;
  private const int ReconnectFailureThreshold = 3;
  private const float ErrorLogIntervalSeconds = 2f;

  public struct Config
  {
    public VrScreenAnchorMode anchorMode;
    public float widthInMeters;
    public float distanceInMeters;
    public float verticalOffsetInMeters;
    public bool flipVertically;
    public bool flipHorizontally;
    public OpenVrApplicationMode applicationMode;
  }

  private sealed class OverlaySlot
  {
    public readonly string key;
    public readonly string name;
    public ulong handle;
    public bool isShown;
    public bool hasLoadedTexture;
    public bool isRawSubmitPending;
    public bool hasAppliedTransform;
    public float rawSubmitDeadline;

    public OverlaySlot(string key, string name)
    {
      this.key = key;
      this.name = name;
    }
  }

  private readonly TrackedDevicePose_t[] _poses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
  private readonly OverlaySlot[] _slots =
  {
    new OverlaySlot(PrimaryOverlayKey, PrimaryOverlayName),
    new OverlaySlot(SecondaryOverlayKey, SecondaryOverlayName),
  };

  private Config _config;
  private int _displayedSlotIndex = -1;
  private int _loadingSlotIndex = -1;
  private bool _hasWorldAnchor;
  private HmdMatrix34_t _worldAnchor;
  private int _consecutiveFailures;
  private float _nextErrorLogTime;

  public OpenVrGameViewOverlay(Config config)
  {
    ApplyConfiguration(config);
  }

  public bool IsActive => _slots[0].handle != 0 || _slots[1].handle != 0;

  public void ApplyConfiguration(Config config)
  {
    _config = config;
    _hasWorldAnchor = false;

    var overlay = OpenVR.Overlay;
    foreach (var slot in _slots)
    {
      slot.hasAppliedTransform = false;
      if (slot.handle != 0 && overlay != null)
      {
        ApplySlotConfiguration(overlay, slot);
      }
    }
  }

  // 非表示または背面の Overlay へだけ次フレームを送る。
  public bool Submit(byte[] pixels, int width, int height)
  {
    if (pixels == null || width <= 0 || height <= 0 || pixels.Length < width * height * 4)
    {
      return false;
    }

    var overlay = OpenVR.Overlay;
    if (overlay == null)
    {
      return false;
    }

    RefreshState(overlay);
    if (_loadingSlotIndex >= 0)
    {
      return false;
    }

    var targetIndex = _displayedSlotIndex < 0 ? 0 : 1 - _displayedSlotIndex;
    var target = _slots[targetIndex];
    if (!EnsureOverlayCreated(overlay, targetIndex) || !ApplyTransform(overlay, target))
    {
      return false;
    }

    var pinnedPixels = GCHandle.Alloc(pixels, GCHandleType.Pinned);
    EVROverlayError submitResult;
    try
    {
      submitResult = overlay.SetOverlayRaw(
        target.handle,
        pinnedPixels.AddrOfPinnedObject(),
        (uint)width,
        (uint)height,
        4u);
    }
    finally
    {
      pinnedPixels.Free();
    }

    if (submitResult != EVROverlayError.None)
    {
      RegisterFailure(
        overlay,
        targetIndex,
        nameof(CVROverlay.SetOverlayRaw),
        submitResult,
        recreateSlot: true);
      return false;
    }

    target.isRawSubmitPending = true;
    target.rawSubmitDeadline = Time.realtimeSinceStartup + RawSubmitTimeoutSeconds;
    _loadingSlotIndex = targetIndex;
    return true;
  }

  public bool CanAcceptFrame()
  {
    var overlay = OpenVR.Overlay;
    if (overlay == null)
    {
      return false;
    }

    RefreshState(overlay);
    return _loadingSlotIndex < 0;
  }

  public void Recenter()
  {
    _hasWorldAnchor = false;
    foreach (var slot in _slots)
    {
      slot.hasAppliedTransform = false;
    }
  }

  public void Hide()
  {
    var overlay = OpenVR.Overlay;
    foreach (var slot in _slots)
    {
      if (slot.handle == 0)
      {
        continue;
      }

      if (overlay != null)
      {
        var result = overlay.HideOverlay(slot.handle);
        if (result != EVROverlayError.None && !IsInvalidHandleError(result))
        {
          LogFailure(nameof(CVROverlay.HideOverlay), result);
        }
      }

      slot.isShown = false;
    }
  }

  public void Dispose()
  {
    InvalidateAllOverlays(OpenVR.Overlay, destroyOverlays: true);
  }

  private bool EnsureOverlayCreated(CVROverlay overlay, int slotIndex)
  {
    var slot = _slots[slotIndex];
    if (slot.handle != 0)
    {
      return true;
    }

    var handle = 0ul;
    var error = overlay.CreateOverlay(slot.key, slot.name, ref handle);
    if (error != EVROverlayError.None)
    {
      RegisterFailure(
        overlay,
        slotIndex,
        nameof(CVROverlay.CreateOverlay),
        error,
        recreateSlot: false);
      return false;
    }

    slot.handle = handle;
    slot.isShown = false;
    slot.hasLoadedTexture = false;
    slot.isRawSubmitPending = false;
    slot.hasAppliedTransform = false;
    ApplySlotConfiguration(overlay, slot);
    return true;
  }

  private void ApplySlotConfiguration(CVROverlay overlay, OverlaySlot slot)
  {
    overlay.SetOverlayWidthInMeters(slot.handle, Mathf.Max(0.1f, _config.widthInMeters));
    overlay.SetOverlayFlag(slot.handle, VROverlayFlags.IgnoreTextureAlpha, true);
    overlay.SetOverlayAlpha(slot.handle, 1f);
    overlay.SetOverlaySortOrder(slot.handle, BackSortOrder);
    ApplyTextureBounds(overlay, slot);
  }

  private void RefreshState(CVROverlay overlay)
  {
    for (var index = 0; index < _slots.Length; index++)
    {
      ProcessSlotEvents(overlay, index);
    }

    for (var index = 0; index < _slots.Length; index++)
    {
      var slot = _slots[index];
      if (!slot.isRawSubmitPending || Time.realtimeSinceStartup < slot.rawSubmitDeadline)
      {
        continue;
      }

      slot.isRawSubmitPending = false;
      if (_loadingSlotIndex == index)
      {
        _loadingSlotIndex = -1;
      }

      RegisterFailure(
        overlay,
        index,
        "SetOverlayRaw completion",
        EVROverlayError.TimedOut,
        recreateSlot: true);
    }

    RecoverDisplayedOverlay(overlay);
  }

  private void ProcessSlotEvents(CVROverlay overlay, int index)
  {
    var slot = _slots[index];
    if (slot.handle == 0)
    {
      return;
    }

    var overlayEvent = default(VREvent_t);
    var eventSize = (uint)Marshal.SizeOf(typeof(VREvent_t));
    while (slot.handle != 0 && overlay.PollNextOverlayEvent(slot.handle, ref overlayEvent, eventSize))
    {
      switch ((EVREventType)overlayEvent.eventType)
      {
        case EVREventType.VREvent_ImageLoaded:
          slot.isRawSubmitPending = false;
          slot.hasLoadedTexture = true;
          if (_loadingSlotIndex == index)
          {
            _loadingSlotIndex = -1;
          }

          _consecutiveFailures = 0;
          PromoteLoadedSlot(overlay, index);
          break;

        case EVREventType.VREvent_ImageFailed:
          slot.isRawSubmitPending = false;
          if (_loadingSlotIndex == index)
          {
            _loadingSlotIndex = -1;
          }

          RegisterFailure(
            overlay,
            index,
            "SetOverlayRaw image load",
            EVROverlayError.RequestFailed,
            recreateSlot: true);
          return;

        case EVREventType.VREvent_OverlayShown:
          slot.isShown = true;
          break;

        case EVREventType.VREvent_OverlayHidden:
          slot.isShown = false;
          break;

        case EVREventType.VREvent_OverlayDestroyed:
          ResetSlot(index, overlay, destroyOverlay: false);
          return;
      }
    }
  }

  // ロードが完了した背面を手前へ移す。旧フレームは背面に表示したまま残す。
  private void PromoteLoadedSlot(CVROverlay overlay, int newFrontIndex)
  {
    var newFront = _slots[newFrontIndex];
    var oldFrontIndex = _displayedSlotIndex;

    if (oldFrontIndex >= 0 && oldFrontIndex != newFrontIndex)
    {
      var oldFront = _slots[oldFrontIndex];
      if (oldFront.handle != 0)
      {
        overlay.SetOverlaySortOrder(oldFront.handle, BackSortOrder);
      }
    }

    var sortResult = overlay.SetOverlaySortOrder(newFront.handle, FrontSortOrder);
    if (sortResult != EVROverlayError.None)
    {
      RegisterFailure(
        overlay,
        newFrontIndex,
        nameof(CVROverlay.SetOverlaySortOrder),
        sortResult,
        recreateSlot: true);
      return;
    }

    if (!newFront.isShown && !ShowSlot(overlay, newFrontIndex))
    {
      return;
    }

    _displayedSlotIndex = newFrontIndex;

    // Recenter や設定変更後は、旧フレーム側も新しい前面と完全に同じ座標へ揃える。
    // 旧フレームは背面に残すため、座標差による二重表示を防ぐ必要がある。
    if (oldFrontIndex >= 0 && oldFrontIndex != newFrontIndex)
    {
      var oldFront = _slots[oldFrontIndex];
      if (oldFront.handle != 0)
      {
        ApplyTransform(overlay, oldFront);
      }
    }
  }

  private void RecoverDisplayedOverlay(CVROverlay overlay)
  {
    if (_displayedSlotIndex < 0
        || _displayedSlotIndex >= _slots.Length
        || _slots[_displayedSlotIndex].handle == 0
        || !_slots[_displayedSlotIndex].hasLoadedTexture)
    {
      _displayedSlotIndex = FindLoadedSlotIndex();
    }

    if (_displayedSlotIndex < 0)
    {
      return;
    }

    var displayed = _slots[_displayedSlotIndex];
    if (displayed.isShown && !overlay.IsOverlayVisible(displayed.handle))
    {
      displayed.isShown = false;
    }

    if (!displayed.isShown)
    {
      overlay.SetOverlaySortOrder(displayed.handle, FrontSortOrder);
      ShowSlot(overlay, _displayedSlotIndex);
    }
  }

  private int FindLoadedSlotIndex()
  {
    for (var index = 0; index < _slots.Length; index++)
    {
      if (_slots[index].handle != 0 && _slots[index].hasLoadedTexture)
      {
        return index;
      }
    }

    return -1;
  }

  private bool ShowSlot(CVROverlay overlay, int index)
  {
    var slot = _slots[index];
    var result = overlay.ShowOverlay(slot.handle);
    if (result == EVROverlayError.None)
    {
      slot.isShown = true;
      _consecutiveFailures = 0;
      return true;
    }

    RegisterFailure(
      overlay,
      index,
      nameof(CVROverlay.ShowOverlay),
      result,
      recreateSlot: true);
    return false;
  }

  private void RegisterFailure(
    CVROverlay overlay,
    int slotIndex,
    string operation,
    EVROverlayError error,
    bool recreateSlot)
  {
    _consecutiveFailures++;
    LogFailure(operation, error);

    if (recreateSlot && slotIndex >= 0 && slotIndex < _slots.Length)
    {
      ResetSlot(slotIndex, overlay, destroyOverlay: !IsInvalidHandleError(error));
    }

    if (_consecutiveFailures < ReconnectFailureThreshold)
    {
      return;
    }

    _consecutiveFailures = 0;
    InvalidateAllOverlays(overlay, destroyOverlays: false);
    OpenVrRuntime.RequestReconnect($"{operation}: {error}");
  }

  private void ResetSlot(int index, CVROverlay overlay, bool destroyOverlay)
  {
    var slot = _slots[index];
    if (slot.handle != 0 && destroyOverlay && overlay != null)
    {
      overlay.DestroyOverlay(slot.handle);
    }

    slot.handle = 0;
    slot.isShown = false;
    slot.hasLoadedTexture = false;
    slot.isRawSubmitPending = false;
    slot.hasAppliedTransform = false;
    slot.rawSubmitDeadline = 0f;

    if (_loadingSlotIndex == index)
    {
      _loadingSlotIndex = -1;
    }

    if (_displayedSlotIndex == index)
    {
      _displayedSlotIndex = FindLoadedSlotIndex();
    }
  }

  private void InvalidateAllOverlays(CVROverlay overlay, bool destroyOverlays)
  {
    for (var index = 0; index < _slots.Length; index++)
    {
      ResetSlot(index, overlay, destroyOverlays);
    }

    _displayedSlotIndex = -1;
    _loadingSlotIndex = -1;
    _hasWorldAnchor = false;
  }

  private void LogFailure(string operation, EVROverlayError error)
  {
    if (Time.realtimeSinceStartup < _nextErrorLogTime)
    {
      return;
    }

    _nextErrorLogTime = Time.realtimeSinceStartup + ErrorLogIntervalSeconds;
    Debug.LogWarning($"{nameof(OpenVrGameViewOverlay)}: {operation} に失敗しました ({error})。");
  }

  private static bool IsInvalidHandleError(EVROverlayError error)
  {
    return error == EVROverlayError.UnknownOverlay
      || error == EVROverlayError.InvalidHandle;
  }

  private void ApplyTextureBounds(CVROverlay overlay, OverlaySlot slot)
  {
    var bounds = new VRTextureBounds_t
    {
      uMin = _config.flipHorizontally ? 1f : 0f,
      uMax = _config.flipHorizontally ? 0f : 1f,
      vMin = _config.flipVertically ? 1f : 0f,
      vMax = _config.flipVertically ? 0f : 1f,
    };
    overlay.SetOverlayTextureBounds(slot.handle, ref bounds);
  }

  private bool ApplyTransform(CVROverlay overlay, OverlaySlot slot)
  {
    if (_config.anchorMode == VrScreenAnchorMode.HeadLocked)
    {
      var headLocal = ToHmdMatrix34(
        new Vector3(0f, _config.verticalOffsetInMeters, _config.distanceInMeters),
        Quaternion.identity);
      var result = overlay.SetOverlayTransformTrackedDeviceRelative(
        slot.handle,
        OpenVR.k_unTrackedDeviceIndex_Hmd,
        ref headLocal);
      slot.hasAppliedTransform = result == EVROverlayError.None;
      return slot.hasAppliedTransform;
    }

    if (!_hasWorldAnchor && !TryBuildWorldAnchor(out _worldAnchor))
    {
      return false;
    }

    _hasWorldAnchor = true;
    if (slot.hasAppliedTransform)
    {
      return true;
    }

    var worldAnchor = _worldAnchor;
    var transformResult = overlay.SetOverlayTransformAbsolute(
      slot.handle,
      ETrackingUniverseOrigin.TrackingUniverseStanding,
      ref worldAnchor);
    slot.hasAppliedTransform = transformResult == EVROverlayError.None;
    return slot.hasAppliedTransform;
  }

  private bool TryBuildWorldAnchor(out HmdMatrix34_t worldAnchor)
  {
    worldAnchor = default;
    if (!TryGetHeadPose(out var headPosition, out var headRotation))
    {
      return false;
    }

    var yaw = Quaternion.Euler(0f, headRotation.eulerAngles.y, 0f);
    var screenPosition = headPosition
      + yaw * Vector3.forward * _config.distanceInMeters
      + Vector3.up * _config.verticalOffsetInMeters;
    worldAnchor = ToHmdMatrix34(screenPosition, yaw);
    return true;
  }

  private bool TryGetHeadPose(out Vector3 position, out Quaternion rotation)
  {
    position = Vector3.zero;
    rotation = Quaternion.identity;

    var system = OpenVrRuntime.TryGetSystem(_config.applicationMode);
    if (system == null)
    {
      return false;
    }

    system.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, 0f, _poses);
    var headPose = _poses[OpenVR.k_unTrackedDeviceIndex_Hmd];
    if (!headPose.bPoseIsValid)
    {
      return false;
    }

    position = headPose.mDeviceToAbsoluteTracking.GetPosition();
    rotation = headPose.mDeviceToAbsoluteTracking.GetRotation();
    return true;
  }

  private static HmdMatrix34_t ToHmdMatrix34(Vector3 position, Quaternion rotation)
  {
    var m = Matrix4x4.Rotate(rotation);
    return new HmdMatrix34_t
    {
      m0 = m.m00,
      m1 = m.m01,
      m2 = -m.m02,
      m3 = position.x,

      m4 = m.m10,
      m5 = m.m11,
      m6 = -m.m12,
      m7 = position.y,

      m8 = -m.m20,
      m9 = -m.m21,
      m10 = m.m22,
      m11 = -position.z,
    };
  }
}
