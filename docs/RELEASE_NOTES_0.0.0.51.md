# Actor Morpher v0.0.0.51

## 日本語

- ウィンドウを上下にスクロールしても、上部のタブを表示したままにしました。
- Actor一覧の現在装備に、武器・サブ武器のアイコン、名前、モデル番号、Variant、染色を追加しました。
- 武器アイコンから、自キャラの現在のクラス・ジョブ用の武器を選択できます。名前・モデル番号（`w9005`など）での検索と、お気に入りに対応しています。
- 武器・サブ武器のアイコンまたは名前を右クリックすると、その手の武器だけを外せます。
- 武器の変更時にモーションも更新するようにしました。ジョブ自体は変更しません。
- ピン留めした外見の差分が装備だけの場合は、装備変更の処理で維持するようにしました。装備復元後にモデルなどが元の状態と一致している場合も、追加のモデル再生成を省きます。
- 装備変更時のアニメーション状態を調べる診断記録を追加しました。

武器の編集はActor一覧で行います。一括装備のコピー元には武器を含みません。Young NPCモデルで必要になるジョブのモーション・武器装着データは別途必要です。CMC側の修復やPMPは、この配布物には含みません。

### 確認が残っている項目

- 武器の右クリック解除後の再装備・明示的な復元と、ピン留め中の表示。
- カットシーン中の一括装備変更・復元で発生したTポーズの解消確認。今回の対策で全ケースが解消したことは未確認です。
- タブ固定後のスクロール、サイズ変更、各タブの操作。

## English

- The top tabs now stay visible while the window content scrolls.
- Added mainhand and offhand weapon icons, names, model numbers, variants, and dyes to the Actor list's current equipment.
- Click a weapon icon to choose a weapon for your own current class/job. Name and model-number searches (such as `w9005`) and favorites are supported.
- Right-click a weapon's icon or name to remove only that hand's weapon.
- Weapon changes now also update weapon motions without changing the actor's class/job.
- Pinned appearances now use equipment updates when only the outfit differs. Restoring an outfit also avoids an additional model recreation when the model and other non-outfit fields already match the original game appearance.
- Added diagnostic observations of animation state during equipment changes.

Weapons are edited in the Actor list; they are not part of the Bulk Outfit source. Young NPC models may need separate job-motion and weapon-attachment assets. The CMC repair and companion PMP assets are not included in this package.

### Checks still pending

- Re-equipping and explicitly restoring weapons after right-click removal, including pinned actors.
- In-game confirmation that the reported cutscene T-pose issue during Bulk Outfit changes/restoration is resolved. Resolution of all cases has not been confirmed.
- Scrolling, resizing, and tab interactions with the fixed tab bar.
