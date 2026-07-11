# 次チャット用タスク

`C:\work\ddrgp_scorelog` で作業してください。`AGENTS.md` とDB保存境界Skillに従い、実装・検証後にこのファイルを更新してください。画像、`metadata.csv`、`data/`、`logs/`、実入力JSON、ローカルDBはGit管理しません。

## 推奨モデル

GPT-5.6 Sol

モデルはCodexのモデルピッカーで手動選択します。この記載だけでは自動切替されません。人手レビュー手順と正式保存境界をまたぐ回帰を扱うため、品質優先でSolを推奨します。

## 推論レベル

high

## 作業ブランチ

今回の完了ブランチ:

```powershell
codex/m8-personal-score-db-validation-receipt
```

merge済みなら最新 `main` から次のブランチを作成してください。未mergeなら、このブランチの先端を取り込んでから作成してください。

```powershell
codex/m8-personal-score-db-review-workflow-regression
```

開始時に確認:

```powershell
git status --short --branch
git log --oneline -5
git fetch --all --prune
```

## 今回までの結果と固定した判断

- `--personal-score-db-save-input-validate-output <path>` は `--personal-score-db-save-input-validate <path>` とだけ組み合わせる明示optionとして追加した。
- receiptは標準出力または標準エラーと同じ `validation_result_schema_version=1`、入力path、`adapter_status`、`save_input_constructed`、`reasons` の5 keyだけを持つ。
- receiptはUTF-8 BOMなし、LF、固定key順、末尾改行付きで、明示された `data/` 配下の新規 `.json` に1件だけ生成する。既存ファイルは上書きしない。
- output path、拡張子、既存ファイル、必須ペア、他mode排他はinput loadと出力作成より先に検査する。
- receiptの有無でready/excluded/unresolved/invalid、終了コード0/0/1/2、strict loaderとadapter各1回の契約を変えない。
- invalid input JSON/schemaでも、出力先が有効ならinvalid validation投影をreceiptへ記録する。
- receiptは正式値、候補材料、template本文、DB path/schema/row、duplicate照会結果を持たない。
- receiptはレビュー結果の記録であり、レビュー承認、DB互換性、既存DB内duplicate非衝突、並行writer安全性、実保存成功を保証しない。
- receipt outputを指定しない従来validationは、引き続きDB、`data/`、`logs/`、diagnostic outputを作成・変更しない。
- template生成、strict loader/adapter、明示DB保存、duplicate preflight、collision時のsource/analysis記録、transaction rollback、DB拒否境界は変更していない。

## 次に進める実作業

template生成から明示保存までを自動連鎖させず、人が各段階を明示実行する最小レビュー手順を回帰として固定してください。新しい保存機能やschemaは追加せず、既存CLIを組み合わせたend-to-end fixtureテストと、コピー可能なREADME手順を主成果物にします。

必須境界:

- `tests/test_personal_score_db_cli_save.py` または専用の小さなCLI workflowテストで、空template生成、fixture相当の人手編集、validation receipt生成、明示DB保存を順に実行する。
- 未編集templateはvalidationで `unresolved` のまま、receiptが生成されても保存へ自動遷移しないことを固定する。
- 編集済みready入力はvalidation receiptで `ready` を記録できるが、receipt生成だけではDBファイルや `logs/` を作らないことを固定する。
- 実保存は `--personal-score-db-save-input` と `--personal-score-db-save-database` を別途明示したときだけ行い、新規正式DBにsource/play/analysis各1件を記録することを固定する。
- save CLIはreceiptを入力や承認証明として要求・消費しない。正式入力JSONだけをstrict loadし、receipt pathや内容から正式値を補完しない。
- invalid/unresolved validation receiptから保存を自動実行しない。既存の終了コードと副作用境界を維持する。
- READMEへtemplate生成、ローカルでの人手編集、validation receipt確認、明示保存、DB diagnostic確認の順をコピー可能なコマンドで記載する。
- READMEでは各段階が独立した明示操作であり、receiptは承認や保存成功の証明ではないことを明記する。
- 設計docsとDB保存境界Skillは、既存契約で不足する境界が見つかった場合だけ最小更新する。新しいSkill/Subagentは作らない。

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

- template、validation、receipt、save、diagnosticの1コマンド自動連鎖
- receiptを承認証明、署名、認可token、保存可否判定として扱う変更
- receiptへの正式値、候補材料、template本文、input hash、DB schema/row、duplicate照会結果の記録
- receiptの `logs/` 出力、append/JSONL化、署名、承認者情報、履歴管理
- save CLIがreceiptを必須入力として検証・消費する変更
- templateへの候補値、preview値、metadata値、相対時刻の自動転記
- 正式値、duplicate key、ID、時刻、rank、clear typeの自動生成・補完
- input/validation/template/result schema version 2、既定入力/出力/DB path
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

- CLI save/template/validation/receipt: 54 passed
- file save: 15 passed
- adapter: 7 passed
- formal writer: 10 passed
- schema/diagnostic: 55 passed
- M7/M8回帰: 71 passed、44 deselected
- 全テスト: 347 passed
- Ruff: passed
- compileall: passed
- Skill validator: `Skill is valid!`
- `python -m tools.vision_poc`: 221/221 correct、accuracy 1.000、false positive 0、false negative 0、transition countup shape candidates 3
- `git diff --check`: passed
- 実行不能な検証: なし

pytest実行時に `pytest_chalice` から `pkg_resources` deprecated warningが出るが、テスト失敗ではない。既知の機能リスクは、validation/receiptがDBを開かないためDB互換性とDB内duplicateを判定しないこと、並行writer間でduplicate preflight後・insert前に同じkeyが書かれた場合は2件目がUNIQUE拒否・transaction rollbackになること。receiptは入力内容hashを持たないため、receipt生成後に入力JSONが変更されていないことも証明しない。これらはreceiptの保証外としてREADMEと設計docsに明記済みである。

## コミット/Push方針

- 今回作業分だけをパス単位でステージする。
- `docs/next-task.md` は引き継ぎ仕様として同じコミットへ含める。
- 画像、`metadata.csv`、`data/`、`logs/`、ローカルDB、実入力JSON、生成物をステージしない。
- コード、テスト、README、必要な設計docs/Skillの関連契約を同じコミットへ含める。
- staged diffを確認してからコミットし、通常pushする。force-pushしない。

## 完了条件

- templateから明示保存までの人手主導シーケンスを、既存CLIだけで再現するfixtureテストが通る。
- 未編集templateとinvalid/unresolved receiptが保存へ自動遷移しない。
- ready receipt生成だけではDBや `logs/` が作成・変更されない。
- 明示save pairを別途実行した場合だけ正式DBへsource/play/analysisが記録される。
- save CLIがreceiptから正式値を補完せず、receiptを承認証明として扱わない。
- READMEのコピー可能な手順とコード/テストの副作用境界が一致する。
- 既存template、validation/receipt status、duplicate preflight、DB拒否、transaction rollback、CLI save結果の契約を壊していない。
- 検証が通り、Git管理外ファイルをコミットしていない。
