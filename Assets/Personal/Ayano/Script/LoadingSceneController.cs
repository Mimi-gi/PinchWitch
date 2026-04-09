// Copyright (c) 2025 チーム名. All rights reserved.

using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Loadingシーンに置く。進捗をUIに反映しながら非同期ロードする。
/// </summary>
public class LoadingSceneController : MonoBehaviour
{
  [SerializeField] private Slider _progressBar;   // 進捗バー（任意）
  [SerializeField] private float _minDisplayTime = 1.0f; // 最低表示秒数

  private void Start()
  {
    StartCoroutine(LoadRoutine());
  }

  private IEnumerator LoadRoutine()
  {
    float elapsed = 0f;
    AsyncOperation op = GameSceneManager.StartPendingLoad();
    op.allowSceneActivation = false;

    while (!op.isDone)
    {
      elapsed += Time.deltaTime;

      // 進捗を0〜1に変換（AsyncOperationは最大0.9まで）
      float progress = Mathf.Clamp01(op.progress / 0.9f);

      if (_progressBar != null)
      {
        _progressBar.value = progress;
      }

      // ロード完了 かつ 最低表示時間を超えたら遷移
      if (op.progress >= 0.9f && elapsed >= _minDisplayTime)
      {
        op.allowSceneActivation = true;
      }

      yield return null;
    }
  }
}
