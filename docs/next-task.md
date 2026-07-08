# 次チャット用タスク

`C:\work\ddrgp_scorelog` で作業してください。必ず `AGENTS.md` のプロジェクトルールに従ってください。`docs/next-task.md` は次チャット用の引き継ぎ仕様として扱い、実装・検証が終わった後に更新してください。`docs/next-task.md` の更新だけで作業完了扱いにしないでください。

## 推論レベル

high

## 作業ブランチ

次の主作業はM8なので、作業ブランチは `codex/m8-save-payload-preview` を推奨します。

作業開始時に以下を確認してください。

- `git status --short --branch`
- `git log --oneline -5`
- M7 PR `#5` (`codex/m7-save-readiness-review`) がmerge済みなら、最新 `main` から `codex/m8-save-payload-preview` を作る。
- M7 PR `#5` が未mergeなら、PRブランチ `codex/m7-save-readiness-review` の先端を取り込んでから `codex/m8-save-payload-preview` を作るか、M7 PRのmergeを待つ。
- `metadata.csv`、`data/`、`logs/`、ローカル素材、ローカルDBがコミット対象に入っていないこと。

## 現在地

- M7は完了扱いでよい。
- M7 PR: `https://github.com/tts1374/ddrgp_scorelog/pull/5`
- `m7_save_readiness_review.*` は、confirmed-events保存候補ごとにM3材料、M7a数字材料、任意M5 identity材料を束ねる保存判定前レビューとして実装済み。
- `m7_save_decision_preview.*` は、`m7_save_readiness_review_rows` を入力にして `preview_save_candidate` / `blocked_readiness` / `needs_identity_review` / `needs_digit_review` / `missing_required_material` を分ける保存判定プレビューとして実装済み。
- `m7_save_decision_preview.json` / Markdown には、`preview_save_candidate` のM5 source、jacket status、identity signal statusの件数と代表、`needs_identity_review` の理由別代表、`needs_digit_review` のROI別代表が出る。
- `preview_save_candidate` はM8へ渡す候補材料が揃ったプレビュー状態であり、保存OK、DB保存成功、曲ID/譜面ID確定、保存値確定ではない。
- M8はまだ未実装。次はDB insertではなく、M8 dry-run payload previewを作る。

2026-07-08時点のローカル確認:

- M5なし:
  - `python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_save_readiness`
  - 分類: 221/221 correct、false positives 0、false negatives 0。
  - `m7_save_decision_preview.json`: `target_count=60`、`preview_candidate_count=0`、`preview_status_counts={"blocked_readiness":60}`。
- M5あり:
  - `python -m tools.vision_poc --m5-jacket-match --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_m5_readiness`
  - 分類: 221/221 correct、false positives 0、false negatives 0。
  - M5 jacket match: features 69、candidates 60、diagnostics 118。
  - `m7_save_decision_preview.json`: `target_count=60`、`preview_candidate_count=60`、`preview_status_counts={"preview_save_candidate":60}`。
  - preview candidate M5 source counts: `{"jacket_feature":57,"title_linehash_dict":3}`。
  - preview candidate jacket status counts: `{"ambiguous":3,"matched":57}`。
  - preview candidate identity signal status counts: `{"composite_resolved_candidate":3,"jacket_resolved_candidate":57}`。
- `python -m tools.vision_poc --no-ocr`: 221/221 correct。
- `python -m pytest tests`: 186 passed。

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

M4/M5 DB生成や曲名正規化へ踏み込む場合は追加で読む資料:

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
- 個人スコアDBへのinsert、DBスキーマ本実装、低信頼度ログ本番仕様
- OCR結果やM7a認識結果から保存値を本番確定すること
- `ready_for_save_review`、`preview_save_candidate`、M8で追加する `payload_ready` をDB保存可能、保存成功、曲ID/譜面ID確定として扱うこと
- M5 `identity_signal_*` から曲ID/譜面IDを保存用確定すること
- M7aの `recognized_digits` を保存値確定として扱うこと
- ROI座標定義の大変更
- Tesseract OCR全体の撤去やOCR方式全面刷新
- duplicate key の本格実装差し替え
- M4 Releases配布の実装
- Windows常駐アプリUI
- プロジェクト専用Skill/Subagentの作成

M7a/M5/M7で完了済みとして扱い、次チャットで蒸し返さないもの:

- M7a 8 ROIの数字認識入口
- M7a横持ち集約
- M7a digit review representatives
- M7a / Tesseract comparison review representatives
- ローカルテンプレートあり環境の8 ROI 480/480 match確認
- M7 readiness への任意M5 `identity_signal_*` / `jacket_match_status` 接続
- M5 identity reviewable時に `song_title` / `artist` OCR不足だけで `blocked_m3_material` にしない挙動
- `m7_save_decision_preview.*` のstatus語彙と基本CSV列
- `m7_save_decision_preview.*` のM5 source / jacket status / identity status診断、identity review理由別代表、digit review ROI別代表

## 次に必ず進める実作業

M8 dry-run payload previewを追加する。DB保存ではなく、`preview_save_candidate` から「将来DBへ保存するならこの材料になる」という候補payloadを出す段階に留める。

第一候補は、`m7_save_decision_preview_rows` を入力にした `m8_save_payload_preview.csv`、`m8_save_payload_preview.json`、`m8_save_payload_preview.md` の追加です。

- 入力は `m7_save_decision_preview_rows`。
- payload対象は原則 `preview_status=preview_save_candidate` の行だけにする。
- `preview_save_candidate` 以外はpayload化せず、summary上で `excluded_preview_status_counts` のように除外件数として読む。
- payload preview status は保存OKではなく、M8本実装前のdry-run語彙に留める。
- 初期語彙案:
  - `payload_ready`
  - `missing_identity_candidate`
  - `missing_digit_value`
  - `unsupported_preview_status`
- `payload_ready` はM8へ渡す仮payload材料が揃った状態であり、DB保存可能、保存成功、曲ID/譜面ID確定、保存値確定ではない。
- payload候補には最低限以下を含める。
  - `organized_file`
  - `timestamp_ms`
  - `confirmation_mode`
  - `source_preview_status`
  - `source_preview_reason`
  - `identity_signal_song_id`
  - `identity_signal_chart_id`
  - `identity_signal_source`
  - `m5_identity_signal_status`
  - `m5_jacket_match_status`
  - `score_digits`
  - `max_combo`
  - `marvelous`
  - `perfect`
  - `great`
  - `good`
  - `miss`
  - `ex_score`
- 数字列はM7aの `*_recognized_digits` からpayload候補へ写す。ただし保存値確定ではなく、`*_expected_value` や `*_match` はレビュー材料として必要なら併記する。
- JSON summaryには `target_count`、`payload_candidate_count`、`payload_status_counts`、`excluded_preview_status_counts`、代表を出す。
- Markdownには `payload_ready` 代表、identity欠落代表、digit欠落代表、preview対象外status代表を出す。
- fixtureでは、M5ありready、候補ID欠落、数字欠落、preview対象外statusをネットワーク、画像、`metadata.csv` 非依存で固定する。
- 既存の `m7_save_readiness_review.*` と `m7_save_decision_preview.*` のCSV列や意味を壊さない。
- 仕様語彙やsummaryの読み方を追加した場合は、`docs/implementation-roadmap.md`、関連する `docs/design/`、`tools/vision_poc/README.md` も同じ作業で更新する。
- 主作業完了後、今回の結果を踏まえて `docs/next-task.md` を次チャット用に更新する。

代替候補:

- 先に `docs/design/04_data_model.md` にM8 payload previewのフィールド契約を追加しつつ、最小fixture関数だけ実装する。ただしdocs整理だけで終えないこと。
- `m8_save_payload_preview.*` のMarkdown代表だけを先に追加し、CSVの細かい列は次に回す。

## 検証コマンド

M7で通したコマンド:

```powershell
python -m pytest tests\test_vision_poc_ocr.py -k "m7_save_decision or m7_save_readiness or m7a"
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

M4/M5境界へさらに触った場合は、M5完了確認として `tests\test_master_match.py` と M5 jacket match のPoC実行も再確認すること。

## コミット/Push方針

- `metadata.csv`、`data/`、`logs/`、ローカル素材、ローカルDBはコミットしない。
- `samples/screenshots/cropped/` と `samples/screenshots/organized/` 配下の画像はローカル素材扱いでコミットしない。
- `samples/screenshots/organized/digit_templates/` などのM7aテンプレート画像はコミットしない。
- `docs/next-task.md` は引き継ぎ仕様としてコミット対象に含める。
- コード、README、docs、テストに変更がある場合のみ、今回作業分だけをステージしてコミットする。
- `data/master/ddrgp-master.sqlite`、`data/master/master-summary.json`、M5/M7a/M7/M8 PoC出力、ROI画像、OCR画像、解析ログはステージしない。
- 仕様語彙、出力ファイル名、summaryの読み方、保存境界、OCR/M7a/M7/M8対象境界を変えた場合は、関連する `docs/design/` または `tools/vision_poc/README.md` を同じコミットに含める。
- コミットがある場合は作業ブランチをpushする。

## 完了条件

- M8 dry-run payload previewが `m7_save_decision_preview_rows` を入力にしている。
- `preview_status=preview_save_candidate` だけをpayload対象にしている。
- `preview_save_candidate` 以外はpayload化せず、除外件数または代表として読める。
- `payload_ready` がDB保存可能、保存成功、曲ID/譜面ID確定、保存値確定として扱われていない。
- M8 payload previewがDB insert、DBスキーマ本実装、低信頼度ログ本番仕様へ進んでいない。
- M7 save readiness review と M7 save decision preview の既存CSV列や意味を壊していない。
- duplicate、`rejected_transition`、未確定候補、non-result が上流対象外のまま。
- `identity_signal_*`、`m5_identity_reviewable`、`blocked_identity_signal` が曲ID/譜面ID確定として扱われていない。
- M7aの `recognized_digits`、`expected_value`、`match` が保存値確定として扱われていない。
- M3材料不足、M7a数字レビュー、M5候補観測未解決、候補ID欠落、必須材料欠落を混同せず読める。
- M5あり実行では、M5 identity reviewable + M7a all digits の行が `preview_save_candidate` を経てM8 payload preview対象へ進む。
- M5未実行時は、preview上で `needs_identity_review` またはM7 readiness上の blockerとして止まり、M8 payload対象にならない。
- M7a 8 ROI 480/480 matchを壊していない。
- 既存Tesseract OCR出力を壊していない。
- M5の通常候補、診断出力、coverage summary、`identity_signal_*` の意味を変更していない。
- fixtureテストがネットワーク、画像、`metadata.csv` に依存せず通る。
- 画像PoCやM3境界を触った場合は、`python -m tools.vision_poc --no-ocr` が全正解。
- 生成DB、テンプレート素材、PoC出力、`metadata.csv` 実体や画像をコミットしていない。
- 検証コマンドが通っている。
- コミット/Pushする場合は、Git管理対象外ファイルを含めていない。
