# 次チャット用タスク

`C:\work\ddrgp_scorelog` で作業してください。必ず `AGENTS.md` のプロジェクトルールに従ってください。このファイルは次チャット用の引き継ぎ仕様です。作業では `docs/next-task.md` の更新だけで完了扱いにせず、コード、テスト、CLI、README、workflow、または設計docsなど、実行可能な成果物変更を1つ以上進めてください。

## 推論レベル

high

## 作業ブランチ

M8 preview完了PRはmerge済みです。次チャットでは最新 `main` から、正式M8用の新ブランチを作ってください。

推奨ブランチ:

```powershell
codex/m8-personal-score-db-schema-design
```

開始時に以下を確認してください。

- `git status --short --branch`
- `git log --oneline -5`
- `git fetch --all --prune`
- `main` が最新であること。
- `metadata.csv`、`data/`、`logs/`、ローカル素材、ローカルDBがコミット対象に入っていないこと。

## 現在地

M8 previewは完了済みとして扱います。

完了済みのM8 preview範囲:

- `m8_save_payload_preview.*`
- `m8_planned_play_records.*`
- `m8_score_db_write_preview.*`
- `--m8-score-db-output` による明示file output preview
- `schema_version=1` と `PRAGMA user_version=1` によるpreviewスキーマ識別
- `created_by_preview=tools.vision_poc.m8_score_db_preview` と `preview_metadata` によるpreview生成物識別
- `schema_contract_scope=preview_minimal_plays` と `production_schema_status=not_production_schema` によるpreview専用最小スキーマ識別
- file output previewの `database_schema_version`、`database_preview_metadata`、`database_plays_row_count`、`database_plays_schema_columns`
- metadata / row count / schema readback mismatch fixture
- Markdown report上のreadback mismatch reason表示fixture

M8 previewで確認した重要な境界:

- `payload_ready`、保存予定レコード、write preview、file output previewは、本番DB保存成功ではない。
- M8 preview最小 `plays` は正式個人スコアDBスキーマではない。
- `identity_signal_*` は曲ID/譜面IDの候補観測であり、保存用確定IDではない。
- M7a `recognized_digits` は保存値候補であり、保存値確定ではない。
- DB readback診断欄はpreview DBの内部整合確認であり、正式スキーマ確定ではない。

## 次に必ず進める実作業

次はM8本体の最初の作業として、正式個人スコアDBのスキーマ設計とmigration境界を固めます。まだ本番insert実装へは進まないでください。

第一候補:

- `docs/design/10_personal_score_db_schema.md` を新規追加する。
- 正式 `ddrgp-scores.sqlite` の初期スキーマ案を整理する。
- M8 preview最小 `plays` と正式個人スコアDB `plays` を明確に別物として書く。
- `plays`、保存スキップ/解析ログ、DB metadata、migration metadata、source capture reference の責務を分ける。
- `PRAGMA user_version`、metadata table、互換チェック、既存DB拒否/移行のmigration方針を整理する。
- 設計と連動する小さな検証成果物を追加する。例: schema定数、schema contract test、または設計docの必須語彙を守る軽量テスト。

初回M8本体で決めたい候補:

- 正式 `plays` に含める列: `play_id`、`played_at`、`master_version`、`song_id`、`chart_id`、`score`、`max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss`、`ex_score`、`rank`、`clear_type`、`capture_hash`、`duplicate_key`、`analysis_confidence`、`app_version`、`created_at`。
- OCR raw / normalized、M5 identity signal、M7a recognized digits、preview statusなどを正式 `plays` に直接入れるか、解析ログ側に逃がすか。
- 保存成功ログ、保存スキップログ、低信頼度ログをDB内tableにするか、JSONログ参照にするか。
- 既存DBがpreview DBだった場合に拒否するか、正式DBとは別物として扱うか。
- duplicate key本格化をDBスキーマ設計へどこまで含めるか。

代替候補:

- 先に `docs/design/10_personal_score_db_schema.md` ではなく `docs/design/10_save_and_migration_boundary.md` として、保存成功/スキップ、migration、既存DB拒否条件を中心に整理する。
- ただし、どちらの場合も本番DB insert、既定自動保存、duplicate key本格実装、低信頼度ログ本番保存はまだ実装しない。

## 必読資料

- `AGENTS.md`
- `docs/next-task.md`
- `docs/implementation-roadmap.md`
- `docs/design/03_event_and_save_boundary.md`
- `docs/design/04_data_model.md`
- `docs/design/05_storage_io_spec.md`
- `docs/design/06_regression_guard.md`
- `docs/design/08_master_db_generation.md`
- `docs/design/09_master_match_poc.md`
- `tools/vision_poc/README.md`
- `tools/vision_poc/runner.py`
- `tests/test_vision_poc_ocr.py`
- `tests/test_vision_poc_result_events.py`
- `master/README.md`
- `master/builder.py`
- `master/inspect.py`
- `tests/test_master_builder.py`
- `tests/test_master_match.py`

## スコープ外

- スクリーンショット画像、`samples/screenshots/metadata.csv`、`data/`、`logs/`、ローカル素材、ローカルDBのGit管理
- `samples/screenshots/cropped/` と `samples/screenshots/organized/` 配下のローカル追加画像コミット
- `samples/screenshots/organized/digit_templates/` などのM7aテンプレート画像コミット
- 本番キャプチャAPI、実キャプチャデバイス依存コード、常駐監視ループ、非同期処理
- Windows常駐アプリUI
- 正式個人スコアDBへの実insert
- 既定自動保存、常時保存処理、本番用の自動DB insert
- 低信頼度ログ本番保存の実装
- duplicate key の本格差し替え実装
- M5 `identity_signal_*` から曲ID/譜面IDを保存用確定すること
- M7aの `recognized_digits` を保存値確定として扱うこと
- OCR結果やM7a認識結果から保存値を本番確定すること
- ROI座標定義の大変更
- Tesseract OCR全体の撤去やOCR方式全面刷新
- M4 Releases配布の実装
- プロジェクト専用Skill/Subagentの作成

## 検証コマンド

最低限実行するコマンド:

```powershell
python -m pytest tests\test_vision_poc_ocr.py -k "m8"
python -m pytest tests\test_vision_poc_ocr.py -k "m7_save_decision or m7_save_readiness or m7a or m8"
python -m ruff check tools\vision_poc pyproject.toml tests
python -m compileall master tools\vision_poc
python -m pytest tests
git diff --check
```

正式DB schema定数やmigration境界のテストを追加した場合は、その新規テストを明示的に実行してください。

M8 preview既存契約を触った場合は、追加で以下を確認してください。既存DBファイルがあると拒否されるため、実行ごとに新しい `data/` 配下パスを使うこと。

```powershell
python -m tools.vision_poc --m5-jacket-match --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m8_next_check --m8-score-db-output data\vision_poc_m8_next_check\ddrgp-scores.sqlite
```

画像PoCや分類境界へ触った場合は、追加で以下も確認してください。

```powershell
python -m tools.vision_poc --no-ocr
python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_save_readiness
python -m tools.vision_poc --m5-jacket-match --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_m5_readiness
```

M4/M5境界やmaster DB生成へ触った場合は、`tests\test_master_match.py`、`tests\test_master_builder.py`、M5 jacket match のPoC実行も再確認してください。

## コミット/Push方針

- `metadata.csv`、`data/`、`logs/`、ローカル素材、ローカルDBはコミットしない。
- `samples/screenshots/cropped/` と `samples/screenshots/organized/` 配下の画像はローカル素材扱いでコミットしない。
- `samples/screenshots/organized/digit_templates/` などのM7aテンプレート画像はコミットしない。
- `docs/next-task.md` は引き継ぎ仕様としてコミット対象に含める。
- コード、README、docs、テストに変更がある場合のみ、今回作業分だけをステージしてコミットする。
- `data/master/ddrgp-master.sqlite`、`data/master/master-summary.json`、M5/M7a/M7/M8 PoC出力、ROI画像、OCR画像、解析ログ、`ddrgp-scores.sqlite` はステージしない。
- 仕様語彙、出力ファイル名、summaryの読み方、保存境界、OCR/M7a/M7/M8対象境界を変えた場合は、関連する `docs/design/` または `tools/vision_poc/README.md` を同じコミットに含める。
- コミットがある場合は作業ブランチをpushする。

## 完了条件

- M8 previewは完了済みとして扱い、正式M8のスキーマ設計またはmigration境界が1つ以上進んでいる。
- `docs/design/`、コード、テスト、READMEのいずれかに、次の実装へつながる成果物変更がある。
- 正式個人スコアDB保存の実insertにはまだ踏み込んでいない。
- M8 preview最小 `plays` と正式個人スコアDBスキーマを混同していない。
- preview DB readback診断を正式スキーマ確定や保存成功として扱っていない。
- `identity_signal_*`、`m5_identity_reviewable`、`blocked_identity_signal` が曲ID/譜面ID確定として扱われていない。
- M7aの `recognized_digits`、`expected_value`、`match` が保存値確定として扱われていない。
- duplicate、`rejected_transition`、未確定候補、non-result が上流対象外のまま。
- 生成DB、テンプレート素材、PoC出力、`metadata.csv` 実体や画像をコミットしていない。
- 検証コマンドが通っている。
- コミット/Pushする場合は、Git管理対象外ファイルを含めていない。
