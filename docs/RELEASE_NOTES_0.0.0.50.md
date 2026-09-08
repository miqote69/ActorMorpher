# Actor Morpher v0.0.0.50

## 日本語

- 一括装備の適用対象フィルターと除外フィルターを、見出しと枠のあるパネルに整理しました。コピー元装備は折り畳めます。
- 一括装備に「ターゲットに適用」を追加しました。現在のゲーム内ターゲットへ直接適用できます。
- コピー元を「自分の現在の外見」「ターゲットの現在の外見」「自分のミラージュプレート」から選び、共通の「装備を取り込む」ボタンで取り込めるようにしました。
- ミラージュプレートのデータが未読込の場合は、取り込み操作で取得を開始し、受信後にコピー元を更新します。ゲーム内のミラージュプレート画面を手動で開く必要はありません。取得中の表示とキャンセル操作も追加しました。
- ミラージュプレート取り込み時に、以前のコピー元の頭装備非表示設定を引き継がないようにしました。
- 一括装備の「変更したActorを復元」は、ピン留め中のActorを対象から除外します。
- 一括装備のフェイスアクセサリーを右クリックで外せるように修正しました。
- 3Dプレビューで読み込み済みのモデル・マテリアルデータを再利用し、モデルを切り替える際の重複処理を減らしました。

ミラージュプレートから取り込むのは保存済みの防具・アクセサリーと染色です。武器は対象外で、フェイスアクセサリーとバイザーは現在のコピー元の値を保ちます。取り込みだけではActorへ適用されません。

## English

- Reorganized Bulk Outfit's target and exclusion filters into clearly labeled panels. The source equipment section can be collapsed.
- Added **Apply to Target** to Bulk Outfit for applying the source outfit directly to the current in-game target.
- Added a single source selector for your current appearance, the current target's appearance, and your saved glamour plates, with one **Import Equipment** action.
- Importing a glamour plate now requests unloaded plate data and updates the source when it arrives, without requiring you to manually open the game's glamour plate window. Loading status and cancellation are available.
- Glamour plate imports now show headgear instead of inheriting a previous source's hidden-headgear setting.
- Bulk Outfit's **Restore Modified Actors** now excludes pinned actors.
- Fixed right-click removal of facewear from the Bulk Outfit source.
- Reused loaded model and material data in the 3D preview to reduce repeated processing when switching models.

Glamour plate imports use saved armor, accessories, and dyes. Weapons are excluded; facewear and visor state retain their current source values. Importing alone does not apply the outfit to an actor.
