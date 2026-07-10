# 次チャット用タスク

`C:\work\ddrgp_scorelog` で作業してください。必ず `AGENTS.md` のプロジェクトルールに従ってください。このファイルは次チャット用の引き継ぎ仕様です。作業では `docs/next-task.md` の更新だけで完了扱いにせず、コード、テスト、CLI、README、workflow、または設計docsなど、実行可能な成果物変更を1つ以上進めてください。`docs/next-task.md` は実装・検証が終わった後の引き継ぎ更新として扱ってください。

## 推論レベル

high

## 作業ブランチ

今回の作業ブランチは以下です。

```powershell
codex/m8-personal-score-db-log-boundary
```

このブランチがmerge済みなら、次チャットでは最新 `main` から次フェーズ用の新ブランチを作ってください。未mergeなら、このブランチの先端を取り込んでから続けてください。

次の推奨ブランチ:

```powershell
codex/m8-personal-score-db-log-schema
```

開始時に以下を確認してください。

- `git status --short --branch`
- `git log --oneline -5`
- `git fetch --all --prune`
- `main` または継続元ブランチが最新であること。
- `metadata.csv`、`data/`、`logs/`、ローカル素材、ローカルDBがコミット対象に入っていないこと。

## 今回までの作業結果

M8 previewは完了済みとして扱います。正式個人スコアDB本体では、schema contract、互換チェック、空DB初期化境界、connection/file path単位のwrite前準備境界、diagnostic dict / Markdown投影、CLI標準出力入口、`data/` 配下へのdiagnostic file output、`logs/` 配下へのdiagnostic JSONL log output、diagnostic log schema検査まで進んでいます。本番insert、既定自動保存、既存DB migration実行、低信頼度ログ本番保存、source capture保存にはまだ進んでいません。

今回追加・更新したもの:

- `--personal-score-db-diagnostic-log-output <path>`
- log output先を `logs/` 配下に限定する境界。
- log output形式をJSONLに固定し、拡張子は `.jsonl` のみ許可。
- diagnostic log outputはappend方式。CLI実行1回につき1行だけ追加する。
- log recordは `log_schema_version=1`、`event_type=personal_score_db_diagnostic`、mode、format、exit code相当status、対象DB path、任意の diagnostic output path、diagnostic dictを持つ。
- log recordの必須key、schema version、event type、mode、format、exit code、status、`diagnostic.is_compatible` との整合をappend前に検査する。
- JSONL appendは空行なし、1行1JSONとして読めることをテストで固定した。
- `--personal-score-db-diagnostic-output` と log outputを同時指定した場合、`data/` 側には標準出力と同じ診断テキストを保存し、`logs/` 側には diagnostic dict と file output path参照を保存する。
- compatible、空DB初期化、M8 preview拒否、unknown拒否、manual migration required、非SQLiteファイル、ディレクトリ拒否の代表ケースで、標準出力、`data/` file output、`logs/` log outputの境界をCLI経由テストで固定した。
- JSON file outputとlog outputの併用では、標準出力JSON、`data/` ファイルJSON、log record内diagnostic dictが同じ診断内容になることをテストした。
- `logs/` 外指定や `.jsonl` 以外のlog outputは、`prepare-write` のDB作成・初期化より前に拒否する。
- `tools/vision_poc/README.md`、`docs/design/05_storage_io_spec.md`、`docs/design/06_regression_guard.md`、`docs/design/10_personal_score_db_schema.md`、`docs/implementation-roadmap.md` にdiagnostic log output境界を反映した。

固定済みの主なCLI境界:

- `inspect` mode は存在しないpathや非SQLiteファイルを正式DBとして作成・変更しない。
- `prepare-write` mode は空DBだけ初期化し、M8 preview DB、unknown DB、metadata identity mismatch、manual migration候補、非SQLiteファイル、ディレクトリを自動修復しない。
- diagnostic outputは診断テキストの保存だけで、本番insert、自動migration、既定自動保存、低信頼度ログ本番保存には進まない。
- diagnostic output先はDB pathとは独立に指定し、`data/` 配下だけを許可する。
- diagnostic log outputは診断ログのappendだけで、本番insert、自動migration、既定自動保存、低信頼度ログ本番保存、source capture保存には進まない。
- diagnostic log output先は `logs/` 配下だけを許可し、`.jsonl` に限定する。
- `manual_migration_required` は backup方針と明示確認を決めるまで欠落table作成や `user_version` 修正をしない。
- M8 preview最小 `plays` と正式個人スコアDB `plays` は別物。

まだ進めていないこと:

- diagnostic log outputと将来の低信頼度ログ本番仕様の詳細な読み分け。
- 正式個人スコアDBへの本番insert。
- 既定自動保存。
- duplicate key本格実装。
- 低信頼度ログ本番保存。
- 既存DBの実migration実行。
- source capture参照の本格保存境界。

## 次に必ず進める実作業

次はdiagnostic logと将来の解析ログ/低信頼度ログ/source capture参照の分離を進めてください。本番insertはまだ実装しないでください。

第一候補:

- source capture参照の本格保存へ入る前に、正式DB schema上の `source_captures` と `analysis_logs.log_path` / JSONL diagnostic logの責務分担を `docs/design/10_personal_score_db_schema.md` または `docs/design/04_data_model.md` に固定する。
- diagnostic log outputはDB診断ログとして完了扱いにし、低信頼度ログ本番保存やsource capture保存とは別物として読むことを明確化する。
- ただしDB insert、source capture実保存、失敗画像保存、低信頼度ログ本番保存にはまだ進まない。

第二候補:

- 正式DB insertへ進む直前の入力契約として、M8 planned records / save payload preview / diagnostic result のどこから正式保存処理へ渡すかをdocs/testsで固定する。
- ただし実insertや既定自動保存はまだ実装しない。

このフェーズで決めたい候補:

- `diagnostic_output_path` は未指定時の空文字を維持するか、次フェーズでnull相当へ変える必要があるか。
- prepare-writeで新規DBを初期化した場合、log側では最終diagnostic内の `file_preparation` で十分か、初期/最終statusをトップレベルにも出すか。
- 将来の低信頼度ログ本番仕様ではJSONL diagnostic logと同じファイルへ混在させない方針にするか、`event_type` で混在可能にするか。

## 必読資料

- `AGENTS.md`
- `docs/next-task.md`
- `docs/implementation-roadmap.md`
- `docs/design/03_event_and_save_boundary.md`
- `docs/design/04_data_model.md`
- `docs/design/05_storage_io_spec.md`
- `docs/design/06_regression_guard.md`
- `docs/design/10_personal_score_db_schema.md`
- `tools/vision_poc/README.md`
- `tools/vision_poc/runner.py`
- `tools/vision_poc/personal_score_db_schema.py`
- `tests/test_personal_score_db_schema.py`
- `tests/test_vision_poc_ocr.py`
- `tests/test_vision_poc_result_events.py`

## スコープ外

- スクリーンショット画像、`samples/screenshots/metadata.csv`、`data/`、`logs/`、ローカル素材、ローカルDBのGit管理
- `samples/screenshots/cropped/` と `samples/screenshots/organized/` 配下のローカル追加画像コミット
- `samples/screenshots/organized/digit_templates/` などのM7aテンプレート画像コミット
- 本番キャプチャAPI、実キャプチャデバイス依存コード、常駐監視ループ、非同期処理
- Windows常駐アプリUI
- 正式個人スコアDBへの実insert
- 既定自動保存、常時保存処理、本番用の自動DB insert
- 低信頼度ログ本番保存の実装
- source captureの実保存、失敗画像保存
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

- `python -m pytest tests\test_personal_score_db_schema.py`: 54 passed
- `python -m pytest tests\test_vision_poc_ocr.py -k "m8"`: 20 passed
- `python -m pytest tests\test_vision_poc_ocr.py -k "m7_save_decision or m7_save_readiness or m7a or m8"`: 71 passed
- `python -m ruff check tools\vision_poc pyproject.toml tests`: passed
- `python -m compileall master tools\vision_poc`: passed
- `python -m pytest tests`: 260 passed
- `git diff --check`: passed

正式DB diagnostic CLIや出力境界を触った場合は、`tests\test_personal_score_db_schema.py` を明示的に実行してください。`docs/next-task.md` 更新後に `git diff --check` も実行してください。

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

- 正式個人スコアDBのdiagnostic表示、file output境界、diagnostic log境界、解析ログ境界、source capture参照境界、または次のオープン境界が1つ以上進んでいる。
- `docs/design/`、コード、テスト、READMEのいずれかに、次の実装へつながる成果物変更がある。
- 正式個人スコアDB保存の実insertにはまだ踏み込んでいない。
- 既存DBの自動migrationを実行していない。
- 空DB以外を正式schema初期化として自動変更していない。
- M8 preview最小 `plays` と正式個人スコアDBスキーマを混同していない。
- M8 preview DBを正式個人スコアDBとして受け入れていない。
- unknown DBやmetadata identity mismatchを正式DBとして受け入れていない。
- `manual_migration_required` をbackup/明示確認なしで自動変更していない。
- diagnostic output / diagnostic log output を本番insert、低信頼度ログ本番保存、source capture保存として扱っていない。
- `identity_signal_*`、`m5_identity_reviewable`、`blocked_identity_signal` が曲ID/譜面ID確定として扱われていない。
- M7aの `recognized_digits`、`expected_value`、`match` が保存値確定として扱われていない。
- duplicate、`rejected_transition`、未確定候補、non-result が上流対象外のまま。
- 生成DB、テンプレート素材、PoC出力、`metadata.csv` 実体や画像をコミットしていない。
- 検証コマンドが通っている。
- コミット/Pushする場合は、Git管理対象外ファイルを含めていない。
