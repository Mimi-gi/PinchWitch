//=============================================================
// ファイル名  : ResultPanelController.cs
// 作成者     : 彩乃
// 作成日     : 2025/04/15
// 説明       : ステージクリア時に表示するパネル。
//              クリア判定側から Show() を呼んでもらう。
//              _isDebug にチェックを入れると数秒後に自動表示される。
//=============================================================

using Cysharp.Threading.Tasks;
using UnityEngine;
using System;

public class ResultPanelController : MonoBehaviour
{
  //=========================================================
  // 定数
  //=========================================================

  /// <summary>Resultパネルのオブジェクト名。Hierarchyの名前と合わせること。</summary>
  private const string ResultPanelName = "ResultPanel";

  //=========================================================
  // 内部変数
  //=========================================================

  /// <summary>クリアパネル本体。AwakeでFind取得。</summary>
  private GameObject _resultPanel;

#if UNITY_EDITOR
  [Header("デバッグ設定（Editor専用）")]
  /// <summary>チェックを入れると数秒後にクリアパネルが自動表示される。</summary>
  [SerializeField] private bool _isDebug = false;

  /// <summary>デバッグ時に自動表示するまでの秒数。</summary>
  [SerializeField] private float _debugDisplayDelay = 3f;
#endif

  //=========================================================
  // Unity イベント
  //=========================================================

  private void Awake()
  {
    // transform.Find()は直接の子を名前で検索する
    // 非アクティブのオブジェクトも検索できる
    // 見つからない場合はnullを返す
    Transform found = transform.Find(ResultPanelName);

    // .gameObjectを呼ぶ前にnullチェックをする
    // transform.Find().gameObjectと1行で書くと
    // nullのときに.gameObjectの時点でクラッシュするため分けている
    if (found == null)
    {
      Debug.LogError($"ResultPanelController: '{ResultPanelName}'という名前の子オブジェクトが見つかりません。");
      return; // 以降の処理を止める
    }

    // nullでないことが確認できてから.gameObjectを取り出す
    _resultPanel = found.gameObject;
  }

  private void Start()
  {
    _resultPanel.SetActive(false);

#if UNITY_EDITOR
    if (_isDebug)
    {
      DebugAutoShowAsync().Forget();
    }
#endif
  }

  //=========================================================
  // デバッグ用
  //=========================================================

#if UNITY_EDITOR
  /// <summary>
  /// デバッグ用。数秒後にクリアパネルを自動表示するコルーチン。
  /// </summary>
  private async UniTaskVoid DebugAutoShowAsync()
  {
    await UniTask.Delay(TimeSpan.FromSeconds(_debugDisplayDelay), cancellationToken: destroyCancellationToken);
    Show();
  }
#endif

  //=========================================================
  // Public メソッド（外部からの繋ぎ口）
  //=========================================================

  /// <summary>
  /// クリア判定側からこれを呼んでもらう。繋ぎ口。
  /// </summary>
  public void Show()
  {
    Time.timeScale = 0f;
    _resultPanel.SetActive(true);
  }

  //=========================================================
  // ボタンイベント
  //=========================================================

  /// <summary>NextStageボタンに繋ぐ。</summary>
  public void OnNextStageButtonPressed()
  {
    Time.timeScale = 1f;
    GameSceneManager.GoNextStage();
  }

  /// <summary>
  /// Titleボタンに繋ぐ。機能として保持。
  /// </summary>
  public void OnTitleButtonPressed()
  {
    Time.timeScale = 1f;
    GameSceneManager.GoTitle();
  }
}
