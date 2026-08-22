using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

// ゲーム画面を Meta Quest などのヘッドセット内へ、仮想スクリーンとして映す。
//
// GrabInputController(掴み入力)とは独立した関心事なので別コンポーネントにしてある。
// 使うシーンの Solution などに追加し、enableMirror を入れるだけで動く。
// SteamVR に接続できない環境では何もしないため、付けっぱなしでも通常のモニター再生に影響しない。
//
// 仕組み:
//   1. フレーム終端でゲーム画面を RenderTexture へ取り込む
//   2. 表示用の小さい RenderTexture へ縮小する
//   3. AsyncGPUReadback で CPU へ読み戻す(GPU を待たないので描画が止まらない)
//   4. 読み戻したピクセルを OpenVR のオーバーレイへ渡す
[DisallowMultipleComponent]
public sealed class VrGameViewMirror : MonoBehaviour
{
  // GPU 読み戻しが完了しないまま永久停止しないための上限。
  private const float ReadbackTimeoutSeconds = 2f;
  // 同じ GPU 障害で Console を埋めないためのログ間隔。
  private const float ReadbackErrorLogIntervalSeconds = 2f;

  [Header("Mirror")]
  // ヘッドセットへゲーム画面を映すかどうか。
  [SerializeField] private bool enableMirror = true;
  // 転送するフレームレート。上げるほど滑らかになるが読み戻しの負荷が増える。
  [SerializeField] private float targetFramesPerSecond = 30f;
  // 転送する画像の高さ(px)。幅は画面のアスペクト比から決まる。
  [SerializeField] private int transferHeight = 720;

  [Header("Screen")]
  // 仮想スクリーンの置き方。
  [SerializeField] private VrScreenAnchorMode anchorMode = VrScreenAnchorMode.World;
  // 仮想スクリーンの横幅(m)。
  [SerializeField] private float screenWidthInMeters = 3f;
  // 頭からスクリーンまでの距離(m)。
  [SerializeField] private float screenDistanceInMeters = 2.5f;
  // 目線からの上下オフセット(m)。正で上。
  [SerializeField] private float screenVerticalOffsetInMeters;
  // 画面が上下逆さに映る場合に入れる。
  [SerializeField] private bool flipVertically;
  // 画面が鏡像(左右逆)に映る場合に入れる。上下と両方入れると 180 度回転になる。
  [SerializeField] private bool flipHorizontally = true;
  // World モードでスクリーンを目の前へ置き直すキー。
  [SerializeField] private KeyCode recenterScreenKey = KeyCode.M;

  [Header("OpenVR")]
  // SteamVR への接続の仕方。GrabInputController 側と揃える。
  [SerializeField] private OpenVrApplicationMode applicationMode = OpenVrApplicationMode.Scene;

  // オーバーレイ表示の本体。
  private OpenVrGameViewOverlay _overlay;
  // 画面をそのまま取り込む RenderTexture。
  private RenderTexture _captureTexture;
  // 転送サイズまで縮小した RenderTexture。
  private RenderTexture _transferTexture;
  // 読み戻したピクセルを受ける管理配列。
  private byte[] _pixelBuffer;
  // 実行中の読み戻し要求。
  private AsyncGPUReadbackRequest _readbackRequest;
  // 読み戻し要求が実行中かどうか。
  private bool _hasPendingReadback;
  // 現在の読み戻しを開始した時刻。
  private float _readbackStartTime;
  // 次に GPU 読み戻し障害のログを出してよい時刻。
  private float _nextReadbackErrorLogTime;
  // 次に転送してよい時刻。
  private float _nextCaptureTime;
  // フレーム終端を待つコルーチン。
  private Coroutine _captureRoutine;
  // 非対応環境の警告を出し終えたかどうか。
  private bool _hasWarnedUnsupported;

  private void OnEnable()
  {
    // SteamVR / Quest Link がフォーカスを取っても画面転送を止めない。
    Application.runInBackground = true;
    _overlay = new OpenVrGameViewOverlay(BuildOverlayConfig());
    _captureRoutine = StartCoroutine(CaptureLoop());
  }

  private void OnDisable()
  {
    if (_captureRoutine != null)
    {
      StopCoroutine(_captureRoutine);
      _captureRoutine = null;
    }

    // 読み戻しの完了先が消えるため、破棄前に必ず待ち切る。
    if (_hasPendingReadback)
    {
      _readbackRequest.WaitForCompletion();
      _hasPendingReadback = false;
    }

    _overlay?.Dispose();
    _overlay = null;
    ReleaseTextures();
  }

  private void OnValidate()
  {
    targetFramesPerSecond = Mathf.Clamp(targetFramesPerSecond, 1f, 90f);
    transferHeight = Mathf.Clamp(transferHeight, 128, 2160);
    screenWidthInMeters = Mathf.Max(0.1f, screenWidthInMeters);
    screenDistanceInMeters = Mathf.Max(0.1f, screenDistanceInMeters);
    _overlay?.ApplyConfiguration(BuildOverlayConfig());
  }

  private void Update()
  {
    if (Input.GetKeyDown(recenterScreenKey))
    {
      RecenterScreen();
    }
  }

  // 仮想スクリーンを現在の頭の向きの正面へ置き直す。
  public void RecenterScreen()
  {
    _overlay?.Recenter();
  }

  // ヘッドセットへの表示を実行時に切り替える。
  public void SetMirrorEnabled(bool isEnabled)
  {
    if (enableMirror == isEnabled)
    {
      return;
    }

    enableMirror = isEnabled;
    if (!enableMirror)
    {
      _overlay?.Dispose();
      _overlay = new OpenVrGameViewOverlay(BuildOverlayConfig());
    }
  }

  private IEnumerator CaptureLoop()
  {
    var endOfFrame = new WaitForEndOfFrame();
    while (true)
    {
      yield return endOfFrame;
      TryCaptureFrame();
    }
  }

  private void TryCaptureFrame()
  {
    CompletePendingReadback();

    if (!enableMirror || _overlay == null)
    {
      // 止めた瞬間の絵が residual として残らないよう、明示的に隠す。
      _overlay?.Hide();
      return;
    }

    // 非同期読み戻しが使えない環境では、同期待ちで描画を止めるより表示を諦める。
    if (!SystemInfo.supportsAsyncGPUReadback)
    {
      if (!_hasWarnedUnsupported)
      {
        _hasWarnedUnsupported = true;
        Debug.LogWarning(
          $"{nameof(VrGameViewMirror)}: この環境は AsyncGPUReadback に非対応のため、ヘッドセットへの表示を行いません。",
          this);
      }

      return;
    }

    if (Time.realtimeSinceStartup < _nextCaptureTime || _hasPendingReadback)
    {
      return;
    }

    // SteamVR へ繋がっていない環境ではキャプチャ自体を行わない。
    if (OpenVrRuntime.TryGetSystem(applicationMode) == null)
    {
      return;
    }

    // SteamVR が前の Raw 画像をロードし終えるまで次の GPU 読み戻しを開始しない。
    if (!_overlay.CanAcceptFrame())
    {
      return;
    }

    _nextCaptureTime = Time.realtimeSinceStartup + 1f / Mathf.Max(1f, targetFramesPerSecond);

    if (!EnsureTextures())
    {
      return;
    }

    // 画面に出ているものをそのまま取り込む。UI も含まれる。
    ScreenCapture.CaptureScreenshotIntoRenderTexture(_captureTexture);
    Graphics.Blit(_captureTexture, _transferTexture);

    _readbackRequest = AsyncGPUReadback.Request(_transferTexture, 0, TextureFormat.RGBA32);
    _hasPendingReadback = true;
    _readbackStartTime = Time.realtimeSinceStartup;
  }

  private void CompletePendingReadback()
  {
    if (!_hasPendingReadback)
    {
      return;
    }

    if (!_readbackRequest.done)
    {
      if (Time.realtimeSinceStartup - _readbackStartTime < ReadbackTimeoutSeconds)
      {
        return;
      }

      // 非同期要求を放置したまま同じ RenderTexture を再利用できないため、異常時だけ同期して回収する。
      LogReadbackFailure("GPU 読み戻しがタイムアウトしたため、完了を同期的に待ちます。");
      _readbackRequest.WaitForCompletion();
      if (!_readbackRequest.done)
      {
        return;
      }
    }

    _hasPendingReadback = false;
    if (_readbackRequest.hasError || _overlay == null || _transferTexture == null)
    {
      if (_readbackRequest.hasError)
      {
        LogReadbackFailure("GPU 読み戻しに失敗したため、転送用 RenderTexture を作り直します。");
        ReleaseTextures();
      }

      return;
    }

    var data = _readbackRequest.GetData<byte>();
    var required = _transferTexture.width * _transferTexture.height * 4;
    if (data.Length < required)
    {
      return;
    }

    // NativeArray.CopyTo は長さの完全一致を要求するため、読み戻しサイズちょうどで確保する。
    if (_pixelBuffer == null || _pixelBuffer.Length != data.Length)
    {
      _pixelBuffer = new byte[data.Length];
    }

    data.CopyTo(_pixelBuffer);
    _overlay.Submit(_pixelBuffer, _transferTexture.width, _transferTexture.height);
  }

  // 画面サイズに追従して RenderTexture を用意する。
  private bool EnsureTextures()
  {
    var screenWidth = Mathf.Max(1, Screen.width);
    var screenHeight = Mathf.Max(1, Screen.height);

    if (_captureTexture == null
        || !_captureTexture.IsCreated()
        || _captureTexture.width != screenWidth
        || _captureTexture.height != screenHeight)
    {
      ReleaseTexture(ref _captureTexture);
      _captureTexture = new RenderTexture(screenWidth, screenHeight, 0, RenderTextureFormat.ARGB32)
      {
        name = "VrGameViewMirror_Capture",
      };
      if (!_captureTexture.Create())
      {
        return false;
      }
    }

    var targetHeight = Mathf.Clamp(transferHeight, 128, screenHeight);
    var targetWidth = Mathf.Max(2, Mathf.RoundToInt(targetHeight * (float)screenWidth / screenHeight));
    // 幅を偶数に揃える。奇数幅は読み戻し時の行パディングでずれることがある。
    targetWidth -= targetWidth % 2;

    if (_transferTexture == null
        || !_transferTexture.IsCreated()
        || _transferTexture.width != targetWidth
        || _transferTexture.height != targetHeight)
    {
      ReleaseTexture(ref _transferTexture);
      _transferTexture = new RenderTexture(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32)
      {
        name = "VrGameViewMirror_Transfer",
      };
      if (!_transferTexture.Create())
      {
        return false;
      }

      _pixelBuffer = null;
    }

    return true;
  }

  private void ReleaseTextures()
  {
    ReleaseTexture(ref _captureTexture);
    ReleaseTexture(ref _transferTexture);
    _pixelBuffer = null;
  }

  private static void ReleaseTexture(ref RenderTexture texture)
  {
    if (texture == null)
    {
      return;
    }

    texture.Release();
    Destroy(texture);
    texture = null;
  }

  private void LogReadbackFailure(string message)
  {
    if (Time.realtimeSinceStartup < _nextReadbackErrorLogTime)
    {
      return;
    }

    _nextReadbackErrorLogTime = Time.realtimeSinceStartup + ReadbackErrorLogIntervalSeconds;
    Debug.LogWarning($"{nameof(VrGameViewMirror)}: {message}", this);
  }

  private OpenVrGameViewOverlay.Config BuildOverlayConfig()
  {
    return new OpenVrGameViewOverlay.Config
    {
      anchorMode = anchorMode,
      widthInMeters = screenWidthInMeters,
      distanceInMeters = screenDistanceInMeters,
      verticalOffsetInMeters = screenVerticalOffsetInMeters,
      flipVertically = flipVertically,
      flipHorizontally = flipHorizontally,
      applicationMode = applicationMode,
    };
  }
}
