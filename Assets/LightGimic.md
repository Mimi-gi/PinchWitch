# 光ギミック

## 要件
- 指向性のあるライト(これは2D内の影に関わる影にも関わる。単なる明かりではない)
- ライトが2Dの領域内に当たると、その領域内は「影によって生成された足場も元から存在していた足場も」透過、つまりPLはそこに障害が無いモノとして動作する
- ライトも通常のオブジェクト同様回転やスケールが可能
- ライトは楕円柱状に光柱を発する。つまり、奥側や手前側に寄せたときのスケーリングは発生しない
- ライトは3Dオブジェクトによって遮られる。つまり、ライトの照射方向にオブジェクトがある場合はそのオブジェクトによって遮られず2D面に当たった光のみが効果を発揮する
- 2D面に描画するライトは、影同様にUnityおLightとしての光ではなく別の光として描画する
- 光の通り道は半透明の光の領域として表現する

## それに伴う敵の実装について
- 敵にHP的なものを追加
- 光が敵に当たると、移動速度が遅くなり徐々に敵のHPが減っていく。また、光に当たっている間はy方向の移動は行わない
- HPが0になると完全にその場で停止し、プレイヤーは敵に当たっても死ななくなるし素通りできるようになる

---

# 詰めた仕様(2026-07-06 追記)

## 用語
- ライト(光柱): `LightBeamEmitter` を付けたギミックオブジェクト
- 光領域: 光柱が遮蔽を除いて 2D 面(projectionWall)に実際に到達した領域

## 座標系と形状
- 照射方向 = `transform.forward`(+Z)。回転は Transform の回転そのまま
- 断面 = ローカル XY の単位円(直径 1)。`localScale.x / y` で楕円に拡縮する
- 平行光(楕円柱)なので、壁との距離を変えても光領域の大きさは変わらない(要件どおり)
- 壁は影システムと同じ `ShadowProjectionContext.projectionWall` を共用(`projectionWallOverride` で個別指定も可)

## 遮蔽判定(3D)
- 断面を `samplesPerAxis × samplesPerAxis`(既定 24)でグリッドサンプリングし、楕円内の各点から forward へ 3D Raycast
- 壁より手前で `occluderMask`(既定: 全レイヤー)の **3D コライダー**に当たったサンプルは遮蔽扱い
- 影システムと違いメッシュの Read/Write は不要。代わりに遮蔽したいオブジェクトには 3D コライダーが必要
- サンプル行ごとに連続した点灯セルを矩形ストリップ(平行四辺形)へまとめ、壁面上のワールド四角形群として出力
- 輪郭の細かさはサンプリング解像度依存。ギザつきが気になる場合は `samplesPerAxis` を上げる

## 2D 側の透過(足場に穴を開ける)
- `CompositeCollider2D` の `CompositeOperation.Difference` で光領域を足場形状から減算する方式
- `LightPassablePlatform2D` を足場に付けると、子オブジェクト `LightHole2D`(PolygonCollider2D / Difference / compositeOrder=100)が光領域を毎回反映する
- 影生成足場(`ShadowColliderOutput`)には `EnsureSetup` 時に `LightPassablePlatform2D` が自動付与される → 影足場は無設定で透過対応
- 元から存在する足場には手動で `LightPassablePlatform2D` を付ける(Rigidbody2D(Static)+CompositeCollider2D が無ければ自動追加し、既存コライダーを Merge 化する)
- プレイヤーの `obstacleLayers` は `~0`(全レイヤー)のため、レイヤー切替ではなくコライダー形状自体を削るこの方式を採用

## 描画(Unity の Light は不使用)
- 光柱: 半透明の楕円柱メッシュ(`LightBeamVisual`、光源→壁)。遮蔽物の後ろにある部分は深度テストで自然に隠れる
- 2D 面の光領域: コライダーの穴と同一の四角形群からメッシュを生成し、壁法線方向に少し浮かせて描画(`LightRegionOutput`)。コライダーと見た目が必ず一致する
- マテリアル未指定時は URP Unlit の透過マテリアルを自動生成(色は Inspector の fallbackColor)
- 敵の被光判定は物理トリガーではなく四角形群への点内包判定(`LightRegionRegistry.IsPointLit`)。プレイヤー物理と干渉しない

## 敵
- `EnemyHealth2D`: `maxHealth`(既定 100)。`HealthDepleted` / `HealthRestored` イベントを発行。R リセット(ResetOrder=90)で全回復
- `EnemyLightReaction2D`:
  - 毎フレーム `LightRegionRegistry.IsPointLit`(コライダー中心)で被光判定
  - 被光中: `damagePerSecond`(既定 20)で HP 減少。水平パトロールは `litSpeedMultiplier`(既定 0.5)へ減速、垂直パトロールは倍率 0 で停止(= y 方向の移動をしない)
  - HP0: パトロール停止・接触死無効(`EnemyTouchRespawn2D.SetLethal(false)`)・全コライダー isTrigger 化(素通り)・スプライトを `depletedTint` に変色
  - R リセット時は `HealthRestored` を受けて全状態を元に戻す

## シーンへの置き方
1. 空の GameObject に `LightBeamEmitter` を付け、+Z を壁へ向ける。ライト筐体の見た目メッシュは子に置く(筐体に 3D コライダーを付ける場合は `originForwardOffset` で自己遮蔽を回避)
2. 掴んで回転・スケールさせたい場合は、既存ギミック同様 Grabbable レイヤー + Rigidbody を付ける(掴みシステム側の対応で完結)
3. `LightRegionOutput` / `LightBeamVisual` / `LightHole2D` は実行時に自動生成されるため配置不要
4. 元から存在する足場に `LightPassablePlatform2D` を付ける。敵には `EnemyHealth2D` + `EnemyLightReaction2D` を付ける

## 実装ファイル
- `Assets/Scripts/LightGimic/LightBeamEmitter.cs` — サンプリング・光領域構築の中心
- `Assets/Scripts/LightGimic/LightRegionOutput.cs` — 光領域の保持・描画・点内包判定
- `Assets/Scripts/LightGimic/LightRegionRegistry.cs` — 全光領域の静的レジストリ(変更イベント)
- `Assets/Scripts/LightGimic/LightPassablePlatform2D.cs` — 足場への穴あけ
- `Assets/Scripts/LightGimic/LightBeamVisual.cs` — 光柱の半透明メッシュ
- `Assets/Scripts/LightGimic/LightLitQuad.cs` — 光領域四角形(点内包判定つき)
- `Assets/Scripts/LightGimic/LightGimicRenderingUtility.cs` — 透過マテリアル生成
- `Assets/Scripts/EnemyHealth2D.cs` / `EnemyLightReaction2D.cs` — 敵側
- 変更: `ShadowColliderOutput`(LightPassablePlatform2D 自動付与) / `EnemyPatrolMover2D`(外部速度倍率 API) / `EnemyTouchRespawn2D`(接触死フラグ)

## 未対応・今後の拡張候補
- ~~光柱メッシュは遮蔽物で短くならない(壁まで一律)~~ → 2026-07-07 対応済み(「実装計画 B」参照)
- 複数ライトは同時動作するが、穴あけ・被光判定は全ライトの合算
- 光領域の見た目にグラデーション・ノイズ等を足す場合は `LightRegionOutput` のマテリアル差し替えで対応

# 実装計画(2026-07-07 追記)

## A. 掴み(Grabbable)対応 【実装済み】
既存の掴みシステム(`GrabInteractor` / `MouseGrabController`)をそのまま使い、ライトを他の Grabbable と同じ扱いにする。

### 仕組みの前提(掴みシステム側の要件)
- `MouseGrabController` のレイキャストが当たる **3D コライダー**(Grabbable レイヤー = 3)が必要
- コライダーの `attachedRigidbody` から Rigidbody を解決するため、**Rigidbody は親(LightBeam 本体)に置き、コライダーは子でもよい**
- `GrabInteractor.CanGrabBody` は Rigidbody の GameObject のレイヤーを見る → LightBeam 本体もレイヤー 3 にする
- 回転は既存の `RotateGrabbedObject`(既定 Y 軸モード)で可能。スケールは Transform で調整(実行時変更なし)

### シーン構成(Stage_10 1 に適用済み)
```
LightBeam (レイヤー3, LightBeamEmitter apertureLocalZ=0.35, Rigidbody: mass1/useGravity/回転XYZ固定=112)
└─ LightHousing (レイヤー3, Cube メッシュ, BoxCollider, 影キャスト無効)
     localPosition (0, 0, 0) / localScale (1.1, 1.1, 0.7)
```
- **筐体は transform 原点に置く(動かさない)**。掴み回転(`MoveRotation`)は transform 原点まわりなので、筐体をずらすと回転時に見た目の中心とビームがずれる
- 照射口は `apertureLocalZ`(0.35)で筐体前面へオフセット。`originForwardOffset`(0.05)と合わせて**自己遮蔽を回避**(筐体はローカル z ∈ [-0.35, +0.35])
- 筐体の `m_CastShadows: 0` — Unity 影が「足場に見える偽の影」を落とさないようにする
- リリース時は掴みシステムの仕様どおり真下に落下(他 Grabbable と同一挙動)。固定したい場合は Rigidbody を kinematic 化するか Config の restore フラグで調整
- Rigidbody の Constraints は他の Grabbable プレハブと同じ回転 XYZ 固定(112)。物理では回転せず、掴み中だけ `GrabInteractor` が固定を外して回転操作を受け付け、リリース後に固定へ戻る

## B. 光柱メッシュの遮蔽(途中で遮られる表現) 【実装済み】
現状の光柱は「外周リングを壁まで伸ばしたチューブ」で、遮蔽物があっても短くならない。これを**遮蔽サンプリングと同じグリッドを使った深度付きボリューム**に置き換える。

### 方針
- `SampleOcclusion` の Raycast を `RaycastHit` 付きに変更し、セルごとに**光の到達距離**(遮蔽物ヒット距離 or 壁までの距離)を記録する(追加レイキャストなし)
- 光柱メッシュをセル単位で構築:
  1. **終端キャップ**: 楕円内の各セルに、到達距離の位置で断面と平行な四角形
  2. **段差スカート**: 隣接セル間で到達距離が異なる場合、その差分を繋ぐ側面四角形(遮蔽物の輪郭に沿った「切り口」になる)
  3. **外周壁**: 楕円境界のセル縁は照射口(z=0)から到達距離までの側面四角形(旧チューブに相当)
- 変更検知ハッシュに到達距離(1cm 量子化)を含める → 遮蔽物が奥行き方向に動いてもメッシュが追従
- `LightBeamVisual.UpdateTube` は頂点/三角形リストを受け取る `UpdateMesh` に置き換え(メッシュ構築はグリッドを持つ Emitter 側)

### トレードオフ
- 光柱の輪郭・切り口はセル解像度(`samplesPerAxis`、既定 24)依存の角ばった見た目になる。壁の光領域も同じ解像度なので統一感はある。滑らかにしたい場合は `samplesPerAxis` を上げる(最大 64)
- `beamPerimeterSegments` は不要になるため削除

### 追記(2026-07-07): 光柱の滑らか化
グリッド全面をセル四角形で張る方式は輪郭が階段状になるため、以下の構成に変更した(サンプリング系は不変):
- **側面**: 解析的な楕円チューブ(`beamPerimeterSegments`、既定 48。見た目専用の分割数として復活)。外周頂点ごとの長さは到達距離グリッドを参照し、壁到達なら輪郭点自身の壁面距離で滑らかに着地、遮蔽ならセルの到達距離で途切れる
- **終端キャップ**: 遮蔽されたセルのみ(壁到達分は `LightRegionOutput` の壁面メッシュが描くため省略)
- **段差スカート**: 到達距離が異なる隣接セル間のみ。両方壁到達の場合は張らない(斜め壁での不要な階段を防止)
- 遮蔽の「切り口」だけはセル解像度依存のまま(壁の光領域と同じ解像度で統一感を維持)
