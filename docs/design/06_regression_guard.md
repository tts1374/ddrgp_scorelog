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
- WPF single-frame manifestのdirectory相対 `image_path` を読める。
- WPF補助列 `capture_source`、`width`、`height`、`captured_at_utc` と、同じ行へ追加したexpected columnsを `FrameInput.row` に保持する。
- 1280x720のWPF実captureはDPI scaleを二重適用せず、既存1280x720 ROIへそのまま渡す。
- WPF連続session manifestは複数のdirectory相対 `frames/frame-*.png` を取得順に解決し、capture補助列と追加expected columnsを保持する。
- WPF連続sessionの `timestamp_ms` はstrictly increasingで、manifest再読込は `confirmation_mode=time` を維持する。

## confirmation

- metadata mode は `confirmation_mode=frames`。
- timestamped mode は `confirmation_mode=time`。
- manifest mode は `confirmation_mode=time`。
- dry-run capture provider 由来のmanifest再読込は `confirmation_mode=time`。
- timestamp付き入力では継続フレーム数だけで確定しない。
- 短時間に偏った連続フレームは、枚数が多くても確定しない。
- 十分な継続時間を満たした result candidate は確定する。
- 複数screen_type混在シナリオでも short result は未確定、sustained result は確定、duplicate result は `duplicate=true` になる。
- WPF単発manifest 1行だけではresult candidateをconfirmed resultへ昇格しない。
- WPF連続sessionはcapture UIだけで分類やconfirmed result生成を起動しない。

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
- 保存前validation receiptは標準出力と同じ5 keyの投影だけを持ち、正式値、候補材料、template本文、DB情報を持たない。
- receipt出力はvalidation inputとの明示ペア、`data/` 配下の新規 `.json` に限定し、path/拡張子/既存ファイル/option排他をinput load前に拒否する。
- receiptの有無でready/excluded/unresolved/invalid、終了コード0/0/1/2、strict loader/adapter各1回の契約を変えない。
- receiptなしの従来validationはDB、`data/`、`logs/`、diagnostic outputを作成・変更しない。

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
- `needs_identity_review` の `preview_reason` と代表は、`m5_not_run`、`m5_identity_not_reviewable`、`identity_signal_id_missing` を混同しない。
- M7a digit reviewが必要な行は `needs_digit_review` とし、M3 readiness blockerやM5 identity blockerと混同しない。
- M7 readiness上のM3 blockerは `blocked_readiness` とし、必須PoC材料欠落は `missing_required_material` として分ける。
- `preview_save_candidate` は M5 source、jacket status、identity signal status のsummary countsと代表を出し、M8へ渡す候補材料の偏りを読めるようにする。
- `needs_digit_review` はROI別の `recognized_digits`、`expected_value`、`match`、`failure_reason` を代表で読めるようにする。
- CSVに出す `identity_signal_song_id` / `identity_signal_chart_id` は候補観測であり、曲ID/譜面ID確定として扱わない。
- CSVに出すM7aの `recognized_digits`、`expected_value`、`match` は候補値レビュー材料であり、保存値確定として扱わない。
- M7 save decision preview は DB insert、低信頼度ログ本番仕様、保存値本番確定に進まない。

## M8 save payload preview

- `m8_save_payload_preview.csv`、`m8_save_payload_preview.json`、`m8_save_payload_preview.md` は、`m7_save_decision_preview_rows` を入力にする。
- payload候補は `preview_status=preview_save_candidate` の行だけにする。
- `preview_save_candidate` 以外は `unsupported_preview_status` としてpayload材料にせず、`excluded_preview_status_counts` と代表で読めるようにする。
- payload status は `payload_ready`、`missing_identity_candidate`、`missing_digit_value`、`unsupported_preview_status` に限る。
- `payload_ready` はdry-run payload材料が揃った状態であり、DB保存可能、保存成功、曲ID/譜面ID確定、保存値確定として扱わない。
- `identity_signal_song_id` / `identity_signal_chart_id` / `identity_signal_source` はM5候補観測を写したもので、保存用確定IDではない。
- `score_digits`、`max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss`、`ex_score` はM7aの `*_recognized_digits` を写した候補値であり、保存値確定として扱わない。
- `*_expected_value` と `*_match` はレビュー材料としてだけ読む。
- M8 save payload preview は DB insert、低信頼度ログ本番仕様に進まない。

## M8 planned play records

- `m8_planned_play_records.csv`、`m8_planned_play_records.json`、`m8_planned_play_records.md` は、`m8_save_payload_preview_rows` を入力にする。
- 保存予定レコードへ変換するのは `payload_preview_status=payload_ready` の行だけにする。
- `unsupported_preview_status`、`missing_identity_candidate`、`missing_digit_value` は保存予定レコードへ変換しない。
- 最小 `plays` スキーマは in-memory SQLite fixtureで検証する。実ファイルDBは `--m8-score-db-output` 明示時だけ生成する。
- `played_at_ms` は `timestamp_ms` 由来の暫定値で、timestampなし入力では `0` として扱う。
- `song_id` / `chart_id` はM5候補観測であり、曲ID/譜面ID確定として扱わない。
- `score`、`max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss`、`ex_score` はM7a候補値であり、保存値確定として扱わない。
- 保存予定レコードは DB保存可能、DB保存成功、低信頼度ログ本番仕様として扱わない。

## M8 score DB write preview

- `m8_score_db_write_preview.csv`、`m8_score_db_write_preview.json`、`m8_score_db_write_preview.md` は、`m8_planned_play_records_rows` だけを入力にする。
- 非ready payloadは上流の planned records で止め、このpreviewへ入力しない。
- 新規 in-memory SQLite `plays` テーブルへinsertし、実ファイルDBは生成しない。
- summary/report の `schema_name=m8_score_db_preview`、`schema_version=1`、`schema_version_source=PRAGMA user_version` を維持する。
- summary/report の `schema_contract_scope=preview_minimal_plays` と `production_schema_status=not_production_schema` を維持し、preview専用最小 `plays` 契約であって正式個人スコアDBスキーマではないことを確認できる。
- preview `plays` のinsert対象列は `play_id` と `created_at` を除いた列として `M8_PLANNED_PLAY_RECORD_FIELDNAMES` と同じ順序に保ち、integer列は `M8_SCORE_DB_WRITE_PREVIEW_INTEGER_FIELDS` と一致させる。この確認はpreview最小スキーマの内部整合ガードであり、正式個人スコアDBスキーマ確定ではない。
- summary/report の `created_by_preview=tools.vision_poc.m8_score_db_preview` と `preview_metadata_table=preview_metadata` を維持する。
- SQLite側の `preview_metadata` 表はpreview生成物識別だけに使い、正式マイグレーションや本番保存成功の根拠として扱わない。
- `insert_target_count`、`inserted_count`、`row_count_after_insert`、`excluded_count` をsummaryで確認できる。
- write preview status は `inserted_in_memory`、`skipped_invalid_planned_record` に限る。
- `inserted_in_memory` はDB insert境界のdry-run確認であり、本番DB保存成功、曲ID/譜面ID確定、保存値確定として扱わない。
- `schema_version=1` はpreviewスキーマ契約の識別子であり、本番DB保存成功、曲ID/譜面ID確定、保存値確定として扱わない。
- `schema_contract_scope` と `production_schema_status` はpreview専用最小スキーマ識別であり、本番DB保存成功、正式スキーマ確定、曲ID/譜面ID確定、保存値確定として扱わない。
- `created_by_preview` はpreview生成物識別子であり、本番DB保存成功、曲ID/譜面ID確定、保存値確定として扱わない。
- `skipped_invalid_planned_record` は planned row contractの不足や整数列不正を示し、非ready payloadをここで再判定するための語彙ではない。
- timestampなし入力の `played_at_ms=0` は暫定値のままinsert境界へ渡す。

## M8 score DB file output preview

- `--m8-score-db-output` は `--m7a-digit-recognition` と一緒に明示された場合だけ有効にする。
- 出力先は `data/` 配下の新規SQLiteファイルに限定する。
- 実ファイルDBの `PRAGMA user_version` は `1` にし、summary/report の `schema_version=1` と一致させる。
- 実ファイルDBの `preview_metadata.created_by_preview` は `tools.vision_poc.m8_score_db_preview` にし、summary/report の `created_by_preview` と一致させる。
- 実ファイルDBの `preview_metadata.schema_contract_scope` は `preview_minimal_plays`、`preview_metadata.production_schema_status` は `not_production_schema` にし、summary/report の同名欄と一致させる。
- summary/report の `database_schema_version` と `database_preview_metadata` は実DBから読み戻した診断欄として維持し、定数のpreview識別欄と混同しない。
- summary/report の `database_readback_matches_preview_contract` と `database_readback_mismatch_reasons` は、`database_schema_version` と `database_preview_metadata` がpreview契約と一致するかの診断欄として維持する。
- `database_readback_mismatch_reasons` では、`database_preview_metadata.<key>_missing` は期待key欠落、`database_preview_metadata.<key>_mismatch` はkeyはあるが値が違う状態として読み分ける。
- summary/report の `database_plays_row_count` は実DBの `plays` 行数readbackとして維持し、`database_plays_row_count_matches_insert_counts` と `database_plays_row_count_mismatch_reasons` で `inserted_count` / `row_count_after_insert` との一致だけを診断する。
- summary/report の `database_plays_schema_columns` は実DBの `PRAGMA table_info(plays)` 由来のschema readback診断欄として維持する。
- summary/report の `database_plays_insert_columns_match_planned_contract`、`database_plays_integer_fields_match_preview_contract`、`database_plays_schema_mismatch_reasons` は、実ファイルpreview DBの列順、`play_id` / `created_at` のDB側補助列境界、preview INTEGER列がpreview最小 `plays` 契約と一致するかだけを診断する。
- `database_plays_schema_mismatch_reasons` では、列順不一致、`play_id` / `created_at` 欠落またはplanned contract混入、integer列不一致を読み分けられる状態にする。
- readback一致診断欄、row count一致診断欄、schema readback診断欄を本番DB保存成功、正式スキーマ確定、曲ID/譜面ID確定、保存値確定として扱わない。
- planned rows が0件の明示file outputでも、readback一致診断欄、row count一致診断欄、schema readback診断欄は `true` と空理由にする。
- 既定実行、`--m7a-digit-recognition` だけの実行、M5なし実行では実ファイルDBへ保存予定レコードをinsertしない。
- 入力は `m8_planned_play_records_rows` だけにする。
- 非ready payload、M5未実行、identity不足、digit不足の行をfile output側で再判定しない。
- file output preview status は `inserted_to_file_preview`、`skipped_invalid_planned_record` に限る。
- `inserted_to_file_preview` は明示指定されたpreview DBへのinsert確認であり、本番DB保存成功、曲ID/譜面ID確定、保存値確定として扱わない。
- file output preview の `schema_version=1` はpreviewスキーマ契約の識別子であり、本番DB保存成功、曲ID/譜面ID確定、保存値確定として扱わない。
- file output preview の `schema_contract_scope` と `production_schema_status` はpreview専用最小スキーマ識別であり、本番DB保存成功、正式スキーマ確定、曲ID/譜面ID確定、保存値確定として扱わない。
- file output preview の `created_by_preview` はpreview生成物識別子であり、本番DB保存成功、曲ID/譜面ID確定、保存値確定として扱わない。
- M5なしで planned rows が0件の場合は、空の `plays` スキーマDB、`preview_metadata`、readback欄、`inserted_count=0` として確認する。

## 正式個人スコアDB diagnostic

- `--personal-score-db-diagnostic` の既定 `inspect` mode は読み取り専用に保つ。
- `--personal-score-db-diagnostic-mode prepare-write` は新規DBファイルまたは0 byte空ファイルだけ正式初期schemaへ進める。
- compatible DB、空DB初期化、M8 preview拒否、unknown拒否、manual migration required、非SQLiteファイル、ディレクトリ拒否の診断語彙を維持する。
- `--personal-score-db-diagnostic-output` は標準出力と同じ診断を `data/` 配下へ保存するだけにする。
- diagnostic output のMarkdownは `.md` / `.markdown`、JSONは `.json` に限定し、formatと拡張子の不一致を拒否する。
- diagnostic output の `data/` 外指定はDB準備より前に拒否し、prepare-write対象の新規DBを作らない。
- `--personal-score-db-diagnostic-log-output` は診断1回につき1行のJSONLを `logs/` 配下へappendするだけにする。
- diagnostic log output は `.jsonl` に限定し、`logs/` 外指定や拡張子不一致をDB準備より前に拒否し、prepare-write対象の新規DBを作らない。
- diagnostic log record は `event_type=personal_score_db_diagnostic`、mode、format、exit code相当status、対象DB path、diagnostic output path、diagnostic dictを持つ。
- diagnostic log record の必須key、schema version、event type、mode、format、exit code、status、`diagnostic.is_compatible` の整合をappend前に検査する。
- diagnostic log output は複数回appendしても空行を出さず、1行1JSONとして読める状態に保つ。
- diagnostic output / diagnostic log output は本番insert、自動migration、既定自動保存、低信頼度ログ本番保存、source capture保存として扱わない。
- diagnostic log output はDB診断ログであり、`analysis_logs.log_path` が将来参照する本番解析ログや低信頼度ログとは別ファイルとして扱う。
- `source_captures` は元フレーム参照だけを保持し、解析ログ本文、DB診断ログ、低信頼度ログ本文を持たない。

## Analysis artifact contract

- version 1詳細JSONはstrictな必須key・型・status整合で検査し、unknown keyを拒否する。
- 正式play値、validation receipt key、DB diagnostic payloadを詳細JSONへ混入させない。
- `analysis_logs.log_path` は空文字または `logs/analysis_details/**/*.json` だけを許可する。
- failure imageは詳細JSONの別fieldで `logs/analysis_failures/` だけを参照し、source captureやdiagnostic logと混同しない。
- path traversal、絶対path、backslash、namespace外、想定外拡張子を副作用前に拒否する。
- retentionはUTC基準のpure計算に留め、既存ファイルの作成・削除やcleanupを開始しない。
- 明示生成はvalid payloadと安全な新規outputだけを受け付け、UTF-8 BOMなし、LF、決定的key順、末尾改行でatomicに1件を公開する。
- invalid schema、unsafe path、拡張子不正、既存output、option混在では親directoryを作らず、publish失敗では一時/部分ファイルを残さない。
- failure image pathはcontract検査だけを行い、画像の生成・copyをしない。artifact生成だけでDB insertせず、saveだけでartifactを暗黙生成しない。

### Artifact/save orchestration guard

- 現行artifact CLIとsave CLIの独立性、status、終了コードを変えず、接続は単発明示orchestration入口だけが担当する。
- 入力/adapter、artifact要否と共有ID/status/path、DB互換性、早期duplicate予告、artifact publish、DB transactionの順に進む。早期衝突でも停止せず、transaction内duplicate preflightでsource/analysisを記録し、UNIQUE制約も維持する。
- 低信頼度/errorの`excluded`だけartifact必須、ready・その他skip・DB duplicateは任意、unresolved/invalidは生成禁止とする。
- orchestration入口がartifact output pathと `analysis_logs.log_path` の一致を副作用前に保証する。
- artifact失敗ではDB未実行、artifact成功後のDB失敗ではrowをrollbackしてartifactを保持する。同一payloadだけ再利用し、既存fileを上書き・削除しない。
- `artifact_created_db_failed` を保存成功へ丸めず、`duplicate` / `excluded` の `play_id=null` を成功playとして扱わない。
- M9 manual WPF入口はworkflow入力、正式DB、表示用master DBを明示選択し、既存workflowを1回だけ呼ぶ。C#側でstrict入力や正式値を再構築しない。
- Python executableとrepository rootの探索は単発保存の実行時まで遅延し、探索失敗でread-only viewerの起動や通常閲覧を妨げない。
- UIは `saved` / `written=true` / 非null `play_id` だけread-only再読込し、再読込履歴に同じIDがあることを確認する。excluded、duplicate、unresolved、invalid、DB拒否、artifact partial successではplay反映を行わない。
- viewer単独のDB選択、履歴、詳細、自己ベスト操作はwrite processを起動せず、個人DBとmaster DBのhashを変えない。
- candidate material、正式play値、analysis detail本文を相互投影せず、receipt、DB diagnostic、failure image、source captureの責務を混ぜない。
- validationだけではDB、`data/`、`logs/`、画像を作成・変更しない。

## 正式個人スコアDB save input / transaction

- 正式保存入力はM8 preview payload/rowを直接受け取らず、timezone付き時刻、master version、正式ID、rank、clear type、正式duplicate key、confidence、app versionが確定済みであることを要求する。
- timestampなしpreviewの `played_at_ms=0`、PoCの `score:` / `file:` duplicate key、`source_kind=unknown` を正式writerへ渡さない。
- pure adapterは `candidate_material` の `identity_signal_*`、M7a `recognized_digits`、`played_at_ms` / `timestamp_ms` を正式play値へ暗黙昇格せず、正式値不足時に `unresolved` から保存入力を返さない。
- duplicateと明示された低信頼度/error/skipはadapterで `excluded` となり、`play=None` を維持する。
- 保存成功は `confirmed_result=true`、`duplicate=false`、`event_type=confirmed`、`analysis_status=saved`、`save_boundary_status=save_ready` に限る。
- duplicate、低信頼度、error、その他skipは `plays` を作らず、source captureとanalysisだけを記録する。
- source capture、play、analysisのID/hash/app version/confidenceが一致しない入力はDB準備前に拒否する。
- `source_captures`、任意の `plays`、`analysis_logs` は1 transactionで書き、途中失敗では同じ呼び出しのrowを残さない。
- 明示ファイル保存APIはadapterをDB準備より先に実行し、`unresolved` ではDBファイルも親ディレクトリも作らない。
- 明示ファイル保存APIは新規/0 byte/compatible正式DBだけへ書き、preview / unknown / metadata identity mismatch / manual migration候補 / 非SQLite / ディレクトリを変更せず拒否する。
- 明示ファイル保存APIの `written=true` はtransaction完了を表し、play rowの有無は `play_id` で区別する。`excluded` を保存成功playへ丸めない。
- 既存正式playと明示 `duplicate_key` が衝突したready入力は、2件目のplayを作らず `skipped` / `duplicate` / `duplicate_key_already_saved` / `duplicate=true` のanalysisへ変換する。Python API/CLIは `excluded` / `written=true` / `play_id=null` を返す。
- duplicate collisionのsource capture / analysis IDは新規一意値を要求し、完全同一ID再送を冪等成功へ丸めない。UNIQUE拒否や他のinsert失敗では今回の部分rowを残さない。
- DB保存直前preflight後の並行writer raceはこの単一プロセスPoCでは制御せず、`plays.duplicate_key` のUNIQUE制約とrollbackを維持する。
- 単発CLIは入力JSON pathと正式DB pathの必須ペアだけから明示ファイル保存APIを1回呼び、通常PoC、timestamped/manifest runner、`--m8-score-db-output`、既定自動保存へ接続しない。
- CLI JSON loaderはversion、必須/未知key、nested object/null、厳密な型をadapter前に検査し、boolとintを混同しない。
- CLI loaderはM5 `identity_signal_*`、M7a `recognized_digits`、`played_at_ms` / `timestamp_ms` を `formal_play` へ暗黙コピーしない。
- 保存前validation CLIは同じstrict loaderとadapterだけを各1回使い、ready/excluded/unresolved/invalidと終了コード0/0/1/2を固定する。
- validation結果は入力path、adapter status、save input構築可否、理由だけを返し、正式値や候補材料を再掲しない。
- validation optionとsave pair、diagnostic、通常PoC/M8 preview optionの混在を副作用前に拒否し、DB、親ディレクトリ、`data/`、`logs/`、diagnostic outputを作らない。
- validation readyをDB互換性、DB内duplicateなし、並行writer安全性、実保存成功として扱わない。
- review templateは `input_schema_version=1` と現行loaderの全必須構造を固定順で持ち、UTF-8 BOMなし・LF・末尾改行付きで生成する。
- review templateは `candidate_material={}`、未確定の全 `formal_play` field、`exclusion=null` を持ち、未編集状態をadapter/validationで `unresolved` に保つ。
- template生成はmetadata、M5/M7a/M8 preview、manifest、画像、DBを読まず、候補値、相対時刻、正式ID、正式duplicate keyを生成・補完しない。
- template optionは `data/` 配下の新規 `.json` だけを許可し、既存ファイル、`data/` 外、他option混在を出力副作用前に拒否する。DB、`logs/`、画像、diagnostic outputを作らない。
- 片方だけのCLI option、入力schema不正、`unresolved` はDB作成・変更前に非0終了する。`ready` / `excluded` だけが終了コード0でtransaction完了し、結果JSONの `play_id` でplay有無を区別する。
- CLI経由でも新規/0 byte/compatible正式DBだけを許可し、preview / unknown / metadata identity mismatch / manual migration候補 / 非SQLite / ディレクトリを変更せず拒否する。

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

## WPF single-frame capture guard

- pickerはユーザーの `1フレーム取得` 操作でだけ開き、viewer閲覧やmanual保存から暗黙実行しない。
- capture adapter、output writer、UI status mappingをinterfaceで分離し、Windows APIを使わないfakeでcancelと主要失敗statusを固定する。
- target終了、0x0、resize、device lost、access拒否、write失敗を保存成功へ丸めない。
- 成功・失敗・cancel後にframe、frame pool、capture session、D3D device、streamを解放し、同一processで次の明示captureを実行できるようにする。
- outputは `data/windows_capture/` 配下の一意directoryへatomicに公開し、既存出力を上書きせず、失敗時にstagingや部分manifestを残さない。
- capture output rootは操作時にrepository rootから解決し、process cwdへprivate画像を逸脱させず、探索失敗で通常viewer起動を妨げない。
- manifestは `image_path,timestamp_ms` を維持し、`screen_type=unknown` とcapture補助列を任意列としてmanifest readerへ渡す。

## WPF continuous capture session guard

- pickerで選択した1つのwindowに対し、session中は同じcapture item、D3D11 device、frame pool、capture sessionを所有する。
- 明示停止かつ1frame以上の場合だけ、staging上のframes、manifest、metadataを一意なfinal directoryへatomicに公開する。
- cancel、0frame停止、resize、target closed、device lost、write失敗ではstagingとqueued frameを破棄し、成功sessionへ丸めない。
- resizeは自動追従せず `Resized` で停止し、再選択を求める。
- 二重開始を拒否し、停止は冪等に扱い、event購読とWinRT/D3D resourceを一度だけ解放する。
- bounded queueで中間frameをdropしても、保存frameの順序とstrictly increasing timestampを維持する。
- `連続取得を開始` のcapture-only UIは分類、OCR、identity、confirmed event、正式save input、workflow、正式DB、viewer履歴を起動しない。
- `連続取得・保存` だけが完成manifest後に解析を起動し、capture失敗時は解析・workflowを呼ばない。
- capture saveは未確定/transitionをworkflow前で除外し、confirmed eventを直列に最大1回ずつ既存workflowへ渡す。
- candidate/raw/expected/preview/相対時刻はformal値へコピーせず、採用済みfield source、confidence、完全性不足を `unresolved` に保つ。
- DB duplicate、excluded、unresolved、invalid、artifact partial failure、DB拒否をsavedへ丸めず、transaction済みplayだけread-only再読込する。
- fatal event statusはsession `workflow_failed` とCLI非0終了へ伝播し、同sessionのcommit済みplayと失敗理由を両方表示する。
- capture接続は正式DB schema version 1、writer transaction、duplicate、manual workflow入口を変更しない。
- `.NET build/test` とcapture列を読む対象Python testを実行する。画像分類・ROI・OCR・confirmed-events生成を変更しない場合、Vision PoC本体の再実行は不要とする。
- capture save接続を変更した場合は、保存候補境界、formal昇格negative、workflow status写像、DB duplicate、viewer再読込のPython/.NET testと、利用可能な実capture manifestのdry-runを実行する。

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
## Personal score DB migration contract guard

`tests/test_personal_score_db_migration_contract.py` と `tests/fixtures/personal_score_db_migration/plan-matrix-v1.json` は、current / older supported / newer unsupported / unknown / preview / identity mismatch / partial state、登録済みtransition以外のtarget拒否、backup path安全性と既存file conflict、dry-run無変更と予定step投影、明示確認、終了コードを固定する。全execution stepの失敗について、backup検証前はsource無変更、commit前のtransaction失敗はrollback、commit以後はmanual recoveryとなることも固定する。

このcontractテストは実DB backupやmigrationを生成しない。既存save/orchestration/diagnostic CLIの回帰は従来テストで維持し、migration contractをそれらへ接続しないことを前提とする。

`tests/test_personal_score_db_migration_status.py` はschema inspectionからpure contractへの状態写像、JSON/Markdown projection、status/dry-runの同一contract利用、backup path read-only検査、専用CLI排他を固定する。fixtureの前後でsource DB hashが変わらず、backup pathが作成されないことを確認する。
