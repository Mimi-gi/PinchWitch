using System;
using System.Threading.Tasks;
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
//
// OpenVR.Init は 1 回で数十 ms〜(SteamVR 起動時は数秒)かかるため、別スレッドで実行する。
// 接続が終わるまで TryGetSystem は null を返し、呼び出し側は「未接続」として扱う。
public static class OpenVrRuntime
{
  // 接続失敗後に再試行するまでの秒数。
  private const float RetryIntervalSeconds = 15f;
  // 終了時、実行中の接続試行の完了を待つ上限(ミリ秒)。
  private const int ShutdownWaitMilliseconds = 5000;

  // 別スレッドで実行した OpenVR.Init の結果。
  private readonly struct InitResult
  {
    public readonly CVRSystem System;
    public readonly EVRInitError Error;
    public readonly Exception Exception;

    public InitResult(CVRSystem system, EVRInitError error, Exception exception)
    {
      System = system;
      Error = error;
      Exception = exception;
    }
  }

  // 確立済みの接続。null なら未接続。
  private static CVRSystem _system;
  // 実行中の接続試行。null なら試行していない。
  private static Task<InitResult> _pendingInit;
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
  // 接続できない環境(SteamVR 未インストール等)や接続試行中は null を返す。
  public static CVRSystem TryGetSystem(OpenVrApplicationMode applicationMode)
  {
    if (_system == null)
    {
      if (_pendingInit != null)
      {
        CompletePendingInitIfDone();
      }
      else
      {
        TryStartInitialize(applicationMode);
      }
    }

    return _system;
  }

  private static void TryStartInitialize(OpenVrApplicationMode applicationMode)
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

    var applicationType = applicationMode == OpenVrApplicationMode.Background
      ? EVRApplicationType.VRApplication_Background
      : EVRApplicationType.VRApplication_Scene;

    // 終了時に試行中の接続を確実に解放できるよう、試行前に購読しておく。
    SubscribeQuitOnce();
    _pendingInit = Task.Run(() => RunInit(applicationType));
  }

  // 別スレッドで実行する。Unity API は呼ばない。
  private static InitResult RunInit(EVRApplicationType applicationType)
  {
    try
    {
      var error = EVRInitError.None;
      var system = OpenVR.Init(ref error, applicationType);
      return new InitResult(system, error, null);
    }
    catch (Exception exception)
    {
      return new InitResult(null, EVRInitError.Unknown, exception);
    }
  }

  // 接続試行が終わっていれば結果を取り込む(メインスレッドで呼ぶ)。
  private static void CompletePendingInitIfDone()
  {
    if (!_pendingInit.IsCompleted)
    {
      return;
    }

    var result = _pendingInit.Result;
    _pendingInit = null;

    if (result.Exception != null)
    {
      Debug.LogWarning(
        $"{nameof(OpenVrRuntime)}: OpenVR の初期化中に例外が発生しました。{RetryIntervalSeconds} 秒後に再試行します。\n{result.Exception}");
      return;
    }

    if (result.System == null)
    {
      if (_lastLoggedError != result.Error)
      {
        _lastLoggedError = result.Error;
        Debug.LogWarning(
          $"{nameof(OpenVrRuntime)}: OpenVR の初期化に失敗しました ({result.Error})。{RetryIntervalSeconds} 秒ごとに再試行します。");
      }

      return;
    }

    _system = result.System;
    _lastLoggedError = EVRInitError.None;
    Debug.Log($"{nameof(OpenVrRuntime)}: OpenVR (SteamVR) へ接続しました。");
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
    // 接続試行の途中で終了した場合も、成功していれば接続を残さないよう完了を待って取り込む。
    if (_pendingInit != null)
    {
      var pendingInit = _pendingInit;
      _pendingInit = null;
      if (pendingInit.Wait(ShutdownWaitMilliseconds) && pendingInit.Result.System != null)
      {
        _system = pendingInit.Result.System;
      }
    }

    if (_system == null)
    {
      return;
    }

    _system = null;
    OpenVR.Shutdown();
  }
}
