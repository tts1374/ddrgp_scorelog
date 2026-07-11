# 次チャット用タスク

`C:\work\ddrgp_scorelog` で作業してください。`AGENTS.md` とDB保存境界Skillに従い、実装・検証後にこのファイルを更新してください。画像、`metadata.csv`、`data/`、`logs/`、ローカルDBはGit管理しません。

## 推奨モデル

GPT-5.6 Sol

モデルはCodexのモデルピッカーで手動選択します。この記載だけでは自動切替されません。既存契約に沿う小規模な入口・テスト追加だけならTerraでも構いません。

## 推論レベル

high

## 作業ブランチ

今回の完了ブランチ:

```powershell
codex/m8-personal-score-db-save-input-adapter
```

merge済みなら最新 `main` から次のブランチを作成してください。未mergeなら、このブランチの先端を取り込んでから作成してください。

```powershell
codex/m8-personal-score-db-explicit-file-save
```

開始時に確認:

```powershell
git status --short --branch
git log --oneline -5
git fetch --all --prune
```

## 今回までの結果

- `tools/vision_poc/personal_score_db_save_adapter.py` にpreview候補材料とレビュー済み正式値を分離するpure adapterを追加した。
- adapter結果を `ready` / `unresolved` / `excluded` に固定した。
- `ready` は全正式値を明示した場合だけplayつき `PersonalScoreDbSaveInput` を返す。
- `unresolved` は不足・不正理由を返し、正式保存入力を返さない。
- duplicate、明示された低信頼度/error/skipは `excluded` とし、`play=None` のsource capture + analysis入力だけを返す。
- `candidate_material` の `identity_signal_*`、M7a `recognized_digits`、`played_at_ms` / `timestamp_ms` は正式ID・数字・実時刻へコピーしない。
- rank、clear type、master version、正式duplicate keyを既定値で補完しない。
- adapterはrunner/CLIやDBファイル保存へ未接続で、副作用を持たない。
- README、設計docs、ロードマップ、DB保存境界Skillをadapter契約へ同期した。

固定した判断:

- preview候補材料と正式play値は別入力型のまま維持する。
- `analysis_confidence` の低信頼度しきい値をadapter内で新規決定しない。上流で低信頼度と判断した場合に明示的なexclusionを渡す。
- duplicateは正式 `plays` rowへ変換しない。
- adapterの `unresolved` はwriterへ渡せない。
- `played_at` / `captured_at` はtimezone付きISO 8601、正式writerの `source_kind` は `manifest` / `timestamped` / `capture` / `manual` に限定する。

## 次に進める実作業

明示指定された新規またはcompatibleな正式個人スコアDBファイルへ、adapter結果と既存transaction writerを使って1件保存する薄いPython API入口を追加してください。既定自動保存や通常PoC実行への接続は行いません。

入口の必須境界:

- 呼び出し元がDB pathと `PersonalScoreDbSaveAdapterInput` を明示する。
- adapter結果が `ready` または `excluded` の場合だけファイル準備とwriterへ進む。
- `unresolved` はDBファイル作成・変更より前に拒否し、理由を呼び出し元へ返す。
- 新規ファイルまたは0 byte空ファイルは正式初期schemaへ進める。
- 既存compatible正式DBは同じwriterで追記できる。
- M8 preview DB、unknown DB、metadata identity mismatch、`manual_migration_required`、非SQLite、ディレクトリを拒否し、自動修復しない。
- 保存成功はsource/play/analysis、duplicate/低信頼度/skipはsource/analysisだけを1 transactionで記録する。
- writer失敗時のtransaction rollback契約を維持する。
- preview DBの `--m8-score-db-output` と正式DB pathを混同しない。

API結果には最低限、adapter status、保存されたか、play IDの有無、source capture ID、analysis ID、DB pathを含めることを検討してください。失敗時にDB診断をファイルへ自動出力したり、低信頼度ログ本番保存へ広げたりはしません。

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
- `tools/vision_poc/personal_score_db_save_adapter.py`
- `tools/vision_poc/personal_score_db_save.py`
- `tools/vision_poc/personal_score_db_schema.py`
- `tools/vision_poc/runner.py`
- `tests/test_personal_score_db_save_adapter.py`
- `tests/test_personal_score_db_save.py`
- `tests/test_personal_score_db_schema.py`
- `tests/test_vision_poc_ocr.py`

## スコープ外

- 実キャプチャAPI、常駐監視、非同期処理、Windows UI
- 既定自動保存、常時保存、通常の `python -m tools.vision_poc` への正式保存接続
- 既存DBの自動migration、backup/migration実行
- `manual_migration_required` DBの自動変更
- duplicate key生成方式の本格実装
- 低信頼度ログファイルと失敗画像の本番保存
- M5/M7a候補を確定ID/値へ暗黙昇格する処理
- OCR confidenceから保存しきい値を新規決定する処理
- ROI座標の大変更、OCR方式全面刷新
- 画像、`metadata.csv`、`data/`、`logs/`、ローカルDB、ローカルテンプレートのコミット
- 追加のプロジェクト専用Skill/Subagent作成

## 検証コマンド

```powershell
python -m pytest tests\test_personal_score_db_save_adapter.py
python -m pytest tests\test_personal_score_db_save.py
python -m pytest tests\test_personal_score_db_schema.py
python -m pytest tests\test_vision_poc_ocr.py -k "m7_save_decision or m7_save_readiness or m7a or m8"
python -m ruff check tools\vision_poc pyproject.toml tests
python -m compileall master tools\vision_poc
python -m pytest tests
python -X utf8 "$env:USERPROFILE\.codex\skills\.system\skill-creator\scripts\quick_validate.py" ".agents\skills\review-ddrgp-db-save-boundary"
git diff --check
```

今回の結果:

- adapter/formal save/schema対象: 70 passed
- M7/M8回帰: 71 passed、44 deselected
- 全テスト: 276 passed
- Ruff: passed
- compileall: passed
- Skill validator: `Skill is valid!`
- `python -m tools.vision_poc`: 221/221 correct、accuracy 1.000
- `git diff --check`: passed

## コミット/Push方針

- 今回作業分だけをステージする。
- `docs/next-task.md` は引き継ぎ仕様として含める。
- 画像、`metadata.csv`、`data/`、`logs/`、ローカルDB、生成物をステージしない。
- コード、テスト、README、docs、Skillを変更した場合は関連する契約を同じコミットへ含める。
- コミットしたら作業ブランチをpushする。

## 完了条件

- 明示DB pathとadapter入力から、正式DBファイルへ1件保存する薄いAPI入口がある。
- `ready` はsource/play/analysisを保存できる。
- `excluded` はsource/analysisだけを保存し、playsを作らない。
- `unresolved` はDBファイル作成・変更前に止まる。
- 新規/0 byte/compatible DBの成功テストと、preview/unknown/manual migration/non-SQLite/directory拒否テストがある。
- transaction rollback、preview非昇格、正式DB識別の既存契約を維持している。
- 既定自動保存、通常runner接続、既存DB migrationを開始していない。
- 関連docs、README、Skillを同期している。
- 検証が通り、Git管理外ファイルをコミットしていない。
