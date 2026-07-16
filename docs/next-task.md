# 現在PR完了記録

developer-only jacket collectorが前回正常に読み込んだmaster/catalogのpathを端末ユーザー単位で記憶し、次回起動時に同じ組合せをstrict read-only projectionで再検証して自動読込するようにした。DB本体、schema、capture、catalog writerは変更していない。

## 今回の完了範囲

- `%LOCALAPPDATA%\DDRGpScorelog\JacketCatalogCollector\database-paths.v1.json` に、setting schema versionとmaster/catalogのabsolute pathだけをUTF-8 JSONでatomic保存するloader/storeを追加した。
- setting未作成の初回起動はfile/directoryを作らず、従来どおり手動選択を案内する。
- `master/catalogを選択` のstrict read-only projectionが両方成功した場合だけ保存pathを更新し、dialog完了やprojection失敗では最後に正常だったsettingを変更しない。
- collector起動時に保存pathを同じprojection経路で再検証し、両方が有効な場合だけ自動読込する。current master versionとcatalog schemaは従来の画面で確認でき、手動変更も維持した。
- setting未設定、破損、unknown field、version不一致、relative path、読取権限不足相当、DB欠損・非互換・組合せ不整合ではcollectorを終了せず、settingとDBを変更しないまま手動選択へ戻す。
- setting保存だけが失敗した場合は、正常検証済みのread-only projectionを利用可能なままactionableな診断を表示する。
- 起動時の自動読込を既存`IsBusy`と共通の取消tokenへ接続し、Loaded eventの多重実行を抑止した。
- setting store、ViewModel起動/手動読込、XAML startup bindingの回帰testとREADMEを同期した。
- testはOS temporary directoryとin-memory stubだけを使い、既存local setting、master/catalog、capture画像、artifact/checkpoint、評価dataset/reportを変更していない。

## 維持した境界

- path以外のDB内容、認証情報、将来の自動保存optionをsettingへ保存していない。
- projection schema、catalog schema、manual review revision/history、observation artifact/checkpoint、capture lifecycleを変更していない。
- 自動読込したDBへのmutation、DBのコピー・移動・作成・修復・migration・削除を行っていない。
- `INFORMATION` detector、複合identity、既存artifact backfill、opt-in自動保存へ進んでいない。
- 公開 `DDRGpScoreViewer`、正式個人スコアDB、ゲーム操作、cleanup/retentionへ接続していない。

## 未決事項

- 次PR候補は下記の合意済み順序では`INFORMATION` panel/title line detectorだが、ROI、line-hash方式、連続frame数、animation安定条件を固定する実capture fixtureが未整備であり、現時点では実装可能な次PR仕様へ昇格しない。
- 実captureのexpected付き評価母数と、M5c-4の固定gateを満たすtitle/artist取得方式は未確認。
- source locator/retention、reject/cleanupの操作契約、自動保存opt-inをsession単位または端末settingへ置く判断は引き続き未固定。
- 下記の合意済み後続仕様は順序と境界の記録に限定し、必要fixtureと各契約が固定された後に個別のIssue/PR仕様へ昇格する。

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
