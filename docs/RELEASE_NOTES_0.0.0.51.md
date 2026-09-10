# Actor Morpher v0.0.0.51

## 日本語

- 設定タブの隣にリリースノートを追加しました。更新内容をプラグイン内で確認でき、表示言語の設定に合わせて日本語・英語・ドイツ語・フランス語が切り替わります。
- ウィンドウを上下にスクロールしても、上部のタブを表示したままにしました。
- Actor一覧の現在装備に、武器・サブ武器のアイコン、名前、モデル番号、Variant、染色を追加しました。
- 武器アイコンから、自キャラの現在のクラス・ジョブ用の武器を選択できます。名前・モデル番号（`w9005`など）での検索と、お気に入りに対応しています。
- 武器・サブ武器のアイコンまたは名前を右クリックすると、その手の武器だけを外せます。
- 武器の変更時にモーションも更新するようにしました。ジョブ自体は変更しません。
- ピン留めした外見の差分が装備だけの場合は、装備変更の処理で維持するようにしました。装備復元後にモデルなどが元の状態と一致している場合も、追加のモデル再生成を省きます。
- 装備変更時のアニメーション状態を調べる診断記録を追加しました。

武器の編集はActor一覧で行います。一括装備のコピー元には武器を含みません。Young NPCモデルで必要になるジョブのモーション・武器装着データは別途必要です。これらの追加データは、本プラグインには含まれません。

## English

- Added a Release Notes tab next to Settings. Read updates inside the plugin in Japanese, English, German or French, following your UI language setting.
- The top tabs now stay visible while the window content scrolls.
- Added mainhand and offhand weapon icons, names, model numbers, variants, and dyes to the Actor list's current equipment.
- Click a weapon icon to choose a weapon for your own current class/job. Name and model-number searches (such as `w9005`) and favorites are supported.
- Right-click a weapon's icon or name to remove only that hand's weapon.
- Weapon changes now also update weapon motions without changing the actor's class/job.
- Pinned appearances now use equipment updates when only the outfit differs. Restoring an outfit also avoids an additional model recreation when the model and other non-outfit fields already match the original game appearance.
- Added diagnostic observations of animation state during equipment changes.

Weapons are edited in the Actor list; they are not part of the Bulk Outfit source. Young NPC models may need separate job-motion and weapon-attachment assets. These additional assets are not included in this plugin.

## Deutsch

- Neben den Einstellungen gibt es jetzt den Tab „Versionshinweise“. Die Änderungen lassen sich direkt im Plugin auf Japanisch, Englisch, Deutsch oder Französisch lesen, passend zur eingestellten UI-Sprache.
- Die oberen Tabs bleiben beim Scrollen des Fensterinhalts sichtbar.
- Die aktuelle Ausrüstung in der Akteurliste zeigt jetzt Symbole, Namen, Modellnummern, Varianten und Färbungen für Haupt- und Nebenhandwaffen.
- Über das Waffensymbol lassen sich Waffen für die aktuelle Klasse bzw. den aktuellen Job deines eigenen Charakters auswählen. Namenssuche, Modellnummernsuche (z. B. `w9005`) und Favoriten werden unterstützt.
- Ein Rechtsklick auf das Symbol oder den Namen einer Waffe legt nur die Waffe dieser Hand ab.
- Beim Waffenwechsel werden auch die Waffenanimationen aktualisiert. Klasse und Job des Akteurs bleiben unverändert.
- Bei angehefteten Erscheinungsbildern wird die Ausrüstung direkt aktualisiert, wenn nur das Outfit abweicht. Beim Wiederherstellen eines Outfits entfällt eine zusätzliche Neuerstellung des Modells, wenn Modell und übrige Merkmale bereits dem ursprünglichen Erscheinungsbild im Spiel entsprechen.
- Für die Untersuchung des Animationszustands bei Ausrüstungsänderungen wurden Diagnoseaufzeichnungen ergänzt.

Waffen werden in der Akteurliste bearbeitet und gehören nicht zur Quelle für Massen-Outfits. Junge NPC-Modelle können zusätzliche Daten für Jobanimationen und Waffenbefestigungen benötigen. Diese zusätzlichen Daten sind nicht in diesem Plugin enthalten.

## Français

- Un onglet « Notes de version » a été ajouté à côté des paramètres. Les nouveautés sont consultables dans le plugin en japonais, anglais, allemand ou français, selon la langue choisie pour l’interface.
- Les onglets du haut restent visibles lorsque le contenu de la fenêtre défile.
- L’équipement actuel de la liste des acteurs affiche désormais les icônes, noms, numéros de modèle, variantes et teintures des armes principales et secondaires.
- Cliquez sur l’icône d’une arme pour choisir une arme adaptée à la classe ou au job actuel de votre propre personnage. La recherche par nom ou numéro de modèle (par exemple `w9005`) et les favoris sont disponibles.
- Un clic droit sur l’icône ou le nom d’une arme retire uniquement l’arme de cette main.
- Les changements d’arme mettent aussi à jour les animations d’arme, sans modifier la classe ni le job de l’acteur.
- Les apparences épinglées utilisent désormais une mise à jour de l’équipement lorsque seule la tenue diffère. La restauration d’une tenue évite aussi de recréer le modèle si celui-ci et les autres caractéristiques correspondent déjà à l’apparence d’origine dans le jeu.
- Des observations de diagnostic de l’état des animations lors des changements d’équipement ont été ajoutées.

Les armes se modifient dans la liste des acteurs ; elles ne font pas partie de la source de tenue en lot. Les modèles de jeunes PNJ peuvent nécessiter des données supplémentaires d’animations de job et de fixation des armes. Ces données supplémentaires ne sont pas incluses dans ce plugin.
