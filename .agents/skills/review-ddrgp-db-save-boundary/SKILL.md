---
name: review-ddrgp-db-save-boundary
description: DDRGP scorelogの保存可否、正式個人スコアDB、duplicate、diagnostic log、analysis log、source captureの責務境界をレビューする。M7/M8の保存判定、正式DB schemaや保存入力、DB診断、低信頼度ログ、source_captures、plays、analysis_logsを変更・レビューするときに使う。OCR精度調整やROI変更だけで保存境界に影響しない作業には使わない。
---

# Review DDRGP DB Save Boundary

保存候補の観測材料、正式保存値、DB診断、本番解析ログ、元フレーム参照を混同していないか確認する。現在のフェーズ制約を守りながら、コード、テスト、設計docsの契約を同期させる。

## Read First

- `AGENTS.md`
- `docs/next-task.md`
- `docs/design/03_event_and_save_boundary.md`
- `docs/design/04_data_model.md`
- `docs/design/05_storage_io_spec.md`
- `docs/design/06_regression_guard.md`
- `docs/design/10_personal_score_db_schema.md`

変更対象に応じて `tools/vision_poc/README.md`、`tools/vision_poc/personal_score_db_schema.py`、`tools/vision_poc/personal_score_db_save.py`、`tools/vision_poc/personal_score_db_save_adapter.py`、`tools/vision_poc/runner.py`、関連テストも読む。

## Workflow

1. `docs/next-task.md` の現在フェーズとスコープ外を確認する。明示されていない本番insert、自動migration、既定自動保存へ進めない。
2. 変更対象を次の責務へ分類する: イベント確定、候補材料、正式保存入力、`plays`、`source_captures`、`analysis_logs`、DB diagnostic output/log。
3. 入力から出力までの値の由来を追い、候補値を正式保存値へ暗黙昇格させていないか確認する。
4. 変更前に、影響する不変条件を既存テストで特定する。未固定なら最小の回帰テストを追加する。
5. コード変更は現在フェーズに必要な最小境界へ留め、関連する `docs/design/` と `tools/vision_poc/README.md` を同じ作業で同期する。
6. 対象別検証と全体検証を実行し、生成DB、画像、`data/`、`logs/`、`metadata.csv` をGit対象へ入れない。

## Boundary Checklist

- 保存直前対象は `confirmed_result=true` かつ `duplicate=false` を維持する。
- M5 `identity_signal_*`、`m5_identity_reviewable`、M7a `recognized_digits`、`expected_value`、`match` は候補材料であり、曲ID、譜面ID、正式保存値ではない。
- 正式保存入力はM8 preview payload/rowを直接受け取らず、未解決の時刻、rank、clear type、master version、duplicate keyを暗黙補完しない。
- previewから正式入力へのadapterは候補材料と明示的な正式値を分け、`unresolved` から保存入力を返さず、`excluded` からplayを作らない。
- 明示ファイル保存はadapterをDB準備より先に評価し、`unresolved` でDBファイルや親ディレクトリを作成・変更しない。
- 明示ファイル保存は新規/0 byte/compatible正式DBだけを受け入れ、preview、unknown、metadata identity mismatch、`manual_migration_required`、非SQLite、ディレクトリを自動修復しない。
- 明示CLI保存は入力JSON pathと正式DB pathの必須ペアだけで動かし、JSON loaderで必須/未知key、nested object/null、bool/intを含む型をadapter前に検査する。
- 明示CLI保存は候補材料や相対時刻を `formal_play` へ暗黙コピーせず、`unresolved` と不正JSONでDBファイルや親ディレクトリを作成・変更しない。
- PoCのscore/file由来 `duplicate_key` を正式duplicate keyとして扱わない。
- DB diagnostic output/logはDB検査の記録であり、本番insert、低信頼度ログ、source capture保存ではない。
- `source_captures` は元フレーム参照を持ち、解析ログ本文やDB診断ログを持たない。
- `analysis_logs` は保存判断と再調査の入口を持ち、`plays` の正式保存値を二重管理しない。
- `analysis_logs.log_path` とDB diagnostic JSONLを同一責務にしない。
- M8 preview DBと正式個人スコアDBを相互に受け入れない。
- unknown DB、metadata identity mismatch、`manual_migration_required` を自動修復しない。
- 保存不可、低信頼度、重複、例外を、成功したplay rowへ丸めない。

## Implementation Guidance

- insert前フェーズでは、pure function、schema helper、diagnostic projectionなど、副作用のない契約を優先する。
- insertが将来明示的にスコープへ入った場合も、トランザクション境界、duplicate、source capture参照、analysis log、失敗時の原子性を別々にテストする。
- schemaや保存境界の語彙を変えたら、コード定数または型、schemaテスト、設計docs、READMEの読み方を揃える。
- レビュー依頼では、所感より先に重大度順の指摘とファイル/行参照を出す。問題がなければ、残るテスト不足や将来フェーズのリスクを明記する。

## Validation

変更対象に合わせて絞り込みを先に実行し、完了前に全体検証を実行する。

```powershell
python -m pytest tests\test_personal_score_db_save.py
python -m pytest tests\test_personal_score_db_save_adapter.py
python -m pytest tests\test_personal_score_db_file_save.py
python -m pytest tests\test_personal_score_db_schema.py
python -m pytest tests\test_vision_poc_ocr.py -k "m7_save_decision or m7_save_readiness or m7a or m8"
python -m ruff check tools\vision_poc pyproject.toml tests
python -m compileall master tools\vision_poc
python -m pytest tests
git diff --check
```

正式DB schema、diagnostic CLI、source capture、analysis logを触った場合は `tests\test_personal_score_db_schema.py` を省略しない。

## Report

- 変更した責務境界
- 維持した不変条件
- 実行した検証と結果
- 未解決事項と次フェーズへ送る項目
