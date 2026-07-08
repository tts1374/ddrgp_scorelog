# 次チャット用タスク

`C:\work\ddrgp_scorelog` で作業してください。必ず `AGENTS.md` のプロジェクトルールに従ってください。`docs/next-task.md` は次チャット用の引き継ぎ仕様として扱い、実装・検証が終わった後に更新してください。`docs/next-task.md` の更新だけで作業完了扱いにしないでください。

## 推論レベル

high

## 作業ブランチ

今回の作業ブランチは `codex/m8-save-payload-preview` です。

次の主作業はM8の続きなので、作業ブランチは `codex/m8-score-db-schema-preview` を推奨します。

作業開始時に以下を確認してください。

- `git status --short --branch`
- `git log --oneline -5`
- このM8 payload preview PRがmerge済みなら、最新 `main` から `codex/m8-score-db-schema-preview` を作る。
- 未mergeなら、`codex/m8-save-payload-preview` の先端を取り込んでから新ブランチを作るか、このPRのmergeを待つ。
- `metadata.csv`、`data/`、`logs/`、ローカル素材、ローカルDBがコミット対象に入っていないこと。

## 今回までの作業結果

- M8 dry-run payload previewを追加した。
- `m8_save_payload_preview.csv`、`m8_save_payload_preview.json`、`m8_save_payload_preview.md` を `--m7a-digit-recognition` 実行時に生成する。
- 入力は `m7_save_decision_preview_rows`。
- `preview_status=preview_save_candidate` だけをpayload候補として扱い、それ以外は `unsupported_preview_status` としてpayload材料へ昇格しない。
- payload status は `payload_ready`、`missing_identity_candidate`、`missing_digit_value`、`unsupported_preview_status`。
- `payload_ready` はdry-run payload材料が揃った状態であり、DB保存可能、保存成功、曲ID/譜面ID確定、保存値確定ではない。
- CSVには `organized_file`、`timestamp_ms`、`confirmation_mode`、`source_preview_status`、`source_preview_reason`、M5由来の `identity_signal_song_id` / `identity_signal_chart_id` / `identity_signal_source`、`m5_identity_signal_status`、`m5_jacket_match_status`、M7a 8 ROIの候補数字を出す。
- M7a数字列は `*_recognized_digits` 由来の候補値で、`*_expected_value` / `*_match` はレビュー材料としてだけ併記する。
- JSON summaryには `target_count`、`payload_candidate_count`、`payload_ready_count`、`payload_status_counts`、`excluded_preview_status_counts`、status別代表、ready / identity欠落 / digit欠落 / preview対象外の代表を出す。
- Markdownには `Payload Ready Representatives`、`Identity Missing Representatives`、`Digit Missing Representatives`、`Preview Exclusion Representatives` を出す。
- fixtureで M5ありready、候補ID欠落、数字欠落、preview対象外status をネットワーク、画像、`metadata.csv` 非依存で固定した。
- 仕様更新として `docs/implementation-roadmap.md`、`docs/design/03_event_and_save_boundary.md`、`docs/design/04_data_model.md`、`docs/design/06_regression_guard.md`、`tools/vision_poc/README.md` を更新した。
- `m7_save_readiness_review.*` と `m7_save_decision_preview.*` の既存CSV列や意味は変更していない。

2026-07-08時点のローカル確認:

- M5なし:
  - `python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_save_readiness`
  - 分類: 221/221 correct、false positives 0、false negatives 0。
  - `m7_save_decision_preview.json`: `target_count=60`、`preview_candidate_count=0`、`preview_status_counts={"blocked_readiness":60}`。
  - `m8_save_payload_preview.json`: `target_count=60`、`payload_candidate_count=0`、`payload_ready_count=0`、`payload_status_counts={"unsupported_preview_status":60}`、`excluded_preview_status_counts={"blocked_readiness":60}`。
- M5あり:
  - `python -m tools.vision_poc --m5-jacket-match --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_m5_readiness`
  - 分類: 221/221 correct、false positives 0、false negatives 0。
  - M5 jacket match: features 69、candidates 60、diagnostics 118。
  - `m7_save_decision_preview.json`: `target_count=60`、`preview_candidate_count=60`、`preview_status_counts={"preview_save_candidate":60}`。
  - preview candidate M5 source counts: `{"jacket_feature":57,"title_linehash_dict":3}`。
  - preview candidate jacket status counts: `{"ambiguous":3,"matched":57}`。
  - preview candidate identity signal status counts: `{"composite_resolved_candidate":3,"jacket_resolved_candidate":57}`。
  - `m8_save_payload_preview.json`: `target_count=60`、`payload_candidate_count=60`、`payload_ready_count=60`、`payload_status_counts={"payload_ready":60}`、`excluded_preview_status_counts={}`。
  - payload ready groups: `jacket_feature/matched/jacket_resolved_candidate=57`、`title_linehash_dict/ambiguous/composite_resolved_candidate=3`。
- `python -m tools.vision_poc --no-ocr`: 221/221 correct。
- `python -m pytest tests`: 187 passed。

## 必読資料

- `AGENTS.md`
- `docs/next-task.md`
- `docs/implementation-roadmap.md`
- `docs/design/03_event_and_save_boundary.md`
- `docs/design/04_data_model.md`
- `docs/design/05_storage_io_spec.md`
- `docs/design/06_regression_guard.md`
- `docs/design/09_master_match_poc.md`
- `tools/vision_poc/README.md`
- `tools/vision_poc/runner.py`
- `tools/vision_poc/master_match.py`
- `tests/test_vision_poc_ocr.py`
- `tests/test_vision_poc_result_events.py`
- `tests/test_master_match.py`

M4/M5 DB生成、曲名正規化、または個人スコアDBの実SQLiteファイル生成へ踏み込む場合は追加で読む資料:

- `docs/design/08_master_db_generation.md`
- `docs/design/07_m3_chart_field_review.md`
- `master/README.md`
- `master/builder.py`
- `master/inspect.py`
- `tests/test_master_builder.py`

## スコープ外

- スクリーンショット画像、`samples/screenshots/metadata.csv`、`data/`、`logs/`、ローカルDBのGit管理
- `samples/screenshots/cropped/` と `samples/screenshots/organized/` 配下のローカル追加画像コミット
- `samples/screenshots/organized/digit_templates/` などのM7aテンプレート画像コミット
- 本番キャプチャAPI、実キャプチャデバイス依存コード、常駐監視ループ、非同期処理
- Windows常駐アプリUI
- 本番用の自動DB insert、常時保存処理、低信頼度ログ本番仕様
- `payload_ready` を保存OK、DB保存成功、曲ID/譜面ID確定、保存値確定として扱うこと
- M5 `identity_signal_*` から曲ID/譜面IDを保存用確定すること
- M7aの `recognized_digits` を保存値確定として扱うこと
- OCR結果やM7a認識結果から保存値を本番確定すること
- ROI座標定義の大変更
- Tesseract OCR全体の撤去やOCR方式全面刷新
- duplicate key の本格実装差し替え
- M4 Releases配布の実装
- プロジェクト専用Skill/Subagentの作成

M7a/M5/M7/M8 dry-runで完了済みとして扱い、次チャットで蒸し返さないもの:

- M7a 8 ROIの数字認識入口
- M7a横持ち集約
- M7a digit review representatives
- M7a / Tesseract comparison review representatives
- ローカルテンプレートあり環境の8 ROI 480/480 match確認
- M7 readiness への任意M5 `identity_signal_*` / `jacket_match_status` 接続
- M5 identity reviewable時に `song_title` / `artist` OCR不足だけで `blocked_m3_material` にしない挙動
- `m7_save_decision_preview.*` のstatus語彙と基本CSV列
- `m7_save_decision_preview.*` のM5 source / jacket status / identity status診断、identity review理由別代表、digit review ROI別代表
- `m8_save_payload_preview.*` のCSV / JSON / Markdown出力
- M8 dry-run status語彙と、`preview_save_candidate` 以外をpayload材料にしない境界

## 次に必ず進める実作業

M8 dry-run payload previewは完了したため、次はDB insertをPoC本流へ直結せず、個人スコアDBの最小スキーマ契約とpayload変換境界を小さく実装する。

第一候補は、`m8_save_payload_preview_rows` の `payload_ready` 行を入力にした「保存予定レコード」変換と、個人スコアDBスキーマの最小fixtureを追加することです。

- `payload_ready` 行だけを保存予定レコード変換の入力にする。
- `unsupported_preview_status`、`missing_identity_candidate`、`missing_digit_value` は保存予定レコードへ変換しない。
- まずはネットワーク、画像、`metadata.csv`、ローカルDBファイルに依存しないfixtureで、SQLite in-memoryまたはSQL文/row dictの契約を固定する。
- `plays` 相当の最小フィールド案:
  - `played_at` または `timestamp_ms` 由来の暫定時刻
  - `song_id`
  - `chart_id`
  - `score`
  - `max_combo`
  - `marvelous`
  - `perfect`
  - `great`
  - `good`
  - `miss`
  - `ex_score`
  - `source_organized_file`
  - `source_confirmation_mode`
  - `analysis_payload_status`
  - `identity_signal_source`
  - `m5_identity_signal_status`
  - `m5_jacket_match_status`
- 変換後も `identity_signal_*` とM7a数字列はPoC候補値由来であることを明示し、保存用確定・本番DB保存成功とは扱わない。
- 実ファイルDBへ書く場合は必ず `data/` 配下に出し、Git管理しない。ただし次作業ではまずin-memoryまたはdry-run row contractを優先する。
- duplicate key の本格差し替えはまだ行わない。必要ならDBスキーマ上の将来列や未決事項としてだけ整理する。
- 仕様語彙、出力ファイル名、summaryの読み方、保存境界を追加・変更した場合は、`docs/implementation-roadmap.md`、関連する `docs/design/`、`tools/vision_poc/README.md` も同じ作業で更新する。
- 主作業完了後、今回の結果を踏まえて `docs/next-task.md` を次チャット用に更新する。

代替候補:

- 先に `docs/design/04_data_model.md` と `docs/design/05_storage_io_spec.md` に、M8個人スコアDBの最小フィールド契約と未決事項を追加しつつ、payload row -> play row の最小fixture関数だけ実装する。ただしdocs整理だけで終えないこと。
- `m8_save_payload_preview.*` に保存予定レコード変換前の不足診断を1つ追加する。ただし既にpayload preview本体は完了済みなので、M8 dry-run出力の蒸し返しだけで終えないこと。

## 検証コマンド

今回通したコマンド:

```powershell
python -m pytest tests\test_vision_poc_ocr.py -k "m7_save_decision or m7_save_readiness or m7a or m8"
python -m ruff check master tools\vision_poc pyproject.toml tests
python -m compileall master tools\vision_poc
python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_save_readiness
python -m tools.vision_poc --m5-jacket-match --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_m5_readiness
python -m tools.vision_poc --no-ocr
python -m pytest tests
git diff --check
```

次チャットで最低限実行するコマンド:

```powershell
python -m pytest tests\test_vision_poc_ocr.py -k "m7_save_decision or m7_save_readiness or m7a or m8"
python -m ruff check master tools\vision_poc pyproject.toml tests
python -m compileall master tools\vision_poc
python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_save_readiness
python -m tools.vision_poc --m5-jacket-match --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_m5_readiness
python -m tools.vision_poc --no-ocr
python -m pytest tests
git diff --check
```

M3 OCRは主ルートではなく、必要な場合だけ補助・診断として確認する:

```powershell
python -m tools.vision_poc --m3-song-artist-ocr --m5-jacket-match --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-rois --output data\vision_poc_m7_m3ocr_m5
```

M4/M5境界やmaster DB生成へ触った場合は、`tests\test_master_match.py`、`tests\test_master_builder.py`、M5 jacket match のPoC実行も再確認すること。

## コミット/Push方針

- `metadata.csv`、`data/`、`logs/`、ローカル素材、ローカルDBはコミットしない。
- `samples/screenshots/cropped/` と `samples/screenshots/organized/` 配下の画像はローカル素材扱いでコミットしない。
- `samples/screenshots/organized/digit_templates/` などのM7aテンプレート画像はコミットしない。
- `docs/next-task.md` は引き継ぎ仕様としてコミット対象に含める。
- コード、README、docs、テストに変更がある場合のみ、今回作業分だけをステージしてコミットする。
- `data/master/ddrgp-master.sqlite`、`data/master/master-summary.json`、M5/M7a/M7/M8 PoC出力、ROI画像、OCR画像、解析ログはステージしない。
- 次作業でローカル個人スコアDBを生成した場合も、DBファイルはステージしない。
- 仕様語彙、出力ファイル名、summaryの読み方、保存境界、OCR/M7a/M7/M8対象境界を変えた場合は、関連する `docs/design/` または `tools/vision_poc/README.md` を同じコミットに含める。
- コミットがある場合は作業ブランチをpushする。

## 完了条件

- `m8_save_payload_preview_rows` の `payload_ready` 行だけを保存予定レコード変換の入力にしている。
- `unsupported_preview_status`、`missing_identity_candidate`、`missing_digit_value` が保存予定レコードへ進まない。
- 変換fixtureがネットワーク、画像、`metadata.csv`、ローカルDBファイルに依存せず通る。
- `payload_ready`、保存予定レコード、in-memory DB fixtureがDB保存成功、曲ID/譜面ID確定、保存値確定として扱われていない。
- `identity_signal_*`、`m5_identity_reviewable`、`blocked_identity_signal` が曲ID/譜面ID確定として扱われていない。
- M7aの `recognized_digits`、`expected_value`、`match` が保存値確定として扱われていない。
- 既存の `m7_save_readiness_review.*`、`m7_save_decision_preview.*`、`m8_save_payload_preview.*` のCSV列や意味を壊していない。
- duplicate、`rejected_transition`、未確定候補、non-result が上流対象外のまま。
- M5あり実行では、M5 identity reviewable + M7a all digits の行が `preview_save_candidate` を経て `payload_ready` へ進む。
- M5未実行時は、preview上で `blocked_readiness` または `needs_identity_review` として止まり、M8 payload ready対象にならない。
- M7a 8 ROI 480/480 matchを壊していない。
- 既存Tesseract OCR出力を壊していない。
- M5の通常候補、診断出力、coverage summary、`identity_signal_*` の意味を変更していない。
- 画像PoCやM3境界を触った場合は、`python -m tools.vision_poc --no-ocr` が全正解。
- 生成DB、テンプレート素材、PoC出力、`metadata.csv` 実体や画像をコミットしていない。
- 検証コマンドが通っている。
- コミット/Pushする場合は、Git管理対象外ファイルを含めていない。
