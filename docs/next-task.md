# 次チャット用タスク

`C:\work\ddrgp_scorelog` で作業してください。`AGENTS.md` とDB保存境界Skillに従い、実装・検証後にこのファイルを更新してください。画像、`metadata.csv`、`data/`、`logs/`、実入力JSON、ローカルDBはGit管理しません。

## 推奨モデル

GPT-5.6 Sol

モデルはCodexのモデルピッカーで手動選択します。この記載だけでは自動切替されません。正式保存入力のvalidation境界、CLI option排他、副作用順序を扱うため、品質優先でSolを推奨します。

## 推論レベル

high

## 作業ブランチ

今回の完了ブランチ:

```powershell
codex/m8-personal-score-db-duplicate-preflight
```

merge済みなら最新 `main` から次のブランチを作成してください。未mergeなら、このブランチの先端を取り込んでから作成してください。

```powershell
codex/m8-personal-score-db-input-validation-cli
```

開始時に確認:

```powershell
git status --short --branch
git log --oneline -5
git fetch --all --prune
```

## 今回までの結果と固定した判断

- `write_personal_score_db_save()` は正式DB準備後・source insert前に、呼び出し元が明示した `formal_play.duplicate_key` を既存 `plays` へ照会する。
- 衝突時は2件目のplayを作らず、今回入力のsource captureとanalysisだけを同じtransactionで記録する。
- collision analysisは `analysis_status=skipped`、`save_boundary_status=duplicate`、`skip_reason=duplicate_key_already_saved`、`duplicate=true` に固定した。
- Python file API / CLI結果はcollisionを `adapter_status=excluded`、`written=true`、`play_id=null`、理由 `duplicate_key_already_saved` として返す。CLI終了コードはtransaction完了として0を維持する。
- `PersonalScoreDbWriteResult` は `save_boundary_status`、`skip_reason`、`duplicate` も返し、connection単位APIでもcollisionを機械可読に区別できる。
- collision入力の `capture_id` / `analysis_id` は新しい一意値を要求する。同一IDの完全再送は冪等成功へ丸めず、UNIQUE拒否時に今回rowをrollbackする。
- 既存rollback契約はduplicate key以外のplay UNIQUE違反でも固定し、source/play/analysisの部分rowを残さない。
- preflightとinsertの間に別connectionが書く並行writer raceは残る。単一プロセスPoCを越えるロック制御は実装せず、既存 `plays.duplicate_key` UNIQUE制約を最終防壁として維持した。
- duplicate key生成方式、CLI JSON schema version、新規既定path、diagnostic/低信頼度ログ出力、preview/候補値から正式値への昇格は変更していない。
- 新規/0 byte/compatible正式DBと、preview/unknown/metadata identity mismatch/manual migration/non-SQLite/directory拒否の境界は維持した。

## 次に進める実作業

レビュー済み正式JSONをDBへ書く前に、同じstrict loaderとadapterだけを明示実行できる「保存入力validation CLI」を追加してください。人手レビュー手順の機械的な事前検査入口であり、DB保存や正式値自動生成には進めません。

必須境界:

- `--personal-score-db-save-input-validate <path>` のような単独の明示optionから、`load_personal_score_db_save_input()` と `adapt_personal_score_db_save_input()` だけを1回実行する。
- validation modeはDB pathを受け取らず、DBファイル、親ディレクトリ、`data/`、`logs/`、diagnostic outputを作成・変更しない。
- strict JSON loaderの `input_schema_version=1`、必須/未知key、重複key、object/null、bool/int/number/string検査をそのまま再利用し、別validatorを複製しない。
- 結果はJSONで、validation result schema version、入力path、`adapter_status`、正式save inputを構築できたか、理由を機械可読に返す。正式値や候補材料を標準出力へ丸ごと再掲しない。
- `ready` / `excluded` は終了コード0、`unresolved` は1、不正JSON/schema/option混在は2とし、既存save CLIの意味と揃える。
- validation optionは既存のsave必須ペア、DB diagnostic、通常PoC/M8 preview実行と明示的に排他にする。排他違反は副作用前に拒否する。
- `ready` はDB保存成功やduplicate preflight通過を意味しない。既存DBを開かないため、DB内duplicate collisionはvalidation modeでは判定しない。
- `excluded` はplayなし正式analysis入力を構築できた状態、`unresolved` は正式保存入力を構築できない状態として既存adapter語彙を維持する。
- M5/M7a候補、preview duplicate key、相対時刻を正式値へ昇格せず、正式JSONを自動生成・補完しない。
- fixtureでready、excluded、unresolved、不正JSON、option排他、DB非生成をテストする。既存CLI JSON input schema versionは変更しない。
- 人手レビューで確認する正式値と、validationが保証しない事項（DB互換性、DB duplicate、並行writer、実保存）をREADMEと設計docsへ同期する。

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
- `tools/vision_poc/runner.py`
- `tools/vision_poc/README.md`
- `tests/test_personal_score_db_cli_save.py`
- `tests/test_personal_score_db_file_save.py`
- `tests/test_personal_score_db_save_adapter.py`
- `tests/test_personal_score_db_save.py`
- `tests/test_personal_score_db_schema.py`
- `tests/test_vision_poc_ocr.py`

## スコープ外

- validation CLIからのDB作成、DB検査、duplicate照会、DB保存
- 正式JSONの自動生成・補完、候補値から正式値への昇格
- duplicate key生成方式の本格実装・差し替え
- 完全同一リクエスト再送の冪等化
- 並行writer、ロック戦略、常駐監視、非同期処理、Windows UI
- 実キャプチャAPI、実キャプチャデバイス依存コード
- 既定自動保存、通常PoC/timestamped/manifest runnerからの暗黙保存
- M5/M7a/M8 previewからの正式値・duplicate key自動確定
- 既存DBの自動migration、backup/migration実行、自動repair
- 低信頼度ログファイル、失敗画像、diagnostic output/logの自動保存
- CLI JSON input schema version 2、save result schema変更、既定入力/DB pathの導入
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

- CLI save: 25 passed
- file save: 15 passed
- adapter: 7 passed
- formal writer: 10 passed
- schema/diagnostic: 55 passed
- CLI/file/adapter/writer/schema合計: 112 passed
- M7/M8回帰: 71 passed、44 deselected
- 全テスト: 318 passed
- Ruff: passed
- compileall: passed
- Skill validator: `Skill is valid!`
- `python -m tools.vision_poc`: 221/221 correct、accuracy 1.000、false positive 0、false negative 0
- `git diff --check`: passed
- 実行不能な検証: なし

pytest実行時に `pytest_chalice` から `pkg_resources` deprecated warningが出るが、テスト失敗ではない。既知の機能リスクは、並行writer間でpreflight後・insert前に同じduplicate keyが書かれた場合、2件目が `excluded` ではなくUNIQUE拒否・transaction rollbackになること。並行writer制御は現フェーズ外として明記済み。

## コミット/Push方針

- 今回作業分だけをパス単位でステージする。
- `docs/next-task.md` は引き継ぎ仕様として同じコミットへ含める。
- 画像、`metadata.csv`、`data/`、`logs/`、ローカルDB、実入力JSON、生成物をステージしない。
- コード、テスト、README、設計docs、Skillの関連契約を同じコミットへ含める。
- staged diffを確認してからコミットし、通常pushする。force-pushしない。

## 完了条件

- strict JSON loaderとadapterを再利用する明示validation CLIがあり、DB pathなしでready/excluded/unresolved/invalidを機械可読に区別できる。
- validation実行がDB、親ディレクトリ、`data/`、`logs/`、diagnostic outputを作成・変更しない。
- ready/excluded/unresolved/invalidの終了コードと理由が既存save CLI語彙と一致する。
- save pair、diagnostic、通常PoC/M8 previewとのoption排他を副作用前に検査する。
- validation readyをDB保存成功やDB duplicateなしと誤認させない。
- candidate値、preview duplicate key、相対時刻を正式値へ暗黙昇格せず、正式JSONを自動生成しない。
- 既存duplicate preflight、DB拒否、transaction rollback、CLI save結果の契約を壊していない。
- 関連README、設計docs、DB保存境界Skillを同期している。
- 検証が通り、Git管理外ファイルをコミットしていない。
