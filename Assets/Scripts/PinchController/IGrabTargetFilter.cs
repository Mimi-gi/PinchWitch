using UnityEngine;

// 掴めるかどうかを、レイヤー以外の条件で追加判定する。
// シーンごとの事情(ステージ選択で未解禁のカードは掴めない等)を GrabInteractor に持ち込まないための差し込み口。
public interface IGrabTargetFilter
{
  bool CanGrab(Rigidbody body);
}
