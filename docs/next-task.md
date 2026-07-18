# 現在PR完了記録: DDR WORLD snapshot jacket評価

完成済みDDR WORLD公式music snapshotのjacketと、`song_select` grid由来current ROI v2 jacketを
networkなしで突き合わせるdeveloper-only評価CLIを追加した。snapshot取得、画像/ODS/XLSX/
catalog/master DB更新、manual review反映、INFORMATION検出、正式保存判定は行っていない。

## 今回の完了範囲

- 完成済み`ddrworld-music-snapshot-manifest-v1`だけを受け入れ、manifest/summary、failure、
  song/image件数、URL別metadata、local path、SHA-256、画像decodeをstrictに検査する。
- authoritativeな`candidate_truth_audit_v2.ods`のReview sheetを読み、audit/observation一意性、
  `confirmed` / `rejected`、confirmed truth必須値、M4 title/artist exact一致を検査する。
- current ROI v2 catalogをread-only/immutableで開き、ODSと同じ292 observation集合、
  `m5-jacket-v2`、`m5c-jacket-rgb-grid-v1`、`jacket_crop`だけを受け入れる。
- 公式title/artistをM4 canonical/aliasへ保守的に対応付け、公式側unresolved、M4側
  `ddrworld_present` / `grand_prix_only_candidate` / `not_in_ddrworld_candidate`を分ける。
  unresolvedな公式表記が残る間は、M4側の未対応曲を未収録やGP専用と確定しない。
- confirmed grid jacketを対応済み公式jacket全件へ順位付けし、top-1/3/5/10、truth rank/distance、
  1位と2位のmargin、precision、coverage、誤一致、保留をCSV/JSON/Markdownへ出す。
- 既存M5のdistance 0.24、ambiguity delta 0.015を診断値として維持し、今回実測に合わせた
  threshold tuningやproduction採用を行わない。
- 入力ODS、catalog/master DB、snapshot metadataのhashを評価前後で比較し、変化時はoutputを
  publishしない。既存outputは上書きしない。
- synthetic ODS/SQLite/image/snapshotだけを使うtestで正常、alias、ambiguity、corrupt hash、
  incomplete snapshot、truth/catalog不整合、既存output、CLI失敗を固定する。

## 実データread-only評価

`20260718-official-v1`は1272曲で、M4へのmappingはcanonical exact 1102件、alias exact 4件、
canonical表記差9件、unresolved 157件だった。保守的に対応できた公式曲は1115件である。
M4側の未対応はGP専用候補116件、その他のDDR WORLD未収録候補51件だが、公式側unresolvedが
あるため確定分類ではない。
`candidate_truth_audit_v2.ods`の292観測は`confirmed=285` / `rejected=7`で、7件の明確なcapture
mismatchはjacket評価から除外した。

confirmed 285件のうちtruthが対応済みsnapshotにあるものは260件だった。

- top-1: 254/260（97.6923%）
- top-3 / top-5 / top-10: 260/260（100%）
- 既存M5 threshold: `matched_correct=249`、`matched_false=0`、`hold_ambiguous=11`、
  `hold_truth_not_in_snapshot=25`
- decision precision: 100%
- confirmed全体に対するdecision coverage: 87.3684%
- snapshot対応済みtruthに対するdecision coverage: 95.7692%

この実績はlocal snapshot/truth/catalog/masterの組合せに対するPoC評価であり、曲ID確定、
正式保存可能、catalog/master更新、threshold採用を意味しない。生成したCSV/JSON/Markdown、
公式jacket、grid/capture画像、ODS/XLSX、DBはGit、PR、artifact、Releaseへ含めない。

# 次PR

## 完了状態

保存済み公式snapshotとconfirmed grid jacketのnetwork-free照合、top-k/precision/coverage/
誤一致/保留評価は完了した。top-1誤り6件はすべて既存margin gateで保留され、採択誤りは0件だった。
公式snapshot 157件は保守的なM4 title/artist対応でunresolvedのまま保持している。

## 未決事項

- 公式snapshot側unresolved 157件のうち、実際のM4未収録と表記差を追加の根拠でどう分けるか。
- `hold_ambiguous` 11件を、同一jacket、類似jacket、crop差、その他へ分類して後続信号を
  評価するか。今回PRでは個別例外やthreshold tuningを行わない。
- 正常285件とcapture mismatch 7件を使うINFORMATION検出評価の入力contract、report語彙、
  座標・thresholdの採用条件。snapshot/grid jacket評価とは独立して扱う。

次PRの実装仕様は上記未決事項から一意に決まらない。今回PR完了後は次機能へ進まない。
