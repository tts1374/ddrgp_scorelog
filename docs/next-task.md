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

- M7保存判定前レビュー `m7_save_readiness_review.*` に、任意のM5 jacket候補観測を接続した。
- `m7_save_readiness_review_rows()` は従来の `m3_save_candidate_summary_rows`、`m7a_digit_save_candidate_summary_rows` に加えて、任意の `m5_jacket_rows` を受け取る。
- `--m5-jacket-match` 実行時は、M5の `identity_signal_*` と `jacket_match_status` を M7 readiness の参照列として出す。
- `identity_signal_status=jacket_resolved_candidate` / `composite_resolved_candidate` の場合だけ、M5側材料をレビュー可能な候補観測として扱う。
- M5入力があり、M3材料とM7a数字材料が揃っていてもM5候補観測が未解決の場合は `blocked_identity_signal` で止める。
- M5未実行時は、従来どおりM3 + M7a材料だけで `ready_for_save_review` まで進める。
- 追加・更新したM7 readiness列:
  - `m5_identity_material_status`
  - `m5_identity_signal_status`
  - `m5_identity_signal_source`
  - `m5_identity_signal_song_id`
  - `m5_identity_signal_chart_id`
  - `m5_identity_signal_title`
  - `m5_identity_signal_reason`
  - `m5_jacket_match_status`
- readiness status は `ready_for_save_review`、`blocked_m3_material`、`blocked_digit_review`、`blocked_identity_signal`、`missing_required_material`。
- `ready_for_save_review`、`m5_identity_reviewable`、`identity_signal_*` は保存OK、DB保存成功、曲ID/譜面ID確定ではない。
- fixtureでは、M5解決済み、M5未解決、M5行欠落、M5未実行の読み方を追加確認した。
- 2026-07-08のローカル実行:
  - `python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_save_readiness`
  - 分類: 221/221 correct、false positives 0、false negatives 0。
  - `m7_save_readiness_review.json` は `target_count=60`、`readiness_status_counts={"blocked_m3_material":60}`、`m5_identity_material_status_counts={"m5_not_run":60}`。
  - `m7a_digit_save_candidate_summary.json` は `aggregate_status_counts={"all_digits_recognized":60}`。
- 2026-07-08のM5同時ローカル実行:
  - `python -m tools.vision_poc --m5-jacket-match --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_m5_readiness`
  - 分類: 221/221 correct、false positives 0、false negatives 0。
  - M5 jacket match: features 69、candidates 60、diagnostics 118。
  - `m7_save_readiness_review.json` は `target_count=60`、`readiness_status_counts={"blocked_m3_material":60}`。
  - 同じsummaryで `m5_identity_material_status_counts={"m5_identity_reviewable":60}`。
  - `m5_identity_signal_status_counts={"jacket_resolved_candidate":57,"composite_resolved_candidate":3}`。
  - M5材料は接続できたが、現行M3保存候補summaryが `song_title` / `artist` を必須材料として扱っているため、`--no-ocr` では全件 `blocked_m3_material` のまま。
  - ただし現方針では、数字系はM7a、曲同定はM5 `identity_signal_*` が主であり、曲名/artist OCRは主ルートではなく補助・診断扱いに寄せる。
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
- 個人スコアDB保存、低信頼度ログ本番仕様
- OCR結果やM7a認識結果から保存値を本番確定すること
- `song_title` / `artist` OCR成功をM7 readinessの主条件にすること
- M5 `identity_signal_*` から曲ID/譜面IDを保存用確定すること
- ROI座標定義の大変更
- Tesseract OCR全体の撤去やOCR方式全面刷新
- duplicate key の本格実装差し替え
- M4 Releases配布の実装
- Windows常駐アプリUI
- プロジェクト専用Skill/Subagentの作成

M7a/M5接続で完了済みとして扱い、次チャットで蒸し返さないもの:

- M7a 8 ROIの数字認識入口
- M7a横持ち集約
- M7a digit review representatives
- M7a / Tesseract comparison review representatives
- ローカルテンプレートあり環境の8 ROI 480/480 match確認
- M7 readiness への任意M5 `identity_signal_*` / `jacket_match_status` 接続
- M5未実行時にM3 + M7a材料だけで従来どおりレビューする挙動

## 次に必ず進める実作業

M7保存判定前レビューで残っている `blocked_m3_material` を、OCR非依存方針に合わせて再整理する。

第一候補は、M7 readiness のM3材料判定を、M5 identity とM7a数字材料を主ルートにした保存前レビューへ寄せる小さな仕様・実装変更です。

- M7 readiness では、M5 `identity_signal_status=jacket_resolved_candidate` / `composite_resolved_candidate` がある場合、`song_title` / `artist` OCR不足だけで `blocked_m3_material` にしない。
- M3側の主材料は、当面 `play_style` / `difficulty` / `level` など譜面材料として扱う。
- `song_title` / `artist` は optional diagnostic / legacy reference として残し、M7 readiness の保存前レビュー主条件から外す、またはM5 identity reviewable時に blocker から除外する。
- `m3_blocking_fields` の扱いを変える場合は、元のM3 summaryの意味を壊さず、M7 readiness側で「保存前レビュー上のM3 blocker」を別に読む。
- Markdownに、OCR非依存のM7読み方として `blocked_m3_material`、`blocked_digit_review`、`blocked_identity_signal`、`missing_required_material` の次アクション表を追加する。
- fixtureでは、`song_title artist` だけが不足していても M5 identity reviewable + M7a all digits なら `ready_for_save_review` へ進めるケースを追加する。
- M5未実行時は、曲同定材料がないため従来どおり `song_title` / `artist` 不足を止めるか、`missing_required_material` / `blocked_identity_signal` のどちらで読むかを仕様化する。
- 実装を変えた場合は、ネットワーク、画像、`metadata.csv` に依存しないfixtureテストを追加または更新する。
- 仕様語彙や読み方を変えた場合は、`docs/implementation-roadmap.md`、関連する `docs/design/`、`tools/vision_poc/README.md` も同じ作業で更新する。
- 主作業完了後、今回の結果を踏まえて `docs/next-task.md` を次チャット用に更新する。

代替候補:

- `m7_save_readiness_review.*` の代表に M5 identity の `jacket_resolved_candidate` / `composite_resolved_candidate` 内訳代表を追加し、M3で止まっていてもM5側材料が揃っていることを読みやすくする。
- `song_title` / `artist` OCRを optional diagnostic として明示し、既存M3 summaryとM7 readiness summaryの読み分けを README のM7確認手順として明記する。

## 検証コマンド

今回通したコマンド:

```powershell
python -m pytest tests\test_vision_poc_ocr.py -k "m7_save_readiness"
python -m pytest tests\test_vision_poc_ocr.py -k "m7_save_readiness or m7a"
python -m ruff check master tools\vision_poc pyproject.toml tests
python -m compileall master tools\vision_poc
python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_save_readiness
python -m tools.vision_poc --no-ocr
python -m pytest tests
python -m tools.vision_poc --m5-jacket-match --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_m5_readiness
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
- M3材料不足、M7a数字レビュー、M5候補観測未解決、必須材料欠落を混同せず読める。
- M7a 8 ROI 480/480 matchを壊していない。
- 既存Tesseract OCR出力を壊していない。
- M5の通常候補、診断出力、coverage summary、`identity_signal_*` の意味を変更していない。
- fixtureテストがネットワーク、画像、`metadata.csv` に依存せず通る。
- 画像PoCやM3境界を触った場合は、`python -m tools.vision_poc --no-ocr` が全正解。
- 生成DB、テンプレート素材、PoC出力、`metadata.csv` 実体や画像をコミットしていない。
- 検証コマンドが通っている。
- コミット/Pushする場合は、Git管理対象外ファイルを含めていない。
