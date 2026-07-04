# M3 Chart Field Review Notes

M3 chart-field PoC の期待値レビュー結果を、ローカル `metadata.csv` をGit管理せずに共有するためのメモです。ここに書く内容は、`samples/screenshots/metadata.csv` の実体更新ではなく、次にローカル期待値を直すときの判断材料です。

## 2026-07-04 difficulty review

対象は `m3_chart_field_template_diagnostics.md` に出ていた `difficulty` mismatch 5件です。実画像、`data/vision_poc/rois/<画像名>/difficulty.png`、metadata、ファイル名を突き合わせた結果、5件すべてでROI上の表示が metadata / ファイル名由来の期待値と食い違っていました。

| organized_file | metadata / filename | ROI visual | current extraction | review conclusion |
|---|---|---|---|---|
| `organized/result/result_056_sp_difficult_lv12_taj_he_spitz_retouch_score991070.png` | `DIFFICULT` | `EXPERT` | `EXPERT` | local metadata / filename correction candidate |
| `organized/result/result_073_sp_expert_lv13_final_brutal_sister_flandre_s_score976420.png` | `EXPERT` | `DIFFICULT` | `DIFFICULT` | local metadata / filename correction candidate |
| `organized/result/result_084_sp_difficult_lv09_nagisa_no_koakuma_lovely_radio_score992230.png` | `DIFFICULT` | `CHALLENGE` | `CHALLENGE` | local metadata / filename correction candidate |
| `organized/result/result_085_sp_expert_lv17_revolution_score882780.png` | `EXPERT` | `CHALLENGE` | `CHALLENGE` | local metadata / filename correction candidate |
| `organized/result/result_102_sp_expert_lv13_the_legend_of_max_score984710.png` | `EXPERT` | `DIFFICULT` | `DIFFICULT` | local metadata / filename correction candidate |

読み方:

- この結論はローカル素材の期待値レビューであり、`roi-template-nearest` を採用済みテンプレート照合として扱う根拠ではない。
- `samples/screenshots/metadata.csv` とスクリーンショット画像はGit管理しないため、このファイルには修正候補だけを残す。
- ローカル metadata を修正した場合は、`python -m tools.vision_poc --no-ocr` を再実行し、`difficulty` の mismatch 件数と confirmed-events 境界がどう変わったかを確認する。
- ファイル名に難易度が含まれているため、metadataだけを直すと `filename-baseline` は mismatch になる。ファイル名またはファイル名由来baselineの扱いを変えるかどうかは別作業として判断する。

## Next review unit

`play_style` と `level` は現ローカル素材の `roi-template-nearest` で 60/60 match ですが、同分布内の leave-one-out 診断として読む範囲に留めます。`difficulty` は上記5件の期待値候補をローカル側で解消してから、テンプレート素材と confirmed-events 参照の分割、または追加素材での外部検証へ進みます。
