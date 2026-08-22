# Meta Quest コントローラー対応 説明書・仕様書

作成日: 2026-08-12 / 対象: PinchWitch（BitSummit 2026 展示ビルド）

この文書は Meta Quest のコントローラーを PinchWitch の入力デバイスとして使うための **セットアップ手順（説明書）** と **内部仕様（仕様書）** をまとめたものです。VR 入力全体の設計方針は [VrSpec.md](VrSpec.md) を参照してください。

> **重要 — 実機未検証です。**
> 本対応はコード実装とコンパイル確認までを行った段階で、Quest 実機での動作確認は未実施です。とくに後述の「ボタン割り当て」と「HMD 非装着運用」は実機で最初に確認すべき項目です。

---

## 1. 結論：どういう仕組みで動くか

Quest のコントローラーを **Quest Link で PC に繋ぎ、SteamVR (OpenVR) 経由で読む** 方式です。

```
Quest コントローラー
   └─(HMD のカメラが追跡)→ Quest 本体
        └─(Quest Link: USB / Air Link)→ PC の Oculus アプリ
             └─(OpenVR ドライバ)→ SteamVR
                  └─(openvr_api.dll)→ OpenVrPoseSource → ゲーム
```

ゲームの映像はモニターに出ます。加えて `VrGameViewMirror` を使えば、**同じ画面をヘッドセット内の仮想スクリーンにも映せます**（後述）。

### 最重要の制約：HMD の電源を入れておく必要がある

Quest のコントローラーは **HMD 本体のカメラが位置を追跡** しています。Lighthouse 系（Vive / Index）のように外部ベースステーションが追跡するわけではないため、

- **HMD の電源が入っていること**
- **コントローラーが HMD のカメラの視野内にあること**

の両方が満たされないと位置が取れません。

**推奨は「普通に被って遊ぶ」構成です。** `VrGameViewMirror`（後述）でゲーム画面をヘッドセット内に映せるようになったため、被ったまま画面を見てプレイできます。この場合トラッキングは Quest 本来の使い方そのものなので、以下の制約は問題になりません。

頭に被らない運用にする場合は、HMD を「プレイヤーの手元が見える位置・向き」に置いたうえで、装着センサー（額の近接センサー）を塞いでスリープさせない工夫が要ります。

これは Quest 方式に固有の弱点で、[VrSpec.md](VrSpec.md) が当初 Quest を候補から外していた理由でもあります。展示本番までにこの運用が成立するか、実機で必ず確認してください。成立しない場合は Vive Tracker などの Lighthouse 系に切り替える判断が必要になります。

---

## 2. 必要なもの

| 種別 | 内容 |
|---|---|
| ハードウェア | Meta Quest 2 / 3 / Pro 本体とコントローラー |
| | Quest Link 用の USB-C ケーブル（Air Link でも可だが有線を推奨） |
| | Quest Link の要件を満たす Windows PC |
| ソフトウェア | Meta Quest Link（PC 版 Oculus アプリ） |
| | SteamVR |
| プロジェクト側 | 追加インストールは不要。`Assets/Plugins/OpenVR/` に配置済みの OpenVR SDK を使う |

Unity の XR パッケージや SteamVR Unity Plugin は **入れません**。導入すると避けていた Input System が復活してしまうため、`openvr_api.cs` / `openvr_api.dll` を直接叩く方式にしています。

---

## 3. セットアップ手順

### 3-1. PC 側の準備（初回のみ）

1. Meta Quest Link（PC 版 Oculus アプリ）をインストールし、Quest アカウントでサインインする
2. SteamVR をインストールする
3. Oculus アプリの「設定 → ベータ」で **「パブリックテストチャンネル」** は原則オフのままにする（挙動が変わることがあるため）

### 3-2. Quest 本体の準備（起動のたび）

1. Quest の電源を入れ、ガーディアン（境界）を設定する
2. USB-C ケーブルで PC に接続し、ヘッドセット内で **Quest Link を開始** する
3. コントローラーの電源が入っていること、Quest 側で追跡されていることを確認する
4. ヘッドセットを被って遊ぶ場合はそのまま装着する（`VrGameViewMirror` で画面が中に映る）。被らない運用にする場合のみ、HMD をプレイヤーの手元が見える位置に設置し、装着センサーを塞いでスリープを防ぐ

### 3-3. Unity 側の設定

1. 対象シーンを開く（`Stage_1`〜`Stage_10`, `Stage_Practice`, `Title`, `StageSelect`）
2. Hierarchy の **`Solution`** GameObject を選択する
3. **`GrabInputController`** コンポーネントの `Input Mode` を **`Vr`** に変更する
4. シーンを保存する

> **注意：シーンごとに個別の設定が必要です。**
> シーンに保存されている値は旧コンポーネント（`MouseGrabInputController` + `VrGrabInputController` の 2 つに分かれていた頃）のもので、`Input Mode` を持ちません。未設定のシーンは既定値の `Mouse` として読まれます。VR を使うシーンはすべて手動で `Vr` に切り替えて保存し直してください。
>
> 2026-08-12 時点で、全 13 シーンに VR 設定一式（`TrackingOrigin` オブジェクト、`Hold` / `Wrist`、`positionScale: 20`、`Controller Visual`）を適用済みですが、**`Input Mode` はすべて `Mouse` のまま**です。VR で動かすシーンは個別に `Vr` へ切り替えてください。
>
> `Controller Visual` は Title のみ未設定です（そのシーンだけ手カーソル用プレハブが存在しないため）。

### 3-4. 動作確認

1. Unity で再生する
2. Console に `OpenVrRuntime: OpenVR (SteamVR) へ接続しました。` が出れば接続成功
3. コントローラーを動かし、Scene ビューで照準の線（`Debug.DrawLine`）が追従するか確認する
4. コントローラーを操作の中心に構えて **`C` キー** を押す。手は `Calibrated Hand World Position`（既定はワールド原点）にワープし、そこを基準にコントローラーの動きが反映されるようになる
5. 掴めるオブジェクトに近づけてアウトラインが出れば、トラッキングと座標変換は成功

### 手のワープ先を設定する

`C` を押した瞬間、手は必ず `Calibrated Hand World Position` の座標にワープします。既定値の `(0, 0, 0)` はワールド原点で、掴みたいオブジェクトが宙にある場合はほぼ確実に届きません（Stage_3 の `yRange` は `{0, 4.5}` なので、原点は床すれすれです）。

ステージごとに掴みたいオブジェクトの中心付近の座標を `Calibrated Hand World Position` に設定してください。以後 `C` を押すたびに、その位置を基準としてコントローラーの動きが反映されます。

### ヘッドセットにゲーム画面を映す

`VrGameViewMirror` コンポーネントを `Solution` に追加すると、ゲーム画面がヘッドセット内の**仮想スクリーン**（目の前に浮かぶ大きな平面）に映ります。これでヘッドセットを被ったままプレイできるため、装着センサーを塞ぐ・額に乗せるといった運用上の工夫が不要になります。

`Enable Mirror` を入れて再生するだけで動きます。SteamVR に接続できない環境では何もしないので、付けっぱなしでも通常のモニター再生には影響しません。

| 項目 | 既定値 | 説明 |
|---|---|---|
| `Enable Mirror` | ON | ヘッドセットへ映すかどうか |
| `Target Frames Per Second` | 30 | 転送するフレームレート。上げるほど滑らかだが読み戻し負荷が増える |
| `Transfer Height` | 720 | 転送する画像の高さ(px)。幅は画面のアスペクト比から決まる |
| `Anchor Mode` | `World` | `World` は空間に固定（首を振っても画面が動かない）。`HeadLocked` は常に視界の正面 |
| `Screen Width In Meters` | 3.0 | 仮想スクリーンの横幅(m) |
| `Screen Distance In Meters` | 2.5 | 頭からスクリーンまでの距離(m) |
| `Screen Vertical Offset In Meters` | 0 | 目線からの上下オフセット(m)。正で上 |
| `Flip Vertically` | ON | 画面が上下逆さに映る場合に切り替える |
| `Recenter Screen Key` | `M` | `World` モードでスクリーンを目の前へ置き直す |
| `Application Mode` | `Scene` | SteamVR への接続の仕方。`GrabInputController` と揃える |

スクリーンは表示開始時に自動で目の前へ配置されます。見当たらない場合や向きがずれた場合は `M` を押してください。

> **画面が上下逆さに映ったら** `Flip Vertically` を切り替えてください。画面の取り込み結果の並びはグラフィックス API によって変わるため、実機で確認するまでどちらが正しいか確定できません。

#### なぜ立体視ではなく「平面スクリーン」なのか

このゲームは 2D の影を足場にするモニター向けタイトルで、立体視でレンダリングする意味がありません。そのため HMD 内に 1 枚の大きなスクリーンを出す方式にしています。

実装上の理由もあります。ゲーム画面を VR に出す正攻法は GPU テクスチャのハンドルを OpenVR へ直接渡すこと（`Submit` / `SetOverlayTexture`）ですが、本プロジェクトは**マルチスレッドレンダリングが有効**（`m_MTRendering: 1`）なため、これを行うと Unity のレンダースレッドと OpenVR がグラフィックスデバイスを取り合ってクラッシュします。SteamVR Unity Plugin はネイティブプラグインでこれを回避していますが、本プロジェクトは `openvr_api` を直接叩く方針のためその手段がありません。

そこで CPU バッファを渡す `SetOverlayRaw` を使っています。`AsyncGPUReadback` で GPU を待たずに画面を読み戻すため描画は止まらず、GPU デバイスに触らないので競合そのものが起きません。代償は読み戻しのメモリ帯域で、既定の 720p / 30fps なら約 110MB/s と実用範囲です。滑らかさが足りなければ `Target Frames Per Second` を、負荷が高ければ `Transfer Height` を調整してください。

### 6DoF 計測ログで可動域を測る

`Position Scale` を実機に合わせて決めるための機能です。`Log Pose Diagnostics` を ON にすると、位置 3 軸と回転 3 軸の現在値、および **観測した可動域の累積** が一定間隔で Console に出ます。

```
[VR 6DoF] OpenVR 右手 #1 | 可動域 x  0.834 y  0.482 z  0.431 m
  位置 tracking (m)  x   0.213   y   1.184   z  -0.402
  回転 (deg)     pitch  -12.4   yaw   38.1  roll    2.7
  位置 world         x   0.639   y   3.552   z  -1.206
  可動域 tracking (m) x   0.834 [ -0.311 ..  0.523] y   0.482 [  0.941 ..  1.423] z   0.431 [ -0.612 .. -0.181]
  可動域 world (unit) x   2.502 [ -0.933 ..  1.569] y   1.446 [  2.823 ..  4.269] z   1.293 [ -1.836 .. -0.543]
```

測り方の手順です。

1. `Log Pose Diagnostics` を ON にして再生する
2. コントローラーを操作の中心に構えて `C` を押す（キャリブレーションと同時に可動域の計測もリセットされる）
3. **無理なく動かせる範囲を一通り往復させる**（左右・上下・前後）
4. `可動域 tracking (m)` の行を見る。これが実際の腕の可動域
5. `可動域 world (unit)` の行と、そのステージの `xRange` / `yRange` / `zRange` を見比べる

ワールド側の幅がステージの可動範囲より狭ければ、その差の比率だけ `Position Scale` を上げます。上の例なら x 方向は 0.834m しか動かせず、ワールドでは 2.502 しかカバーできていません。Stage_3 の `xRange` は `{-6, 6}`（幅 12）なので、端まで届かせるには 5 倍近くにする必要がある計算になります。

ただし `Position Scale` を上げるほど手ブレも同じ倍率で増幅され、細かい配置がしづらくなります。数値どおりに上げるのではなく、届く範囲と扱いやすさのバランスを見て決めてください。測り直したいときは `V` で可動域だけリセットできます。

> 計測ログは Console を占有するため、**調整が終わったら OFF に戻してください。**

---

## 4. 操作方法

### 実機コントローラー（Quest Touch）

| 操作 | 割り当て | OpenVR ボタン ID |
|---|---|---|
| 掴む / 離す | **トリガー** または **グリップ** | `SteamVR_Trigger` (33) / `Grip` (2) |
| ステップ回転（`Stepped` 時のみ） | **A / X ボタン** | `A` (7) |
| 回転軸の切り替え（`Stepped` 時のみ） | **B / Y ボタン** | `ApplicationMenu` (1) |
| 基準点のキャリブレーション | キーボード **`C`** | — |

トリガーとグリップはどちらでも掴めるようにしてあります。SteamVR のレガシー入力のボタン ID を使っているので、Vive / Index コントローラーを繋いだ場合も対応するボタンでそのまま動きます。

掴みの挙動は `Trigger Mode` 設定で変わります。既定の `Toggle` は「押すたびに掴む／離す」、`Hold` は「押している間だけ掴む」です。展示では握りっぱなしにならない `Toggle` を推奨します。

### 回転方式：手首でまわす（推奨）

`Vr Rotation Mode` で 2 通りから選べます。**VR で遊ぶなら `Wrist` を推奨します。**

| モード | 挙動 |
|---|---|
| `Wrist` | **コントローラーの向きをそのまま反映する。** 掴んだまま手首をひねると、オブジェクトが 1:1 で同じだけ回る |
| `Stepped` | A / X ボタンで 1 段ずつ回す。マウス操作と同じ挙動。回転軸は B / Y で X↔Y を切り替える |

`Wrist` は掴んだ瞬間からの **回転差分** を適用します。掴んだ瞬間にオブジェクトがコントローラーの絶対的な向きへ飛ぶことはなく、そこから手首を回した角度だけが反映されます。持ち直しも効きます。

`Wrist` を選ぶと A / X・B / Y ボタンは何もしなくなります（ステップ回転は `Stepped` 専用のため）。

> **速度制限について**
> 掴み中の回転追従には `Rotation Degrees Per Second`（`HandPinchController`、既定 180°/秒）の速度制限がかかりますが、これは**ステップ回転が 90° を滑らかに回り込んで見せるための設定**です。手首は簡単に数百°/秒出るため、この制限が `Wrist` にもかかるとオブジェクトが手の動きから遅れ、ゴム紐のような挙動になります。
> そのため `Wrist`（内部的には `GrabInteractor.SetGrabbedObjectRotation`）は速度制限を外して即座に追従するようにしてあります。ステップ回転側の見た目は従来どおりです。

### モック操作（実機なしでの確認用）

`Input Mode` を `MouseVrMock` にすると、マウスとキーボードで 6DoF を模擬できます。割り当ては [VrSpec.md](VrSpec.md) の表を参照してください。

---

## 5. Inspector 設定リファレンス

`GrabInputController` の VR 関連項目です。

### Mode

| 項目 | 既定値 | 説明 |
|---|---|---|
| `Input Mode` | `Mouse` | `Mouse` / `MouseVrMock` / `Vr` を排他的に切り替える |

### VR

| 項目 | 既定値 | 説明 |
|---|---|---|
| `Vr Trigger Mode` | `Toggle` | `Toggle` は押すたびに掴む／離す。`Hold` は押している間だけ |
| `Vr Rotation Mode` | `Stepped` | `Wrist` はコントローラーの向きを 1:1 で反映（**VR ではこちらを推奨**）。`Stepped` はボタンで 1 段ずつ回転 |
| `Vr Position Mapping Mode` | `Relative` | `Relative` は掴んだ瞬間からの移動量だけを反映（位置が飛ばない）。`Absolute` は絶対位置を対応させる |
| `Vr Targeting Mode` | `Proximity` | `Proximity` は手の周囲の球で探す。`Ray` は前方への光線で指して掴む |
| `Vr Grab Radius` | 1.0 | Proximity で掴める範囲の半径（ワールド単位） |
| `Vr Hover Radius` | 2.0 | 射程外ホバー表示を出す範囲の半径 |
| `Vr Ray Distance` | 100 | Ray モードの最大距離 |

### VR Device (OpenVR)

| 項目 | 既定値 | 説明 |
|---|---|---|
| `Vr Preferred Hand` | `Right` | 優先して使う手。未接続なら逆の手 → 接続中の任意のコントローラーへ自動フォールバック |
| `Vr Application Mode` | `Scene` | SteamVR への接続の仕方。下記参照 |

`Vr Application Mode` の違い:

- **`Scene`（既定・推奨）** — SteamVR を自動起動し、シーンアプリケーションとして入力フォーカスを確保する。ボタン入力が確実に取れる。HMD 内は待機画面のままだが、表示先はモニターなので問題ない
- **`Background`** — 起動済みの SteamVR に表示を奪わず相乗りする。SteamVR が未起動だと接続できず、SteamVR Home など他のシーンアプリが入力フォーカスを持っているとボタンが取れないことがある

### VR Tracking Space

| 項目 | 既定値 | 説明 |
|---|---|---|
| `Tracking Origin` | 未設定 | トラッキング空間の原点として扱う Transform。未設定ならワールド原点 |
| `Position Scale` | 3 | 実空間 1m をワールド空間の何ユニットとして扱うか |
| `Calibrate Center Key` | `C` | 現在のコントローラー位置を基準点として記録し直すキー |
| `Calibrated Hand World Position` | `(0, 0, 0)` | `C` を押した瞬間、手がワープする先のワールド座標。ステージごとに掴みたいオブジェクトの高さ・位置が違うため、シーンごとに設定する |

### VR Diagnostics

| 項目 | 既定値 | 説明 |
|---|---|---|
| `Log Pose Diagnostics` | OFF | 6DoF の計測ログを Console に出す。可動域調整用なので普段は OFF |
| `Pose Log Interval Seconds` | 1.0 | ログを出す間隔（秒）。下限 0.1 |
| `Reset Pose Range Key` | `V` | 累積した可動域を捨てて測り直すキー。`C` でも同時にリセットされる |

### Hand Visual

| 項目 | 既定値 | 説明 |
|---|---|---|
| `Controller Visual` | 未設定 | コントローラー位置に追従させる表示用オブジェクト |
| `Draw Aim Ray` | ON | 照準線を `Debug.DrawLine` で描画する（Scene ビュー / Gizmos 有効時の Game ビュー） |

---

## 6. 内部仕様

### 6-1. クラス構成

| クラス | 責務 |
|---|---|
| `IVrPoseSource` | 姿勢とボタン入力を供給する抽象。実機とモックを差し替える境界 |
| `OpenVrPoseSource` | **実機実装。** OpenVR 経由で姿勢とボタンを取得する |
| `MockVrPoseSource` | モック実装。マウス+キーボードで 6DoF を模擬 |
| `OpenVrRuntime` | OpenVR ランタイムへの接続をプロセス全体で 1 つ管理する static クラス |
| `VrTrackingSpaceMapper` | トラッキング空間 → ワールド空間の座標変換とキャリブレーション |
| `VrGrabController` | 掴み・移動・回転・解放のロジック本体 |
| `VrPoseDiagnostics` | 6DoF の計測ログ。瞬時値と軸ごとの可動域を累積する |
| `OpenVrGameViewOverlay` | ゲーム画面を OpenVR オーバーレイとして HMD に表示する |
| `VrGameViewMirror` | 画面キャプチャと転送を駆動する MonoBehaviour |
| `GrabInputController` | シーンに置く MonoBehaviour の入口。上記を組み立てて駆動する |

`GrabInputController` は `HandPinchController` から `GrabInteractor` / `OutlineController` / `GrabInputCoordinator` を受け取ります。掴み処理そのものは `Mouse` モードと完全に共通で、VR 対応は「姿勢とボタンの供給元が変わっただけ」という構造になっています。

### 6-2. 1 フレームの流れ（`Vr` モード）

1. `GrabInputController.Update()` が `HandPinchController` から共有依存を取得する
2. `OpenVrPoseSource.Poll()`
   - `OpenVrRuntime.TryGetSystem()` で `CVRSystem` を取得（未接続なら 15 秒間隔で再接続を試みる）
   - `GetTrackedDeviceIndexForControllerRole()` で使うコントローラーを決める（優先手 → 逆の手 → 任意）
   - `GetDeviceToAbsoluteTrackingPose()` で姿勢、`GetControllerState()` でボタンを取得
3. `TryGetPose()` でトラッキング空間の位置・回転を受け取る
4. `C` キーが押されていれば `VrTrackingSpaceMapper.CalibrateCenter(trackingPosition, calibratedHandWorldPosition)` で基準点を更新
5. 前フレームとの差分から移動量を求め、`VrTrackingSpaceMapper` でワールド空間へ変換して `PoseSample` を組む
6. `VrGrabController.Process()` が掴み判定・追従・回転・解放を処理する

### 6-3. 座標変換

`VrTrackingSpaceMapper` が次の式で変換します。

```
ワールド位置 = trackingOrigin.position
             + trackingOrigin.rotation * ((トラッキング位置 - 基準点) * positionScale)
```

`trackingOrigin` が未設定ならワールド原点・無回転として扱います。`positionScale` は 0.01 未満に潰れないよう下限が入っています。

基準点（`_centerOffset`）は `C` を押した瞬間、**その時点のトラッキング位置が `calibratedHandWorldPosition` に変換されるよう逆算**して決めます。

```
基準点 = トラッキング位置 - (trackingOrigin.rotation の逆回転 * (calibratedHandWorldPosition - trackingOrigin.position)) / positionScale
```

`calibratedHandWorldPosition` の既定値 `Vector3.zero`、`trackingOrigin` 未設定の場合は「基準点 = トラッキング位置」に簡約され、旧仕様（ワールド原点にワープ）と完全に一致します。シーンごとに `calibratedHandWorldPosition` を設定すれば、`trackingOrigin` 用の空 GameObject を置かなくても、`C` を押した瞬間に手を任意のワールド座標へワープさせられます。

OpenVR は右手座標系、Unity は左手座標系ですが、変換は `openvr_api.cs` 同梱の `HmdMatrix34_t.GetPosition()` / `GetRotation()` に任せています（Z 軸の符号反転を含む）。

### 6-4. 接続管理の方針

`OpenVrRuntime` は接続をプロセス全体で 1 つだけ持ちます。

- `OpenVR.Init()` はメインスレッドをブロックする重い処理のため、シーン遷移のたびに繋ぎ直さず使い回す
- 解放はアプリ終了時（エディタでは再生終了時）のみ
- 接続に失敗した場合は **15 秒間隔** で再試行する。毎フレーム試すとゲームが引っかかるため
- SteamVR 自体が未インストールの環境では警告を 1 回出して以後は何もしない（通常の開発マシンで `Vr` モードのまま再生してもゲームは動き続ける）

### 6-5. 回転の適用

`GrabInteractor` は目標回転（`_targetRotation`）を持ち、`FixedTick` で `MoveRotation` して追従させます。目標回転の与え方が 2 通りあり、追従の仕方が変わります。

| 入口 | 使う側 | 追従 |
|---|---|---|
| `RotateGrabbedObject(axis, degrees)` | ステップ回転（マウス / `Stepped`） | `rotationDegreesPerSecond` の速度制限つき。90° の回り込みを見せる |
| `SetGrabbedObjectRotation(rotation)` | `Wrist` | 速度制限なしで即座に到達。毎フレーム最新の向きが渡る前提 |

`Wrist` の目標回転は次の式で求めます。掴んだ瞬間の姿勢を基準にした差分なので、掴み始めにオブジェクトが飛びません。

```
目標回転 = (現在のコントローラー回転 * Inverse(掴み開始時のコントローラー回転)) * 掴み開始時のオブジェクト回転
```

### 6-6. トラッキング喪失時の扱い

- **一瞬の遮蔽** — 直前の姿勢を保持し続けます。掴み中のオブジェクトが手放されません
- **コントローラーの切断** — `IsAvailable` が false になり、掴み中なら即座に解放して物理挙動へ戻します

### 6-7. 他入力との排他制御

`GrabInputCoordinator` が `Hand` / `Pointer` / `Vr` のいずれか 1 つにロックを与えます。掴み始めた入力ソースがロックを取り、その間ほかのソースは掴み操作を受け付けません。

加えて `GrabInputController` は `Input Mode` で選ばれた 1 つの入力パスだけを駆動し、モードを切り替えた瞬間に前のパスの掴みを必ず解除します。

---

## 7. トラブルシューティング

| 症状 | 確認すること |
|---|---|
| Console に「SteamVR がインストールされていないため〜」 | SteamVR をインストールする |
| Console に「OpenVR の初期化に失敗しました」 | Quest Link が開始されているか。SteamVR が起動しているか。エラーコードを SteamVR のドキュメントで確認する |
| 接続できたがコントローラーが認識されない | Quest 側でコントローラーが追跡されているか。SteamVR のステータス画面にコントローラーのアイコンが出ているか |
| 姿勢は動くがボタンが効かない | `Vr Application Mode` を `Scene` にする。SteamVR Home を無効化する。SteamVR のコントローラーバインド設定でレガシー入力が有効か確認する |
| ボタンの割り当てが説明書と違う | 実機未検証の項目です。`OpenVrPoseSource.cs` 冒頭の `GrabButtonMask` / `RotateButtonMask` / `AxisToggleButtonMask` を実機に合わせて変更してください |
| 手を動かしても位置が飛ぶ・追従が変 | `C` キーでキャリブレーションし直す。`Position Scale` を調整する |
| 腕の可動域とゲーム内の可動域が合わない | `Position Scale`（既定 3）を調整する。ステージごとに可動範囲が異なる点に注意 |
| `Vr` にしたのにマウス操作のまま | シーンの `GrabInputController` の `Input Mode` が `Vr` で保存されているか確認する |

---

## 8. 未検証事項・今後の課題

実機到着後に確認・調整が必要な項目です。

1. **ボタン割り当ての実機確認** — SteamVR のレガシー入力（`GetControllerState`）による Quest Touch のボタン ID 対応。本実装は一般的なレガシーバインドを前提にしており、実機で異なる可能性があります
2. **ヘッドセット内表示の確認** — 仮想スクリーンが見えるか、上下が正しいか（逆なら `Flip Vertically`）、30fps で実用に足るか
3. **HMD 非装着運用の成立性** — 被らない運用を選ぶ場合のみ。装着センサーを塞いだ状態でスリープせず追跡を維持できるか
4. **全シーンの `Input Mode` 切り替え** — 全 13 シーンとも `Mouse` のまま。VR で動かすシーンは `Vr` にする必要があります
5. **`VrGameViewMirror` の配置** — 現状どのシーンにも未配置。使うシーンの `Solution` に追加する
6. **`Position Scale` の調整** — 全シーン `20` で統一済みだが実機未検証。6DoF 計測ログで可動域を測って詰める
7. **`Calibrated Hand World Position` の調整** — 全シーン既定値 `(0, 0, 0)`。ステージごとに掴みたいオブジェクトの位置へ合わせる
8. **旧コンポーネントの残骸掃除** — 各シーンの `Solution` に削除済みスクリプトを指す missing script が残っています。動作に影響はありませんが警告が出ます
9. **`StageSelectControlller` の無効化漏れ** — ステージ決定演出中に `HandPinchController` しか無効化しておらず、`GrabInputController` は動き続けます

---

## 9. 実装ファイル

| ファイル | 内容 |
|---|---|
| [OpenVrPoseSource.cs](OpenVrPoseSource.cs) | 実機実装。姿勢取得とボタン割り当て |
| [OpenVrRuntime.cs](OpenVrRuntime.cs) | OpenVR 接続のプロセス全体管理 |
| [IVrPoseSource.cs](IVrPoseSource.cs) | 姿勢供給の抽象 |
| [MockVrPoseSource.cs](MockVrPoseSource.cs) | マウス+キーボードによるモック |
| [VrTrackingSpaceMapper.cs](VrTrackingSpaceMapper.cs) | 座標変換・キャリブレーション |
| [VrGrabController.cs](VrGrabController.cs) | 掴み・移動・回転のロジック本体 |
| [VrPoseDiagnostics.cs](VrPoseDiagnostics.cs) | 6DoF 計測ログ |
| [OpenVrGameViewOverlay.cs](OpenVrGameViewOverlay.cs) | ゲーム画面の HMD 表示（OpenVR オーバーレイ） |
| [VrGameViewMirror.cs](VrGameViewMirror.cs) | 画面キャプチャと転送の駆動 |
| [../GrabInputController.cs](../GrabInputController.cs) | シーンに置く MonoBehaviour の入口 |
| `Assets/Plugins/OpenVR/` | OpenVR SDK バイナリ（`openvr_api.cs` / `.dll`） |
