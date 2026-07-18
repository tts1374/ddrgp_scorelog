# 現在PR完了記録

M5c song select観測のjacketとartist ROIからpanel境界を除去し、ROI由来のjacket参照を旧契約と混在させないversion境界を追加した。OCR language、前処理profile、confidence gate、candidate昇格条件、catalog schemaは変更していない。

## 今回の完了範囲

- jacket ROIを1280x720基準 `(x=809, y=27, width=149, height=149)`、`m5c-song-select-jacket-roi-v2`へ更新した。
- title/artist ROI contractを`m5c-song-select-title-artist-roi-v2`へ更新した。titleは `(306, 58, 470, 34)` のまま、artistは上5px・左3pxを除去した `(309, 97, 467, 23)` とした。
- current jacket feature extractorを`m5-jacket-v2`へ更新し、旧ROIから生成した`m5-jacket-v1` referenceがcurrent matchingへ混入しないようにした。
- Python/C#双方のROI、manifest、ingest、fixture、docsを同じ契約へ同期した。
- 旧v1 manifestをcurrent評価が副作用なしで拒否する回帰testと、1280x720および2倍scaleの座標testを追加した。

## 実画像とtruth監査の実績

Git管理外のcurrent source 57件を旧/new ROIで比較した。new jacket ROIは右端・下端のpanel境界を除去し、jacket本体の切れはなかった。new artist ROIは上部水平線と左側panel境界を除去し、文字の切れはなかった。既存source、crop、manifest、checkpoint、master/catalog DBは変更していない。

`candidate_truth_audit.ods`の57件はすべて`confirmed`かつtruth title/artist/song ID入力済みで、M4 masterとのexact一致を確認した。候補precisionはいずれも100%で、候補正解数は次のとおりだった。

- 現行baseline: 43件（canonical exact 39件 + title match / artist mismatch 4件）。
- title `jpn+eng`, `psm=6`: 44件（41件 + 3件）。
- title `jpn+eng`, `psm=7`: 44件（41件 + 3件）。`psm=6/7`の候補集合は同一だった。
- artist `jpn+eng`, 10倍sharpen: 38件（37件 + 1件）。
- artist `jpn+eng`, 10倍no-sharpen: 40件（39件 + 1件）。

title `jpn+eng`の44件はbaseline候補集合に対して9件増・8件減であり、単純な上位互換ではない。artistも候補件数だけでは採用方式を一意に決められない。このPRではROIの目視上明確なpanel境界だけを修正し、OCR profile選択へ進んでいない。

## 維持した境界

- 旧v1のlocal artifact、reference、crop、checkpointを削除、移動、上書き、migrationしていない。
- catalog schema version 1、projection version 4、M4 master、observation ID、manual review revision/historyを変更していない。
- OCR language、title PSM、artist scale/sharpen、normalization、confidence 0.90 gate、candidate条件、auto-confirm条件を変更していない。
- source/crop、master/catalog DB、persisted song/candidate/review statusを変更していない。
- local ODS、画像、DB、report、contact sheetをGit管理していない。

# 次PR

## 完了状態

57件のcandidate truth監査と、同じ57件でのjacket/artist ROI境界確認は完了した。現行ROIはv2として固定され、旧v1由来referenceとの混在をcurrent extractor versionで防いでいる。

## 未決事項

- title `eng`と`jpn+eng`のunionまたはfallbackを独立仕様にするか。監査57件ではunion候補は52件だが、採用条件、優先順位、競合時の扱いは未決である。
- title `psm=6/7`は監査対象の候補集合が同一であり、どちらを採用するかは今回結果から一意に決まらない。
- v2 ROIでlocal catalogを再収集・再構築する運用時期と対象範囲。既存v1 dataのin-place migrationは行わない。
- v2 ROIで516件のfull diagnosticを再実行し、language、scale、sharpenの採用方式を再評価するか。

次PRの仕様は既存資料と今回実測だけから一意に決まらない。上記の採用方式を推測して実装せず、今回PR完了後は次機能へ進まない。
