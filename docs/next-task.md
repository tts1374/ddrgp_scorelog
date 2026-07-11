# 次チャット用タスク

`C:\work\ddrgp_scorelog` で作業してください。`AGENTS.md` とDB保存境界Skillに従い、実装・検証後にこのファイルを更新してください。画像、`metadata.csv`、`data/`、`logs/`、実入力JSON、ローカルDBはGit管理しません。

## 推奨モデル

GPT-5.6 Sol

モデルはCodexのモデルピッカーで手動選択します。この記載だけでは自動切替されません。正式DBのduplicate境界、transaction、副作用順序を扱うため、品質優先でSolを推奨します。

## 推論レベル

high

## 作業ブランチ

今回の完了ブランチ:

```powershell
codex/m8-personal-score-db-explicit-cli-save
```

merge済みなら最新 `main` から次のブランチを作成してください。未mergeなら、このブランチの先端を取り込んでから作成してください。

```powershell
codex/m8-personal-score-db-duplicate-preflight
```

開始時に確認:

```powershell
git status --short --branch
git log --oneline -5
git fetch --all --prune
```

## 今回までの結果と固定した判断

- `--personal-score-db-save-input <path>` と `--personal-score-db-save-database <path>` の必須ペアから、`save_personal_score_db_file()` を1回だけ呼ぶCLI入口を追加した。
- 片方だけの指定とdiagnostic modeとの混在は、DB作成・変更前に拒否する。通常PoC、timestamped/manifest runner、`--m8-score-db-output` には接続していない。
- JSON外部形式はUTF-8の `input_schema_version=1` とした。候補材料、source/analysis値、object/nullの `formal_play`、object/nullの `exclusion` を分離した。
- loaderはtop-level/nestedの必須key、未知key、object/null、string/bool/integer/number、重複key、versionをadapter前に検査する。Python上のboolをintegerとして受け入れない。
- `candidate_material.identity_signal_*`、M7a `recognized_digits`、`played_at_ms`、top-level `timestamp_ms` を `formal_play` へ暗黙コピーしない。
- transaction完了した `ready` / `excluded` は終了コード0、adapterの `unresolved` はDB準備前に終了コード1、JSON/schema/DB拒否は終了コード2とした。
- 結果JSONは `result_schema_version`、DB path、adapter status、written、任意のplay ID、source capture ID、analysis ID、理由を持つ。
- CLI専用output file、diagnostic JSONL、低信頼度ログファイル、入力/DBの既定pathを導入していない。
- 新規/0 byte/compatible正式DBへのCLI保存と、preview/unknown/metadata identity mismatch/manual migration/non-SQLite/directory拒否をfixtureで固定した。
- 架空の外部入力契約fixtureを `tests/fixtures/personal_score_db_cli/ready-v1.json` に置いた。実スコア、ローカルpath、ローカルDBではない。
- `written=true` はreadyまたはexcludedのtransaction完了を表し、正式play保存の有無は `play_id` で区別する。
- writer途中失敗は同じ呼び出しのsource/play/analysisをrollbackする既存契約を維持した。

## 次に進める実作業

正式DBに同じ明示 `formal_play.duplicate_key` のplayが既にある場合、2件目のplayを保存成功へ進めない「DB保存直前duplicate preflight」を追加してください。duplicate keyの生成方式は変更せず、呼び出し元がレビュー済み正式値として明示したkeyだけを使います。

必須境界:

- 既存compatible正式DBの `plays.duplicate_key` を保存直前に確認し、衝突時は新しいplay rowを作らない。
- 衝突を汎用DB errorや成功playへ丸めず、duplicateとして機械可読に返す。source captureとanalysisを記録するか、完全無変更で返すかは、既存 `excluded` 契約、ID衝突、transaction原子性を比較して先に決め、docsとテストで一意に固定する。
- 新規/0 byte DBではpreflightのために余分な既定path、ログ、diagnostic outputを作らない。
- preview DB、unknown DB、metadata identity mismatch、manual migration、非SQLite、ディレクトリの拒否境界を変えない。
- adapterの `unresolved` と不正JSONは、duplicate照会やDB準備より前に止める。
- M5/M7a候補、score/file由来preview duplicate key、相対時刻を正式値へ昇格しない。
- 既存UNIQUE制約とtransaction rollbackを維持し、同じkeyの連続呼び出しで部分rowを残さない。
- 単一プロセスPoCを越える並行writer制御やロック戦略は先取りしない。残るraceがある場合は明記する。
- Python APIと単発CLIの両方でduplicate collisionをテストする。既存CLI JSON schema versionは変更しない。

## 必読資料

- `AGENTS.md`
- `.agents/skills/review-ddrgp-db-save-boundary/SKILL.md`
- `docs/next-task.md`
- `docs/implementation-roadmap.md`
- `docs/design/03_event_and_save_boundary.md`
- `docs/design/04_data_model.md`
- `docs/design/05_storage_io_spec.md`
- `docs/design/06_regression_guard.md`
- `docs/design/10_personal_score_db_schema.md`
- `tools/vision_poc/personal_score_db_cli_save.py`
- `tools/vision_poc/personal_score_db_file_save.py`
- `tools/vision_poc/personal_score_db_save_adapter.py`
- `tools/vision_poc/personal_score_db_save.py`
- `tools/vision_poc/personal_score_db_schema.py`
- `tools/vision_poc/runner.py`
- `tools/vision_poc/README.md`
- `tests/test_personal_score_db_cli_save.py`
- `tests/test_personal_score_db_file_save.py`
- `tests/test_personal_score_db_save_adapter.py`
- `tests/test_personal_score_db_save.py`
- `tests/test_personal_score_db_schema.py`
- `tests/test_vision_poc_ocr.py`

## スコープ外

- duplicate key生成方式の本格実装・差し替え
- 並行writer、常駐監視、非同期処理、Windows UI
- 実キャプチャAPI、実キャプチャデバイス依存コード
- 既定自動保存、常時保存、通常PoC/timestamped/manifest runnerからの暗黙保存
- M5/M7a/M8 previewからの正式値・duplicate key自動確定
- 既存DBの自動migration、backup/migration実行、自動repair
- `manual_migration_required` DBの自動変更
- 低信頼度ログファイル、失敗画像、diagnostic output/logの自動保存
- OCR confidenceから保存しきい値を新規決定する処理
- ROI座標の大変更、OCR方式全面刷新
- CLI JSON schema version 2や既定入力/DB pathの導入
- 画像、`metadata.csv`、`data/`、`logs/`、ローカルDB、実入力JSONのコミット
- 追加のプロジェクト専用Skill/Subagent作成

## 検証コマンド

```powershell
python -m pytest tests\test_personal_score_db_cli_save.py
python -m pytest tests\test_personal_score_db_file_save.py
python -m pytest tests\test_personal_score_db_save_adapter.py
python -m pytest tests\test_personal_score_db_save.py
python -m pytest tests\test_personal_score_db_schema.py
python -m pytest tests\test_vision_poc_ocr.py -k "m7_save_decision or m7_save_readiness or m7a or m8"
python -m ruff check tools\vision_poc pyproject.toml tests
python -m compileall master tools\vision_poc
python -m pytest tests
python -m tools.vision_poc
python -X utf8 "$env:USERPROFILE\.codex\skills\.system\skill-creator\scripts\quick_validate.py" ".agents\skills\review-ddrgp-db-save-boundary"
git diff --check
```

今回実際に実行した結果:

- CLI save: 24 passed
- file save: 14 passed
- adapter: 7 passed
- formal writer: 8 passed
- schema/diagnostic: 55 passed
- CLI/file/adapter/writer/schema結合: 107 passed
- M7/M8回帰: 71 passed、44 deselected
- 全テスト: 314 passed
- Ruff: passed
- compileall: passed
- Skill validator: `Skill is valid!`
- `python -m tools.vision_poc`: 221/221 correct、accuracy 1.000、false positive 0、false negative 0
- `git diff --check`: passed
- 実行不能な検証: なし

pytest実行時に `pytest_chalice` から `pkg_resources` deprecated warningが出るが、テスト失敗ではない。今回のCLI/DB保存境界に対する既知の機能リスクはない。

## コミット/Push方針

- 今回作業分だけをパス単位でステージする。
- `docs/next-task.md` は引き継ぎ仕様として同じコミットへ含める。
- 画像、`metadata.csv`、`data/`、`logs/`、ローカルDB、実入力JSON、生成物をステージしない。
- コード、テスト、fixture、README、設計docs、Skillの関連契約を同じコミットへ含める。
- staged diffを確認してからコミットし、通常pushする。force-pushしない。

## 完了条件

- 既存compatible正式DBで明示duplicate keyの衝突を保存直前に検出できる。
- duplicate collisionで2件目のplay rowを作らず、結果を機械可読に区別できる。
- collision時のsource capture / analysisの扱いとtransaction境界がdocs、Python API、CLI、テストで一致する。
- `unresolved` / 不正JSONはDB照会・作成・変更前に止まる。
- 新規/0 byte/compatible DBと各拒否DBの既存契約を維持する。
- candidate値、preview duplicate key、相対時刻を正式値へ暗黙昇格しない。
- duplicate key生成方式、並行writer制御、既定自動保存、migration、diagnostic/低信頼度ログ自動出力へ進んでいない。
- 関連README、設計docs、DB保存境界Skillを同期している。
- 検証が通り、Git管理外ファイルをコミットしていない。
