# イベントと保存境界

分類結果を時系列イベントとして解釈し、DB保存へ進めてよい行と保存してはいけない行を分ける設計です。現時点ではPython PoCの `result_events.csv` と `confirmed-events` OCR対象選定が正本です。

## 目的

- `result_candidate=true` だけで保存しない。
- 継続確認済みのリザルトだけを保存候補にする。
- duplicate と transition を保存対象外にする。
- OCR対象とDB保存候補の境界を一致させる。

## 主要概念

### `result_shape_candidate`

リザルト画面らしい形状検出。

例:

- RESULTSヘッダー
- 詳細リザルト枠
- ランク周辺やスコア周辺の形状

これは保存候補そのものではない。`transition_countup_*` はここが `true` でも保存対象外になり得る。

### `result_candidate`

単発フレーム分類で保存候補に見えるか。

これはOCR候補の参考母数ではあるが、保存確定ではない。実キャプチャでは一瞬の誤検出や遷移フレームが混ざるため、継続条件を満たすまで保存しない。

### `confirmed_result`

継続条件を満たし、保存直前候補として確定したか。

metadata mode:

- フレーム数ベース
- `confirmation_mode=frames`

timestamped / manifest / dry-run / 将来キャプチャ:

- 時間ベース
- `confirmation_mode=time`

### `duplicate`

同一リザルトが duplicate window 内で再確定したか。

`duplicate=true` の行は `confirmed_result=true` でも保存しない。

### `event_type`

イベントの読み分け。

- `none`: 未確定。保存しない。
- `confirmed`: 重複ではない保存候補。
- `duplicate`: duplicate window 内の重複確定。保存しない。
- `rejected_transition`: `transition_countup_*` など保存不可遷移。保存しない。

## 保存境界

DB保存へ進めてよい条件:

```text
confirmed_result=true
duplicate=false
```

同等に、現行PoCでは以下も保存候補として読める。

```text
event_type=confirmed
```

ただし将来 `event_type` が増える場合でも、保存境界の基本は `confirmed_result=true` かつ `duplicate=false` とする。

保存しない行:

- `duplicate=true`。`confirmed_result=true` でも保存しない。
- `event_type=rejected_transition`。
- `event_type=none`。
- `result_candidate=true` だが `confirmed_result=false` の未確定候補。
- `result_shape_candidate=true` だけの行。

## OCR対象境界

`--ocr-target confirmed-events` は保存直前OCR相当の評価モードです。

対象:

```text
confirmed_result=true
duplicate=false
```

対象外:

- `result_candidate=true` だが未確定
- `duplicate=true`
- `event_type=rejected_transition`
- non-result

`--ocr-target result-candidate` は従来互換と副作用確認用であり、保存直前評価の成功扱いにはしない。

OCR対象境界、expected coverage、profile採用判断は別条件として読む。`confirmed-events` は「OCRを試す保存直前イベント」を選ぶ境界であり、DB保存成功やOCR成功を意味しない。対象イベントに対して期待値がないROIは `no_expected_values`、一部だけ期待値があるROIは `partially_evaluated` として扱い、どちらも最終的な保存品質の採用根拠にはしない。profile採用候補として読めるのは、confirmed-events 対象で expected coverage が `evaluated` かつ `recommended_profiles` があるROIだけです。`partially_evaluated` は暫定、`no_expected_values` は `reference_profiles` が出ていても目視参考に留める。OCR品質は `score_ocr_summary.json`、`score_ocr_profiles_summary.json`、`ocr_expected_coverage.md`、`ocr_roi_report.md` のROI別 `match_count` / `mismatch_count` / `empty_ocr_count` / `no_expected_value_count` を見て判断する。

M3入口の曲・譜面情報ROIも、評価対象は同じ confirmed-events 境界へ寄せる。ただし `song_title`、`artist`、`play_style`、`difficulty`、`level`、`rank` / `expected_rank` は数字OCRの expected coverage ではなく、`m3_metadata_expected_coverage.md` と `m3_metadata_expected_template.csv` で別に読む。これは期待値列が保存直前イベントにそろっているかの確認であり、曲名OCR、ランクテンプレート照合、マスタ照合の成功を意味しない。

M3 chart-field 抽出PoCの入口では、有限候補で扱いやすい `play_style`、`difficulty`、`level` を優先する。`m3_chart_fields.csv` は全イベント行を出し、`confirmed_result=true` かつ `duplicate=false` の行だけを `chart_field_target=true` にする。duplicate、`rejected_transition`、未確定候補、non-result は対象外理由を付ける。これは chart-field 評価対象の一覧であり、数字OCR expected coverage、曲名OCR、artist OCR、rank OCR、テンプレート照合、マスタ照合の成功とは分けて読む。

`m3_chart_field_extraction.csv` と `m3_chart_field_extraction_summary.json` は、同じ対象境界に対して抽出値、期待値、match、status、failure_reason を出す別レポートです。現行の extractor は `filename-baseline` で、ローカル `organized_file` 名から `SP/DP`、難易度、`lvXX` を正規化する初期baselineに留める。これはROI画像特徴、OCR、テンプレート照合、マスタ照合の成功ではない。

`m3_chart_field_image_feature_extraction.csv` と `m3_chart_field_image_feature_extraction_summary.json` は、ROI画像特徴由来の軽い比較baselineです。extractor は `roi-feature-nearest-centroid` で、confirmed-events 対象の `play_style`、`difficulty`、`level` ROIから明度、白/黄/シアン/緑比率、エッジ比率などを取り、期待値ラベルごとの leave-one-out centroid に最も近い値を出す。これは画像特徴の診断用であり、OCR、テンプレート照合、マスタ照合の成功ではない。画像特徴やテンプレート比較へ進む場合も、対象境界と `match` / `mismatch` / `empty_extraction` / `no_expected_value` / `skipped` の status 語彙を維持する。

`m3_chart_field_image_feature_diagnostics.md` は、同じ画像特徴baselineの mismatch を混同表と代表ROIで確認する補助レポートです。`play_style` の単発mismatch、`difficulty` の期待値/抽出値の混同、`level` の弱さを次のPoC単位へ渡すために読み、採用根拠やテンプレート照合成功として扱わない。

`m3_chart_field_template_extraction.csv` と `m3_chart_field_template_extraction_summary.json` は、ローカル `samples/screenshots/organized/chart_field_templates/` 画像と confirmed-events 対象の result ROIを参照する最近傍テンプレート比較PoCです。extractor は `roi-template-nearest` で、テンプレートファイル名や metadata 期待値から期待ラベルを読み、confirmed-events 対象の `play_style`、`difficulty`、`level` ROIと同じROIを比較する。confirmed-events 由来の参照は評価中の同一フレームを除く leave-one-out にする。これは追加テンプレート素材と評価セット由来参照を使った同分布内の比較実験であり、OCR、マスタ照合、採用済みテンプレート照合の成功ではない。参照がない環境では対象行を `empty_extraction` / `no_template_references` として扱い、期待ラベルの参照テンプレートがない mismatch は `missing_expected_template_reference` として通常の mismatch と区別する。通常の112件分類回帰セットとは混同しない。

## transition_countup の扱い

`transition_countup_*` はリザルト形状が出ていても保存対象外とする。

期待されるイベント:

```text
result_shape_candidate=true
result_candidate=false
confirmed_result=false
event_type=rejected_transition
duplicate=false
```

この扱いは、カウントアップ中のスコアや判定数を誤保存しないための回帰ガードです。

## time-based confirmation

timestamp 付き入力では、フレーム数ではなく継続時間で確定する。

現行PoCの基準:

```text
CONFIRMED_RESULT_MIN_DURATION_MS = 1000
```

考え方:

- FPS固定に依存しない。
- フレーム欠落やFPS揺れがあっても、実時間で保存確定を判断する。
- timestamp が短時間に偏っている場合は、フレーム数が多くても確定しない。

## duplicate window

timestamp 付き入力では時間窓で重複判定する。

現行PoCの基準:

```text
DUPLICATE_WINDOW_MS = 90000
```

timestamp なし入力ではフレーム窓で重複判定する。

```text
DUPLICATE_WINDOW_FRAMES = 90
```

## 現行duplicate key

現行PoCでは簡易キーを使う。

- ファイル名に `scoreXXXXXX` があれば `score:<digits>`
- なければ `file:<filename>`

これはローカルPoC用の簡易キーであり、本格実装ではない。score だけで寄せるため、同点別曲や同点別譜面を誤って duplicate 扱いする可能性がある。DB保存へ進む前に、本格キーへ差し替える。

将来候補:

- score + 曲名 + 難易度
- score + 判定数
- perceptual hash
- マスタ照合後の正規化済み result id

## `result_events.csv` の読み方

`result_events.csv` は保存直前イベント契約のCSVです。各列の意味は以下です。

- `frame_index`: 入力順の0始まりフレーム番号。
- `organized_file`: 入力画像を表す相対名またはmanifest上の画像名。
- `screen_type`: metadataまたはmanifest上の画面種別。未知なら空欄や `unknown` もあり得る。
- `result_candidate`: 単発フレーム分類で保存候補に見えるか。これだけでは保存しない。
- `result_shape_candidate`: リザルト画面らしい形状検出。`transition_countup_*` では `true` でも保存対象外。
- `confirmed_result`: 継続条件を満たしたか。duplicate行でも `true` になり得る。
- `event_type`: `none` / `confirmed` / `duplicate` / `rejected_transition` の現行イベント解釈。
- `duplicate`: duplicate window 内の同一キー再確定か。`true` の行は保存しない。
- `duplicate_key`: 現行PoCの簡易重複キー。
- `reason`: 分類理由、継続条件、重複距離などの確認用メモ。
- `timestamp_ms`: timestamped / manifest / dry-run 由来の入力時刻。metadata modeでは空欄。
- `candidate_duration_ms`: timestamp付き入力で候補が継続した時間。metadata modeでは空欄。
- `confirmation_mode`: `frames` または `time`。

保存候補確認では以下を見る。

- `event_type`
- `confirmed_result`
- `duplicate`
- `timestamp_ms`
- `candidate_duration_ms`
- `confirmation_mode`
- `reason`

`result_candidate=true` の行数だけで保存成功を判断しない。

## `result_events_summary.json` の読み方

見るべき値:

- `confirmed_count`: 重複ではない保存候補数
- `confirmed_result_count`: duplicate を含む確定フレーム数
- `duplicate_count`: 重複除外数
- `rejected_transition_count`: 遷移除外数
- `first_confirmed_timestamp_ms`: timestamp付き入力で最初に保存確定した時刻
- `confirmation_mode_counts`: `time` / `frames` の分布

confirmed-events OCR評価では、あわせて `score_ocr_summary.json` の `skipped_duplicate_count`、`skipped_unconfirmed_count`、`skipped_rejected_transition_count` を見る。duplicate、未確定候補、`rejected_transition` がOCR対象外のまま維持されていることを確認するための値であり、OCR精度そのものはROI別の expected coverage と match / mismatch で読む。

M3曲・譜面情報の期待値列確認では、`m3_metadata_expected_coverage.md` の `total confirmed-events` が保存直前イベント数と一致していること、duplicate、`rejected_transition`、未確定候補、non-result が対象外であることを見る。数字OCR用の `ocr_expected_coverage.md` とは別レポートとして扱う。

M3 chart-field 評価の足場では、`m3_chart_fields.csv` と `m3_chart_fields_summary.json` を見る。`chart_field_target_count` が保存直前イベント数と一致し、`excluded_counts` で duplicate、`rejected_transition`、未確定候補、non-result が対象外に残っていることを確認する。対象fieldは当面 `play_style`、`difficulty`、`level` に限定する。抽出評価へ進む場合は `m3_chart_field_extraction.csv` と `m3_chart_field_extraction_summary.json` を合わせて読み、`filename-baseline` の match をROI/OCR/テンプレート照合の成功扱いにしない。ROI画像特徴の比較は `m3_chart_field_image_feature_extraction.csv`、`m3_chart_field_image_feature_extraction_summary.json`、`m3_chart_field_image_feature_diagnostics.md` を読み、`roi-feature-nearest-centroid` を本格テンプレート照合の成功扱いにしない。ローカルテンプレート素材との比較は `m3_chart_field_template_extraction.csv` と `m3_chart_field_template_extraction_summary.json` を読み、`roi-template-nearest` を採用済みテンプレート照合やマスタ照合の成功扱いにしない。

## M0/M1で固定すること

- 保存境界は `confirmed_result=true` かつ `duplicate=false`。
- `transition_countup_*` は保存対象外。
- manifest / dry-run / 将来キャプチャは `confirmation_mode=time`。
- dry-run sequence scenario でも short result、sustained result、duplicate、`transition_countup_*` を同じ保存境界で確認する。
- duplicate key の本格差し替えは別フェーズ。
- DB保存はこの境界が固まってから実装する。
