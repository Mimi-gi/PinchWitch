# ステージエディタ 説明書

ステージ（地形・敵・ギミック・掴めるブロック）を作り、ScriptableObject（StageData）として保存するためのツールです。
保存したステージは、ゲームの共通シーン `Stage_Runtime` が読み込んで組み立てます。

---

## 全体の流れ

```
Stage_Authoring シーンで編集 ──「シーン → SO に保存」──▶ StageData（Assets/StageData/*.asset）
                                                          │
                                         StageCatalog に番号で登録（Resources/StageCatalog.asset）
                                                          │
ゲーム: GameSceneManager.GoStage(n) ──▶ Stage_Runtime シーン ──▶ StageRuntimeLoader が StageData から組み立て
```

- **カタログに登録されたステージ番号**：共通シーン `Stage_Runtime` で読み込まれます。
- **登録されていない番号**：今までどおり個別シーン（`Stage_N.unity`）が読み込まれます。
- **stage_1（チュートリアル）**：専用の仕組みがあるため、シーンのまま運用します。

---

## 開き方

メニュー `Tools > PinchWitch > Stage Editor`

### ウィンドウ上部（全タブ共通）

| 項目 | 説明 |
|---|---|
| パレット | 配置できる Prefab とタイルの一覧（`Assets/Editor/StageEditor/StagePalette.asset`） |
| フォルダから自動登録 | 下記のフォルダを走査してパレットを作り直す |
| 2D 壁面 Z | 敵・鍵など 2D オブジェクトを置く平面の z。既定は 5（影足場と同じ平面） |
| スナップ | 配置時のグリッド間隔。0 でスナップなし |

**自動登録の対象**
- `Prefabs/Enemy`：敵
- `Prefabs/Gimmick`：ギミック
- `Prefabs/Grabbable_Object`：ブロック（サブフォルダも含む）
- `Goal.prefab` と `LightBeamEmitter.prefab`
- `Assets/Tilemap` のタイル

---

## タブ

### 1. 配置

パレットで選んだ Prefab を、SceneView のクリックで置きます。

| 操作 | 動作 |
|---|---|
| 左クリック | 配置する（Undo 可） |
| 右クリック / Esc | 選択を解除する |
| 「配置後も選択を維持」 | オンにすると同じものを連続で置ける |

**置かれる場所**
- **3D の Rigidbody を持つもの**（掴めるブロック・光源）：床（y=0）の上。Prefab 本来の高さが保たれます。
- **それ以外**（敵・鍵・ゴールなど）：2D 壁面（z=5）の上。

**置いたものの親**
- 敵：`Stage/Enemies`
- ギミック：`Stage/Gimmicks`
- ブロック：`Stage/Blocks`

### 2. 敵パラメータ

シーン内の敵（移動コンポーネント `IEnemyMover` を持つもの）を、種類ごとに一覧表示します。

- **選んで編集**：選んだ敵の、移動・スタン・被光のパラメータを編集できます。
- **一括編集**：Ctrl/Shift+クリックで複数選ぶか「この種類をすべて選択」を押すと、まとめて編集できます。
- **表示**：その敵を SceneView の中央に映します。

**SceneView 上のハンドル**（敵を選択すると表示）

| 敵 | ハンドル |
|---|---|
| 巡回の敵（`EnemyPatrolMover2D`） | 巡回範囲の線。端の点をドラッグすると範囲（moveRange）が変わる |
| 線に沿って動く敵（`EnemyPathMover2D`） | 経路の各点をドラッグで移動（0.25 単位でスナップ）。点の追加・削除はインスペクタのボタンで行う |
| トランプ兵（`EnemyWalker2D`） | 向きと崖判定の位置を表示（赤＝崖で反転、緑＝崖から落ちる） |

### 3. ブロック組み立て

正面から見たマス目を塗って、掴めるブロックの Prefab を作ります。

1. 横・縦のマス数、1マスの大きさ、奥行きを決める
2. 「空 / 通常 / ガラス」を選んで、マスをクリックして塗る
3. Prefab 名を入れて「Prefab を生成」を押す

- **保存先**：`Assets/Prefabs/Grabbable_Object/Custom/`。生成するとパレットにも自動で追加されます。
- **ガラスのマス**：影（足場）を落とさず、光線も通します。掴んで動かすことはできます。
- **雛形**：生成には `Grabbable_2Block_Tate.prefab` を雛形として使います。Rigidbody や影生成の設定はそこから引き継ぎます。

### 4. 地形

パレットのタイルで、シーン内の Tilemap を塗ります。

| モード | 操作 |
|---|---|
| 塗る / 消す | クリック、またはドラッグ |
| 矩形で塗る / 矩形で消す | ドラッグで範囲を指定 |

- **対象 Tilemap**：当たり判定のある Tilemap（`Tilemap_ground`）が優先して選ばれます。
- **SceneView で編集する**：オフにすると、通常の選択操作に戻ります。
- **Tile Palette を開く**：Unity 標準の Tile Palette ウィンドウを開きます。

### 5. ステージデータ

| ボタン | 動作 |
|---|---|
| シーンを作成 / 作り直す | `Stage_10.unity` を雛形に `Stage_Runtime` と `Stage_Authoring` を作る（配置物とタイルは空にする） |
| 作成用シーンを開く | `Stage_Authoring` を開く |
| 新規 | 新しい StageData を作る（`Assets/StageData/`） |
| シーン → SO に保存 | 今のシーンの内容を、選択中の StageData に書き出す |
| SO → シーンに読み込み | StageData の内容をシーンに展開する（今ある配置物とタイルは置き換え。Undo 可） |
| この番号で登録 | 選択中の StageData をステージ番号でカタログに登録する |
| 選択 / 解除 | カタログの各行から、編集対象にする / 登録を外す |
| 既存ステージを一括変換 | `Stage_2〜10.unity` を読み取って SO を作り、カタログに登録する（元のシーンは変更しない） |

保存・読み込みは、`StageSceneAnchors` を持つシーン（`Stage_Authoring`）でだけ行えます。

---

## StageData に保存されるもの

| 内容 | 詳細 |
|---|---|
| タイル | Tilemap ごとのセル座標とタイル |
| Grid の位置 | `Grid_2DWorld` の位置 |
| プレイヤー開始位置 / 復帰位置 | Player と SpawnPoint の位置 |
| 配置物 | ステージ用 Prefab の参照、名前、位置、回転、大きさ、アクティブ状態 |
| 変更値 | 配置物のコンポーネントのうち、Prefab から値を変えたもの（JSON） |

**ステージ用 Prefab として扱うもの**
- `Prefabs/Enemy/`、`Prefabs/Gimmick/`、`Prefabs/Grabbable_Object/` の中の Prefab
- `Goal.prefab`、`LightBeamEmitter.prefab`

### 保存されないもの・注意点

- **ほかのオブジェクトへの参照**：保存できないので、Prefab 側の参照がそのまま使われます。
- **配置物の子オブジェクトの変更**：保存されるのは、配置物のルートにあるコンポーネントの変更だけです。
- **共通シーン側のもの**：カメラ・ライト・UI・背景・床・カーテンなどは保存されません。ステージごとに変えたい場合は相談してください。
- **Prefab でない配置物**：Prefab になっていないブロックや敵は、保存時に自動で Prefab 化されます（保存先は下表）。結果は Console に出ます。

| 配置物の種類 | Prefab 化したときの保存先 |
|---|---|
| ブロック | `Prefabs/Grabbable_Object/Converted/` |
| それ以外 | `Prefabs/Gimmick/Converted/` |

---

## テストプレイ

- **Stage_Authoring で Play**：作ったステージをそのまま試せます。ゴールしても、クリアの記録やシーン遷移は起きません（`StageAuthoringMarker`）。
- **Stage_Runtime で Play**：`StageRuntimeLoader` の `overrideStage` に StageData を入れると、そのステージを読み込みます。空ならカタログの先頭のステージを読み込みます。

---

## ギミック・敵の Prefab を作り直す

| メニュー | 生成されるもの |
|---|---|
| `Tools > PinchWitch > Blocks > Generate Glass Block Presets` | ガラス・半分ガラスのブロック（`Grabbable_Object/Custom/`） |
| `Tools > PinchWitch > Gimmicks > Generate Enemy And Key Presets` | トランプ兵（赤・緑）、幽霊、線移動の敵、鍵、鍵ブロック |

再生成すると、同名の Prefab は上書きされます。

---

## 主なファイル

### 実行時（`Assets/Scripts/StageData/`）

| ファイル | 役割 |
|---|---|
| `StageData.cs` | ステージ 1 つ分のデータ |
| `StageCatalog.cs` | ステージ番号と StageData の対応表 |
| `StageBuilder.cs` | StageData をシーンに組み立てる（実行時とエディタで共有） |
| `StageRuntimeLoader.cs` | 共通シーンでステージを読み込む |
| `StageSceneAnchors.cs` | Grid・Player・SpawnPoint・配置先の参照 |
| `StageAuthoringMarker.cs` | 作成用シーンの目印 |

### エディタ（`Assets/Editor/StageEditor/`）

| ファイル | 役割 |
|---|---|
| `StageEditorWindow.cs` | ウィンドウ本体 |
| `Stage*Tab.cs` | 各タブ |
| `StageDataSerializer.cs` | シーンと StageData の相互変換 |
| `StageSceneSetup.cs` | シーンの作成と既存ステージの変換 |
| `GrabbableBlockFactory.cs` | ブロックの生成 |
| `GimmickPresetFactory.cs` | 敵・鍵の生成 |
| `EnemyPatrolMover2DEditor.cs`、`EnemyPathMover2DEditor.cs` | SceneView のハンドル |
