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

## 2026-07-04 holdout adoption candidate review

`roi-template-holdout` は参照を `chart_field_templates/` だけに限定し、confirmed-events result ROI を評価専用に分ける診断です。現ローカル素材では 180 attempts / 110 match / 70 mismatch で、内訳は `play_style` 60/60 match、`difficulty` 43/60 match、`level` 7/60 match でした。

M3-3では `m3_chart_field_adoption_candidates_summary.json` と `m3_chart_field_adoption_candidates.md` を追加し、採用候補と参照不足を分けて読みます。`play_style` は holdout 全件matchのため `adoption_candidate` として次段階の採用候補にできます。ただし、この判断はPoC上の採用候補であり、本番採用済みテンプレート照合、OCR、マスタ照合の成功ではありません。

`difficulty` は `DIFFICULT` テンプレートがないため、17件が `DIFFICULT -> BASIC` の `missing_expected_template_reference` です。保存前判断へ渡す語彙では `missing_reference` として扱い、追加テンプレート素材なしで採用候補へ進めません。

`level` は 6/9/10/11/12/13/16/17 など、confirmed-events 評価対象に出る多数レベルがテンプレート未収録です。53件の mismatch はすべて `missing_expected_template_reference` として読み、保存前判断へは `missing_reference` で渡す候補にします。

追加テンプレート素材が必要な場合も画像はGit管理しません。このファイルには、必要ラベル、採用候補にできるfield、採用不可理由だけを残します。

## 2026-07-04 blocker resolution order review

M3-7では `m3_save_candidate_blocker_resolution_plan.json` と Markdown を追加し、M3-5集約の未ready fieldを次の解消順として整理します。このレポートはM3-6代表整理と同じ confirmed-events 境界だけを対象にし、duplicate、`rejected_transition`、未確定候補、non-result は含めません。

`python -m tools.vision_poc --no-ocr --no-rois` 由来の解消順では、先にローカルテンプレート参照ラベルを増やす対象として `difficulty=DIFFICULT` 17件、`level=12` 11件、`level=13` 11件、`level=6` 7件、`level=9` 7件、`level=16` 6件、`level=11` 4件、`level=17` 4件、`level=10` 3件が出ました。`difficulty` の43件と `level` の7件は `field_needs_template_references` として、不足ラベル追加後にfield全体が採用候補へ戻るか再確認する対象です。

同じ実行では `song_title` / `artist` は `ocr_not_run` のため、解消順では `--m3-song-artist-ocr` を実行して実失敗理由へ分解する次手として扱います。これはOCR未実行の整理であり、曲名正規化、ファジーマッチ、マスタ照合の失敗ではありません。

`python -m tools.vision_poc --m3-song-artist-ocr --ocr-target confirmed-events --no-rois --output data\vision_poc_m3_song_artist` 由来の解消順では、テンプレート参照不足の後に `song_title empty_ocr=2`、`artist empty_ocr=22` が出ます。`song_title` は主要項目のOCR入口失敗代表として、`artist` は左右切れがある補助項目のOCR入口失敗代表として読むに留めます。

このM3-7解消順は、追加テンプレート素材やOCR入口の代表ROIを見る順番のレビュー補助です。テンプレート画像、OCR画像、PoC出力はGit管理せず、必要ラベル、代表ROI、判断だけをdocsに残します。DB保存可否判定、マスタ照合、ファジーマッチ、曲名正規化には進みません。

## 2026-07-04 local template reference addition

新規スクリーンショット `スクリーンショット (142)` から `(149)` を左上 1280x720 で crop し、ローカル素材として `samples/screenshots/cropped/` と `samples/screenshots/organized/chart_field_templates/` に配置しました。元画像、crop画像、テンプレート画像はGit管理しません。

追加したテンプレートは以下です。

| source | template file | label |
|---|---|---|
| `スクリーンショット (142).png` | `chart_field_template_142_single_difficult_lv12.png` | `SINGLE DIFFICULT Lv12` |
| `スクリーンショット (143).png` | `chart_field_template_143_single_beginner_lv06.png` | `SINGLE BEGINNER Lv6` |
| `スクリーンショット (144).png` | `chart_field_template_144_single_difficult_lv09.png` | `SINGLE DIFFICULT Lv9` |
| `スクリーンショット (145).png` | `chart_field_template_145_single_difficult_lv11.png` | `SINGLE DIFFICULT Lv11` |
| `スクリーンショット (146).png` | `chart_field_template_146_single_difficult_lv13.png` | `SINGLE DIFFICULT Lv13` |
| `スクリーンショット (147).png` | `chart_field_template_147_single_basic_lv10.png` | `SINGLE BASIC Lv10` |
| `スクリーンショット (148).png` | `chart_field_template_148_single_expert_lv16.png` | `SINGLE EXPERT Lv16` |
| `スクリーンショット (149).png` | `chart_field_template_149_single_expert_lv17.png` | `SINGLE EXPERT Lv17` |

配置後のローカルテンプレート数は37枚です。`python -m tools.vision_poc --no-ocr --no-rois` では112件全正解を維持し、`m3_chart_field_adoption_candidates_summary.json` で `play_style`、`difficulty`、`level` がすべて 60/60 match、`adoption_candidate` になりました。M3-5集約では `play_style`、`difficulty`、`level` が60件すべて `ready` になり、M3-7解消順の残りは `song_title` / `artist` のOCR入口だけになりました。

## 2026-07-04 M3-8 chart-field final review

M3-8では、37テンプレート配置後の `play_style` / `difficulty` / `level` をPoC上の採用候補として整理します。ここでの `adoption_candidate` と M3-5 の `ready` は、confirmed-events 60件の `roi-template-holdout` が期待値と一致したことを示すだけです。本番採用済みテンプレート照合、DB保存可能、マスタ照合成功、ファジーマッチ成功、曲名正規化成功としては扱いません。

M3の残りは `song_title empty_ocr=2` と `artist empty_ocr=22` のOCR入口代表失敗を読む単位に絞ります。`song_title` は主要項目のOCR入口失敗代表、`artist` は左右切れがある補助項目のOCR入口失敗代表です。どちらも、曲名正規化やマスタ照合の失敗判定ではありません。

## 2026-07-04 M3-9 song/artist OCR entry failure review

M3-9では `m3_song_artist_ocr_entry_failures_summary.json` と Markdown を追加し、M3-4のOCR入口行から `engine_unavailable`、`ocr_failed`、`empty_ocr` だけを入口失敗代表として分けて読みます。`no_expected_value` は期待値整備の問題として入口失敗代表には含めません。

ローカル37テンプレート配置後に以下を再確認しました。

```powershell
python -m tools.vision_poc --m3-song-artist-ocr --ocr-target confirmed-events --no-rois --output data\vision_poc_m3_song_artist
```

結果は confirmed-events 60件、OCR試行120件、`song_title empty_ocr=2`、`artist empty_ocr=22` でした。`song_title` の代表は `8000000` と `天弓ノ舞` で、主要項目のOCR入口失敗代表として読みます。`artist` は `TAG`、`DKC Crew`、`Ryu☆` など短い表示や左右切れの影響を含む補助項目のOCR入口失敗代表として読み、`song_title` と同じ改善対象として混ぜません。

この確認はOCR入口の観察であり、曲名正規化、ファジーマッチ、マスタ照合、DB保存可否判定の成功/失敗ではありません。生成されたOCR画像、PoC出力、`data/` 配下のJSON/MarkdownはGit管理しません。
