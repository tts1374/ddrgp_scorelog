# 回帰ガード

今後の実装で壊してはいけない挙動を整理する。テスト、README、PoC実行確認の基準として使う。

## 入力契約

- manifest mode は `image_path,timestamp_ms` の最小CSVを読める。
- manifest mode は `screen_type` がなくても動く。
- manifest mode は任意の expected columns を保持する。
- `timestamp_ms` は空、非整数、負数、非単調増加をエラーにする。
- `image_path` 空欄と存在しない画像パスはエラーにする。
- dry-run capture provider はファイル名昇順にフレームを供給する。
- dry-run capture provider は `timestamp_ms` を単調増加させる。
- dry-run capture provider の出力先は `data/` 配下に限定する。
- dry-run capture provider の生成manifestは manifest mode で読める。
- dry-run sequence scenario は複数 `screen_type`、OCR期待値列、補助列を保持したmanifestを生成できる。
- dry-run sequence scenario の生成manifestは manifest mode で読める。

## confirmation

- metadata mode は `confirmation_mode=frames`。
- timestamped mode は `confirmation_mode=time`。
- manifest mode は `confirmation_mode=time`。
- dry-run capture provider 由来のmanifest再読込は `confirmation_mode=time`。
- timestamp付き入力では継続フレーム数だけで確定しない。
- 短時間に偏った連続フレームは、枚数が多くても確定しない。
- 十分な継続時間を満たした result candidate は確定する。
- 複数screen_type混在シナリオでも short result は未確定、sustained result は確定、duplicate result は `duplicate=true` になる。

## 保存境界

- `result_candidate=true` だけでは保存対象にしない。
- 保存対象は `confirmed_result=true` かつ `duplicate=false` だけ。
- 現行PoCでは `event_type=confirmed` は保存候補として扱える。
- 将来 `event_type` が増えても、基本境界は `confirmed_result=true` かつ `duplicate=false`。
- `event_type=duplicate` は保存しない。
- `event_type=rejected_transition` は保存しない。
- `event_type=none` は保存しない。
- duplicate の行は `confirmed_result=true` でも保存しない。
- 未確定の `result_candidate=true` は保存しない。
- `result_shape_candidate=true` だけでは保存しない。
- 現行 `duplicate_key` はファイル名由来のローカルPoC簡易キーのまま維持し、M1では本格キーへ差し替えない。

## transition_countup

- `transition_countup_*` は保存対象外。
- `transition_countup_*` は `result_shape_candidate=true` でもよい。
- `transition_countup_*` は `result_candidate=false` を期待する。
- `transition_countup_*` は `event_type=rejected_transition` として扱う。
- `transition_countup_*` は confirmed-events OCR対象外。

## OCR対象

- `--ocr-target result-candidate` は従来互換として `result_candidate=true` を対象にする。
- `--ocr-target confirmed-events` は `confirmed_result=true` かつ `duplicate=false` のみを対象にする。
- confirmed-events は保存直前OCR相当の評価として扱う。
- duplicate は confirmed-events OCR対象外。
- rejected transition は confirmed-events OCR対象外。
- 未確定候補は confirmed-events OCR対象外。
- dry-run sequence scenario の confirmed-events OCR対象は `confirmed_result=true` かつ `duplicate=false` のみ。
- `score_ocr_summary.json` では duplicate と rejected transition を `skipped_duplicate_count` / `skipped_rejected_transition_count` で読み分けられる。

## expected coverage

- confirmed-events 対象で expected columns が全試行にあるROIは `evaluated` として扱う。
- confirmed-events 対象で expected columns が一部だけあるROIは `partially_evaluated` として扱う。
- confirmed-events 対象で expected columns がないROIは `no_expected_values` として扱う。
- `no_expected_values` はOCR成功扱いにしない。
- `partially_evaluated` は暫定評価として扱う。
- `evaluated` / `partially_evaluated` / `no_expected_values` の意味を変えない。
- timestamped が生成する manifest は metadata の期待値列を保持する。
- manifest 再実行でも expected coverage の読み替えを維持する。
- profile比較では `no_expected_values` の `reference_profiles` は目視参考に留め、採用根拠にしない。
- profile比較では `partially_evaluated` の推奨は暫定扱いにし、期待値不足を埋めてから判断する。
- profile比較では `evaluated` かつ `recommended_profiles` があるROIだけを `recommendation_readiness=adoption_candidate` として読む。
- profile比較では `no_expected_values` の `recommended_profiles` を空に保ち、`recommendation_readiness=reference_only` として読む。
- profile比較では `partially_evaluated` の推奨を `recommendation_readiness=tentative` として読む。
- `ex_score` の `low-threshold` は confirmed-events 対象かつ expected coverage が `evaluated` の場合だけ採用候補として読む。
- `score_ocr_profiles_summary.json` と `ocr_roi_report.md` では default profile と推奨profileの差を確認できる補助情報を維持する。

## M3 metadata expected coverage

- 曲・譜面情報ROIの期待値列確認は confirmed-events 境界、つまり `confirmed_result=true` かつ `duplicate=false` だけを対象にする。
- duplicate、`event_type=rejected_transition`、未確定 `result_candidate`、non-result は M3 metadata expected coverage 対象外にする。
- `song_title`、`artist`、`play_style`、`difficulty`、`level`、`rank` / `expected_rank` は数字OCR expected coverage と混同しない。
- `m3_metadata_expected_coverage.md` と `m3_metadata_expected_template.csv` は期待値列の充足確認であり、曲名OCR、テンプレート照合、マスタ照合の成功扱いにはしない。
- `expected_rank` は `score_ocr_summary.json`、`score_ocr_profiles_summary.json`、`ocr_expected_coverage.md` の `evaluated` / `partially_evaluated` / `no_expected_values` 判定に含めない。

## M3 song/artist OCR entry

- `m3_song_artist_ocr.csv`、`m3_song_artist_ocr_summary.json`、`m3_song_artist_ocr.md` は `--m3-song-artist-ocr` 指定時だけ生成する。
- M3 song/artist OCR入口は confirmed-events 境界、つまり `confirmed_result=true` かつ `duplicate=false` だけを対象にする。
- duplicate、`event_type=rejected_transition`、未確定 `result_candidate`、non-result は M3 song/artist OCR対象外にする。
- 対象fieldは `song_title` と `artist` に限定し、`play_style` / `difficulty` / `level` の chart-field adoption candidates と混同しない。
- `ocr_raw` はOCRエンジン出力そのもの、`pre_normalized_text` は改行と連続空白だけを畳んだレビュー用文字列として扱う。
- `pre_normalized_text` を曲名正規化、ファジーマッチ、マスタ照合、保存可否の成功扱いにしない。
- OCRエンジンがない環境でもPoCは落とさず、`failure_reason=engine_unavailable` として記録する。
- `failure_reason` は `engine_unavailable`、`ocr_failed`、`empty_ocr`、`no_expected_value` に寄せる。
- `song_title` は主要項目、`artist` は左右切れがある補助項目として読む。

## M3 chart-field evaluation

- M3 chart-field 評価の入口は当面 `play_style`、`difficulty`、`level` に限定する。
- `m3_chart_fields.csv` の `chart_field_target=true` は confirmed-events 境界、つまり `confirmed_result=true` かつ `duplicate=false` だけにする。
- duplicate、`event_type=rejected_transition`、未確定 `result_candidate`、non-result は `chart_field_target=false` のまま、`exclusion_reason` で区別する。
- `m3_chart_fields_summary.json` は `chart_field_target_count` と `excluded_counts` で対象境界を確認できる。
- `m3_chart_fields.csv` と `m3_chart_fields_summary.json` は数字OCR expected coverage、曲名OCR、artist OCR、rank OCR、テンプレート照合、マスタ照合の成功扱いにはしない。
- `m3_chart_field_extraction.csv` と `m3_chart_field_extraction_summary.json` も confirmed-events 境界だけを抽出評価対象にする。
- 現行 extractor の `filename-baseline` はローカル `organized_file` 名からの初期baselineであり、ROI画像特徴、OCR、テンプレート照合、マスタ照合の成功扱いにはしない。
- `m3_chart_field_image_feature_extraction.csv` と `m3_chart_field_image_feature_extraction_summary.json` も confirmed-events 境界だけを抽出評価対象にする。
- `roi-feature-nearest-centroid` はROI画像特徴の軽い比較baselineであり、OCR、テンプレート照合、マスタ照合の成功扱いにはしない。
- `m3_chart_field_image_feature_diagnostics.md` は mismatch の混同表と代表ROIを読む補助レポートであり、OCR、テンプレート照合、マスタ照合の成功扱いにはしない。
- `m3_chart_field_template_extraction.csv` と `m3_chart_field_template_extraction_summary.json` も confirmed-events 境界だけを抽出評価対象にする。
- `roi-template-nearest` はローカル `chart_field_templates` 素材と confirmed-events result ROI の leave-one-out 最近傍比較PoCであり、OCR、マスタ照合、採用済みテンプレート照合の成功扱いにはしない。
- `m3_chart_field_template_diagnostics.md` は mismatch の混同表、代表ROI、`difficulty` の期待値レビュー候補を読む補助レポートであり、OCR、採用済みテンプレート照合、マスタ照合の成功扱いにはしない。
- `m3_chart_field_template_holdout_extraction.csv` と `m3_chart_field_template_holdout_extraction_summary.json` も confirmed-events 境界だけを抽出評価対象にする。
- `roi-template-holdout` は confirmed-events result ROI を評価専用にし、参照をローカル `chart_field_templates` 素材だけに限定する比較PoCであり、OCR、マスタ照合、採用済みテンプレート照合の成功扱いにはしない。
- `m3_chart_field_template_holdout_diagnostics.md` は holdout の mismatch と参照不足を読む補助レポートであり、参照元に confirmed-events result ROI を含めないことを確認する。
- `m3_chart_field_adoption_candidates_summary.json` と `m3_chart_field_adoption_candidates.md` は `roi-template-holdout` を根拠にしたM3-3採用候補レビューであり、OCR、マスタ照合、採用済みテンプレート照合の成功扱いにはしない。
- 採用候補レビューでは `play_style` のように holdout 全件matchのfieldだけを `adoption_candidate` とし、参照不足があるfieldは `needs_template_references` として本番採用候補から分ける。
- `difficulty` の期待値レビュー結果は `docs/design/07_m3_chart_field_review.md` に残し、Git管理外の `metadata.csv` 実体やローカル画像の代わりに参照する。
- confirmed-events result ROI を参照に加えても、評価中の同一フレームは参照から除外する。
- holdout 比較では confirmed-events result ROI を参照に加えず、`chart_field_templates/` と評価対象を混同しない。
- `difficulty` は5種類の前景文字色パターンで比較し、ROI全体背景に引っ張られないようにする。
- テンプレート素材や confirmed-events 参照がない環境では `status=empty_extraction`、`failure_reason=no_template_references` として扱い、通常の112件分類回帰セットの期待件数を変えない。
- 期待ラベルの参照テンプレートがない mismatch は `failure_reason=missing_expected_template_reference` として、参照ありの最近傍負けと分けて読めるようにする。
- 保存前判断へ渡すM3 chart-field failure reason は、参照不足を `missing_reference`、期待値不足を `no_expected_value`、抽出空を `empty_extraction`、参照ありの不一致を `low_confidence` に寄せる。
- `level` の単純ROI画像特徴baselineは、match が弱い間は採用候補扱いにしない。
- duplicate、`event_type=rejected_transition`、未確定 `result_candidate`、non-result は chart-field 抽出評価でも `status=skipped` のまま、`failure_reason` で区別する。
- chart-field 抽出評価の status 語彙は `match`、`mismatch`、`empty_extraction`、`no_expected_value`、`skipped` を維持する。
- `rank` は引き続き補助/部分評価として扱い、M3 chart-field の初期対象に含めない。

## OCR出力互換

- `score_ocr.csv` の既存列を維持する。
- profile比較を有効にしても legacy `score_ocr.csv` は default profile の互換出力として維持する。
- OCRエンジンがない環境でもPoCは落とさず、`engine_unavailable` として記録する。
- 前処理画像を保存して目視確認できる状態を維持する。

## ROI方針

- ROI座標は 1280x720 基準。
- 実画像サイズへ線形スケールする。
- OCR前処理改善のために、まずは局所前処理で試す。
- ROI座標定義の大変更は別フェーズとして扱う。

## Git管理

- スクリーンショット画像をコミットしない。
- `samples/screenshots/metadata.csv` をコミットしない。
- `data/` をコミットしない。
- `logs/` をコミットしない。
- ローカルDBをコミットしない。
- PoC出力や解析ログをコミットしない。

## 代表検証コマンド

```powershell
python -m ruff check tools\vision_poc pyproject.toml
python -m ruff check tests
python -m compileall tools\vision_poc
python -m pytest tests
python -m tools.vision_poc --sequence-mode timestamped --ocr-target confirmed-events --ocr-rois all --ocr-profile all
python -m tools.vision_poc --sequence-mode manifest --frame-manifest data\vision_poc_timestamped\frame_manifest.csv --frame-root samples\screenshots --ocr-target confirmed-events --ocr-rois all --ocr-profile all
```

dry-run capture provider 入口を変更した場合は、生成manifestを manifest mode で再読込するコマンドも実行する。
dry-run sequence scenario 入口を変更した場合も、生成manifestを manifest mode で再読込するコマンドを実行する。

## 実装を分けるべきもの

以下はM0/M1のついでに混ぜない。

- 本番キャプチャAPI
- 実キャプチャデバイス依存コード
- 常駐監視ループ
- 非同期処理
- DB保存
- OCR方式刷新
- duplicate key の本格差し替え
- ROI座標定義の大変更
