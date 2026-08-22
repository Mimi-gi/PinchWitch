using UnityEngine;
using Valve.VR;

// SteamVR への接続の仕方。
public enum OpenVrApplicationMode
{
  // SteamVR を自動起動し、シーンアプリケーションとして入力フォーカスを確保する。
  // HMD には何も描画しないため、ヘッドセット内は待機画面のままになる(このゲームの表示先はモニターであり問題ない)。
  Scene,

  // 起動済みの SteamVR に表示を奪わず相乗りする。SteamVR が未起動だと接続できず、
  // 他のシーンアプリ(SteamVR Home 等)が入力フォーカスを持っているとボタン入力が取れないことがある。
  Background,
}

// OpenVR (SteamVR) ランタイムへの接続をプロセス全体で 1 つだけ管理する。
// GrabInputController はシーンごとに作り直されるが、SteamVR との接続確立は重い処理のため
// シーンをまたいで使い回し、アプリ終了時(エディタでは再生終了時)にのみ解放する。
public static class OpenVrRuntime
{
  // 接続失敗後に再試行するまでの秒数。
  // OpenVR.Init はメインスレッドをブロックするため、失敗が続く環境で頻繁に呼ぶとゲームが引っかかる。
  private const float RetryIntervalSeconds = 15f;

  // 確立済みの接続。null なら未接続。
  private static CVRSystem _system;
  // Application.quitting を購読済みかどうか。
  private static bool _hasSubscribedQuit;
  // SteamVR 未インストールの警告を出し終えたかどうか。
  private static bool _hasWarnedRuntimeMissing;
  // 次に接続を試行してよい時刻(Time.realtimeSinceStartup 基準)。
  private static float _nextAttemptTime;
  // 直近で警告を出した失敗理由。同じ理由を再試行のたびに繰り返し出力しないために持つ。
  private static EVRInitError _lastLoggedError = EVRInitError.None;

  // Overlay や入力側が接続切れを検出したとき、次回アクセスで初期化し直せる状態へ戻す。
  // SteamVR / Quest Link の再起動後に古い CVRSystem を使い続けないための入口。
  public static void RequestReconnect(string reason)
  {
    if (_system == null)
    {
      return;
    }

    _system = null;
    OpenVR.Shutdown();
    // 一時的なスタンバイで毎フレーム初期化を繰り返さないよう、短い猶予を置く。
    _nextAttemptTime = Time.realtimeSinceStartup + 1f;
    _lastLoggedError = EVRInitError.None;
    Debug.LogWarning($"{nameof(OpenVrRuntime)}: OpenVR 接続を再初期化します ({reason})。");
  }

  // 接続済みなら CVRSystem を返す。未接続なら間隔を空けつつ接続を試みる。
  // 接続できない環境(SteamVR 未インストール等)では null を返し続ける。
  public static CVRSystem TryGetSystem(OpenVrApplicationMode applicationMode)
  {
    if (_system == null)
    {
      TryInitialize(applicationMode);
    }

    return _system;
  }

  private static void TryInitialize(OpenVrApplicationMode applicationMode)
  {
    if (Time.realtimeSinceStartup < _nextAttemptTime)
    {
      return;
    }

    _nextAttemptTime = Time.realtimeSinceStartup + RetryIntervalSeconds;

    // SteamVR 自体が入っていない環境(通常の開発マシン等)では接続しようがないため、
    // 一度だけ知らせて以後は静かに諦める。
    if (!OpenVR.IsRuntimeInstalled())
    {
      if (!_hasWarnedRuntimeMissing)
      {
        _hasWarnedRuntimeMissing = true;
        Debug.LogWarning(
          $"{nameof(OpenVrRuntime)}: SteamVR がインストールされていないため、実機 VR コントローラーは使用できません。");
      }

      return;
    }

    var error = EVRInitError.None;
    var applicationType = applicationMode == OpenVrApplicationMode.Background
      ? EVRApplicationType.VRApplication_Background
      : EVRApplicationType.VRApplication_Scene;
    _system = OpenVR.Init(ref error, applicationType);

    if (_system == null)
    {
      if (_lastLoggedError != error)
      {
        _lastLoggedError = error;
        Debug.LogWarning(
          $"{nameof(OpenVrRuntime)}: OpenVR の初期化に失敗しました ({error})。{RetryIntervalSeconds} 秒ごとに再試行します。");
      }

      return;
    }

    _lastLoggedError = EVRInitError.None;
    Debug.Log($"{nameof(OpenVrRuntime)}: OpenVR (SteamVR) へ接続しました。");
    SubscribeQuitOnce();
  }

  private static void SubscribeQuitOnce()
  {
    if (_hasSubscribedQuit)
    {
      return;
    }

    _hasSubscribedQuit = true;
    Application.quitting += Shutdown;
  }

  private static void Shutdown()
  {
    if (_system == null)
    {
      return;
    }

    _system = null;
    OpenVR.Shutdown();
  }
}
