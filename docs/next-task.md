# 次チャット用タスク

`C:\work\ddrgp_scorelog` で作業してください。`AGENTS.md` とDB保存境界Skillに従い、実装・検証後にこのファイルを更新してください。画像、`metadata.csv`、`data/`、`logs/`、実入力JSON、ローカルDBはGit管理しません。

## 推奨モデル

GPT-5.6 Sol

モデルはCodexのモデルピッカーで手動選択します。この記載だけでは自動切替されません。validation結果の記録は正式値の非再掲、出力副作用、既存validation契約を同時に扱うため、品質優先でSolを推奨します。

## 推論レベル

high

## 作業ブランチ

今回の完了ブランチ:

```powershell
codex/m8-personal-score-db-review-template-cli
```

merge済みなら最新 `main` から次のブランチを作成してください。未mergeなら、このブランチの先端を取り込んでから作成してください。

```powershell
codex/m8-personal-score-db-validation-receipt
```

開始時に確認:

```powershell
git status --short --branch
git log --oneline -5
git fetch --all --prune
```

## 今回までの結果と固定した判断

- `--personal-score-db-save-input-template <path>` は `data/` 配下の新規 `.json` だけへ、schema version 1の空review templateを1件生成する。
- templateはUTF-8 BOMなし、LF、固定key順、末尾改行付きで、現行strict loaderの全必須top-level keyと全 `formal_play` keyを持つ。
- `candidate_material={}`、正式文字列は空文字、正式整数と任意値はnull、`exclusion=null` とし、未編集templateはadapter/validationで `unresolved` になる。
- template生成はmetadata、M5/M7a、M8 preview、manifest、画像、DBを読まず、候補値、正式ID、正式数字、時刻、duplicate keyを生成・補完・転記しない。
- templateの成功結果は生成path、`template_schema_version=1`、status、理由だけを返し、template本文や正式値を標準出力へ再掲しない。
- `.json` 以外、`data/` 外、既存ファイル、validation/save/diagnostic/通常PoC/M8 previewを含む他option混在は出力副作用前に終了コード2で拒否する。
- template生成はレビュー完了、保存可能、DB互換性、既存DB内duplicate非衝突、並行writer安全性、実保存成功を意味しない。
- `--personal-score-db-save-input-validate` のready/excluded/unresolved/invalid、終了コード0/0/1/2、strict loaderとadapter各1回、DB非参照の契約は変更していない。
- 正式DB保存、duplicate preflight、collision時のsource/analysis記録、transaction rollback、DB拒否境界は変更していない。

## 次に進める実作業

人手レビューの証跡をローカルに残せるよう、保存前validation結果の明示的なreceipt出力を追加してください。receiptは標準出力と同じ機械可読なvalidation投影だけを保持し、正式値や候補材料を含めません。入力JSONやDBの既定path、自動validation、自動保存には進めません。

必須境界:

- `--personal-score-db-save-input-validate-output <path>` のような明示optionを、`--personal-score-db-save-input-validate <path>` と必須ペアで追加する。
- receipt出力先は明示された `data/` 配下の新規 `.json` に限定し、既存ファイルを上書きしない。`logs/`、DB、画像、diagnostic outputは作成しない。
- receipt本文は標準出力と同じ `validation_result_schema_version=1`、入力path、`adapter_status`、`save_input_constructed`、`reasons` だけに限定する。正式値、候補材料、template本文、DB情報を追加しない。
- ready/excluded/unresolved/invalidの終了コード0/0/1/2を維持する。receiptの有無でadapter statusや終了コードを変えない。
- 出力pathとoption排他は入力読込や出力作成より先に検査する。validation/output pair以外のsave、template、diagnostic、通常PoC、M8 preview optionとの混在を拒否する。
- UTF-8 BOMなし、LF、固定key順、末尾改行で新規作成し、既存ファイル競合を安全に拒否する。
- receipt出力を明示しない従来validationは、引き続きDB、`data/`、`logs/`、diagnostic outputを作成・変更しない。
- receiptはレビュー記録であって、レビュー承認、DB互換性、DB内duplicate非衝突、並行writer安全性、実保存成功を保証しない。
- ready、unresolved、invalid input schema、既存ファイル、`data/` 外、拡張子不正、option排他、従来の副作用なしvalidationをfixtureでテストする。
- README、設計docs、DB保存境界Skillへreceiptの責務境界と読み方を同期する。

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

- receiptへの正式値、候補材料、template本文、DB schema/row、duplicate照会結果の記録
- receiptの `logs/` 出力、append/JSONL化、署名、承認者情報、履歴管理
- templateへの候補値、preview値、metadata値、相対時刻の自動転記
- 正式値、duplicate key、ID、時刻、rank、clear typeの自動生成・補完
- template生成からのvalidation、DB検査、duplicate照会、DB保存の自動連鎖
- input/validation/template result schema version 2、既定入力/出力/DB path
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

- CLI save/template/validation: 45 passed
- file save: 15 passed
- adapter: 7 passed
- formal writer: 10 passed
- schema/diagnostic: 55 passed
- M7/M8回帰: 71 passed、44 deselected
- 全テスト: 338 passed
- Ruff: passed
- compileall: passed
- Skill validator: `Skill is valid!`
- `python -m tools.vision_poc`: 221/221 correct、accuracy 1.000、false positive 0、false negative 0、transition countup shape candidates 3
- `git diff --check`: passed
- 実行不能な検証: なし

pytest実行時に `pytest_chalice` から `pkg_resources` deprecated warningが出るが、テスト失敗ではない。既知の機能リスクは、validationがDBを開かないためDB互換性とDB内duplicateを判定しないこと、並行writer間でduplicate preflight後・insert前に同じkeyが書かれた場合は2件目がUNIQUE拒否・transaction rollbackになること。これらはvalidation/templateの保証外としてREADMEと設計docsに明記済み。

## コミット/Push方針

- 今回作業分だけをパス単位でステージする。
- `docs/next-task.md` は引き継ぎ仕様として同じコミットへ含める。
- 画像、`metadata.csv`、`data/`、`logs/`、ローカルDB、実入力JSON、生成物をステージしない。
- コード、テスト、README、設計docs、Skillの関連契約を同じコミットへ含める。
- staged diffを確認してからコミットし、通常pushする。force-pushしない。

## 完了条件

- 明示validation/output pairから、新規receipt JSONを1件だけ生成できる。
- receiptは標準出力と同じvalidation投影だけを持ち、正式値、候補材料、template本文、DB情報を含まない。
- ready/excluded/unresolved/invalidと終了コード0/0/1/2、strict loader/adapter契約を維持する。
- receipt出力は `data/` 配下の新規JSONだけで、既存ファイル、DB、`logs/`、画像、diagnostic outputを変更しない。
- receiptを明示しない従来validationは出力副作用なしを維持する。
- option pair、path、拡張子、既存ファイル、他mode排他を副作用前に検査する。
- receiptをレビュー承認、保存可能、DB互換、duplicate非衝突、実保存成功と誤認させない。
- 既存template、validation、duplicate preflight、DB拒否、transaction rollback、CLI save結果の契約を壊していない。
- 関連README、設計docs、DB保存境界Skillを同期している。
- 検証が通り、Git管理外ファイルをコミットしていない。
