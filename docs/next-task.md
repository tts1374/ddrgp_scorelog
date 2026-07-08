# 次チャット用タスク

`C:\work\ddrgp_scorelog` で作業してください。必ず `AGENTS.md` のプロジェクトルールに従ってください。`docs/next-task.md` は次チャット用の引き継ぎ仕様として扱い、実装・検証が終わった後に更新してください。`docs/next-task.md` の更新だけで作業完了扱いにしないでください。

## 推論レベル

high

## 作業ブランチ

`codex/m7-save-readiness-review`

作業開始時に以下を確認してください。

- `git status --short --branch`
- `git log --oneline -5`
- 現在ブランチが `codex/m7-save-readiness-review` であること
- `metadata.csv`、`data/`、`logs/`、ローカル素材、ローカルDBがコミット対象に入っていないこと

## 今回までの作業結果

- M7保存判定前レビューで、M5 identity材料がレビュー可能な場合のM3 blockerをOCR非依存方針へ寄せた。
- 元のM3集約の意味は維持し、`m3_blocking_fields` は従来どおりM3の未ready項目として残す。
- M7保存前レビュー上のM3 blockerとして、`m7_m3_material_status` と `m7_m3_blocking_fields` を追加した。
- M5 `identity_signal_status=jacket_resolved_candidate` / `composite_resolved_candidate` の場合、`song_title` / `artist` OCR不足だけでは `blocked_m3_material` にしない。
- M5未実行時は従来どおり、`song_title` / `artist` 不足をM3 blockerとして扱う。
- `m7_save_readiness_review.md` に status ごとの次アクション表を追加し、`blocked_m3_material`、`blocked_digit_review`、`blocked_identity_signal`、`missing_required_material` の読み分けを明示した。
- fixtureでは、`song_title artist` だけが不足していても M5 identity reviewable + M7a all digits なら `ready_for_save_review` へ進むケースと、M5未実行なら止まるケースを追加した。
- 仕様更新として `docs/implementation-roadmap.md`、`docs/design/03_event_and_save_boundary.md`、`docs/design/06_regression_guard.md`、`tools/vision_poc/README.md` を更新した。
- 2026-07-08のローカルM5なし実行:
  - `python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_save_readiness`
  - 分類: 221/221 correct、false positives 0、false negatives 0。
  - `m7_save_readiness_review.json` は `target_count=60`、`readiness_status_counts={"blocked_m3_material":60}`、`m7_m3_material_status_counts={"m7_m3_blocked":60}`、`m5_identity_material_status_counts={"m5_not_run":60}`。
- 2026-07-08のローカルM5あり実行:
  - `python -m tools.vision_poc --m5-jacket-match --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_m5_readiness`
  - 分類: 221/221 correct、false positives 0、false negatives 0。
  - M5 jacket match: features 69、candidates 60、diagnostics 118。
  - `m7_save_readiness_review.json` は `target_count=60`、`readiness_status_counts={"ready_for_save_review":60}`。
  - 同じsummaryで `m3_overall_status_counts={"not_ready":60}`、`m7_m3_material_status_counts={"m7_m3_ready":60}`、`m5_identity_material_status_counts={"m5_identity_reviewable":60}`。
  - `m5_identity_signal_status_counts={"jacket_resolved_candidate":57,"composite_resolved_candidate":3}`。
  - 各代表では `m3_blocking_fields="song_title artist"` が残る一方、`m7_m3_blocking_fields=""` になっている。
- `m7a_digit_save_candidate_summary.json` は8 ROI合計で各ROI 60/60 `recognized` / match、`aggregate_status_counts={"all_digits_recognized":60}`。
- `python -m tools.vision_poc --no-ocr` は 221/221 correct。
- `python -m pytest tests` は 185 passed。

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
- 個人スコアDB保存、DB insert、低信頼度ログ本番仕様
- OCR結果やM7a認識結果から保存値を本番確定すること
- `ready_for_save_review` をDB保存可能、保存成功、曲ID/譜面ID確定として扱うこと
- M5 `identity_signal_*` から曲ID/譜面IDを保存用確定すること
- `song_title` / `artist` OCR成功をM7 readinessの主条件に戻すこと
- ROI座標定義の大変更
- Tesseract OCR全体の撤去やOCR方式全面刷新
- duplicate key の本格実装差し替え
- M4 Releases配布の実装
- Windows常駐アプリUI
- プロジェクト専用Skill/Subagentの作成

M7a/M5/M7 readinessで完了済みとして扱い、次チャットで蒸し返さないもの:

- M7a 8 ROIの数字認識入口
- M7a横持ち集約
- M7a digit review representatives
- M7a / Tesseract comparison review representatives
- ローカルテンプレートあり環境の8 ROI 480/480 match確認
- M7 readiness への任意M5 `identity_signal_*` / `jacket_match_status` 接続
- M5未実行時にM3 + M7a材料だけで従来どおりレビューする挙動
- M5 identity reviewable時に `song_title` / `artist` OCR不足だけで `blocked_m3_material` にしない挙動

## 次に必ず進める実作業

M7保存判定前レビューがM5ありで全件 `ready_for_save_review` まで進んだため、次はDB保存ではなく、保存判定プレビューの小さな入口を作る。

第一候補は、`m7_save_readiness_review_rows` の結果を入力にした `m7_save_decision_preview.*` の追加です。

- 入力は confirmed-events 対象だけに限定された `m7_save_readiness_review_rows` を使う。
- `ready_for_save_review` 行だけを保存判定プレビュー対象にし、それ以外は `not_ready_for_preview` などで理由を残す。
- 出力は `m7_save_decision_preview.csv`、`m7_save_decision_preview.json`、`m7_save_decision_preview.md` のレビュー補助に留める。
- status語彙は小さく始める。例: `preview_save_candidate`、`blocked_readiness`、`needs_identity_review`、`needs_digit_review`、`missing_required_material`。
- `preview_save_candidate` は保存OK、DB保存成功、曲ID/譜面ID確定ではない。M8の保存処理へ渡す候補材料が揃ったプレビュー状態として読む。
- `identity_signal_song_id` / `identity_signal_chart_id` は候補観測としてCSVに出してよいが、保存値確定とは書かない。
- M7aの数字列も保存値確定ではなく、候補値のレビュー材料として出す。特に `recognized_digits` と `expected_value` / `match` の読み方を維持する。
- low confidence / blocked理由は、M3 readiness、M7a digit review、M5 identity unresolved、必須材料欠落を混同しない。
- Markdownには status別代表と次アクション表を入れる。
- fixtureでは、M5ありready、M5未実行blocked、M5未解決blocked、M7a digit review blocked、必須材料欠落をネットワーク、画像、`metadata.csv` 非依存で固定する。
- 仕様語彙や出力ファイル名を追加した場合は、`docs/implementation-roadmap.md`、関連する `docs/design/`、`tools/vision_poc/README.md` も同じ作業で更新する。
- 主作業完了後、今回の結果を踏まえて `docs/next-task.md` を次チャット用に更新する。

代替候補:

- いきなり新ファイルを増やす前に、`m7_save_readiness_review.md` の代表を拡張し、`ready_for_save_review` 行のM5 identity source別・jacket status別代表を追加する。
- M7保存判定プレビューの status語彙だけを先に docs/design に仕様化し、fixture駆動で最小実装へ進む。

## 検証コマンド

今回通したコマンド:

```powershell
python -m pytest tests\test_vision_poc_ocr.py -k "m7_save_readiness"
python -m pytest tests\test_vision_poc_ocr.py -k "m7_save_readiness or m7a"
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
python -m pytest tests\test_vision_poc_ocr.py -k "m7_save_readiness or m7a"
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
- `data/master/ddrgp-master.sqlite`、`data/master/master-summary.json`、M5/M7a/M7 PoC出力、ROI画像、OCR画像、解析ログはステージしない。
- 仕様語彙、出力ファイル名、summaryの読み方、保存境界、OCR/M7a/M7対象境界を変えた場合は、関連する `docs/design/` または `tools/vision_poc/README.md` を同じコミットに含める。
- コミットがある場合は作業ブランチを push する。

## 完了条件

- M7 save readiness review が confirmed-events 対象だけを入力にしている。
- duplicate、`rejected_transition`、未確定候補、non-result が対象外のまま。
- `m7_save_readiness_review.csv` / `.json` / `.md` が保存判定前レビューに留まり、保存OK/NG判定やDB保存に進んでいない。
- `ready_for_save_review` がDB保存可能、保存成功、曲ID/譜面ID確定として扱われていない。
- `identity_signal_*`、`m5_identity_reviewable`、`blocked_identity_signal` が曲ID/譜面ID確定として扱われていない。
- `m3_blocking_fields` と `m7_m3_blocking_fields` を混同せず読める。
- M3材料不足、M7a数字レビュー、M5候補観測未解決、必須材料欠落を混同せず読める。
- M5あり実行では、M5 identity reviewable + M7a all digits の行が `ready_for_save_review` へ進む。
- M5未実行時は、`song_title` / `artist` OCR不足だけでも従来どおりM3 blockerとして止まる。
- M7a 8 ROI 480/480 matchを壊していない。
- 既存Tesseract OCR出力を壊していない。
- M5の通常候補、診断出力、coverage summary、`identity_signal_*` の意味を変更していない。
- fixtureテストがネットワーク、画像、`metadata.csv` に依存せず通る。
- 画像PoCやM3境界を触った場合は、`python -m tools.vision_poc --no-ocr` が全正解。
- 生成DB、テンプレート素材、PoC出力、`metadata.csv` 実体や画像をコミットしていない。
- 検証コマンドが通っている。
- コミット/Pushする場合は、Git管理対象外ファイルを含めていない。
