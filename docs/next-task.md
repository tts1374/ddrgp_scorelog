# 次チャット用タスク

`C:\work\ddrgp_scorelog` で作業してください。必ず `AGENTS.md` のプロジェクトルールに従ってください。`docs/next-task.md` は次チャット用の引き継ぎ仕様として扱い、実装・検証が終わった後に更新してください。`docs/next-task.md` の更新だけで作業完了扱いにしないでください。

## 推論レベル

high

## 作業ブランチ

`codex/m5-master-match-poc`

作業開始時に以下を確認してください。

- `git status --short --branch`
- `git log --oneline -5`
- 現在ブランチが `codex/m5-master-match-poc` であること

## 今回までの作業結果

- M5参照カバレッジの完了整理をdocs/README/testsで固定した。
  - `docs/design/09_master_match_poc.md` に、`jacket_match_summary.json` と `jacket_reference_coverage_summary.json` の読み分けを明記した。
  - `tools/vision_poc/README.md` に、通常coverageと診断coverageの出力名、summaryの読み方、`expected_missing_feature` / `expected_not_in_chart_candidates` / `expected_unresolved` の扱いを追記した。
  - `docs/implementation-roadmap.md` のM5節を、完了判定と次フェーズ切り分けへ更新した。
  - `tests/test_master_match.py` に、`expected_not_in_chart_candidates` と診断coverage出力名の回帰テストを追加した。
- M5は完了扱いにしてよい。
  - `jacket_match_status=matched` はPoC上の一意候補で、保存OKではない。
  - `identity_signal_status=jacket_resolved_candidate` / `composite_resolved_candidate` はM7以降へ渡す候補観測で、曲ID/譜面ID確定ではない。
  - `jacket_reference_coverage_summary.json` は参照素材カバレッジ診断で、照合成功数ではない。
  - `expected_missing_feature` / `expected_not_in_chart_candidates` / `expected_unresolved` はレビュー材料で、保存候補昇格やGP対象外曲復帰には使わない。
  - duplicate / unconfirmed を含む診断出力は保存候補ではない。
- 2026-07-07の最新ローカル検証結果:
  - M4 DB: 1282 songs / 9594 charts / `song_aliases=39` / `source_snapshots=2`
  - Wiki source hash: `d433ba20255663eec865d043ba73ec6fc8dc6c201d56253fb15e8f0de2dc5d50`
  - 公式収録曲一覧 source hash: `ce7e1ecd782df839b44ca8e993648b4025336360c395ada8f71f81026020cb0d`
  - `grand_prix_play_available_song_count=1181`
  - `free_play_available_song_count=64`
  - `official_availability_matched_song_count=1181`
  - `official_availability_match`: `title_artist=1143` / `unique_title=36` / `alias_title_artist=2` / `not_found=101`
- M5 jacket通常候補の最新結果:
  - `jacket_feature_master`: `target_count=69` / `accepted=69`
  - `jacket_match_status_counts={"ambiguous": 3, "insufficient_input": 0, "matched": 57, "missing_feature": 0, "not_found": 0}`
  - `identity_signal_status_counts={"composite_resolved_candidate": 3, "jacket_resolved_candidate": 57}`
  - `identity_signal_source_counts={"jacket_feature": 57, "title_linehash_dict": 3}`
  - `expected_song_resolution_status_counts={"resolved": 59, "unresolved": 1}`
  - `expected_song_resolution_reason_counts={"title_not_found": 1}`
  - `expected_song_grand_prix_play_available_counts={"True": 59}`
- M5 jacket参照カバレッジ通常候補の最新結果:
  - `target_count=60`
  - `coverage_row_count=7634`
  - `total_candidate_songs=7634`
  - `referenced_candidate_songs=618`
  - `missing_feature_candidate_songs=7016`
  - `row_reference_status_counts={"all_referenced": 2, "partial_referenced": 58}`
  - `expected_song_reference_status_counts={"expected_missing_feature": 5, "expected_not_in_chart_candidates": 1, "expected_referenced": 53, "expected_unresolved": 1}`
- M5 jacket参照カバレッジ診断候補の最新結果:
  - `target_count=118`
  - `coverage_row_count=13790`
  - `total_candidate_songs=13778`
  - `referenced_candidate_songs=1143`
  - `missing_feature_candidate_songs=12635`
  - `row_reference_status_counts={"all_referenced": 2, "insufficient_input": 12, "no_candidate_features": 1, "partial_referenced": 103}`
  - `expected_song_reference_status_counts={"expected_missing_feature": 6, "expected_not_in_chart_candidates": 2, "expected_referenced": 97, "expected_unresolved": 13}`
- M4 canonical/alias確認:
  - `RЁVOLUTIФN`: `songs.title=RЁVOLUTIФN` / artist `TЁЯRA` / `grand_prix_play_available=1` / `official_availability_match=alias_title_artist`
  - Wiki由来 `RËVOLUTIФN` は `song_aliases` に保持される。
- 生成DB、PoC出力、OCR画像、`metadata.csv`、`data/`、`logs/`、ローカル素材、ローカルDBはGit管理しない。

## 必読資料

- `AGENTS.md`
- `docs/next-task.md`
- `docs/implementation-roadmap.md`
- `docs/design/03_event_and_save_boundary.md`
- `docs/design/04_data_model.md`
- `docs/design/05_storage_io_spec.md`
- `docs/design/08_master_db_generation.md`
- `docs/design/09_master_match_poc.md`
- `docs/design/07_m3_chart_field_review.md`
- `tools/vision_poc/README.md`
- `tools/vision_poc/master_match.py`
- `tools/vision_poc/runner.py`
- `master/README.md`
- `master/builder.py`
- `master/inspect.py`
- `tests/test_master_builder.py`
- `tests/test_master_match.py`
- `tests/test_vision_poc_result_events.py`

M7aの数字認識PoCへ進む場合は追加で読む資料:

- `docs/design/00_glossary.md`
- `docs/design/06_regression_guard.md`
- `tests/test_vision_poc_ocr.py`

## スコープ外

- スクリーンショット画像、`samples/screenshots/metadata.csv`、`data/`、`logs/`、ローカルDBのGit管理
- `samples/screenshots/cropped/` と `samples/screenshots/organized/` 配下のローカル追加画像コミット
- 本番キャプチャAPI、実キャプチャデバイス依存コード、常駐監視ループ、非同期処理
- 個人スコアDB保存、保存可否判定本番仕様、低確信度ログ本番仕様
- M4 Releases配布の実装
- Windows常駐アプリUI
- プロジェクト専用Skill/Subagentの作成
- 生成済み `data/master/ddrgp-master.sqlite` のコミット

M5で完了済みとして扱い、次チャットで蒸し返さないもの:

- M5参照カバレッジの出力名と語彙整理
- `jacket_match_summary.json` と `jacket_reference_coverage_summary.json` の読み分け
- `RЁVOLUTIФN` / `RËVOLUTIФN` のM4 canonical/alias整理
- `Inner Spirit -GIGA HiTECH MIX-` と `RЁVOLUTIФN` のjacket参照追加後のM5確認
- title line-hashをjacket ambiguous候補集合内だけに使う境界

M7aでまだやらないこと:

- 保存OK/NG判定、個人スコアDB保存、低信頼度ログ本番仕様
- OCR結果や数字認識結果から保存値を本番確定すること
- 曲ID/譜面IDの保存用確定
- ROI座標定義の大変更
- Tesseract OCR全体の撤去やOCR方式全面刷新
- duplicate key の本格実装差し替え

## 次に必ず進める実作業

次は M7a「スコア系数字認識のOCR脱却」の最小PoCを進める。

- `docs/next-task.md` の更新だけ、または確認結果の記録だけで完了扱いにしない。
- confirmed-eventsだけを対象に、保存値候補になる数字ROIの非OCR認識PoCを追加する。
- 初回対象は、実装量を抑えるため `score_digits` を優先する。余力があれば `ex_score`、`max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss` へ広げる。
- Tesseractではなく、テンプレート、桁分割、画像特徴、または固定UI向けの軽量line/bitmap比較を使う。
- 出力は `data/` 配下に置き、テンプレート素材やローカル画像はGit管理しない。
- status語彙は最初から `recognized` / `ambiguous` / `missing_reference` / `failed_segmentation` / `not_evaluated` などを分ける。
- 既存Tesseract出力とは別summaryとして出し、既存 `score_ocr.csv` / `score_ocr_summary.json` を壊さない。
- M7保存判定やM8 DB保存と混同せず、保存値候補の読み取り材料として扱う。
- コードを追加した場合は、ネットワーク、画像、`metadata.csv` に依存しないfixtureテストを追加する。
- 仕様や読み方を変えた場合は、`docs/implementation-roadmap.md`、関連する `docs/design/`、`tools/vision_poc/README.md` も同じ作業で更新する。

## 検証コマンド

M5完了確認として今回通したコマンド:

```powershell
python -m master --output data\master\ddrgp-master.sqlite
python -m master.inspect data\master\ddrgp-master.sqlite --summary data\master\master-summary.json
python -m tools.vision_poc --m5-master-match --master-db data\master\ddrgp-master.sqlite --output data\master_match_poc --no-rois --no-ocr
python -m tools.vision_poc --m3-song-artist-ocr --m5-master-match --master-db data\master\ddrgp-master.sqlite --output data\master_match_poc_ocr --no-rois
python -m tools.vision_poc --m3-song-artist-ocr --m5-master-match --m5-jacket-match --master-db data\master\ddrgp-master.sqlite --output data\master_match_poc_jacket --no-rois
python -m tools.vision_poc --no-ocr
python -m ruff check master tools\vision_poc pyproject.toml tests
python -m compileall master tools\vision_poc
python -m pytest tests
git diff --check
```

M7aで最低限実行するコマンド:

```powershell
python -m tools.vision_poc --no-ocr
python -m tools.vision_poc --m3-song-artist-ocr --ocr-target confirmed-events --no-rois --output data\vision_poc_m3_song_artist
python -m ruff check master tools\vision_poc pyproject.toml tests
python -m compileall master tools\vision_poc
python -m pytest tests
git diff --check
```

M4/M5境界へ触った場合は、今回通したM5完了確認コマンドも再実行すること。

## コミット/Push方針

- `metadata.csv`、`data/`、`logs/`、ローカル素材、ローカルDBはコミットしない。
- `samples/screenshots/cropped/` と `samples/screenshots/organized/` 配下の画像はローカル素材扱いでコミットしない。
- `docs/next-task.md` は引き継ぎ仕様としてコミット対象に含める。
- コード、README、docs、テストに変更がある場合のみ、今回作業分だけをステージしてコミットする。
- `data/master/ddrgp-master.sqlite`、`data/master/master-summary.json`、M5/M7a PoC出力、ROI画像、OCR画像、解析ログはステージしない。
- 仕様語彙、出力ファイル名、summaryの読み方、保存境界、OCR対象境界を変えた場合は、関連する `docs/design/` または `tools/vision_poc/README.md` を同じコミットに含める。
- コミットがある場合は作業ブランチを push する。

## 完了条件

- M7aの最小PoCが confirmed-events 対象だけを入力にしている。
- 対象数字ROIについて、非OCR方式の認識候補、信頼度または距離、status、failure_reason を出せる。
- `recognized`、`ambiguous`、`missing_reference`、`failed_segmentation` などの失敗理由を混同せず読める。
- 既存Tesseract OCR出力を壊していない。
- M5の通常候補、診断出力、coverage summary、`identity_signal_*` の意味を変更していない。
- M7保存判定やM8 DB保存を実装していない。
- fixtureテストがネットワーク、画像、`metadata.csv` に依存せず通る。
- 画像PoCやM3境界を触った場合は、`python -m tools.vision_poc --no-ocr` が全正解。
- 生成DB、テンプレート素材、PoC出力、`metadata.csv` 実体や画像をコミットしていない。
- 検証コマンドが通っている。
- コミット/Pushする場合は、Git管理対象外ファイルを含めていない。
