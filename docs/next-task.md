# 現在PR: production catalog自動登録 + manual ODS export

PR #53のread-only policy実装を再利用し、developer-onlyの全曲収集後に使うcatalog登録pipelineを
完成させた。schema migrationは行わず、current jacket catalog schema version 1の既存
`auto_confirmed` / `resolution_basis` / `resolution_reason` / Master snapshot列を使う。

## 実装範囲

- `catalog_pipeline_cli`はdry-runを既定とし、新規preflight planへpolicy target、evidence、
  source revision、対象row stateを固定する。`--apply`とplan inputの明示なしではDBを書かない。
- apply時はPR #53 policyを再評価し、planのtarget/song/source/evidenceと一致することを再確認する。
  plan IDを改変検出用hashとして使うが、plan自己申告だけを登録根拠にしない。
- ODS、Master、snapshot metadata/全画像、policy rows、catalog logical guard、対象row stateを検査する。
  dry-run後の変更、stale row、重複observation、不正song、既存manual/rejected/別根拠確定は拒否する。
- すべてのauto targetを既存catalog writer moduleの1つの`BEGIN IMMEDIATE` transactionで更新し、
  途中例外またはcommit直前external driftで全件rollbackする。
- exact evidenceの再applyはno-opとする。auto-managed fieldsだけをlogical guardから正規化するため、
  同じplanの適用済みstateを許しつつ、非対象row、candidate、history、identity driftを拒否する。
- matched song IDを正本とし、canonical title/artistとmaster versionはcurrent Masterから取得する。
  manual action/historyをauto-confirmへ偽装せず、既存初期auto state契約どおりrevision 0を維持する。
- confirmation sourceは`jacket_gate`、`jacket_top3_title_ocr`、`ocr_title_artist_pair`に限定する。
  version付きcanonical evidence JSONへpolicy/snapshot/feature、distance/margin/rank、OCR
  profile/raw/normalized/candidate、matched song IDを保存する。
- manual残件はread-onlyでLibreOffice互換ODSへexportする。MetadataとManual Review sheetに
  schema/export/source revision、observation/revision、画像参照、jacket top-3、OCR、hold reason、
  推奨song、編集用truth song ID/notesを持つ。同じplanからの再exportはbyte-identicalで、
  既存fileを上書きしない。

## 実データ検証

local truth ODS、current ROI v2 catalog、M4 Master、2026-07-18公式snapshotのcopyを使い、292件を
一意に処理した。

| route | 件数 |
|---|---:|
| `jacket_gate` | 249 |
| `jacket_top3_title_ocr` | 7 |
| `ocr_title_artist_pair` | 8 |
| auto合計 | 264 |
| manual ODS | 21 |
| capture mismatch reject | 7 |
| false decision | 0 |

- dry-runでは入力catalog/ODS/Master/snapshot/画像を変更しない。
- validation copyへのapplyは264件だけを`auto_confirmed`へ更新し、manual 21件とrejected 7件を
  変更しない。
- 同じplanの2回目applyは264件すべてno-opである。
- apply後の新しいdry-runも`apply=0 / no_op=264 / manual=21 / rejected=7`を再現する。
- 100件目の注入例外でDB bytes不変、292件`unresolved`、`integrity_check=ok`を確認した。
- non-target catalog driftとdry-run後のODS byte driftをapply前/transaction内guardで拒否し、
  auto rowを残さないことを確認した。
- ODSは21 data rows、capture mismatch 0、空のtruth input 21で、同一planの再exportが
  byte-identicalである。
- local DB、画像、snapshot、ODS、plan、reportは`data/`配下でGit除外される。

## 次候補: ODS dry-run import + atomic apply

編集済みODSのschema/export ID、catalog/master/observation revision、truth song ID、cell type、重複行を
strict検査し、1件でも不正なら全体拒否する別PRを候補とする。既定dry-run、明示apply、単一transaction、
`manual_ods_import` source、再import no-opを必須とし、今回のproduction writer/ODS export PRへ混ぜない。

# 前PR完了記録: jacket自動登録policy read-only評価

PR #52のDDR WORLD snapshot mapping / current ROI v2 jacket rankingを再利用し、authoritativeな
`candidate_truth_audit_v2.ods`の同一observationに保存済みのtitle/artist OCR 2方式を対応付けて、
自動登録候補policyをproduction書込みなしで評価した。

## 完了範囲

- capture mismatchを最初にrejectし、jacket/OCR判定から除外する。
- 既存M5 distance `<= 0.24`かつmargin `>= 0.015`のjacket gateを最優先し、OCR不一致で
  jacket結果を上書きしない。
- jacket holdでは、canonical/alias exactまたは既存の安全な正規化exactだけで一意解決した
  titleがjacket top-3内にあり、OCR方式間競合・複数候補がない場合だけ追加自動判定する。
- snapshot referenceがないsongは、同じ非fuzzy規則でtitle/artist pairが一意解決し、方式間
  競合・複数候補がない場合だけ追加自動判定する。
- 欠損、複数候補、方式間競合、top-3外、未知versionを理由別manual routeへ残す。
- observation別CSV、false decision CSV、JSON summary、Markdown reportへ、将来の根拠候補となる
  policy/snapshot/feature version、distance、margin、rank、OCR raw/normalized/candidate、matched
  song IDを出す。schema変更やwriter追加は行わない。
- ODS、catalog/master DB、snapshot metadata/画像をread-only検査し、前後hash不一致時はoutputを
  publishしない。既存outputも上書きしない。

## 実データ評価

292 observationを一意に評価した。confirmedは285件、capture mismatch rejectは7件である。
PR #52 baselineの`matched_correct=249`、`matched_false=0`、`hold_ambiguous=11`、
`hold_truth_not_in_snapshot=25`を再現した。

| policy route | auto | correct | false | confirmed coverage |
|---|---:|---:|---:|---:|
| `auto_jacket_gate` | 249 | 249 | 0 | 87.3684% |
| `auto_jacket_top3_title_ocr` | 7 | 7 | 0 | 2.4561% |
| `auto_ocr_title_artist_pair` | 8 | 8 | 0 | 2.8070% |
| 合計 | 264 | 264 | 0 | 92.6316% |

- decision precision: 100%
- manual review残件: 21
- OCR方式間song ID競合: 0
- manual内訳: jacket OCR unresolved 12、jacket top-3 miss 1、OCR pair incomplete 8
- false decisionは全自動経路で0件。`false_decisions.csv`はheaderのみである。

これはlocal truth/snapshot/catalog/master/ODSの組合せに対する採用評価であり、catalog自動登録、
正式保存可否、DB schema採用を意味しない。生成report、ODS、DB、snapshot、画像はGitやPRへ含めない。

# 完了したGoal: 全曲収集を開始できるcatalog登録パイプラインの完成

PR #53でfalse decision 0件を確認した3つの自動判定経路をproduction処理へ接続し、
収集済みobservationから高信頼結果を安全にcatalogへ登録し、自動登録できない残件を
ODSへexportできる状態を完成させる。

このGoalの最低到達点は、全曲収集を開始した後に以下を実行できることである。

1. 自動登録予定をdry-runで確認する。
2. 高信頼結果を明示的なapply操作でcatalogへatomicに登録する。
3. 自動登録できない残件をODSへexportする。
4. 同じ入力で再実行しても二重登録や不要な更新が発生しない。

精度改善ではなく、developer-only収集運用を開始できる安全な書込みパイプラインの完成を
最優先とする。

## 必須マイルストーン: production自動登録 + ODS export

PR #53で評価済みのpolicy実装を再利用し、同じ判定結果をproduction writerへ接続する。

自動登録対象は以下の3経路に限定する。

* `auto_jacket_gate`
* `auto_jacket_top3_title_ocr`
* `auto_ocr_title_artist_pair`

以下は自動登録せず、manual review対象としてODSへexportする。

* jacket OCR unresolved
* OCR方式間競合
* jacket top-3外
* snapshotなしでtitle/artist pairが不完全または一意解決不能
* fuzzy一致のみ
* 未知version
* 入力不整合
* その他、PR #53の自動登録条件を満たさないもの

capture mismatchは自動登録およびmanual ODS対象から除外し、`rejected`として扱う。

### 書込み契約

production writerは以下を必須とする。

* dry-runを既定動作とする。
* 明示的なapplyオプションなしではDBを書き換えない。
* apply対象全体を単一transactionで処理する。
* 1件でも検証または書込みに失敗した場合は全件rollbackする。
* observation、catalog、master、policy入力のrevisionまたは同等の整合性を検証する。
* dry-run後に入力が変更された場合はapplyを拒否する。
* 同じ入力を再実行しても二重登録しない。
* すでに同じ根拠で登録済みの場合はno-opとする。
* 既存のmanual review transactionおよびcatalog writer境界を再利用する。
* titleおよびartistは自由入力値ではなく、matched song IDを正本としてMasterから取得する。
* 既存catalogの確定済み情報を暗黙に上書きしない。
* 部分成功状態を残さない。

### 登録根拠

自動登録結果について、既存schemaで安全に保持できる範囲で最低限以下を記録する。

* confirmation source

  * `jacket_gate`
  * `jacket_top3_title_ocr`
  * `ocr_title_artist_pair`
* matched song ID
* policy version
* snapshot IDまたはsnapshot version
* jacket feature version
* jacket distance
* jacket margin
* jacket rank
* OCR profile/version
* OCR rawおよびnormalized値

既存schemaで保持できない情報がある場合は、場当たり的な大規模migrationを行わない。
必要最小限の保存契約を設計し、migrationが不可避なら理由と互換性を明記する。

### ODS export

自動登録できないmanual review残件を、LibreOfficeで編集可能なODSへexportする。

ODSには最低限以下を含める。

* schema/version
* export日時またはexport ID
* source catalog/master revision
* observation ID
* current review status/revision
* capture validity
* 画像またはcontact sheetへの参照
* jacket top-3候補
* rank、distance、margin
* title OCR raw/normalized/candidate
* artist OCR raw/normalized/candidate
* hold reason
* 推奨song ID
* 入力用truth song ID
* notes

XLSXを必須にしない。ODSを正本フォーマットとする。

Exportはread-onlyであり、catalog、master、manual review stateを変更しない。
同じsource revisionからの再exportは安定した内容になることを確認する。

## 検証

最低限、以下を確認する。

* PR #53の実データ判定結果を再現する。
* 292 observationを一意に処理する。
* confirmed 285件のうち264件が自動登録対象となる。
* 自動登録経路のfalse decisionが0件である。
* capture mismatch 7件が登録対象外となる。
* manual review残件21件がODSへ出力される。
* dry-runでは入力DBおよびODSが不変である。
* apply後に期待する264件だけが登録される。
* 同一入力の2回目applyがno-opになる。
* apply途中の例外で全件rollbackされる。
* stale revision、入力hash不一致、不正song ID、重複observationを拒否する。
* 既存確定データとの競合を安全側で拒否する。
* local DB、画像、snapshot、生成ODS、reportをGitへ含めない。

synthetic testには最低限、以下を含める。

* 各自動登録経路の正常系
* manual route
* capture mismatch
* OCR競合
* OCR欠損
* top-3外
* snapshotなし
* stale revision
* 重複apply
* transaction rollback
* 既存確定情報との競合

Ruff、compileall、対象pytest、`git diff --check`を実行する。
実データではdry-run、apply前後のhash確認、再実行no-opを確認する。

## 必須マイルストーン完了後の自己レビュー

PR作成後も作業を終了せず、作成したPRの全diffを取得して、別担当者の観点で
ゼロベースの自己レビューを行う。

重点確認項目:

* false positiveによる誤登録
* dry-run既定の破壊
* applyフラグなしの書込み
* transaction漏れ
* rollback漏れ
* 冪等性不備
* stale revisionの見逃し
* TOCTOU
* 重複observation
* 不正song ID
* 既存確定データの上書き
* local DB、画像、ODS、snapshotの誤commit
* report結果と実際のapply対象の不一致

問題を発見した場合はユーザー確認を求めず、安全側で修正し、再テストしてPRへpushする。
PR作成だけを完了条件としない。

## Stretch Goal: ODS import

必須マイルストーンが完成し、自己レビューと検証が完了した後に余力がある場合、
編集済みODSをmanual review transactionへ反映するimport機能へ続ける。

Importはproduction自動登録 + ODS exportとは別PRに分割する。
前PRが未mergeの場合はstacked PRとして扱ってよいが、依存関係をPR本文へ明記する。

ODS importは以下を必須とする。

* dry-runを既定とする。
* 明示applyなしでは書き込まない。
* ODS schema/versionを検証する。
* export ID、catalog/master revision、observation revisionを検証する。
* truth song IDを正本とし、title/artistをMasterから取得する。
* 不正song ID、重複行、欠損必須値、古いrevisionを拒否する。
* 1件でも不正なら全体を拒否し、部分適用しない。
* apply全体を単一transactionで処理する。
* 同一ODSの再importをno-opにする。
* 自動登録後に状態が変化したobservationを安全側で拒否する。
* `manual_ods_import`をconfirmation sourceとして保持する。

Import実装後も、PR作成後に全diffを自己レビューし、問題を修正して再検証する。

## 優先順位

トークンまたは実行時間が不足する可能性がある場合は、以下の順で完成度を優先する。

1. production自動登録writerの安全性
2. dry-run、atomicity、rollback、冪等性、revision guard
3. ODS export
4. 必須マイルストーンの実データ検証と自己レビュー
5. ODS import
6. Importの追加E2Eおよび運用補強

ODS importへ進むために、自動登録writerの安全性や検証を削らない。

途中で継続確認、PR作成確認、設計選択の確認を求めず、既存設計とAGENTS.mdに従って
安全側の合理的判断で進める。回復不能な外部依存または権限不足がない限り、
同一のGoal実行内で必須マイルストーンを完成させ、余力があればStretch Goalへ続ける。

## スコープ外

* PP-OCR導入
* OCR profile、engine、PSM、language、preprocessing変更
* jacket threshold tuning
* hold ambiguity 11件の原因分類
* shared jacket専用分析
* snapshot unresolved 157件の個別解決
* INFORMATION detector
* 公式snapshot再取得または定期更新
* GUI
* 一般ユーザー向け機能
* jacket feature変更
* 精度向上を目的とした追加実験
* このGoalに不要な大規模schema再設計

## Goal完了条件

最低限、以下を満たした時点でGoalの必須部分を完了とする。

* 高信頼3経路をproductionへ安全に自動登録できる。
* dry-runと明示applyが分離されている。
* applyがatomicかつ冪等である。
* stale inputを拒否できる。
* 自動登録できない残件をODSへexportできる。
* 実データで自動登録264件、manual 21件、capture mismatch 7件の契約を再現する。
* false decision 0件を維持する。
* PR作成後の自己レビューと必要な修正が完了している。
* 全曲収集を開始し、後から安全に一括処理できる運用手順が文書化されている。

ODS importまで完成した場合は、収集、dry-run、自動登録、残件export、手動編集、
dry-run import、atomic applyまでのdeveloper-only運用が閉じていることを追加完了条件とする。
