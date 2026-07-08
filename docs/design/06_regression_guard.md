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
- `m3_song_artist_ocr_entry_failures_summary.json` と `m3_song_artist_ocr_entry_failures.md` は `--m3-song-artist-ocr` 指定時だけ生成する。
- M3 song/artist OCR入口失敗代表は M3 song/artist OCR入口行から `engine_unavailable`、`ocr_failed`、`empty_ocr` だけを集約し、`no_expected_value` は期待値整備問題として分ける。
- `song_title` の入口失敗は主要項目、`artist` の入口失敗は左右切れがある補助項目として別々に読み、同じ改善対象として混ぜない。
- M3 song/artist OCR入口失敗代表を曲名正規化、ファジーマッチ、マスタ照合、保存可否の成功/失敗扱いにしない。

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
- 採用候補レビューでは holdout 全件matchのfieldだけを `adoption_candidate` とし、参照不足があるfieldは `needs_template_references` として本番採用候補から分ける。
- ローカル37テンプレート配置後の `play_style` / `difficulty` / `level` 60/60 match はPoC上の採用候補として読むが、本番採用済みテンプレート照合、OCR、マスタ照合の成功扱いにはしない。
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

## M3 save candidate summary

- `m3_save_candidate_summary.csv`、`m3_save_candidate_summary.json`、`m3_save_candidate_summary.md` は confirmed-events 境界、つまり `confirmed_result=true` かつ `duplicate=false` だけを1行単位で集約する。
- duplicate、`event_type=rejected_transition`、未確定 `result_candidate`、non-result は M3 save candidate summary 対象外にする。
- 集約対象fieldは `song_title`、`artist`、`play_style`、`difficulty`、`level` に限定し、`rank` と数字OCR expected coverage を混同しない。
- status 語彙は `ready`、`missing_reference`、`ocr_unavailable`、`ocr_failed`、`empty_ocr`、`no_expected_value`、`not_adopted` に寄せる。
- `play_style` / `difficulty` / `level` の `ready` は M3-3 の `adoption_candidate` を反映するだけで、本番採用済みテンプレート照合、OCR、マスタ照合の成功扱いにはしない。
- `difficulty` / `level` の参照不足は保存前判断向けに `missing_reference` として読めるようにする。
- `song_title` / `artist` の `ready` は M3-4 OCR入口の観察結果であり、曲名正規化、ファジーマッチ、マスタ照合、DB保存可能を意味しない。
- `--m3-song-artist-ocr` を指定していない場合、`song_title` / `artist` はOCR未実行として `ocr_unavailable` に倒す。

## M3 save candidate blocker representatives

- `m3_save_candidate_blockers_summary.json` と `m3_save_candidate_blockers_summary.md` は M3-5集約の未ready fieldを status / failure reason ごとに代表整理する。
- 対象境界は confirmed-events、つまり `confirmed_result=true` かつ `duplicate=false` だけにする。
- duplicate、`event_type=rejected_transition`、未確定 `result_candidate`、non-result は M3 save candidate blocker representatives 対象外にする。
- 代表には `organized_file`、期待値、抽出値、extractor、`roi_path` を含める。
- この代表整理はレビュー補助であり、DB保存可否判定、マスタ照合、ファジーマッチ、曲名正規化の成功/失敗扱いにはしない。

## M3 save candidate blocker resolution order

- `m3_save_candidate_blocker_resolution_plan.json` と `m3_save_candidate_blocker_resolution_plan.md` は M3-5集約の未ready fieldから解消順を整理する。
- 対象境界は confirmed-events、つまり `confirmed_result=true` かつ `duplicate=false` だけにする。
- duplicate、`event_type=rejected_transition`、未確定 `result_candidate`、non-result は M3 save candidate blocker resolution order 対象外にする。
- `difficulty` / `level` の `missing_reference` は追加すべきローカルテンプレート参照ラベルとして読む。
- `field_needs_template_references` は不足ラベル追加後の再確認であり、個別ROIの保存成功扱いにしない。
- `song_title` / `artist` の `ocr_not_run`、`engine_unavailable`、`empty_ocr` はOCR入口の次手として読み、曲名正規化、ファジーマッチ、マスタ照合の成功/失敗扱いにはしない。
- ローカル37テンプレート配置後に chart-field 3項目が `ready` になった状態では、M3-7解消順の残りを `song_title` / `artist` OCR入口代表失敗に絞る。
- M3-9では `song_title` と `artist` のOCR入口失敗代表を役割別に固定し、chart-field adoption candidates、M3 save candidate summary、M3-6 blocker representatives、M3-7 resolution order、数字OCR expected coverage と混同しない。
- テンプレート画像、OCR画像、PoC出力はGit管理せず、必要ラベル、代表ROI、判断だけをdocsに残す。

## OCR出力互換

- `score_ocr.csv` の既存列を維持する。
- profile比較を有効にしても legacy `score_ocr.csv` は default profile の互換出力として維持する。
- OCRエンジンがない環境でもPoCは落とさず、`engine_unavailable` として記録する。
- 前処理画像を保存して目視確認できる状態を維持する。

## M7a digit recognition

- M7a digit recognition は confirmed-events 境界、つまり `confirmed_result=true` かつ `duplicate=false` だけを対象にする。
- duplicate、`event_type=rejected_transition`、未確定 `result_candidate`、non-result は M7a digit recognition 対象外にする。
- `score_digits` は0から1,000,000までの可変桁表示を扱い、固定6桁前提にしない。
- `score_digits` はカンマや背景ノイズを数字として数えず、大きな数字成分だけを左から読む。
- 1桁から7桁までの可変桁表示をfixtureで維持する。
- `max_combo` はROI左側ラベルや下線を数字として数えず、右側数字領域の前景コンポーネントを分割する。
- `marvelous` はROI左側ラベルや明るい青背景を数字として数えず、右側数字領域の前景コンポーネントを分割する。
- `perfect` はROI左側ラベルや明るい青背景を数字として数えず、右側数字領域の前景コンポーネントを分割する。
- `great` はROI左側ラベルや明るい青背景を数字として数えず、右側数字領域の前景コンポーネントを分割する。
- `good` はROI左側ラベルや明るい青背景を数字として数えず、右側数字領域の前景コンポーネントを分割する。
- `miss` はROI左側ラベル、短いマーカー、明るい青背景を数字として数えず、右側数字領域の白数字前景コンポーネントを分割する。
- `ex_score` はROI左側ラベルを数字として数えず、右側数字領域の前景コンポーネントを分割する。
- `max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss`、`ex_score` は4桁fixtureでも分割・認識できる状態を維持する。
- 判定数系テンプレートはROI別ディレクトリに加えて、共有 `judgment_counts` ディレクトリからも読める。
- `miss` は共有 `judgment_counts` だけでは `ambiguous` になり得るため、ROI別ローカルテンプレートを優先する。
- `max_combo` / `ex_score` 系テンプレートは、共通化候補として共有 `combo_ex_score` ディレクトリからも読める。
- `ex_score` は共有 `combo_ex_score` がない環境でも、既存 `max_combo` テンプレートをfallbackとして読める。
- M7a summary/report はROI別に `segment_count_counts` と `expected_digit_length_counts` を出し、テンプレート不足時でも分割数と期待桁数を確認できる。
- `recognized`、`ambiguous`、`missing_reference`、`failed_segmentation`、`not_evaluated` の語彙を維持し、保存OK/NG判定と混同しない。
- 同じ実行でTesseract結果がある場合だけ、`tesseract_comparison` を参考比較として読む。
- `m7a_digit_save_candidate_summary.csv`、`m7a_digit_save_candidate_summary.json`、`m7a_digit_save_candidate_summary.md` は confirmed-events 1件を1行にし、選択した数字ROIの `recognized_digits`、`status`、`failure_reason`、`match`、`confidence`、`distance` を横持ち集約する。
- M7a save candidate summary の `aggregate_status` は `all_digits_recognized`、`needs_digit_review`、`no_digit_rois` に限り、保存OK/NG判定やDB保存成功として扱わない。
- M7a save candidate summary でも duplicate、`event_type=rejected_transition`、未確定 `result_candidate`、non-result は対象外にする。
- `m7a_digit_save_candidate_review.json` と `m7a_digit_save_candidate_review.md` は、M7a save candidate summary の `needs_digit_review` 行だけをROI別 status / failure reason ごとに代表化する。
- M7a save candidate review の代表には `organized_file`、ROI名、`recognized_digits`、`expected_value`、`status`、`failure_reason`、`match`、`confidence`、`distance`、`segment_count` を含める。
- M7a save candidate review は `missing_reference`、`ambiguous`、`failed_segmentation`、`not_evaluated` を読み分けるレビュー補助であり、保存OK/NG判定やDB保存成功として扱わない。
- `m7a_tesseract_comparison_review.json` と `m7a_tesseract_comparison_review.md` は、同じ実行内の M7a digit rows と default `score_ocr` rows だけを比較し、`same_normalized`、`different_normalized`、`tesseract_unavailable`、`m7a_unavailable` を代表化する。
- M7a Tesseract comparison review は `m7a_digit_recognition_summary.json` の `tesseract_comparison` counts を置き換えない。代表には `organized_file`、ROI名、M7a `recognized_digits` / `status` / `failure_reason`、Tesseract raw / normalized / status / error、`expected_value`、M7a match、Tesseract match を含める。
- M7a Tesseract comparison review も保存OK/NG判定、DB保存、OCR方式刷新として扱わない。
- ローカル digit template 画像はGit管理しない。

## M7 save readiness review

- `m7_save_readiness_review.csv`、`m7_save_readiness_review.json`、`m7_save_readiness_review.md` は、M3 save candidate summary、M7a digit save candidate summary、任意の M5 jacket match rows を入力にする。
- 対象は confirmed-events 境界、つまり `confirmed_result=true` かつ `duplicate=false` の1件1行にする。
- duplicate、`event_type=rejected_transition`、未確定 `result_candidate`、non-result は対象外にする。
- M5未実行時はM3材料とM7a数字材料だけで従来どおりレビューする。
- M5入力がある場合、`identity_signal_status=jacket_resolved_candidate` / `composite_resolved_candidate` だけをレビュー可能なM5候補観測として扱う。
- M5 identity材料がレビュー可能な場合、`song_title` / `artist` OCR不足だけでは `blocked_m3_material` にしない。
- `m3_blocking_fields` は元のM3集約の未ready項目として維持し、M7保存前レビュー上のM3 blockerは `m7_m3_blocking_fields` として別に読む。
- M5未実行時は `song_title` / `artist` 不足もM3 blockerとして扱い、従来のM3 + M7aレビュー境界を維持する。
- readiness status は `ready_for_save_review`、`blocked_m3_material`、`blocked_digit_review`、`blocked_identity_signal`、`missing_required_material` に限る。
- `ready_for_save_review` はPoC材料が揃った状態であり、保存OK/NG判定、DB保存成功、曲ID/譜面ID確定として扱わない。
- `identity_signal_*` は候補観測であり、曲ID/譜面ID確定として扱わない。
- M7 save readiness review は DB insert、低信頼度ログ本番仕様、保存値本番確定に進まない。

## M7 save decision preview

- `m7_save_decision_preview.csv`、`m7_save_decision_preview.json`、`m7_save_decision_preview.md` は、`m7_save_readiness_review_rows` を入力にする。
- 対象はM7 save readiness reviewと同じ confirmed-events 境界、つまり `confirmed_result=true` かつ `duplicate=false` の1件1行にする。
- duplicate、`event_type=rejected_transition`、未確定 `result_candidate`、non-result は上流のM7 readiness対象外のまま維持する。
- preview status は `preview_save_candidate`、`blocked_readiness`、`needs_identity_review`、`needs_digit_review`、`missing_required_material` に限る。
- `preview_save_candidate` はM8へ渡す候補材料が揃ったプレビュー状態であり、保存OK/NG判定、DB保存成功、曲ID/譜面ID確定として扱わない。
- M5未実行の `ready_for_save_review` 行は `needs_identity_review` として止める。
- M5候補観測が未解決、または `identity_signal_song_id` / `identity_signal_chart_id` が欠ける行も `needs_identity_review` として止める。
- M7a digit reviewが必要な行は `needs_digit_review` とし、M3 readiness blockerやM5 identity blockerと混同しない。
- M7 readiness上のM3 blockerは `blocked_readiness` とし、必須PoC材料欠落は `missing_required_material` として分ける。
- CSVに出す `identity_signal_song_id` / `identity_signal_chart_id` は候補観測であり、曲ID/譜面ID確定として扱わない。
- CSVに出すM7aの `recognized_digits`、`expected_value`、`match` は候補値レビュー材料であり、保存値確定として扱わない。
- M7 save decision preview は DB insert、低信頼度ログ本番仕様、保存値本番確定に進まない。

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
