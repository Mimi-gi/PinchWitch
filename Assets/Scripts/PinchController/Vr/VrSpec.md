# VR操作 仕様書

## 目的・背景
- 展示ブース向けの空間入力デバイス。前身は MediaPipe によるカメラハンドトラッキングで、その置き換えとして導入
- ゲームの表示先はモニター。加えて `VrGameViewMirror` によりヘッドセット内の仮想スクリーンにも同じ画面を映せるため、被ったままプレイできる
- 実機は Meta Quest コントローラーを採用(2026-08-12 対応実装)。**Quest Link で PC に接続し、SteamVR (OpenVR) 経由**で通常の SteamVR コントローラーとして扱う
  - Quest のコントローラーは HMD のカメラが追跡するため、HMD の電源が入りトラッキングが動作している必要がある
  - HMD を装着しない運用では、装着センサー(近接センサー)を塞ぐ等でスリープを回避し、コントローラーが HMD のカメラ視野に入る位置に HMD を設置する必要がある。実機で要検証
- `MockVrPoseSource`(マウス+キーボードで 6DoF を模擬)により機材ゼロでも検証できる

## アーキテクチャ
- `IVrPoseSource` — 姿勢とボタン入力を供給する抽象。実機とモックを差し替えるための境界
  - `MockVrPoseSource` — マウス+キーボードで 6DoF を模擬
  - `OpenVrPoseSource` — 実機用。OpenVR (SteamVR) 経由で姿勢とボタンを取得する。Quest Touch / Lighthouse 系(Vive / Index)の両対応
- `OpenVrRuntime` — OpenVR ランタイムへの接続をプロセス全体で 1 つ管理する static クラス。シーンをまたいで接続を使い回し、アプリ終了時(エディタでは再生終了時)にのみ解放する。接続失敗時は 15 秒間隔でリトライし、SteamVR 未インストール環境では一度警告して以後何もしない
- `VrTrackingSpaceMapper` — トラッキング空間の姿勢をワールド空間へ変換。実行時キャリブレーション(中心位置の記録)を持つ
- `VrGrabController` — 掴み・移動・回転のロジック本体。`MouseGrabController` と対になる入力層
- `OpenVrGameViewOverlay` / `VrGameViewMirror` — ゲーム画面を HMD 内の仮想スクリーンへ映す。掴み入力とは独立した関心事なので別コンポーネントにしてある。マルチスレッドレンダリング有効下で GPU ハンドルを OpenVR へ渡すとレンダースレッドと競合して落ちるため、`AsyncGPUReadback` + `SetOverlayRaw` の CPU 経由で転送する
- `VrAdvanceButtonSource` — UI 進行(テキスト送りなど)用の VR ボタン入力。掴み用の `OpenVrPoseSource` とは別系統で、左右どちらの手の A/B(X/Y)でも反応する。`TutorialSequenceController.advanceKeys`(Space / Return / Mouse0)に相乗りする形で組み込み済み
- `GrabInputController` — シーンに置く MonoBehaviour の入口。`HandPinchController` から `GrabInteractor` / `OutlineController` / `GrabInputCoordinator` を受け取り、上記を組み立てて駆動する
  - 旧構成の `MouseGrabInputController` + `VrGrabInputController`(2 コンポーネント常時併走)は、この 1 コンポーネントへ統合済み

## 操作モードの切り替え
`GrabInputController.inputMode`(Inspector)で 1 つの入力パスだけを排他的に駆動する。実行時は `SetInputMode()` でも変更できる。

- `Mouse` — 従来のマウス操作
- `MouseVrMock` — マウス+キーボードで VR コントローラーを模擬する検証用モード
- `Vr` — 実機 VR コントローラー。`SetPoseSource()` で外部実装の差し込みが無ければ、内蔵の `OpenVrPoseSource` を自動生成して使う

モード切り替え時は、直前まで動かしていた入力の grab を必ず解除してから切り替わる。`GrabInputCoordinator` による `Hand` / `Pointer` / `Vr` のオブジェクト単位の排他制御は従来どおりで、`HandPinchController` の手操作との奪い合いを防いでいる。

チュートリアル進行やステージ選択からの一時無効化は `SetInteractionEnabled(bool)` で行う。`TutorialSequenceController` は統合後の `GrabInputController` を参照して呼び出すようになっており、マウス/VR の両パスがまとめて無効化される。

## 実機(Meta Quest)の使い方
1. PC に SteamVR をインストールする
2. Quest を Quest Link(有線 / Air Link)で PC に接続する
3. 再生すると `OpenVrRuntime` が SteamVR へ接続する(`vrApplicationMode: Scene` なら SteamVR を自動起動する)
4. コントローラーを操作の中心に構え、`C` キーを押す。手は `calibratedHandWorldPosition`(シーンごとに Inspector で設定するワールド座標。既定はワールド原点)へワープし、そこを基準にコントローラーの動きが反映される

### Inspector 設定(`GrabInputController` の「VR Device (OpenVR)」欄)
- `vrPreferredHand` — 優先的に使う手(既定: Right)。未接続なら逆の手、それも無ければ接続中の任意のコントローラーへ自動フォールバックする
- `vrApplicationMode`
  - `Scene`(既定)— SteamVR を自動起動し、シーンアプリケーションとして入力フォーカスを確保する。HMD 内は待機画面のままになるが、このゲームの表示先はモニターなので問題ない
  - `Background` — 起動済みの SteamVR に表示を奪わず相乗りする。SteamVR が未起動だと接続できず、SteamVR Home 等が入力フォーカスを持っているとボタンが取れないことがある

### 実機のボタン割り当て(Quest Touch)
| 操作 | 割り当て |
|---|---|
| トリガー または グリップ | 掴み(`triggerMode` に従い Toggle / Hold) |
| A / X ボタン | ステップ回転(`rotationMode: Stepped` のときのみ) |
| B / Y ボタン | 回転軸切り替え(X/Y。`Stepped` のときのみ) |
| `C`(キーボード) | 現在位置を中心としてキャリブレーション |

SteamVR のレガシー入力のボタン ID(トリガー / グリップ / A / アプリケーションメニュー)を使っているため、Vive / Index コントローラーでも対応ボタンでそのまま動く。

### UI 進行(テキスト送り)のボタン割り当て
| 操作 | 割り当て |
|---|---|
| A / X ボタン、または B / Y ボタン(左右どちらでも可) | テキスト送り(`TutorialSequenceController.advanceKeys` の VR 版) |

`VrAdvanceButtonSource` が両手ぶんのコントローラーを個別に確認しており、`GrabInputController` がどちらの手を掴み用にトラッキングしているかとは無関係に動く。`TutorialSequenceController` の `Accept Vr Advance Input` で無効化できる。

> ステップ回転(上表)と同じ物理ボタンを使っているが、通常チュートリアルのナレーション中は `Set3DInteraction` ステップ前で 3D 操作(掴み)自体が無効化されているため、実運用での競合は起きにくい。両方が同時に有効な状況を作る場合は考慮すること。

### 回転方式
`rotationMode` が `Wrist` のときは、ボタンを使わず **コントローラーの向きをそのまま 1:1 で反映** する。掴んだ瞬間からの回転差分を適用するため、掴み始めにオブジェクトが飛ばない。VR 実機では `Wrist` を推奨する。

`GrabInteractor` の回転追従には `rotationDegreesPerSecond`(既定 180°/秒)の速度制限があるが、これはステップ回転の回り込みを見せるための設定であり、手首追従にかかると手の動きから遅れる。そのため `SetGrabbedObjectRotation`(`Wrist` が使う入口)は速度制限を外して即座に追従する。`RotateGrabbedObject`(ステップ回転)は従来どおり制限つき。

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

マウスの左/中/右ボタンは `Mouse` モードとチュートリアル送りが使用しているため、モックはキーボードへ割り当てている。

## SteamVR まわりの方針
- SteamVR Unity Plugin は使わない(XR Management + Valve の OpenVR XR プラグインはメンテ停止しており Unity 6 では動作が期待できず、避けたはずの Input System 導入も復活してしまうため)。OpenVR SDK の `openvr_api.cs` / `openvr_api.dll`(win64)のみを `Assets/Plugins/OpenVR/` に配置し、`Valve.VR.asmdef` で隔離している
- `Input System` / `XR Management` は導入しない。`activeInputHandler: 0`(Legacy)のまま維持する
- ボタン入力は SteamVR のレガシー入力 API(`GetControllerState`)に依存する。動かない場合は `vrApplicationMode` の切り替え、SteamVR Home の無効化、SteamVR のコントローラーバインド設定の確認を試す

## シーン構成(2026-08-12 時点)
- 対象: Stage_1〜10, Stage_Practice, Title, StageSelect(13シーン)。各シーンの `Solution` は共通プレハブ `Assets/Prefabs/Solution.prefab` のインスタンス
- 対象外: Opening, Ending(grab 系コンポーネント自体が存在しないため)

## 既知のギャップ・要対応
- `StageSelectControlller` はステージ決定演出中に `HandPinchController` しか無効化しておらず、`GrabInputController`(マウス/VR)は動き続ける
- 実機の腕の可動域(~0.8m)とゲーム内可動範囲のスケール差は未調整。`positionScale` は `20` で統一してあるが実機未検証。6DoF 計測ログで測って詰める
- 全 13 シーンの `Solution` は共通プレハブ `Assets/Prefabs/Solution.prefab` のインスタンス。`TrackingOrigin` と `handursor` はプレハブの子オブジェクトとして内包しており、`trackingOrigin` / `controllerVisual` の参照はプレハブ内で完結する。`VrGameViewMirror` もプレハブに含まれる
- シーン固有の差分は StageSelect の `xRange: {-8, 8}` のみ(他は `{-6, 6}`)。インスタンス側で上書きしている
- **シーンのプレハブ化はテキスト編集で行ったため、Unity 上での目視確認が未実施**
- `calibratedHandWorldPosition` は全シーン既定値 `(0, 0, 0)` のまま。ステージごとに掴みたいオブジェクトの位置へ調整する必要がある
- Quest を HMD 非装着で運用する際のスリープ回避・トラッキング維持(近接センサー対策、HMD の設置位置)は実機で要検証

## 実装ファイル
- `Assets/Scripts/PinchController/Vr/IVrPoseSource.cs` — 姿勢供給の抽象
- `Assets/Scripts/PinchController/Vr/MockVrPoseSource.cs` — マウス+キーボードによるモック実装
- `Assets/Scripts/PinchController/Vr/OpenVrPoseSource.cs` — 実機実装(OpenVR / SteamVR 経由。Quest Touch のボタン割り当て)
- `Assets/Scripts/PinchController/Vr/OpenVrRuntime.cs` — OpenVR 接続のプロセス全体管理
- `Assets/Scripts/PinchController/Vr/VrTrackingSpaceMapper.cs` — トラッキング空間→ワールド空間変換・キャリブレーション
- `Assets/Scripts/PinchController/Vr/VrGrabController.cs` — 掴み・移動・回転のロジック本体
- `Assets/Scripts/PinchController/GrabInputController.cs` — シーンに置く MonoBehaviour の入口(`inputMode` で Mouse / MouseVrMock / Vr を排他駆動)
- `Assets/Scripts/PinchController/GrabInputContext.cs` — `GrabInputSource` / `GrabInputCoordinator`(入力の排他制御)
- `Assets/Scripts/PinchController/HandPinchController.cs` — 掴み・アウトライン・入力調停の共有ハブ
- `Assets/Scripts/PinchController/Vr/VrPoseDiagnostics.cs` — 6DoF 計測ログ
- `Assets/Scripts/PinchController/Vr/OpenVrGameViewOverlay.cs` / `VrGameViewMirror.cs` — ゲーム画面の HMD 表示
- `Assets/Prefabs/Solution.prefab` — 全シーン共通の Solution(TrackingOrigin / handursor を内包)
- `Assets/Prefabs/handursor.prefab` — 手カーソル(SphereCollider は isTrigger)
- `Assets/Plugins/OpenVR/` — OpenVR SDK バイナリ(`openvr_api.cs` / `.dll`)
