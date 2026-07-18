# 現在PR完了記録: jacket自動登録policy read-only評価

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

# 次PR候補

今回false 0だった3経路の根拠を用いるdeveloper-only catalog自動登録を、独立PRとして検討する。
書込み前にconfirmation source、policy/snapshot/feature/OCR version、matched song ID、distance/margin/
rankを保持する契約と、既存manual review transactionとの境界を固定する。今回manualに残った21件の
ODS exportは、その後の独立PR候補とする。ODSを正本とし、XLSXは必須にしない。

次PRにはODS import、manual apply、schema migration、threshold tuning、OCR profile/engine変更、
snapshot再取得、unresolved 157件の個別解決をまとめない。ExportとImport/catalog applyは責務を
分離する。
