using System;
using System.Runtime.InteropServices;
using UnityEngine;
using Valve.VR;

// ゲーム画面をヘッドセット内へ表示する仮想スクリーンの置き方。
public enum VrScreenAnchorMode
{
  // トラッキング空間に固定する。首を振ると画面が視界の外に出る代わりに、大画面でも酔いにくい。
  // 表示開始時と Recenter() 実行時に、そのときの頭の向きの正面へ置き直す。
  World,

  // 常に視界の正面へ貼り付ける。必ず見える代わりに、首を振ると画面が付いてくるため疲れやすい。
  HeadLocked,
}

// ゲーム画面のピクセルを OpenVR のオーバーレイとしてヘッドセットへ表示する。
//
// このゲームは 2D の影を扱うモニター向けタイトルであり、立体視でレンダリングする意味がない。
// そのため「HMD の中に大きな平面スクリーンを 1 枚出し、そこにゲーム画面を映す」方式を採る。
//
// GPU テクスチャのハンドルを渡す SetOverlayTexture ではなく、CPU バッファを渡す SetOverlayRaw を使う。
// マルチスレッドレンダリングが有効な状態で GPU ハンドルを OpenVR へ渡すと、
// Unity のレンダースレッドと OpenVR がグラフィックスデバイスを取り合って落ちるため。
// SteamVR Unity Plugin はこれをネイティブプラグインで回避しているが、本プロジェクトは
// openvr_api を直接叩く方針なのでその手段が無い。CPU 経由なら競合そのものが起きない。
public sealed class OpenVrGameViewOverlay : IDisposable
{
  // オーバーレイを一意に識別するキー。プロセスをまたいで衝突しないよう固有名にする。
  private const string OverlayKey = "pinchwitch.gameview";
  // SteamVR のオーバーレイ一覧に出る表示名。
  private const string OverlayName = "PinchWitch Game View";
  // SetOverlayRaw の完了イベントが来ない場合に、転送を永遠に待たないための上限。
  private const float RawSubmitTimeoutSeconds = 2f;
  // OpenVR 接続そのものを作り直すまでに許容する連続失敗回数。
  private const int ReconnectFailureThreshold = 3;
  // 同じ障害で Console を埋めないためのログ間隔。
  private const float ErrorLogIntervalSeconds = 2f;

  public struct Config
  {
    // 仮想スクリーンの置き方。
    public VrScreenAnchorMode anchorMode;
    // 仮想スクリーンの横幅(m)。
    public float widthInMeters;
    // 頭からスクリーンまでの距離(m)。
    public float distanceInMeters;
    // 目線からの上下オフセット(m)。正で上。
    public float verticalOffsetInMeters;
    // 画面を上下反転するかどうか。
    // グラフィックス API によって読み出したピクセルの並びが変わるため、逆さに映ったら切り替える。
    public bool flipVertically;
    // 画面を左右反転するかどうか。
    // オーバーレイの板を裏側から見ている場合など、鏡像になったときに切り替える。
    // 上下と左右の両方を入れると 180 度回転になる。
    public bool flipHorizontally;
    // SteamVR への接続の仕方。姿勢取得側と揃える。
    public OpenVrApplicationMode applicationMode;
  }

  // 全トラッキングデバイスの姿勢を受ける使い回しバッファ。
  private readonly TrackedDevicePose_t[] _poses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];

  // 表示設定。
  private Config _config;
  // 生成済みオーバーレイのハンドル。0 なら未生成。
  private ulong _overlayHandle;
  // World モードで一度でも配置済みかどうか。
  private bool _hasPlacedWorldAnchor;
  // 現在オーバーレイを表示状態にしてあるかどうか。
  private bool _isShown;
  // 少なくとも 1 枚の Raw 画像が SteamVR 側でロード済みかどうか。
  private bool _hasLoadedTexture;
  // SetOverlayRaw 後、VREvent_ImageLoaded / ImageFailed を待っているかどうか。
  private bool _isRawSubmitPending;
  // Raw 転送完了を待つ期限。
  private float _rawSubmitDeadline;
  // Overlay API の連続失敗回数。
  private int _consecutiveFailures;
  // 次に障害ログを出してよい時刻。
  private float _nextErrorLogTime;
  public OpenVrGameViewOverlay(Config config)
  {
    ApplyConfiguration(config);
  }

  // オーバーレイが生成済みかどうか。
  public bool IsActive => _overlayHandle != 0;

  // 表示設定を更新する。生成済みなら見た目へ即反映する。
  public void ApplyConfiguration(Config config)
  {
    _config = config;
    if (_overlayHandle == 0)
    {
      return;
    }

    var overlay = OpenVR.Overlay;
    if (overlay == null)
    {
      return;
    }

    overlay.SetOverlayWidthInMeters(_overlayHandle, Mathf.Max(0.1f, _config.widthInMeters));
    ApplyTextureBounds(overlay);
  }

  // 1 フレームぶんのゲーム画面を送る。ピクセルは RGBA32、先頭が左上の並びを前提とする。
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
    if (_isRawSubmitPending || !EnsureOverlayCreated(overlay))
    {
      return false;
    }

    UpdateTransform(overlay);

    // SetOverlayRaw は渡したポインタの中身を同期的に読み取るため、呼び出しの間だけ固定できればよい。
    var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
    EVROverlayError submitResult;
    try
    {
      submitResult = overlay.SetOverlayRaw(
        _overlayHandle,
        handle.AddrOfPinnedObject(),
        (uint)width,
        (uint)height,
        4u);
    }
    finally
    {
      handle.Free();
    }

    if (submitResult != EVROverlayError.None)
    {
      HandleFailure(overlay, nameof(CVROverlay.SetOverlayRaw), submitResult, recreateOverlay: true);
      return false;
    }

    // Raw 画像のロード完了前に次の画像を積むと、SteamVR 側の画像更新が詰まりやすい。
    // 完了イベントを受けるまで 1 要求だけに制限する。
    _isRawSubmitPending = true;
    _rawSubmitDeadline = Time.realtimeSinceStartup + RawSubmitTimeoutSeconds;
    return true;
  }

  // 次の画面キャプチャを開始してよいか確認する。
  // イベント処理と、非表示になった Overlay の再表示もここで行う。
  public bool CanAcceptFrame()
  {
    var overlay = OpenVR.Overlay;
    if (overlay == null)
    {
      return false;
    }

    RefreshState(overlay);
    return !_isRawSubmitPending;
  }

  // 現在の頭の向きの正面へスクリーンを置き直す。World モードでのみ意味を持つ。
  public void Recenter()
  {
    _hasPlacedWorldAnchor = false;
  }

  // スクリーンを隠す。表示を止めても最後のフレームが残り続けないようにするために使う。
  // ハンドルは保持したままなので、次に Submit すればそのまま再表示される。
  public void Hide()
  {
    if (_overlayHandle == 0)
    {
      return;
    }

    var overlay = OpenVR.Overlay;
    if (overlay != null)
    {
      var result = overlay.HideOverlay(_overlayHandle);
      if (result != EVROverlayError.None && !IsInvalidHandleError(result))
      {
        LogFailure(nameof(CVROverlay.HideOverlay), result);
      }
    }

    _isShown = false;
  }

  public void Dispose()
  {
    if (_overlayHandle == 0)
    {
      return;
    }

    InvalidateOverlay(OpenVR.Overlay, destroyOverlay: true);
  }

  // オーバーレイをまだ作っていなければ作る。
  private bool EnsureOverlayCreated(CVROverlay overlay)
  {
    if (_overlayHandle != 0)
    {
      return true;
    }

    var handle = 0ul;
    var error = overlay.CreateOverlay(OverlayKey, OverlayName, ref handle);
    if (error != EVROverlayError.None)
    {
      // KeyInUse や SteamVR 再起動直後の一時エラーも、数回続けば接続ごと作り直す。
      RegisterFailure(overlay, nameof(CVROverlay.CreateOverlay), error, recreateOverlay: false);
      return false;
    }

    _overlayHandle = handle;
    _hasPlacedWorldAnchor = false;
    _hasLoadedTexture = false;
    _isRawSubmitPending = false;

    overlay.SetOverlayWidthInMeters(_overlayHandle, Mathf.Max(0.1f, _config.widthInMeters));
    // ゲーム画面は不透明なので、アルファを見ずに描いてもらう。
    overlay.SetOverlayFlag(_overlayHandle, VROverlayFlags.IgnoreTextureAlpha, true);
    overlay.SetOverlayAlpha(_overlayHandle, 1f);
    ApplyTextureBounds(overlay);
    // 表示は最初のフレームを流し込んだ後に行う(Submit 側)。
    _isShown = false;
    return true;
  }

  // Overlay 固有イベントを処理し、転送完了・非表示化・タイムアウトから復旧する。
  private void RefreshState(CVROverlay overlay)
  {
    if (_overlayHandle == 0)
    {
      return;
    }

    var overlayEvent = default(VREvent_t);
    var eventSize = (uint)Marshal.SizeOf(typeof(VREvent_t));
    while (overlay.PollNextOverlayEvent(_overlayHandle, ref overlayEvent, eventSize))
    {
      switch ((EVREventType)overlayEvent.eventType)
      {
        case EVREventType.VREvent_ImageLoaded:
          _isRawSubmitPending = false;
          _hasLoadedTexture = true;
          _consecutiveFailures = 0;
          break;

        case EVREventType.VREvent_ImageFailed:
          _isRawSubmitPending = false;
          RegisterFailure(overlay, "SetOverlayRaw image load", EVROverlayError.RequestFailed, recreateOverlay: false);
          break;

        case EVREventType.VREvent_OverlayShown:
          _isShown = true;
          break;

        case EVREventType.VREvent_OverlayHidden:
          _isShown = false;
          break;

        case EVREventType.VREvent_OverlayDestroyed:
          InvalidateOverlay(overlay, destroyOverlay: false);
          return;
      }
    }

    if (_isRawSubmitPending && Time.realtimeSinceStartup >= _rawSubmitDeadline)
    {
      _isRawSubmitPending = false;
      RegisterFailure(overlay, "SetOverlayRaw completion", EVROverlayError.TimedOut, recreateOverlay: true);
      return;
    }

    if (_overlayHandle == 0 || _isRawSubmitPending || !_hasLoadedTexture)
    {
      return;
    }

    // SteamVR Dashboard、HMD スタンバイ、再フォーカスなどで隠された場合にも戻す。
    if (_isShown && !overlay.IsOverlayVisible(_overlayHandle))
    {
      _isShown = false;
    }

    if (!_isShown)
    {
      ShowOverlay(overlay);
    }
  }

  private void ShowOverlay(CVROverlay overlay)
  {
    var result = overlay.ShowOverlay(_overlayHandle);
    if (result == EVROverlayError.None)
    {
      _isShown = true;
      _consecutiveFailures = 0;
      return;
    }

    HandleFailure(overlay, nameof(CVROverlay.ShowOverlay), result, recreateOverlay: true);
  }

  private void HandleFailure(
    CVROverlay overlay,
    string operation,
    EVROverlayError error,
    bool recreateOverlay)
  {
    RegisterFailure(overlay, operation, error, recreateOverlay || IsInvalidHandleError(error));
  }

  private void RegisterFailure(
    CVROverlay overlay,
    string operation,
    EVROverlayError error,
    bool recreateOverlay)
  {
    _consecutiveFailures++;
    LogFailure(operation, error);

    if (recreateOverlay)
    {
      InvalidateOverlay(overlay, destroyOverlay: !IsInvalidHandleError(error));
    }

    if (_consecutiveFailures < ReconnectFailureThreshold)
    {
      return;
    }

    _consecutiveFailures = 0;
    InvalidateOverlay(overlay, destroyOverlay: false);
    OpenVrRuntime.RequestReconnect($"{operation}: {error}");
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

  private void InvalidateOverlay(CVROverlay overlay, bool destroyOverlay)
  {
    if (_overlayHandle != 0 && destroyOverlay && overlay != null)
    {
      overlay.DestroyOverlay(_overlayHandle);
    }

    _overlayHandle = 0;
    _hasPlacedWorldAnchor = false;
    _isShown = false;
    _hasLoadedTexture = false;
    _isRawSubmitPending = false;
    _rawSubmitDeadline = 0f;
  }

  private static bool IsInvalidHandleError(EVROverlayError error)
  {
    return error == EVROverlayError.UnknownOverlay
      || error == EVROverlayError.InvalidHandle;
  }

  // 反転の指定をテクスチャ座標側で行う。CPU で並べ替えるより安い。
  // 上下・左右を独立して指定できるため、4 通りの向きすべてに合わせられる。
  private void ApplyTextureBounds(CVROverlay overlay)
  {
    var bounds = new VRTextureBounds_t
    {
      uMin = _config.flipHorizontally ? 1f : 0f,
      uMax = _config.flipHorizontally ? 0f : 1f,
      vMin = _config.flipVertically ? 1f : 0f,
      vMax = _config.flipVertically ? 0f : 1f,
    };
    overlay.SetOverlayTextureBounds(_overlayHandle, ref bounds);
  }

  // スクリーンの位置と向きを更新する。
  private void UpdateTransform(CVROverlay overlay)
  {
    if (_config.anchorMode == VrScreenAnchorMode.HeadLocked)
    {
      // 頭に対する相対配置。OpenVR は -Z が前方なので、その向きへ距離ぶん押し出す。
      var headLocal = ToHmdMatrix34(
        new Vector3(0f, _config.verticalOffsetInMeters, _config.distanceInMeters),
        Quaternion.identity);
      overlay.SetOverlayTransformTrackedDeviceRelative(
        _overlayHandle,
        OpenVR.k_unTrackedDeviceIndex_Hmd,
        ref headLocal);
      return;
    }

    if (_hasPlacedWorldAnchor)
    {
      return;
    }

    if (!TryGetHeadPose(out var headPosition, out var headRotation))
    {
      return;
    }

    // 頭の左右の向き(ヨー)だけを使い、スクリーンが傾かないようにする。
    var yaw = Quaternion.Euler(0f, headRotation.eulerAngles.y, 0f);
    var screenPosition = headPosition
      + yaw * Vector3.forward * _config.distanceInMeters
      + Vector3.up * _config.verticalOffsetInMeters;

    var worldMatrix = ToHmdMatrix34(screenPosition, yaw);
    var result = overlay.SetOverlayTransformAbsolute(
      _overlayHandle,
      ETrackingUniverseOrigin.TrackingUniverseStanding,
      ref worldMatrix);
    if (result == EVROverlayError.None)
    {
      _hasPlacedWorldAnchor = true;
    }
  }

  // HMD の姿勢を取得する。
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

  // Unity(左手系・+Z 前方)の姿勢を OpenVR(右手系・-Z 前方)の 3x4 行列へ変換する。
  //
  // 基底変換 S = diag(1, 1, -1) を用いて T_openvr = S * T_unity * S となる。
  // 回転部分は行または列のどちらか一方だけが Z のとき符号が反転し、
  // 平行移動は Z 成分だけが反転する。openvr_api.cs 側の GetPosition() が
  // (m3, m7, -m11) を返すことと対応が取れている。
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
