# Actor Morpher v0.0.0.56

## 日本語

### v0.0.0.56

- 「変更済みをピン留め」が全エリアに対応しました。全ピン解除後も、同じセッションで変更した別エリアのNPCの装備・キャラメイクを再保存できます。
- NPCが再表示されたとき、ピン留めした装備が反映されるまでの待ち時間を短縮しました。
- プレイヤーの武器ピンは、保存時と同じジョブでのみ有効になります。既存のピンで武器も固定するには、そのジョブでピン留めし直してください。
- NPCの外見を自分に適用したあと、「元の状態に復元」で自キャラに戻れないことがある問題を修正しました。ジョブ変更時の武器に加え、ピン留めしていないときはジョブ・装備変更時に頭装備の表示とフェイスアクセサリーもゲーム側の設定に戻ります。
- 別のActorを選択・ピン留めしたあとも、キャラメイクの「元に戻す」で変更前の状態に戻せるようにしました。
- BuddyRiderでバディチョコボに乗せたNPCが、装備の貼り付けで消えることがある問題を修正しました。

### v0.0.0.55

- ヒューマン系Actorのキャラメイク編集を追加しました。「基本・体型／顔／髪／色・装飾」に整理し、顔・髪型は画像から選べます。変更は操作中に反映されます。
- 性別はアイコンで選択でき、ボディタイプは種族・部族・性別に応じた候補を表示します。色はゲーム内パレットと自由色を切り替えられます。
- プレイヤーやNPCのキャラメイク・装備を、ヒューマン系Actor間で個別にコピー＆ペーストできます。デミヒューマン・モンスターは対象外です。
- 一括装備のコピー元一覧にコピー／貼り付けを追加しました。貼り付けはコピー元を更新し、対象Actorへの適用は従来の適用操作で行います。武器は一括装備の対象外です。
- モデル検索のヒューマン系モデルに装備・キャラメイクのコピーボタンを追加し、「自分に適用／ターゲットに適用」を詳細の上部へ移動しました。
- Actor一覧に「自分から近い順」を追加し、ゲーム内でターゲット中のActorをマークで表示します。
- 一覧で選択したActorの頭上に、名前付きの専用マーカーを表示します。表示モデルやカメラに応じた位置調整、画面外での非表示に対応しました。
- Actor一覧の描画とピン留め状態の確認処理を軽量化しました。
- ピン留めをログイン中のプレイヤーキャラごとに保存し、別キャラへの切り替えや再ログイン・ゲーム再起動時の保持を改善しました。
- 他プラグインでNPC化したミニオンや、マウント・バディチョコボに表示したNPCのピン留め保持と、再表示直後の反映を改善しました。
- ゲームデータ上の名前と元の外見が一致するヒューマン系の同一NPCグループでは、内部IDが異なるNPCにも装備・染色・キャラメイクを自動共有します。旧ピン留めも対象です。
- 同一NPCグループの1体でピン留めを解除すると、グループ全体の共有を停止します。他の個別ピンは残り、共有停止の状態も保存されます。
- 身長・胸囲などの編集で装備が外れる、キャラクターが消える、適用したNPCの外見が元へ戻る問題を修正し、不要な再読み込みとちらつきを減らしました。
- 性別変更時に対応する顔モデルがない場合は、変更先の顔タイプ1へ切り替えます。

### ご利用時の補足

- キャラメイク・装備のコピー＆ペーストはプラグイン内で使用します。一括装備からコピーした内容には武器を含みません。
- 以前のバージョンで保存したミニオンのピン留めが引き継がれない場合は、現在のミニオンを再度ピン留めしてください。

### v0.0.0.54

- エリア移動やNPCの再表示時に、ピン留めした装備を引き継ぐ処理と反映速度を改善しました。
- 頭装備の詳細設定を追加しました。頭装備の表示、バイザー、ヴィエラの耳を切り替えられます。
- 頭装備を非表示にしても、装備と染色を保持するよう修正しました。再表示すると同じ装備に戻ります。
- ミラージュプレートを利用可能なエリアで自動取得し、キャラクター別に保存します。取得後はエリア移動・再起動後も使え、最終取得日時を確認できます。
- フェイスアクセサリーの選択を「すべて／お気に入り」に整理しました。

### v0.0.0.53

- ピン留め中にジョブを変更すると、キャラクターのリロードが繰り返されることがある不具合を修正しました。

### v0.0.0.52

- 装備選択画面を見やすくしました。アイコンを大きくし、名前・番号・ILの表示と選択時の赤背景を整えました。
- 装備を番号順に並べ、「すべて／おしゃれ／お気に入り」で探せるようにしました。おしゃれ装備は装備レベル1・IL1が対象です。
- Actorの詳細を整理しました。キャラメイク情報を見やすくし、細かな情報は折り畳めます。バトルNPC番号（BNpcBase）も表示します。
- Actor一覧で、防具・アクセサリー・武器・サブ武器の染色1・2を自由な色に変更できます。色は操作中に反映されます。右クリックで染色なしにできます。
- メタリックのON／OFFを追加しました。ゲームのメタリック染料をもとに色と光沢を表現します。自由色にも対応し、一括装備のコピー元にも設定できます。
- リリースノートを短く、分かりやすくしました。

## English

### v0.0.0.56

- Pin modified actors now includes all areas. After clearing all pins, you can re-pin NPC equipment and customization changed in another area during the same session.
- Pinned equipment is reapplied sooner when NPCs reappear.
- Player weapons only stay pinned for the job they were saved on. To pin weapons from an older appearance again, re-pin it on the intended job.
- Fixed cases where you could not restore your character after applying an NPC appearance. Weapons now update on job change. When the appearance is unpinned, job or gear changes also restore the game's headgear visibility and facewear settings.
- Reset edits now restores customization to its pre-edit state even after selecting or pinning another actor.
- Fixed NPCs disappearing when pasting equipment onto them while they were riding a companion chocobo through BuddyRider.

### v0.0.0.55

- Added character customization for Human actors, organized into Body, Face, Hair, and Colors & Features. Choose faces and hairstyles from images. Changes appear as you edit.
- Select gender with icons. Body type choices follow the actor's race, clan, and gender. Switch between the game palette and custom colors.
- Copy and paste character customization and equipment separately between Human player characters and NPCs. Demihumans and monsters are not supported.
- Added Copy and Paste to the Bulk Outfit source list. Pasting updates the source; use the existing Apply action to apply it to actors. Bulk Outfit does not include weapons.
- Added equipment and customization copy buttons for Human models in Model Search. Moved Apply to Yourself and Apply to Target to the top of the details panel.
- Added a Nearest to You sort order to the Actor list and a mark for the actor currently targeted in the game.
- Added a named marker above the actor selected in the list. Its position follows the displayed model and camera, and it is hidden when outside the screen.
- Reduced the work needed to draw the Actor list and check pin states.
- Pins are saved separately for each logged-in player character. Improved retention when switching characters, logging in again, or restarting the game.
- Improved pin retention and reapplication after reappearance for minions turned into NPCs by other plugins, and NPCs displayed on mounts or companion chocobos.
- Equipment, dyes, and customization are shared automatically across different internal IDs in the same Human NPC group when their original game-data names and appearances match. Existing pins are included.
- Unpinning one member stops sharing for the entire NPC group. Other individual pins remain, and the stopped sharing state is saved.
- Fixed cases where editing height, bust size, or similar settings removed equipment, made the character disappear, or reverted an applied NPC appearance. Reduced unnecessary reloads and flickering.
- When changing gender, if the same face model is unavailable, the face switches to Type 1 for the new gender.

### Usage notes

- Character customization and equipment copy/paste is used within the plugin. Equipment copied from Bulk Outfit does not include weapons.
- If a minion pin saved by an older version is not carried over, pin the currently displayed minion again.

### v0.0.0.54

- Improved how pinned equipment is retained and reapplied when changing areas or when NPCs reappear.
- Added headgear settings for visibility, visor position and Viera ears.
- Hiding headgear now keeps the selected equipment and dyes. Showing it again restores the same appearance.
- Glamour plates are collected automatically in supported areas and saved separately for each character. Once saved, they remain available after changing areas or restarting. The last acquisition time is shown.
- Simplified the facewear picker to All and Favorites.

### v0.0.0.53

- Fixed an issue where changing jobs with an actor pinned could cause the character to reload repeatedly.

### v0.0.0.52

- Improved the equipment picker with larger icons, clearer names, model numbers and item levels, and an aligned red selection background.
- Equipment is sorted by model number. Browse All, Fashion or Favorites. Fashion includes equipment with equip level 1 and item level 1.
- Simplified Actor details. Character-creation information is easier to find, extra details can be collapsed, and Battle NPC IDs (BNpcBase) are shown.
- Choose any color for both dye channels on armor, accessories, mainhand and offhand weapons in the Actor list. Changes appear as you edit. Right-click for No Dye.
- Added metallic ON/OFF. Colors and shine use the game's metallic dyes as a reference, with support for custom colors and the Bulk Outfit source.
- Made Release Notes shorter and easier to read.

## Deutsch

### v0.0.0.56

- Die Sammelfunktion zum Anheften geänderter Akteure berücksichtigt jetzt alle Gebiete. Nach dem Lösen aller Anheftungen lassen sich die Ausrüstung und Charakteranpassung von NPCs, die in derselben Sitzung in einem anderen Gebiet geändert wurden, erneut anheften.
- Angeheftete Ausrüstung wird beim Wiedererscheinen von NPCs schneller angewendet.
- Waffen von Spielercharakteren bleiben nur für den Job angeheftet, mit dem sie gespeichert wurden. Heften Sie ältere Einträge mit dem gewünschten Job erneut an, um auch die Waffen wieder anzuheften.
- Ein Fehler bei der Wiederherstellung des eigenen Charakters nach dem Anwenden eines NPC-Aussehens wurde behoben. Waffen wechseln jetzt mit dem Job. Ist das Aussehen nicht angeheftet, übernimmt ein Job- oder Ausrüstungswechsel auch die Spieleinstellungen für die Sichtbarkeit der Kopfbedeckung und für Gesichtsaccessoires.
- In der Charakteranpassung funktioniert „Zurücksetzen“ auch nach dem Auswählen oder Anheften eines anderen Akteurs.
- Ein Fehler wurde behoben, durch den mit BuddyRider auf einen Mitstreiter-Chocobo gesetzte NPCs beim Einfügen von Ausrüstung verschwinden konnten.

### v0.0.0.55

- Die Charakteranpassung für Akteure der Kategorie Human wurde hinzugefügt, aufgeteilt in Körper, Gesicht, Haare sowie Farben und Merkmale. Gesichter und Frisuren lassen sich anhand von Bildern auswählen. Änderungen werden während der Bearbeitung angewendet.
- Das Geschlecht wird über Symbole gewählt. Die verfügbaren Körpertypen richten sich nach Volk, Stamm und Geschlecht. Für Farben stehen die Spielpalette und freie Farben zur Auswahl.
- Charakteranpassung und Ausrüstung können getrennt zwischen Spielercharakteren und NPCs der Kategorie Human kopiert und eingefügt werden. Demihumane und Monster werden nicht unterstützt.
- Die Quellliste für Massen-Outfits bietet jetzt Kopieren und Einfügen. Einfügen aktualisiert die Quelle; die Übertragung auf Akteure erfolgt über die bisherige Anwenden-Aktion. Massen-Outfits enthalten keine Waffen.
- Die Modellsuche bietet bei Human-Modellen Schaltflächen zum Kopieren von Ausrüstung und Charakteranpassung. Auf sich selbst anwenden und Auf Ziel anwenden stehen jetzt oben in den Details.
- Die Akteurliste kann nach Entfernung zum eigenen Charakter sortiert werden. Das aktuelle Ziel im Spiel wird markiert.
- Über dem in der Liste ausgewählten Akteur erscheint eine Markierung mit Namen. Ihre Position richtet sich nach dem angezeigten Modell und der Kamera; außerhalb des Bildschirms wird sie ausgeblendet.
- Die Darstellung der Akteurliste und die Prüfung des Anheftungsstatus wurden ressourcenschonender gestaltet.
- Anheftungen werden für jeden angemeldeten Spielercharakter getrennt gespeichert. Ihre Übernahme bei Charakterwechseln, erneuter Anmeldung und Spielneustarts wurde verbessert.
- Anheftungen und ihre erneute Anwendung wurden für durch andere Plugins in NPCs verwandelte Begleiter sowie für NPCs auf Reittieren oder Mitstreiter-Chocobos verbessert.
- Ausrüstung, Färbungen und Charakteranpassung werden automatisch zwischen verschiedenen internen IDs derselben NPC-Gruppe der Kategorie Human geteilt, wenn Namen und ursprüngliches Aussehen in den Spieldaten übereinstimmen. Bestehende Anheftungen sind eingeschlossen.
- Wird die Anheftung eines Gruppenmitglieds gelöst, endet die gemeinsame Nutzung für die gesamte NPC-Gruppe. Andere individuelle Anheftungen bleiben erhalten. Auch der beendete Freigabestatus wird gespeichert.
- Fehler wurden behoben, durch die Änderungen an Größe, Oberweite oder ähnlichen Werten Ausrüstung entfernten, den Charakter unsichtbar machten oder ein angewendetes NPC-Aussehen zurücksetzten. Unnötige Neuladevorgänge und Flackern wurden reduziert.
- Ist beim Geschlechtswechsel dasselbe Gesichtsmodell nicht verfügbar, wird Gesichtstyp 1 des neuen Geschlechts gewählt.

### Hinweise zur Verwendung

- Kopieren und Einfügen von Charakteranpassung und Ausrüstung erfolgt innerhalb des Plugins. Aus Massen-Outfits kopierte Ausrüstung enthält keine Waffen.
- Wird eine mit einer älteren Version gespeicherte Begleiter-Anheftung nicht übernommen, heften Sie den aktuell angezeigten Begleiter erneut an.

### v0.0.0.54

- Die Übernahme und erneute Anwendung angehefteter Ausrüstung bei Gebietswechseln und beim Wiedererscheinen von NPCs wurde verbessert.
- Neue Kopfbedeckungsoptionen: Sichtbarkeit, Visierstellung und Viera-Ohren.
- Beim Ausblenden bleiben Kopfbedeckung und Färbungen erhalten. Beim Einblenden erscheint dieselbe Ausrüstung wieder.
- Projektionsplatten werden in geeigneten Gebieten automatisch abgerufen und für jeden Charakter getrennt gespeichert. Danach bleiben sie auch nach Gebietswechseln und Neustarts verfügbar. Der letzte Abrufzeitpunkt wird angezeigt.
- Die Auswahl für Gesichtsaccessoires bietet jetzt nur „Alle“ und „Favoriten“.

### v0.0.0.53

- Ein Fehler wurde behoben, durch den ein angehefteter Charakter nach einem Jobwechsel wiederholt neu geladen werden konnte.

### v0.0.0.52

- Die Ausrüstungsauswahl ist übersichtlicher: größere Symbole, klarere Namen, Modellnummern und Gegenstandsstufen sowie ein korrekt ausgerichteter roter Auswahlhintergrund.
- Ausrüstung ist nach Modellnummer sortiert. Zur Auswahl stehen „Alle“, „Mode“ und „Favoriten“. „Mode“ enthält Ausrüstung mit Ausrüstungsstufe 1 und Gegenstandsstufe 1.
- Die Akteurdetails sind übersichtlicher. Informationen zur Charaktererstellung sind leichter zu finden, weitere Details lassen sich einklappen. Kampf-NPC-IDs (BNpcBase) werden ebenfalls angezeigt.
- Beide Färbekanäle von Rüstung, Accessoires sowie Haupt- und Nebenhandwaffen lassen sich in der Akteurliste frei einfärben. Änderungen sind sofort sichtbar. Rechtsklick entfernt die Färbung.
- Metallisch lässt sich auf ON oder OFF stellen. Farben und Glanz orientieren sich an den metallischen Farbstoffen des Spiels. Freie Farben und die Quellausrüstung von Massen-Outfits werden unterstützt.
- Die Versionshinweise sind kürzer und verständlicher.

## Français

### v0.0.0.56

- L’épinglage des acteurs modifiés couvre désormais toutes les zones. Après avoir retiré toutes les épingles, vous pouvez réépingler l’équipement et la personnalisation des PNJ modifiés dans une autre zone pendant la même session.
- L’équipement épinglé est réappliqué plus rapidement lorsque les PNJ réapparaissent.
- Les armes des personnages joueurs restent épinglées uniquement pour le job utilisé lors de l’enregistrement. Pour épingler à nouveau les armes d’une ancienne apparence, réépinglez-la avec le job voulu.
- Correction d’un problème de restauration de votre personnage après lui avoir appliqué une apparence de PNJ. Les armes suivent désormais le changement de job. Si l’apparence n’est pas épinglée, un changement de job ou d’équipement rétablit aussi les réglages du jeu pour la visibilité du couvre-chef et les accessoires de visage.
- Dans la personnalisation, « Réinitialiser » fonctionne aussi après avoir sélectionné ou épinglé un autre acteur.
- Correction de la disparition, lors du collage d’un équipement, des PNJ placés sur un chocobo compagnon avec BuddyRider.

### v0.0.0.55

- Ajout de la personnalisation des acteurs de catégorie Human, organisée en Corps, Visage, Cheveux, et Couleurs et détails. Les visages et coiffures se choisissent à partir d'images. Les modifications sont appliquées pendant l'édition.
- Le sexe se choisit à l'aide d'icônes. Les types de corps proposés dépendent de la race, de l'ethnie et du sexe. Les couleurs peuvent être choisies dans la palette du jeu ou librement.
- La personnalisation et l'équipement peuvent être copiés et collés séparément entre personnages joueurs et PNJ de catégorie Human. Les demi-humains et les monstres ne sont pas pris en charge.
- Ajout des commandes Copier et Coller à la liste source des tenues en lot. Coller met à jour la source ; l'application aux acteurs utilise la commande habituelle. Les tenues en lot n'incluent pas les armes.
- Ajout de boutons pour copier l'équipement et la personnalisation des modèles Human dans la recherche de modèles. Les commandes Appliquer à soi et Appliquer à la cible sont placées en haut des détails.
- Ajout d'un tri par proximité avec votre personnage dans la liste des acteurs et d'un repère pour la cible actuelle du jeu.
- Un marqueur avec le nom apparaît au-dessus de l'acteur sélectionné dans la liste. Sa position suit le modèle affiché et la caméra ; il est masqué lorsqu'il est hors écran.
- Allègement de l'affichage de la liste des acteurs et de la vérification des épingles.
- Les épingles sont enregistrées séparément pour chaque personnage joueur connecté. Amélioration de leur conservation lors d'un changement de personnage, d'une reconnexion ou d'un redémarrage du jeu.
- Amélioration de la conservation et de la réapplication des épingles pour les mascottes transformées en PNJ par d'autres plugins et les PNJ affichés sur des montures ou des chocobos compagnons.
- L'équipement, les teintures et la personnalisation sont automatiquement partagés entre différents identifiants internes d'un même groupe de PNJ de catégorie Human lorsque leurs noms et apparences d'origine dans les données du jeu correspondent. Les épingles existantes sont incluses.
- Retirer l'épingle d'un membre arrête le partage pour tout le groupe de PNJ. Les autres épingles individuelles sont conservées, ainsi que l'état d'arrêt du partage.
- Correction de problèmes où la modification de la taille, du tour de poitrine ou de valeurs similaires retirait l'équipement, faisait disparaître le personnage ou rétablissait son apparence de PNJ d'origine. Réduction des rechargements inutiles et du scintillement.
- Lors d'un changement de sexe, si le même modèle de visage n'est pas disponible, le visage passe au type 1 du nouveau sexe.

### Conseils d'utilisation

- Le copier-coller de la personnalisation et de l'équipement s'utilise au sein du plugin. L'équipement copié depuis les tenues en lot n'inclut pas les armes.
- Si une épingle de mascotte enregistrée avec une ancienne version n'est pas conservée, épinglez à nouveau la mascotte actuellement affichée.

### v0.0.0.54

- Amélioration du maintien et de la réapplication des équipements épinglés lors des changements de zone et de la réapparition des PNJ.
- Ajout de réglages pour afficher le couvre-chef, ajuster la visière et afficher les oreilles des Viéras.
- Masquer le couvre-chef conserve désormais l’équipement et ses teintures. Le réafficher restaure la même apparence.
- Les planches mirage sont récupérées automatiquement dans les zones compatibles et enregistrées séparément pour chaque personnage. Une fois enregistrées, elles restent disponibles après un changement de zone ou un redémarrage. La date de la dernière récupération est affichée.
- Simplification de la sélection des accessoires de visage : « Tous » et « Favoris ».

### v0.0.0.53

- Correction d’un problème qui pouvait recharger le personnage en boucle lors d’un changement de job avec un acteur épinglé.

### v0.0.0.52

- La sélection d’équipement est plus lisible : icônes agrandies, noms, numéros de modèle et niveaux d’objet mieux présentés, et fond rouge de sélection bien aligné.
- L’équipement est trié par numéro de modèle. Choisissez « Tous », « Mode » ou « Favoris ». « Mode » regroupe les équipements de niveau requis 1 et de niveau d’objet 1.
- Les détails des acteurs sont plus clairs. Les informations de création du personnage sont plus faciles à trouver, les détails supplémentaires peuvent être repliés, et les identifiants des PNJ de combat (BNpcBase) sont affichés.
- Choisissez librement les couleurs des deux canaux de teinture des armures, accessoires et armes principales et secondaires dans la liste des acteurs. Les changements sont visibles pendant la modification. Le clic droit retire la teinture.
- Ajout des options métalliques ON et OFF. Les couleurs et la brillance s’appuient sur les teintures métalliques du jeu, avec prise en charge des couleurs libres et de la source de tenue en lot.
- Les Notes de version sont plus courtes et plus claires.
