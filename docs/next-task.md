# 次チャット用タスク

`C:\work\ddrgp_scorelog` で作業してください。`AGENTS.md` とDB保存境界Skillに従い、実装・検証後にこのファイルを更新してください。画像、`metadata.csv`、`data/`、`logs/`、ローカルDBはGit管理しません。

## 推奨モデル

GPT-5.6 Sol

モデルはCodexのモデルピッカーで手動選択します。この記載だけでは自動切替されません。小規模なテスト/docs修正だけならTerraでも構いません。

## 推論レベル

high

## 作業ブランチ

今回の完了ブランチ:

```powershell
codex/m8-personal-score-db-save-input-contract
```

merge済みなら最新 `main` から次のブランチを作成してください。未mergeなら、このブランチの先端を取り込んでから作成してください。

```powershell
codex/m8-personal-score-db-save-input-adapter
```

開始時に確認:

```powershell
git status --short --branch
git log --oneline -5
git fetch --all --prune
```

## 今回までの結果

- `tools/vision_poc/personal_score_db_save.py` に正式保存入力dataclass、pure validation、connection単位のtransaction writerを追加した。
- 正常保存は `source_captures`、`plays`、`analysis_logs` を1 transactionでinsertする。
- duplicate、低信頼度、error、その他skipは `play=None` とし、source captureとanalysisだけを記録する。
- timezoneなし時刻、空のmaster version/rank/clear type、PoC形式のduplicate key、参照不整合をDB準備前に拒否する。
- play insert失敗時に、同じ呼び出しのsource captureとanalysisもrollbackする。
- M8 preview payload/rowは正式入力へ直接変換しない。M5/M7aの値は引き続き候補材料である。
- `docs/implementation-roadmap.md` の現在地と直近MVPをM8正式writerまで更新した。
- DB保存境界Skillへ正式writerテストとpreview非昇格チェックを追加し、実タスクで初回運用した。

固定した判断:

- `played_at` / `captured_at` はtimezone付きISO 8601を必須にする。
- `rank` / `clear_type` 未取得時は空文字で保存せず、保存成功へ進めない。
- 正式writerの `source_kind` は `manifest` / `timestamped` / `capture` / `manual` に限定し、`unknown` を拒否する。
- PoCの `score:` / `file:` duplicate keyを正式保存に使わない。
- DB diagnostic JSONL、本番analysis log、source capture pathの責務を分ける。

## 次に進める実作業

M7/M8 preview材料と明示的な正式値から、`PersonalScoreDbSaveInput` を組み立てられるか判定するpure adapterを追加してください。実ファイルへの既定保存はまだ行いません。

adapterで明示する入力:

- M8 payload/planned recordの候補材料
- 実時刻として確定した `played_at` / `captured_at`
- `master_version`
- 確定済み `rank` / `clear_type`
- `capture_id` / `capture_hash` / source reference
- 正式 `duplicate_key`
- `analysis_confidence`
- `app_version`

adapterの出力候補:

- `ready`: 正式 `PersonalScoreDbSaveInput` を返せる。
- `unresolved`: 正式入力は返さず、不足/未確定理由を列挙する。
- `excluded`: duplicate、低信頼度、その他skipとして `play=None` のanalysis入力を返せる。

必須ガード:

- `played_at_ms=0` や相対 `timestamp_ms` を実時刻へ暗黙変換しない。
- `identity_signal_*` を確定song/chart IDへ暗黙昇格しない。
- M7a `recognized_digits` をレビューなしで正式数字へ暗黙昇格しない。
- rank、clear type、master version、正式duplicate keyを既定値で埋めない。
- unresolvedを保存成功入力へ変換しない。
- duplicate/低信頼度を `plays` rowへ変換しない。

adapter契約がテストで固定できた場合だけ、第二候補として明示指定の新規/compatible正式DBファイルへ同じwriterで保存する薄い入口を検討してください。既定自動保存や既存DB migrationには進みません。

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
- `tools/vision_poc/personal_score_db_save.py`
- `tools/vision_poc/personal_score_db_schema.py`
- `tools/vision_poc/runner.py`
- `tests/test_personal_score_db_save.py`
- `tests/test_personal_score_db_schema.py`
- `tests/test_vision_poc_ocr.py`

## スコープ外

- 実キャプチャAPI、常駐監視、非同期処理、Windows UI
- 既定自動保存、常時保存、既存DBの自動migration
- `manual_migration_required` DBの自動変更
- duplicate key生成方式の本格実装
- 低信頼度ログファイルと失敗画像の本番保存
- M5/M7a候補を確定ID/値へ暗黙昇格する処理
- ROI座標の大変更、OCR方式全面刷新
- 画像、`metadata.csv`、`data/`、`logs/`、ローカルDB、ローカルテンプレートのコミット
- 追加のプロジェクト専用Skill/Subagent作成

## 検証コマンド

```powershell
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

- formal save/schema対象: 63 passed
- 全テスト: 269 passed
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

- preview/解析材料から正式入力へ進める条件と、進めない理由をpure adapterで判定できる。
- unresolved値を暗黙補完せず、正式 `PersonalScoreDbSaveInput` を返さない。
- duplicate/低信頼度は `plays` を作らない。
- 正常、unresolved、excludedのテストがある。
- M8 preview DBと正式DBを混同しない。
- 既存DB migrationや既定自動保存を開始していない。
- 関連docs、README、Skillを同期している。
- 検証が通り、Git管理外ファイルをコミットしていない。
