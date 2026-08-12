# VR操作 仕様書

## 目的・背景
- 展示ブース向けの空間入力デバイス。前身は MediaPipe によるカメラハンドトラッキングで、その置き換えとして導入
- ヘッドセットは使わない。コントローラーのみで位置と向きの両方(6DoF)を取得する
- HMD のカメラが追跡を担う方式(Quest Touch / PSVR2 Sense / WMR)は HMD なしでは原理的に位置追跡ができないため不可
- 外部ベースステーションが追跡する Lighthouse 系(Vive コントローラー / Index コントローラー / Vive Tracker)のみが要件を満たす
- 2026-08-12 時点で実機は未定。`MockVrPoseSource`(マウス+キーボードで 6DoF を模擬)により機材ゼロで検証できる状態にしてある

## アーキテクチャ
- `IVrPoseSource` — 姿勢とボタン入力を供給する抽象。実機とモックを差し替えるための境界
  - `MockVrPoseSource` — マウス+キーボードで 6DoF を模擬(実装済み)
  - `OpenVrPoseSource` — 実機用(未実装。実機導入時に追加)
- `VrTrackingSpaceMapper` — トラッキング空間の姿勢をワールド空間へ変換。実行時キャリブレーション(中心位置の記録)を持つ
- `VrGrabController` — 掴み・移動・回転のロジック本体。`MouseGrabController` と対になる入力層
- `VrGrabInputController` — シーンに置く MonoBehaviour の入口。`HandPinchController` から `GrabInteractor` / `OutlineController` / `GrabInputCoordinator` を受け取り、上記を組み立てて駆動する

## VR とマウス/キーボード操作の切り替えについて
**排他的な「モード切り替えスイッチ」は存在しない。** `MouseGrabInputController` と `VrGrabInputController` は同一 GameObject(`Solution`)に常駐し、常に両方が有効な状態で動く設計になっている。「切り替え」は次の 3 つの階層で起きている。

### 1. 物理入力の重複回避(設計レベル)
マウスの左/中/右ボタンは `MouseGrabController` とチュートリアル送りが専有しているため、`MockVrPoseSource` は掴み操作をキーボード(`G`)に割り当てている(`MockVrPoseSource.cs` 冒頭コメント参照)。これにより同じマウスをマウス操作とVRモックが同時に使っても物理的な入力の奪い合いが起きない。実機導入後はVRコントローラー自体の物理ボタンに置き換わるため、この制約は自然に解消する。

### 2. オブジェクト単位の排他制御(実行時、自動)
実際の「切り替え」の正体は `GrabInputCoordinator`(`GrabInputContext.cs`)。
- `TryClaim(source)` — 掴み始める瞬間に `Hand` / `Pointer` / `Vr` のいずれかがロックを取得する
- ロックを取った入力ソース以外は `IsBlocked(source)` が true になり、掴み操作を一切受け付けなくなる
- `Release(source)` でロック解放。次はどちらでも掴める状態に戻る

つまり「VR操作に切り替える」という明示的な操作はなく、**マウスで掴めばマウス操作中、VRコントローラー(モック)で掴めばVR操作中、と掴んだ瞬間に自動で切り替わり、離すと中立に戻る**。両方が同時に同じオブジェクトを取り合うことはできない。ただし片方が掴んでいる間、もう片方は完全に無反応になる(ホバー表示だけは出続ける)。

### 3. 入力パスそのものの有効/無効(設計時のオン/オフスイッチ)
`enableMouseInput` / `enableVrInput`(各コントローラーの Inspector 値)で、そのパスの入力自体を丸ごと無効化できる。現状は全13シーンで両方 `true`。
実行時に外部スクリプトから呼べる `SetInteractionEnabled(bool)` も各コントローラーが持つ:
- `HandPinchController.SetInteractionEnabled` / `MouseGrabInputController.SetInteractionEnabled` は `TutorialSequenceController`(チュートリアル進行に合わせた3D操作の一時無効化)と `StageSelectControlller`(ステージ選択画面での無効化)から既に呼ばれている
- `VrGrabInputController.SetInteractionEnabled` も同じシグネチャで用意済みだが、**現状どちらの呼び出し元からも呼ばれていない**(下記「既知のギャップ」参照)

## 既知のギャップ・要対応
- `TutorialSequenceController` / `StageSelectControlller` は `mouseGrabInputController` / `handPinchController` しか参照しておらず、VR入力を制御対象に含めていない。チュートリアル中や3D操作を止めたい場面でもVR(モック)だけ動き続ける可能性がある → 両スクリプトに `vrGrabInputController` の参照を追加し、`SetInteractionEnabled` を同時に呼ぶ対応が必要
- 実機の腕の可動域(~0.8m)とゲーム内可動範囲のスケール差は未調整。ステージごとに `xRange` 等の可動域自体が異なる(例: 多くのステージは `{-6, 6}`、Stage_6 のみ `{-8, 8}`)ため、`positionScale`(既定 3)の一律設定では最適化しきれない。実機到着後に詰める
- `trackingOrigin` / `controllerVisual` は全シーン未設定(ワールド原点基準、デバッグ表示なし)。ベースステーションの実設置位置が決まったら調整する

## シーン構成(2026-08-12 時点)
- 対象: Stage_1〜10, Stage_Practice, Title, StageSelect(13シーン)
- 対象外: Opening, Ending(grab系コンポーネント自体が存在しないため)
- 配置先: 各シーンの `Solution` GameObject。`HandPinchController` / `MouseGrabInputController` と同居させ、同じ `HandPinchController` を参照させている
- 設定値: `enableVrInput: true`, `useMockPoseSource: true`(モック駆動), `triggerMode: Toggle` / `rotationMode: Stepped` / `positionMappingMode: Relative`(いずれもマウス操作と挙動を揃えたデフォルト)

## モック操作の割り当て(実機なしでの動作確認用)
| 操作 | 割り当て |
|---|---|
| マウス移動 | トラッキング空間の X/Y 平行移動 |
| `LeftShift` + マウス移動 | 奥行き(X/Z)移動 |
| `LeftAlt` + マウス移動 | Yaw/Pitch 回転 |
| ホイール | Roll 回転 |
| `G` | 掴み(Toggle) |
| `T` | ステップ回転 |
| `Y` | 回転軸切り替え(X/Y) |
| `B` | 姿勢を初期位置へリセット |
| `C` | 現在位置をトラッキング空間の中心としてキャリブレーション |

マウスの左/中/右ボタンは使わない(理由は上記「1. 物理入力の重複回避」を参照)。

## 実機導入時の差し込み方
1. `IVrPoseSource` を実装した `OpenVrPoseSource` を作成する
2. `VrGrabInputController.SetPoseSource(poseSource)` に渡すだけでよい。`VrGrabInputController` 本体は変更不要
3. SteamVR 側設定: `requireHmd: false` / `forcedDriver: null` / `activateMultipleDrivers: true`
4. SteamVR Unity Plugin は使わない方針(XR Management + Valve の OpenVR XR プラグインはメンテ停止しており Unity 6 では動作が期待できず、避けたはずの Input System 導入も復活してしまうため)。代わりに OpenVR SDK の `openvr_api.cs` / `openvr_api.dll`(win64)のみを `Assets/Plugins/OpenVR/` に配置し、`Valve.VR.asmdef` で隔離済み
5. `Input System` / `XR Management` は導入しない。`activeInputHandler: 0`(Legacy)のまま維持する

## 実装ファイル
- `Assets/Scripts/PinchController/Vr/IVrPoseSource.cs` — 姿勢供給の抽象
- `Assets/Scripts/PinchController/Vr/MockVrPoseSource.cs` — マウス+キーボードによるモック実装
- `Assets/Scripts/PinchController/Vr/VrTrackingSpaceMapper.cs` — トラッキング空間→ワールド空間変換・キャリブレーション
- `Assets/Scripts/PinchController/Vr/VrGrabController.cs` — 掴み・移動・回転のロジック本体
- `Assets/Scripts/PinchController/Vr/VrGrabInputController.cs` — シーンに置く MonoBehaviour の入口
- `Assets/Scripts/PinchController/GrabInputContext.cs` — `GrabInputSource` / `GrabInputCoordinator`(入力の排他制御)
- `Assets/Scripts/PinchController/HandPinchController.cs` — 掴み・アウトライン・入力調停の共有ハブ
- `Assets/Plugins/OpenVR/` — 実機用 OpenVR SDK バイナリ(`openvr_api.cs` / `.dll`)。未コミット・未使用(差し込み先のみ用意した状態)
