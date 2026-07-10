# 次チャット用タスク

`C:\work\ddrgp_scorelog` で作業してください。必ず `AGENTS.md` のプロジェクトルールに従ってください。このファイルは次チャット用の引き継ぎ仕様です。作業では `docs/next-task.md` の更新だけで完了扱いにせず、コード、テスト、CLI、README、workflow、または設計docsなど、実行可能な成果物変更を1つ以上進めてください。

## 推論レベル

high

## 作業ブランチ

今回の作業ブランチは以下です。

```powershell
codex/m8-personal-score-db-path-boundary
```

このPR/ブランチがmerge済みなら、次チャットでは最新 `main` から次フェーズ用の新ブランチを作ってください。未mergeなら、このブランチの先端を取り込んでから続きのブランチを作るか、このPRのmergeを待ってください。

推奨ブランチ:

```powershell
codex/m8-personal-score-db-diagnostic-boundary
```

開始時に以下を確認してください。

- `git status --short --branch`
- `git log --oneline -5`
- `git fetch --all --prune`
- `main` または継続元ブランチが最新であること。
- `metadata.csv`、`data/`、`logs/`、ローカル素材、ローカルDBがコミット対象に入っていないこと。

## 今回までの作業結果

M8 previewは完了済みとして扱います。正式M8本体では、正式個人スコアDBのschema contract、互換チェック、空DB初期化境界、connection単位の書き込み前準備入口、ファイルパス単位の準備境界まで進んでいます。

今回追加したもの:

- `PersonalScoreDbFilePreparationResult`
- `prepare_personal_score_db_file_for_write(path)`
- 新規DBファイルと0 byte空ファイルだけ正式初期schemaを作成するテスト
- compatible DBをファイル準備で変更しないテスト
- unknown DB、M8 preview DB、metadata identity mismatch、manual migration候補をファイル準備で自動変更しないテスト
- 非SQLiteファイルとディレクトリを正式DBとして開かず、非SQLiteファイルを変更しないテスト
- `docs/design/10_personal_score_db_schema.md`、`docs/design/05_storage_io_spec.md`、`tools/vision_poc/README.md`、`docs/implementation-roadmap.md` へのファイルパス境界反映

固定済みの主な語彙:

- `compatible`
- `initialize_empty_database`
- `manual_migration_required`
- `reject_m8_preview_database`
- `reject_unknown_database`
- `schema_version_mismatch`
- `m8_preview_database_not_supported`
- `unknown_database_not_supported`
- `missing_table:<table>`
- `score_db_metadata_missing`
- `score_db_metadata.<key>_missing`
- `score_db_metadata.<key>_mismatch`
- `invalid_sqlite_database`

固定済みの境界:

- M8 preview最小 `plays` と正式個人スコアDB `plays` は別物。
- 正式DB候補は `score_db_metadata`、`schema_migrations`、`source_captures`、`plays`、`analysis_logs` を持つ。
- 正式DB互換チェックは `PRAGMA user_version` だけではなく、metadata table と必須tableも見る。
- 空DBだけ `initialize_personal_score_db_if_empty()` で正式初期schemaを作成できる。
- `prepare_personal_score_db_for_write()` は空DBなら初期化し、互換エラーが残るDBは `ValueError` で止める。
- `prepare_personal_score_db_file_for_write(path)` は新規ファイルまたは0 byte空ファイルだけ初期化し、既存の正式DBは変更せずに通す。
- ファイル準備では M8 preview DB、unknown DB、metadata identity mismatch、manual migration候補、非SQLiteファイル、ディレクトリを正式DBとして開かない。
- `manual_migration_required` は backup方針と明示確認を決めるまで欠落table作成や `user_version` 修正をしない。
- 正式個人スコアDBへの本番insert、既定自動保存、既存DB migration実行にはまだ進んでいない。
- preview列、M7a raw候補、OCR raw/normalized は正式 `plays` に混入させない。
- `analysis_logs` は review状態やskip理由を持つが、保存値を二重管理しない。

まだ進めていないこと:

- 正式DB inspection結果のCLI/Markdown/JSON風diagnostic表示
- 正式個人スコアDBへの本番insert
- 既定自動保存
- duplicate key本格実装
- 低信頼度ログ本番保存
- 既存DBの実migration実行
- source capture参照の本格保存境界

## 次に必ず進める実作業

次は正式DBへ書く前の「検査結果を人間がどう読むか」をもう一段進めてください。本番insertはまだ実装しないでください。

第一候補:

- `inspect_personal_score_db_schema()` または `PersonalScoreDbSchemaInspection` を、Markdown/JSON風diagnosticへ変換する軽い関数を追加する。
- `migration_plan_status`、`migration_plan_reason`、`compatibility_errors`、必須table欠落、metadata identity、対象pathがある場合のpath情報を人間が読める形にする。
- compatible / initialize_empty_database / reject_m8_preview_database / reject_unknown_database / manual_migration_required の代表fixtureをテストする。
- diagnostic表示は本番insert、自動migration、低信頼度ログ本番保存、画像保存処理には進めない。

第二候補:

- 正式DBファイル準備結果 `PersonalScoreDbFilePreparationResult` の軽いsummary関数を追加し、`existed_before`、`size_before`、`initialized`、最終inspectionを表示できるようにする。
- 非SQLiteファイルやディレクトリ拒否を、CLI化前に例外語彙として読みやすくする。

このフェーズで決めたい候補:

- diagnostic関数をPython dictで返すか、Markdown文字列まで作るか。
- `manual_migration_required` を自動変更せず、backup/明示確認待ちにする表示語彙。
- 既存ファイルが空DBか未知DBかをdiagnostic上でどう区別するか。
- `rank` / `clear_type` が未取得の場合に正式insertを止めるか、unknown語彙で保存するか。

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
- `docs/design/10_personal_score_db_schema.md`
- `tools/vision_poc/README.md`
- `tools/vision_poc/runner.py`
- `tools/vision_poc/personal_score_db_schema.py`
- `tests/test_personal_score_db_schema.py`
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
- 既存DBの自動migration実行
- `manual_migration_required` DBの自動変更
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
python -m pytest tests\test_personal_score_db_schema.py
python -m pytest tests\test_vision_poc_ocr.py -k "m8"
python -m pytest tests\test_vision_poc_ocr.py -k "m7_save_decision or m7_save_readiness or m7a or m8"
python -m ruff check tools\vision_poc pyproject.toml tests
python -m compileall master tools\vision_poc
python -m pytest tests
git diff --check
```

今回の確認結果:

- `python -m pytest tests\test_personal_score_db_schema.py`: 26 passed
- `python -m pytest tests\test_vision_poc_ocr.py -k "m8"`: 20 passed
- `python -m pytest tests\test_vision_poc_ocr.py -k "m7_save_decision or m7_save_readiness or m7a or m8"`: 71 passed
- `python -m ruff check tools\vision_poc pyproject.toml tests`: passed
- `python -m compileall master tools\vision_poc`: passed
- `python -m pytest tests`: 232 passed
- `git diff --check`: passed

正式DB schema定数、migration境界、初期化境界、ファイルパス境界、diagnostic表示のテストを追加した場合は、その新規テストを明示的に実行してください。

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

- 正式個人スコアDBのdiagnostic表示、解析ログ境界、source capture参照境界、または次のオープン境界が1つ以上進んでいる。
- `docs/design/`、コード、テスト、READMEのいずれかに、次の実装へつながる成果物変更がある。
- 正式個人スコアDB保存の実insertにはまだ踏み込んでいない。
- 既存DBの自動migrationを実行していない。
- 空DB以外を正式schema初期化として自動変更していない。
- M8 preview最小 `plays` と正式個人スコアDBスキーマを混同していない。
- preview DB readback診断を正式スキーマ確定や保存成功として扱っていない。
- M8 preview DBを正式個人スコアDBとして受け入れていない。
- unknown DBやmetadata identity mismatchを正式DBとして受け入れていない。
- `manual_migration_required` をbackup/明示確認なしで自動変更していない。
- `identity_signal_*`、`m5_identity_reviewable`、`blocked_identity_signal` が曲ID/譜面ID確定として扱われていない。
- M7aの `recognized_digits`、`expected_value`、`match` が保存値確定として扱われていない。
- duplicate、`rejected_transition`、未確定候補、non-result が上流対象外のまま。
- 生成DB、テンプレート素材、PoC出力、`metadata.csv` 実体や画像をコミットしていない。
- 検証コマンドが通っている。
- コミット/Pushする場合は、Git管理対象外ファイルを含めていない。
