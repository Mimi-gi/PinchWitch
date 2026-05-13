//=============================================================
// ファイル名  : LoadingSceneController.cs
// 作成者     : 彩乃
// 作成日     : 2025/04/15
// 説明       : 旧Loadingシーン用。現在のシーン遷移では使用しない。
//=============================================================
using UnityEngine;

/// <summary>
/// 旧Loadingシーン用。GameSceneManagerはLoadingを挟まず直接遷移する。
/// </summary>
public class LoadingSceneController : MonoBehaviour
{
  private void Start()
  {
    Debug.LogWarning("[LoadingSceneController] Loadingシーン経由の遷移は廃止されています。GameSceneManagerから直接遷移してください。");
  }
}
