# 現在PR完了記録

developer-only jacket collectorの収集画面を、通常の作業順序が分かるcollection-first UIへ再構成した。detector、capture lifecycle、artifact/checkpoint publish、catalog ingest、DB schemaは変更していない。

## 今回の完了範囲

- 初期表示を `ジャケット収集` とし、preview、window選択、収集開始、曲移動、明示保存を1画面へ集約した。
- 主操作を `このジャケットを保存` に固定し、再開・catalog retryは `詳細・復旧操作`、master/catalog操作、migration、title/artist評価は `管理・設定` へ分離した。
- `DuplicatePreview` などのdetector内部状態を通常画面へ露出せず、未保存stable候補は `新しいジャケットを検出`、保存後は `このジャケットは保存済み` と次操作を表示するpresentation stateを追加した。
- detectorが同じpreviewをduplicateとして扱っても既存のadoptable candidateを維持し、A→B→Aの再訪時もcheckpointの保存済み観測集合から `保存済み` と表示する境界を回帰testで固定した。
- 別jacketへの切替中は直前のstable候補を保存ボタンへ流用せず、表示中jacketがstableまたはduplicateとして確定した場合だけ明示保存を許可する。
- 明示開始前に候補のvisible/minimized状態を確認できる表示を維持した。
- 収集タブが先頭であること、主操作と開始・終了操作が存在すること、内部状態をprimary layoutへbindingしないことをXAML回帰testで固定した。
- READMEへ初回DB選択から収集終了までの基本操作を同期した。
- collector全test、build、実際のWPF起動とcollection-first layoutを確認した。
- ローカルmaster/catalog、source/crop/checkpoint、実capture画像、評価dataset/report、その他生成物は変更していない。

## 維持した境界

- window候補の列挙、明示選択・開始確認、暗黙再選択禁止、capture resource lifecycleを変更していない。
- stable/duplicate判定、明示採用、artifact atomic publish、checkpoint/resume/retry、catalog v1/v2 ingestを変更していない。
- catalog schema、manual review revision/history、runtime current reference、正式個人スコアDBを変更していない。
- ゲーム操作、focus操作、grid自動巡回、公開 `DDRGpScoreViewer`、cleanup/retentionへ進んでいない。

## 未決事項

- 実captureのexpected付き評価母数を収集し、M5c-4の固定gateを満たすtitle/artist取得方式があるか確認する作業は未実施。
- animation、言語、解像度差を含む追加方式/局所前処理が必要かは実測後に判断する。
- gateを満たす方式が得られた場合のcatalog auto-confirm mutation契約、observation receipt version、manual review競合時の接続方法は未固定。
- source locator/retention、reject/cleanupの操作契約は引き続き別責務。

## 次PR仕様: master/catalog選択の記憶

### 正規化概要

collectorが前回正常に読み込んだmaster/catalogのpathを端末ユーザー単位で記憶し、次回起動時に同じ組合せをread-onlyで再検証して自動読込する。DB本体、schema、capture、catalog writerは変更しない。

### In scope

- 正常に読込済みのmaster/catalog absolute pathだけを、repositoryと`data/`の外にあるユーザー単位のlocal app settingへ保存する。
- collector起動時に保存pathの存在と現行projection互換性を再検証し、両方が有効な場合だけ自動読込する。
- `master/catalogを選択`で別の有効な組合せを正常読込した場合だけ、保存pathを置き換える。
- 未設定、欠損、権限不足、破損、version不一致、組合せ不整合を安全に処理し、手動選択へ戻せるactionableな状態を表示する。
- setting loader/storeの対象test、起動時bindingのtest、README同期を含める。

### Out of scope

- DB本体のコピー、移動、作成、修復、migration、削除。
- catalog schema、projection schema、manual review、observation artifact/checkpoint、capture lifecycleの変更。
- `INFORMATION`検出、複合identity、自動保存、既存artifactのbackfill。
- 自動選択したDBに対する暗黙mutation。

### Fixed decisions

- 記憶対象はpathだけとし、DB内容や認証情報をsettingへ複製しない。
- path保存は両DBの正常なread-only projection完了後に行い、選択dialogの完了だけでは更新しない。
- 起動時再検証に失敗してもcollectorを終了せず、DBと既存settingを変更せずに手動選択を案内する。
- 自動読込後も現在のmaster/catalog versionとschemaを画面で確認でき、手動で別pathへ変更できる。
- setting、local DB、診断出力をGit管理しない。

### Pending decisions

- settingの具体的な保存API/形式とapp version更新時の保持方法は、上記境界を満たす範囲で実装時に選定する。
- 自動保存の有効/無効設定を将来同じsettingへ保存するかは後続PRで決める。

### Acceptance criteria

- setting未作成の初回起動は現行どおり手動選択を案内し、settingやDBを作成しない。
- 正常読込後にcollectorを再起動すると、同じmaster/catalogが自動的にread-only読込される。
- 一方または両方のpathが欠損・非互換・読取不能でも起動でき、DB byteを変更せず手動選択へ復帰できる。
- 読込失敗した別pathを選択しても、最後に正常だった保存pathを上書きしない。
- 新しい有効な組合せを正常読込した場合は、次回起動でその組合せが使われる。
- test実行前後でfixture DBと既存local DBがbyte不変であり、settingと生成物がGit差分へ混入しない。

### Open risks / blockers

- 起動時の自動読込と手動読込が同時実行されないよう、既存`IsBusy`/取消境界との整合が必要。
- removable/network pathの一時欠損時に保存pathを消去すると復帰不能になるため、再検証失敗だけではsettingを削除しない。

## 合意済み後続仕様（次PRでは着手しない）

### 正規化概要

DDR GPのsong select画面で、jacket画像だけでなく上部`INFORMATION`の曲名行をversion付き画像特徴として観測し、同一jacketを共有する別曲を区別する。catalog/checkpointに既存の複合identityがある場合は自動保存せず、未登録かつ安全条件を満たす観測だけをopt-inで自動保存する。

### In scope

後続作業は、次の独立してmerge可能な順序へ分割する。

1. `INFORMATION` panel/title line detectorを追加し、表示有無、安定状態、title-line hashをread-only UI/diagnosticへ出す。保存境界は変更しない。
2. version付き複合identityをobservation manifest/checkpointへ追加する。identityはjacket image hashと`INFORMATION` title-line hashから決定的に生成する。
3. catalogへ複合identityを保持・検索できるschemaと、既存local `source.png`からの非破壊migration/backfillを追加する。
4. current checkpointとcatalogの複合identity集合を保存前に照合する、opt-in自動保存を追加する。

### Out of scope

- OCR文字列だけによる曲ID自動確定、master song/chartへの自動割当。
- DDR GPのキー操作、focus操作、grid自動巡回。
- INFORMATION title-line hashを正式な曲名文字列として扱うこと。
- 欠損画像、未知version、曖昧な既存行を推測でbackfillすること。
- 個人スコアDB、M7/M8保存判定、公開`DDRGpScoreViewer`への接続。

### Fixed decisions

- 複合identityはversion、jacket image hash、`INFORMATION` title-line hashから決定的に生成する。
- 同じ曲の難易度変更はtitle-line hashが同じため同一identityとして扱う。
- `New York EVOLVED (Type A/B/C)`や`London EVOLVED ver.A/B/C`のようにjacketが同一でもtitle lineが異なる曲は別identityとして扱う。
- 自動保存条件は、active session、`INFORMATION` panel/title lineの連続安定、jacketの連続安定、未保存identity、current master/catalog/extractor再検証成功のANDとする。
- 同じidentityの表示が続く間は一度だけ判定し、checkpointまたはcatalogに存在する場合は保存せず、登録済みとして次の曲への移動を案内する。
- catalog側の重複抑止対象は`unresolved`、review待ち、確定、再割当、`reopen`を含む既存観測とする。
- `reject`済みidentityも暗黙に自動再収集せず、将来の明示再収集操作が固定されるまで自動保存対象外とする。
- 自動保存は明示的に有効化するopt-inとし、detector失敗、identity不明、既存データ不明では保存しない。
- 保存前readでの重複確認だけに依存せず、catalog writerのtransaction境界でも同一identityの競合を冪等に処理する。

### Pending decisions

- 1280x720基準の`INFORMATION` panel/title line ROI、二値化/line-hash方式、必要連続frame数、animation安定条件は実capture fixtureで固定する。
- 複合identityを既存catalog v2へ追加するschema version、migration CLI、互換projectionの詳細はschema PRで固定する。
- source artifactが欠損してbackfill不能な既存rowのUI表示と明示再収集操作は未固定。
- 自動保存opt-inをsession単位にするか端末settingへ記憶するかは未固定。

### Acceptance criteria

- jacketが同じでtitle lineが異なるType/ver A/B/Cを別identityとして観測できる。
- 同じ曲で難易度だけを変更しても新しいidentityやartifactを作らない。
- current sessionで保存済み、再開checkpointで保存済み、catalogで`unresolved`以降へ進んだidentityはいずれも自動保存しない。
- 登録済みidentityでは、保存ボタン/自動保存を無効化し、次の曲へ移動する案内を表示する。
- INFORMATION非表示、title line不安定、jacket不安定、unknown identity、master/catalog drift、保存処理中、停止後frameでは自動保存しない。
- 同じ未登録identityを連続表示してもartifact/catalog rowは1件だけで、retry/restart/競合でも重複しない。
- migrationはsource artifactが存在し検証成功したrowだけをbackfillし、欠損・改変・未知versionでは元catalogを変更しない。

### Open risks / blockers

- 現行catalogにはtitle-line hashがないため、cross-session/cross-review重複抑止にはversion付き永続fieldと既存source artifactの検証済みbackfillが必要。
- jacket title lineのanimation、language、resolution scaling、選択移動中の残像によりfalse stableが起きる可能性があり、自動保存へ接続する前に実capture fixtureでfalse-positive 0件を確認する必要がある。
- local artifactのlocator/retentionが未固定のため、migrationはartifact欠損を正常な非適用状態として扱う必要がある。

### Issue body patch / append text

上記の`次PR仕様`を次の実装PRの唯一のscopeとし、`合意済み後続仕様`は順序と境界の記録に限定する。各後続PRは直前PRがmergeされ、必要fixtureとschema契約が固定された後に個別のIssue/PR仕様へ昇格する。

### Issue comment draft

前回DB path記憶を次PRへ固定しました。INFORMATION title-lineを使う複合identity、既存catalog重複抑止、自動保存は合意済み後続仕様として記録し、detector、manifest/checkpoint、catalog schema/migration、自動保存を別PRへ分割しています。
