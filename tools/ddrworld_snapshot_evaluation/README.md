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

## Validation

```powershell
python -X utf8 -m pytest tests/test_ddrworld_snapshot_evaluation.py -q
python -X utf8 -m pytest tests/test_ddrworld_auto_registration_policy.py -q
python -X utf8 -m ruff check tools/ddrworld_snapshot_evaluation tests/test_ddrworld_snapshot_evaluation.py tests/test_ddrworld_auto_registration_policy.py
python -X utf8 -m compileall -q tools/ddrworld_snapshot_evaluation
```
