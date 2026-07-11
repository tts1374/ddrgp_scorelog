# 次チャット用タスク

`C:\work\ddrgp_scorelog` で作業してください。`AGENTS.md` とDB保存境界Skillに従い、実装・検証後にこのファイルを更新してください。画像、`metadata.csv`、`data/`、`logs/`、実入力JSON、ローカルDBはGit管理しません。

## 推奨モデル

GPT-5.6 Sol

モデルはCodexのモデルピッカーで手動選択します。この記載だけでは自動切替されません。正式保存入力の人手レビュー境界と、空templateが正式値を自動生成しないことを扱うため、品質優先でSolを推奨します。

## 推論レベル

high

## 作業ブランチ

今回の完了ブランチ:

```powershell
codex/m8-personal-score-db-input-validation-cli
```

merge済みなら最新 `main` から次のブランチを作成してください。未mergeなら、このブランチの先端を取り込んでから作成してください。

```powershell
codex/m8-personal-score-db-review-template-cli
```

開始時に確認:

```powershell
git status --short --branch
git log --oneline -5
git fetch --all --prune
```

## 今回までの結果と固定した判断

- `--personal-score-db-save-input-validate <path>` は既存 `load_personal_score_db_save_input()` と `adapt_personal_score_db_save_input()` だけを各1回実行する。
- validation結果は `validation_result_schema_version=1`、入力path、`adapter_status`、`save_input_constructed`、`reasons` だけを返し、正式値や候補材料を標準出力へ再掲しない。
- validationの `ready` / `excluded` は終了コード0、`unresolved` は1、不正JSON/schemaまたは他option混在は2に固定した。
- validationはDB pathを受け取らず、DBファイル、親ディレクトリ、`data/`、`logs/`、diagnostic outputを作成・変更しない。
- validation optionはsave pair、DB diagnostic、通常PoC、M8 previewを含む他の全optionと排他で、副作用前に拒否する。
- validation `ready` は正式save inputを構築できたことだけを表し、DB互換性、既存DB内duplicate、並行writer、実保存成功を保証しない。
- `excluded` はplayなし正式analysis入力を構築できた状態、`unresolved` は正式save inputを構築できない状態として既存adapter語彙を維持した。
- strict loaderの `input_schema_version=1`、必須/未知key、重複key、object/null、bool/int/number/string検査は変更していない。
- M5/M7a候補、preview duplicate key、相対時刻を正式値へ昇格せず、validationから正式JSONを生成・補完しない。
- 正式DB保存、duplicate preflight、collision時のsource/analysis記録、transaction rollback、DB拒否境界は変更していない。

## 次に進める実作業

レビュー済み正式JSONを人が安全に作り始めるための「空のreview template CLI」を追加してください。templateは候補値やpreview出力を取り込まず、全項目を人手で確認・入力するための構造だけを明示生成します。validation CLIと組み合わせる入口であり、自動補完やDB保存には進めません。

必須境界:

- `--personal-score-db-save-input-template <path>` のような単独の明示optionで、`input_schema_version=1` と現行strict loaderが受け取る全必須keyを持つUTF-8 JSON templateを1件生成する。
- 出力先は明示された `data/` 配下の新規 `.json` に限定し、既存ファイルを上書きしない。`logs/`、DB、画像、diagnostic outputは作成しない。
- template生成はmetadata、M5/M7a出力、M8 preview、manifest、画像、DBを読まない。候補ID、候補数字、相対時刻、preview duplicate keyを転記しない。
- `candidate_material` は空object、`formal_play` は全正式fieldを明示した空文字/nullのobject、`exclusion` はnullを既定とし、レビュー前templateがvalidationで `unresolved` になることを固定する。
- boolやsource/analysisの構造上必要なfieldにも値を置く必要がある場合は、安全な未確定値を使い、それを正式値・保存可能値と誤認させない。adapterが `ready` になるtemplateを生成しない。
- template JSONのkey順と末尾改行を固定し、UTF-8 BOMなし・LFで書く。別validatorや別schema定義を複製せず、現行loaderで読み戻して構造互換をテストする。
- template生成optionはvalidation、save pair、DB diagnostic、通常PoC、M8 previewを含む他optionと明示的に排他にし、副作用前に拒否する。
- 結果は生成pathとtemplate schema versionを機械可読JSONで返す。正式値や空template本文を標準出力へ再掲しない。
- 人手で確認・入力する正式値一覧、candidate materialは由来メモに留めること、template生成がレビュー完了や保存可能を意味しないことをREADMEと設計docsへ同期する。
- fixtureで新規生成、loader互換、validation unresolved、既存ファイル拒否、`data/` 外拒否、option排他、DB/`logs/`非生成をテストする。

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
- `tools/vision_poc/personal_score_db_save_adapter.py`
- `tools/vision_poc/personal_score_db_file_save.py`
- `tools/vision_poc/runner.py`
- `tools/vision_poc/README.md`
- `tests/test_personal_score_db_cli_save.py`
- `tests/fixtures/personal_score_db_cli/ready-v1.json`

## スコープ外

- templateへの候補値、preview値、metadata値、相対時刻の自動転記
- 正式値、duplicate key、ID、時刻、rank、clear typeの自動生成・補完
- template生成からのvalidation、DB検査、duplicate照会、DB保存の自動連鎖
- input schema version 2、save/validation result schema変更、既定入力/DB path
- validation CLIのDB作成、DB検査、duplicate照会、DB保存
- duplicate key生成方式の本格実装・差し替え
- 完全同一リクエスト再送の冪等化
- 並行writer、ロック戦略、常駐監視、非同期処理、Windows UI
- 実キャプチャAPI、実キャプチャデバイス依存コード
- 既定自動保存、通常PoC/timestamped/manifest runnerからの暗黙保存
- M5/M7a/M8 previewからの正式値・duplicate key自動確定
- 既存DBの自動migration、backup/migration実行、自動repair
- 低信頼度ログファイル、失敗画像、diagnostic output/logの自動保存
- ROI座標の大変更、OCR方式全面刷新
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

- CLI save/validation: 35 passed
- file save: 15 passed
- adapter: 7 passed
- formal writer: 10 passed
- schema/diagnostic: 55 passed
- M7/M8回帰: 71 passed、44 deselected
- 全テスト: 328 passed
- Ruff: passed
- compileall: passed
- Skill validator: `Skill is valid!`
- validation CLI実コマンド: ready JSON、終了コード0
- `python -m tools.vision_poc`: 221/221 correct、accuracy 1.000、false positive 0、false negative 0、transition countup shape candidates 3
- `git diff --check`: passed
- 実行不能な検証: なし

pytest実行時に `pytest_chalice` から `pkg_resources` deprecated warningが出るが、テスト失敗ではない。既知の機能リスクは、validationがDBを開かないためDB互換性とDB内duplicateを判定しないこと、並行writer間でduplicate preflight後・insert前に同じkeyが書かれた場合は2件目がUNIQUE拒否・transaction rollbackになること。これらはvalidationの保証外としてREADMEと設計docsに明記済み。

## コミット/Push方針

- 今回作業分だけをパス単位でステージする。
- `docs/next-task.md` は引き継ぎ仕様として同じコミットへ含める。
- 画像、`metadata.csv`、`data/`、`logs/`、ローカルDB、実入力JSON、生成物をステージしない。
- コード、テスト、README、設計docs、Skillの関連契約を同じコミットへ含める。
- staged diffを確認してからコミットし、通常pushする。force-pushしない。

## 完了条件

- 明示optionから新規の空review templateを1件だけ生成できる。
- templateは現行strict loaderとschema version 1に互換で、未編集状態ではadapter/validationが `unresolved` になる。
- templateは候補値、preview値、metadata値を読み込まず、正式値を自動生成・補完しない。
- 出力は明示された `data/` 配下の新規JSONだけで、既存ファイル、DB、`logs/`、画像、diagnostic outputを変更しない。
- template modeとvalidation/save/diagnostic/通常PoC/M8 previewのoption排他を副作用前に検査する。
- template生成をレビュー完了、保存可能、DB保存成功と誤認させない。
- 既存validation、duplicate preflight、DB拒否、transaction rollback、CLI save結果の契約を壊していない。
- 関連README、設計docs、DB保存境界Skillを同期している。
- 検証が通り、Git管理外ファイルをコミットしていない。
