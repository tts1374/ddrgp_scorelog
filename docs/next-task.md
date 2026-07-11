# 次チャット用タスク

`C:\work\ddrgp_scorelog` で作業してください。`AGENTS.md` とDB保存境界Skillに従い、実装・検証後にこのファイルを更新してください。画像、`metadata.csv`、`data/`、`logs/`、ローカルDBはGit管理しません。

## 推奨モデル

GPT-5.6 Sol

モデルはCodexのモデルピッカーで手動選択します。この記載だけでは自動切替されません。正式保存入力の外部形式とCLI副作用順序を扱うため、品質優先でSolを推奨します。

## 推論レベル

high

## 作業ブランチ

今回の完了ブランチ:

```powershell
codex/m8-personal-score-db-explicit-file-save
```

merge済みなら最新 `main` から次のブランチを作成してください。未mergeなら、このブランチの先端を取り込んでから作成してください。

```powershell
codex/m8-personal-score-db-explicit-cli-save
```

開始時に確認:

```powershell
git status --short --branch
git log --oneline -5
git fetch --all --prune
```

## 今回までの結果

- `tools/vision_poc/personal_score_db_file_save.py` に、DB pathと `PersonalScoreDbSaveAdapterInput` を明示して1件保存する薄いPython APIを追加した。
- adapterをDB準備より先に評価し、`unresolved` は理由付き `written=false` としてDBファイルも親ディレクトリも作らない。
- `ready` はsource/play/analysis、duplicate・低信頼度・skipの `excluded` はsource/analysisだけを既存writerの1 transactionで保存する。
- 新規ファイル、0 byte空ファイル、既存compatible正式DBへの保存を固定した。
- M8 preview DB、unknown DB、metadata identity mismatch、`manual_migration_required`、非SQLite、ディレクトリは既存の正式DB識別境界で拒否し、自動修復しない。
- writer途中失敗時は、その呼び出しのsource/play/analysisをrollbackする。
- API結果はDB path、adapter status、理由、write完了有無、source capture ID、analysis ID、任意のplay IDを返す。
- 通常runner/CLI、既定自動保存、既存DB migration、diagnostic自動出力、低信頼度ログファイル保存には接続していない。
- README、設計docs、ロードマップ、DB保存境界Skillを明示ファイル保存契約へ同期した。

固定した判断:

- `written=true` はreadyまたはexcludedのtransaction完了を表し、正式play保存の有無は `play_id` で区別する。
- preview候補材料とレビュー済み正式値は別入力のまま維持する。
- `unresolved` はDB pathの準備より前に止める。
- M8 previewの `--m8-score-db-output` と正式個人スコアDB保存は別入口・別schemaのまま維持する。
- DB拒否時に自動repair、migration、diagnostic file/log出力を行わない。

## 次に進める実作業

明示JSON入力ファイルと明示された正式個人スコアDB pathから、今回の `save_personal_score_db_file()` を1回だけ呼ぶCLI入口を追加してください。通常の `python -m tools.vision_poc` の引数として追加して構いませんが、保存用オプションが明示された場合だけ動作し、既定PoC実行には接続しません。

入口の必須境界:

- CLI呼び出し元が保存先DB pathとUTF-8 JSON入力pathの両方を明示する。片方だけの指定はDB作成・変更前に拒否する。
- JSONは `PersonalScoreDbSaveAdapterInput` の外部入力形式とし、`candidate_material`、source/analysis値、任意の `formal_play`、任意の `exclusion` を構造として分離する。
- JSON loaderは必須key、型、未知key、nested object/null、boolとintの混同を検査し、不正入力をadapterやDB準備へ渡さない。
- JSONのM5 `identity_signal_*`、M7a `recognized_digits`、`played_at_ms` / `timestamp_ms` を `formal_play` へ暗黙コピーしない。
- `ready` / `excluded` だけDBへ書き、`unresolved` はDB作成・変更前に理由を標準出力または標準エラーへ返して非0終了する。
- 成功出力にはadapter status、written、play IDの有無、source capture ID、analysis ID、DB path、理由を機械可読なJSONで含める。
- 新規/0 byte/compatible正式DBだけを許可し、preview/unknown/identity mismatch/manual migration/non-SQLite/directoryを拒否する。
- CLI output先の別ファイル、diagnostic JSONL、低信頼度ログファイルは自動生成しない。
- 入力JSONや正式DBの既定pathを導入しない。通常PoC実行、timestamped/manifest runnerからの自動保存へ接続しない。

CLIオプション名、JSONのtop-level version key、終了コードは実装前に既存parser構造へ合わせて決め、READMEとfixtureで固定してください。JSON fixtureはテスト内またはGit管理可能な小さなfixtureに限定し、実スコア・ローカルpath・ローカルDBをコミットしません。

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
- `tools/vision_poc/personal_score_db_file_save.py`
- `tools/vision_poc/personal_score_db_save_adapter.py`
- `tools/vision_poc/personal_score_db_save.py`
- `tools/vision_poc/personal_score_db_schema.py`
- `tools/vision_poc/runner.py`
- `tools/vision_poc/__main__.py`
- `tools/vision_poc/README.md`
- `tests/test_personal_score_db_file_save.py`
- `tests/test_personal_score_db_save_adapter.py`
- `tests/test_personal_score_db_save.py`
- `tests/test_personal_score_db_schema.py`
- `tests/test_vision_poc_ocr.py`

## スコープ外

- 実キャプチャAPI、常駐監視、非同期処理、Windows UI
- 既定自動保存、常時保存、通常PoC実行からの暗黙保存
- timestamped/manifest/M7/M8 preview行からの自動正式値確定・自動保存
- 既存DBの自動migration、backup/migration実行、自動repair
- `manual_migration_required` DBの自動変更
- duplicate key生成方式の本格実装
- 低信頼度ログファイル、失敗画像、diagnostic output/logの自動保存
- M5/M7a候補を確定ID/値へ暗黙昇格する処理
- OCR confidenceから保存しきい値を新規決定する処理
- ROI座標の大変更、OCR方式全面刷新
- 画像、`metadata.csv`、`data/`、`logs/`、ローカルDB、実入力JSONのコミット
- 追加のプロジェクト専用Skill/Subagent作成

## 検証コマンド

```powershell
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

今回の結果:

- file save/adapter/formal save/schema対象: 84 passed
- M7/M8回帰: 71 passed、44 deselected
- 全テスト: 290 passed
- Ruff: passed
- compileall: passed
- Skill validator: `Skill is valid!`
- `python -m tools.vision_poc`: 221/221 correct、accuracy 1.000
- `git diff --check`: passed

## コミット/Push方針

- 今回作業分だけをステージする。
- `docs/next-task.md` は引き継ぎ仕様として含める。
- 画像、`metadata.csv`、`data/`、`logs/`、ローカルDB、実入力JSON、生成物をステージしない。
- コード、テスト、README、docs、Skillを変更した場合は関連する契約を同じコミットへ含める。
- コミットしたら作業ブランチをpushする。

## 完了条件

- 明示JSON入力pathと明示正式DB pathを必須とする単発CLI入口がある。
- JSON loaderが必須key、型、未知key、nested object/nullを検査し、候補材料を正式値へ暗黙昇格しない。
- `ready` はsource/play/analysis、`excluded` はsource/analysisだけを保存できる。
- `unresolved` と不正JSONはDBファイル作成・変更前に非0終了する。
- 新規/0 byte/compatible DBのCLI成功テストと、preview/unknown/manual migration/non-SQLite/directory拒否テストがある。
- CLI結果を機械可読JSONで確認でき、play保存の有無を `play_id` で区別できる。
- transaction rollback、preview非昇格、正式DB識別の既存契約を維持している。
- 既定自動保存、通常runner接続、既存DB migration、diagnostic自動出力を開始していない。
- 関連docs、README、Skillを同期している。
- 検証が通り、Git管理外ファイルをコミットしていない。
