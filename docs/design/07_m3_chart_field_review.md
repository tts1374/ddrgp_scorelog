# M3 Chart Field Review Notes

M3 chart-field PoC の期待値レビュー結果を、ローカル `metadata.csv` をGit管理せずに共有するためのメモです。ここに書く内容は、`samples/screenshots/metadata.csv` の実体更新ではなく、次にローカル期待値を直すときの判断材料です。

## 2026-07-04 difficulty review

対象は `m3_chart_field_template_diagnostics.md` に出ていた `difficulty` mismatch 5件です。実画像、`data/vision_poc/rois/<画像名>/difficulty.png`、metadata、ファイル名を突き合わせた結果、5件すべてでROI上の表示が metadata / ファイル名由来の期待値と食い違っていました。

方針は、metadata の期待値をROIに実際に表示されている値へ合わせ、ファイル名は当面リネームしないことにしました。ローカル `samples/screenshots/metadata.csv` では `note` と `difficulty` を修正済みです。このファイル自体はGit管理しないため、判断と修正内容だけをこのメモに残します。

| organized_file | metadata / filename | ROI visual | current extraction | review conclusion |
|---|---|---|---|---|
| `organized/result/result_056_sp_difficult_lv12_taj_he_spitz_retouch_score991070.png` | `DIFFICULT` | `EXPERT` | `EXPERT` | local metadata corrected to `EXPERT`; filename left as-is |
| `organized/result/result_073_sp_expert_lv13_final_brutal_sister_flandre_s_score976420.png` | `EXPERT` | `DIFFICULT` | `DIFFICULT` | local metadata corrected to `DIFFICULT`; filename left as-is |
| `organized/result/result_084_sp_difficult_lv09_nagisa_no_koakuma_lovely_radio_score992230.png` | `DIFFICULT` | `CHALLENGE` | `CHALLENGE` | local metadata corrected to `CHALLENGE`; filename left as-is |
| `organized/result/result_085_sp_expert_lv17_revolution_score882780.png` | `EXPERT` | `CHALLENGE` | `CHALLENGE` | local metadata corrected to `CHALLENGE`; filename left as-is |
| `organized/result/result_102_sp_expert_lv13_the_legend_of_max_score984710.png` | `EXPERT` | `DIFFICULT` | `DIFFICULT` | local metadata corrected to `DIFFICULT`; filename left as-is |

読み方:

- この結論はローカル素材の期待値レビューであり、`roi-template-nearest` を採用済みテンプレート照合として扱う根拠ではない。
- `samples/screenshots/metadata.csv` とスクリーンショット画像はGit管理しないため、このファイルには判断と修正内容だけを残す。
- ローカル metadata 修正後の `python -m tools.vision_poc --no-ocr` では、`roi-template-nearest` が 180/180 match、`filename-baseline` が difficulty 5件 mismatch になる。
- ファイル名に難易度が含まれているため、metadataだけを直すと `filename-baseline` は mismatch になる。これは当面、ファイル名ラベルのドリフト検出として読む。

## Next review unit

`play_style`、`difficulty`、`level` は現ローカル素材の `roi-template-nearest` で 60/60 match ですが、同分布内の leave-one-out 診断として読む範囲に留めます。参照を `chart_field_templates/` のみに限定する `roi-template-holdout` レポートで、confirmed-events result ROI を評価専用に分けて読みます。次はこの holdout 結果を見て、追加テンプレート素材の不足や採用候補へ進める failure_reason を整理します。
