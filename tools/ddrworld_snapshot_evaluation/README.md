# DDR WORLD snapshot jacket evaluation

保存済みのDDR WORLD公式music snapshotと、`song_select` grid由来のcurrent ROI v2
jacket featureをnetworkなしで突き合わせるdeveloper-only評価CLIです。照合結果はPoC評価であり、
曲ID確定、正式保存可否、catalog/master DB更新を意味しません。

## Input boundary

次の入力をすべてread-onlyで開きます。

- `ddrworld-music-snapshot-manifest-v1`の完成済みsnapshot
- `candidate_truth_audit_v2.ods`の`Review` sheet
- schema version 1のcurrent ROI v2 jacket catalog
- M4 master DB

snapshotはmanifest/summary/status/failure、song/image件数、URL別image metadata、local path、
SHA-256、画像decodeを検査します。truth ODSはaudit/observationの一意性、
`confirmed` / `rejected`語彙、confirmed truthの必須値とM4 exact一致を検査します。catalogは
ODSと同じobservation集合、`m5-jacket-v2`、`m5c-jacket-rgb-grid-v1`、`jacket_crop`だけを
受け入れます。

入力ODS、DB、snapshot metadataのhashを評価前後で比較し、変化した場合はreportを公開しません。
画像、ODS/XLSX、catalog/master DB、manual reviewは更新しません。

## Run

```powershell
python -X utf8 -m tools.ddrworld_snapshot_evaluation `
  --snapshot data/ddrworld_music_snapshot/20260718-official-v1 `
  --truth-ods data/jacket_catalog_collector/candidate-truth-audit-v2/candidate_truth_audit_v2.ods `
  --catalog data/jacket_catalog/catalog-current-roi-v2.sqlite `
  --master data/master/ddrgp-master.sqlite `
  --output data/ddrworld_snapshot_evaluation/20260718-official-v1-roi-v2
```

既存の完成/未完成outputは上書きしません。出力は`data/`配下のローカル生成物で、Git、PR、
artifact、Releaseへ添付しません。

## Mapping and metrics

公式title/artistは、M4 canonical/aliasのexact pair、正規化pair、unique titleの順で保守的に
対応付け、exact、表記差、artist差をmapping statusで分離します。対応できない公式曲は
`unresolved`として残します。M4側は`ddrworld_present`、`grand_prix_only_candidate`、
`not_in_ddrworld_candidate`を分離してreportします。公式側にunresolvedが残るため、M4側の
未対応曲は候補分類に留め、未収録やGP専用と確定しません。

confirmed grid jacketは、既存`m5-jacket-v2`距離で対応済み公式jacket全件を順位付けし、top-1、
top-3、top-5、top-10、1位と2位のmargin、truth距離/rankを出します。既存M5のdistance 0.24と
ambiguity delta 0.015は診断値として使い、このCLIでは調整しません。

出力:

- `snapshot_master_mapping.csv`
- `master_ddrworld_coverage.csv`
- `jacket_evaluation.csv`
- `summary.json`
- `report.md`

`matched_correct` / `matched_false`はtruthに対する評価結果です。productionの候補採用、曲ID確定、
DB保存へ自動反映しません。distance超過、低margin、truthのsnapshot対応不明は保留します。

## Current local evaluation

2026-07-18のread-only評価では、292観測からrejected capture mismatch 7件を除いたconfirmed
285件を使いました。公式1272曲のM4 mappingはcanonical exact 1102件、alias exact 4件、
canonical表記差9件、unresolved 157件でした。M4側の未対応はGP専用候補116件、その他の
DDR WORLD未収録候補51件ですが、公式側unresolvedがあるため確定分類ではありません。
保守的にM4対応できたsnapshot truthは260件で、
top-1は254/260、top-3以降は260/260でした。既存M5 thresholdでは`matched_correct=249`、`matched_false=0`、
`hold_ambiguous=11`、`hold_truth_not_in_snapshot=25`でした。これはlocal snapshot/truth/catalog/
masterの組合せに対する実績であり、threshold採用や正式保存可能を意味しません。

## Read-only auto-registration policy evaluation

同じtruth ODSの`Profile Details`にある既存2方式のtitle/artist OCRをobservation IDで
一意に対応付け、上記jacket rankingへ次の順で適用できます。

1. `rejected` captureを`rejected_capture_mismatch`として除外する。
2. 既存M5 jacket gateを通過したtop-1を`auto_jacket_gate`とする。OCR不一致は
   jacket結果を上書きしない。
3. holdでは、confidence gateを通過したtitle OCRをcanonical/alias exactまたは既存の安全な
   正規化exactだけで解決し、方式間競合・複数候補がなくsong IDがjacket top-3内なら
   `auto_jacket_top3_title_ocr`とする。
4. title/artist pairが同じ非fuzzy規則で一意に解決し、方式間競合・複数候補がなく、そのsongに
   snapshot referenceがなければ`auto_ocr_title_artist_pair`とする。
5. その他を理由別`manual_*`へ残す。top-5/10、truthの正解候補、fuzzy一致は自動判定に使わない。

```powershell
python -X utf8 -m tools.ddrworld_snapshot_evaluation.policy_cli `
  --snapshot data/ddrworld_music_snapshot/20260718-official-v1 `
  --truth-ods data/jacket_catalog_collector/candidate-truth-audit-v2/candidate_truth_audit_v2.ods `
  --catalog data/jacket_catalog/catalog-current-roi-v2.sqlite `
  --master data/master/ddrgp-master.sqlite `
  --output data/ddrworld_auto_registration_evaluation/20260719-policy-v1
```

`policy_evaluation.csv`、`false_decisions.csv`、`summary.json`、`report.md`を新規directoryへ
出し、既存outputは上書きしません。各行はpolicy/snapshot/feature version、jacket
distance/margin/rank、OCR方式別raw/normalized/candidate、matched song IDを保持します。これは将来の
confirmation履歴契約候補であり、今回schema、catalog、master、ODS、snapshot、画像、manual reviewを
変更しません。

2026-07-19の実測は292件（confirmed 285 / capture mismatch 7）を一意に評価し、baseline
`auto_jacket_gate=249`を再現しました。追加は`auto_jacket_top3_title_ocr=7`、
`auto_ocr_title_artist_pair=8`で、全264自動判定がcorrect、false 0、precision 100%、confirmed
coverage 92.6316%、manual残件21でした。経路別falseもすべて0です。この結果はproduction writerや
正式保存判定の採用そのものではありません。

## Production catalog registration and manual ODS export

評価済みpolicyをproduction catalogへ接続するdeveloper-only入口は、既定dry-runでpreflight planを
新規生成します。planは292 observationのpolicy結果、264 auto / 21 manual / 7 rejected、経路別件数、
catalog rowの事前state、Master/ODS/snapshot/policy revision、auto confirmation evidenceを固定します。
`--apply`なしではcatalogを開いて書きません。manual ODS exportは同じdry-run planから生成する
read-only操作で、catalog、Master、manual review stateを変更しません。

```powershell
python -X utf8 -m tools.ddrworld_snapshot_evaluation.catalog_pipeline_cli `
  --snapshot data/ddrworld_music_snapshot/20260718-official-v1 `
  --observations-ods data/jacket_catalog_collector/candidate-truth-audit-v2/candidate_truth_audit_v2.ods `
  --catalog data/jacket_catalog/catalog-current-roi-v2.sqlite `
  --master data/master/ddrgp-master.sqlite `
  --plan-output data/jacket_catalog_registration/run-001-plan.json `
  --manual-ods-output data/jacket_catalog_registration/run-001-manual-review.ods
```

ODSの`Metadata` sheetはschema/export ID、source catalog/master revision、policy version、件数を持ち、
`Manual Review` sheetはobservation/revision、capture validity、画像参照、jacket top-3、distance/margin、
OCR raw/normalized/candidate、hold reason、推奨song、編集用`truth_song_id`/`notes`を持ちます。
同じplanから別pathへ再exportしたODSはbyte-identicalです。既存outputは上書きせず、capture mismatchは
ODSへ含めません。ODS、plan、DB、画像、snapshot、reportは`data/`配下のローカル生成物です。

上記のproduction policy plan用ODSとは別に、current catalogの未反映manual reviewを画像付きで確認する
`#58`のexportは `tools.vision_poc.jacket_catalog_review_projection` の `--manual-xlsx-output` を使います。
こちらは`needs_review` / `unresolved`だけを対象に、`Manual Review`、`Master Songs`、`Metadata`の3 sheetを
持つ単一XLSXを生成します。XLSX内へtitle/artist ROIを埋め込み、出力先は明示指定した任意のpathで、既存fileは置き換えます。

plan確認後だけ、同じ4入力とplanを明示してapplyします。

```powershell
python -X utf8 -m tools.ddrworld_snapshot_evaluation.catalog_pipeline_cli `
  --snapshot data/ddrworld_music_snapshot/20260718-official-v1 `
  --observations-ods data/jacket_catalog_collector/candidate-truth-audit-v2/candidate_truth_audit_v2.ods `
  --catalog data/jacket_catalog/catalog-current-roi-v2.sqlite `
  --master data/master/ddrgp-master.sqlite `
  --plan-input data/jacket_catalog_registration/run-001-plan.json `
  --apply
```

applyはplanの自己hashだけを信頼せず、current policyからtargetとevidenceを再構築し、planと完全一致する
ことを検査します。ODS/Master/snapshot/policy revision、catalog全体のlogical guard revision、対象rowの
state hash、song IDとGP可用性を検査し、全targetを1つの`BEGIN IMMEDIATE` transactionで更新します。
commit直前にODS、Master、snapshot metadataと全snapshot画像を再検査し、drift時は全rowをrollback
します。catalog guardは対象rowのauto-managed fieldsだけを正規化するため、同じplanの再applyは
exact evidenceならno-opとなり、非対象row、manual history、candidate、identity等の変更はstaleとして
拒否します。

catalog schema versionは1のままです。`review_status=auto_confirmed`、`resolution_basis`に
`jacket_gate` / `jacket_top3_title_ocr` / `ocr_title_artist_pair`、`resolution_reason`にversion付き
canonical evidence JSON、`song_id`とMaster由来title/artistを保持します。既存manual確定、rejected、
別song/別根拠のauto確定は上書きせず拒否します。auto-confirmは既存の初期auto state契約どおり
revision 0 / manual historyなしで、manual actionを偽装しません。

apply後に新しいplanをdry-runすると、同じ264件は`no_op`、manual 21件とrejected 7件は不変です。
編集済みODSのimportはこのPRの後続候補であり、この入口はODSをcatalogへimportしません。

## Validation

```powershell
python -X utf8 -m pytest tests/test_ddrworld_snapshot_evaluation.py -q
python -X utf8 -m pytest tests/test_ddrworld_auto_registration_policy.py -q
python -X utf8 -m pytest tests/test_jacket_catalog_pipeline.py tests/test_jacket_reference_catalog.py -q
python -X utf8 -m ruff check tools/ddrworld_snapshot_evaluation tests/test_ddrworld_snapshot_evaluation.py tests/test_ddrworld_auto_registration_policy.py
python -X utf8 -m compileall -q tools/ddrworld_snapshot_evaluation
```
