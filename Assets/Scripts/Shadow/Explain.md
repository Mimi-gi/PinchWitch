# 使い方

## 新しい構成
- `ShadowProjectionContext`
  - シーンに 1 個置く
  - `pointLight` と `projectionWall` をまとめて持つ
  - 2D 側の生成済みコライダー出力の親も管理する
- `ShadowColliderBuilder`
  - 影コライダーを作りたい 3D オブジェクトに付ける
  - `Setup Shadow` を押すと、2D 出力先を自動作成する
  - 必要なら `Create Shadow Only Proxy` で `ShadowsOnly` 用の複製も作る
- `ShadowColliderOutput`
  - `PolygonCollider2D + CompositeCollider2D` の出力先
  - Builder が自動で更新する

## 今の影コライダー生成
- 旧実装の「頂点群 -> 凸包」は使わない
- 代わりに「メッシュ三角形ごとに投影 -> 2D 三角形群を CompositeCollider2D で合成」という流れにしている
- そのため L 字や凹形状の影コライダーを保ちやすい

## 新しいオブジェクトを追加する手順
1. シーンに `ShadowProjectionContext` を 1 個置く
2. `pointLight` と `projectionWall` を設定する
3. 影コライダーを作りたいオブジェクトに `ShadowColliderBuilder` を追加する
4. Inspector の `Setup Shadow` を押す
5. 見た目の影も必要なら `Create Shadow Only Proxy` を押す
6. `Rigidbody` が付いている再配置オブジェクトなら、`ShadowDirtyTarget` は自動で紐づく

## 再配置するオブジェクト
- 掴んで置き直すオブジェクトは `ShadowDirtyTarget` で dirty 管理される
- `Setup Shadow` か `Setup Dirty Tracking` を実行すると、`Rigidbody` があるオブジェクトには自動で `ShadowDirtyTarget` を付ける
- `HandPinchController` は実行時に `ShadowDirtyRegistry` を自動生成する
- 掴み中は古い影コライダーを無効化し、離したあとに Rigidbody が落ち着いた時だけ再生成する
- `Space` で 2D に入る時は再生成せず、すでに出来ているコライダーを有効化する

## Blender モデルで注意すること
- Builder は `MeshFilter.sharedMesh` の頂点と三角形を読む
- そのため、影計算に使うメッシュは `Read/Write Enabled` を ON にする必要がある
- 元メッシュが重い時は、影計算専用の簡略メッシュを `shadowProxyRoot` に割り当てると安定する

## 旧シーンとの互換
- `pointLight / targetCube / projectionWall / shadowRealmCollider` の旧参照もそのまま読む
- 既存の `PlaneStageSetter` はまだ使える
- ただし新規追加では `builders` 配列へ手で足さず、`ShadowProjectionContext` と `Setup Shadow` の流れを推奨
