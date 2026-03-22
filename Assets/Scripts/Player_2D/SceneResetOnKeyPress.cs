using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// R キーが押されたら、リセット対象オブジェクトを初期状態へ戻すスクリプト。
/// シーンを再読み込みしないため、MediaPipe を使うシーンでも比較的安全に扱いやすい。
/// シーンに手動配置しなくても動くように、下のインストーラーが実行時に自動生成する。
/// </summary>
[DisallowMultipleComponent]
public sealed class SceneResetOnKeyPress : MonoBehaviour
{
  [SerializeField] private KeyCode resetKey = KeyCode.R;

  private bool _isResetting;

  private void Update()
  {
    if (_isResetting)
    {
      return;
    }

    if (Input.GetKeyDown(resetKey))
    {
      ResetSceneObjects();
    }
  }

  /// <summary>
  /// 現在アクティブなシーン内にあるリセット対象を、初期状態へ順番に戻す。
  /// </summary>
  [ContextMenu("Reset Scene Objects")]
  public void ResetSceneObjects()
  {
    var activeScene = SceneManager.GetActiveScene();
    if (!activeScene.IsValid())
    {
      Debug.LogWarning("有効なシーンが見つからないため、リセットできません。");
      return;
    }

    _isResetting = true;
    StartCoroutine(ResetSceneObjectsSafely(activeScene));
  }

  /// <summary>
  /// 1 フレーム待ってから対象を初期状態へ戻し、途中の相互干渉を減らす。
  /// </summary>
  private IEnumerator ResetSceneObjectsSafely(Scene activeScene)
  {
    yield return null;
    ResetRegisteredObjects(activeScene);
    _isResetting = false;
  }

  /// <summary>
  /// シーン内の IRuntimeResettable を集め、ResetOrder 順に実行する。
  /// </summary>
  private static void ResetRegisteredObjects(Scene activeScene)
  {
    var resetTargets = new List<IRuntimeResettable>();
    foreach (var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
    {
      if (behaviour == null || behaviour.gameObject.scene != activeScene)
      {
        continue;
      }

      if (behaviour is not IRuntimeResettable resettable)
      {
        continue;
      }

      resetTargets.Add(resettable);
    }

    resetTargets.Sort(static (left, right) => left.ResetOrder.CompareTo(right.ResetOrder));

    foreach (var resetTarget in resetTargets)
    {
      resetTarget.ResetRuntimeState();
    }
  }
}

/// <summary>
/// SceneResetOnKeyPress を実行時に自動生成する補助クラス。
/// すでに同じシーンに存在する場合は重複生成しない。
/// </summary>
public static class SceneResetOnKeyPressInstaller
{
  private const string InstallerObjectName = "SceneResetOnKeyPress";

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  private static void Install()
  {
    var activeScene = SceneManager.GetActiveScene();
    if (!activeScene.IsValid())
    {
      return;
    }

    foreach (var existing in Resources.FindObjectsOfTypeAll<SceneResetOnKeyPress>())
    {
      if (existing == null)
      {
        continue;
      }

      if (existing.gameObject.scene == activeScene)
      {
        return;
      }
    }

    var installerObject = new GameObject(InstallerObjectName);
    SceneManager.MoveGameObjectToScene(installerObject, activeScene);
    installerObject.AddComponent<SceneResetOnKeyPress>();
  }
}
