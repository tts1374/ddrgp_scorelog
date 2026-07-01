# AGENTS.md

このリポジトリで作業するエージェント向けのプロジェクト固有ルールです。一般的な実装方針より、このファイルに書かれた内容を優先してください。

## Project Rules

- スクリーンショット画像、`samples/screenshots/metadata.csv`、PoC出力、解析ログ、ローカルDBはGit管理しない。
- 生成物は原則として `data/` または `logs/` 配下へ出力する。
- 既存のローカル素材や生成物を削除・移動するときは、目的と対象を明確にしてから行う。
- 画像解析PoCは軽量に保ち、まずはローカルで再現できる1コマンド実行を優先する。
- 仕様や判定方針を変えた場合は、関連する `docs/` または `tools/*/README.md` も更新する。

## Development Commands

基本検証:

```powershell
python -m tools.vision_poc
python -m ruff check tools\vision_poc pyproject.toml
python -m compileall tools\vision_poc
```

PoC用の画像処理依存がない環境では以下を使う。ただし、現状のフラット構成では `python -m pip install -e ".[dev]"` が setuptools の自動パッケージ探索で失敗する可能性がある。

```powershell
python -m pip install -e ".[vision]"
python -m pip install --user "ruff>=0.9.0"
```

## Screenshot Assets

- `samples/screenshots/organized/` はローカル素材置き場で、Git管理対象外。
- `samples/screenshots/metadata.csv` はローカル評価の正解データで、Git管理対象外。
- `metadata.csv` の `organized_file` と `screen_type` を分類評価の基準にする。
- `screen_type=result` は `result_candidate=true` を期待する。
- `screen_type=song_select`、`gameplay`、`menu_setup`、`transition` は `result_candidate=false` を期待する。
- `transition_countup_*` はリザルト形状が出ていても保存不可候補として扱い、ログ上で通常の非リザルト遷移と区別する。

## Vision PoC Policy

- 初期PoCは `docs/vision-poc-prep.md` の方針に従う。
- ROI座標は 1280x720 基準で定義し、実画像サイズへ線形スケールする。
- OCR本番精度より先に、ROI切り出し、候補分類、ログ、評価集計を安定させる。
- `result_shape_candidate` と `result_candidate` を分ける。
- `result_shape_candidate` はリザルト画面らしい形状検出を表す。
- `result_candidate` は保存処理や数字OCRへ進める候補を表す。
- 初期分類はOCRなしで、RESULTSヘッダー、詳細リザルト枠、スコア周辺、ランク周辺の色・エッジ・明度特徴を使う。
- 数字OCRへ進む前に、主要ROIの切り出し画像を `data/vision_poc/rois/` で目視確認できる状態を保つ。

## Confirmed Events OCR Policy

- `confirmed-events` は保存直前OCR相当の評価対象で、条件は `confirmed_result=true` かつ `duplicate=false`。
- OCR対象境界とOCR成功条件は別に読む。`confirmed-events` 対象であることは、DB保存成功やOCR成功を意味しない。
- duplicate、`event_type=rejected_transition`、未確定 `result_candidate`、non-result は confirmed-events OCR対象外に保つ。
- timestamped / manifest / dry-run 由来の入力では `confirmation_mode=time` を維持する。
- timestamped が生成する manifest と manifest 再読込では、metadata由来の expected columns を保持する。
- `evaluated` は対象ROIの全OCR試行に期待値がある状態。
- `partially_evaluated` は一部だけ期待値がある暫定状態。採用判断前に不足期待値を埋める。
- `no_expected_values` は期待値がない状態。OCR文字列が出ていても成功扱いにしない。
- profile比較では、`no_expected_values` の `reference_profiles` は目視参考に留め、採用根拠にしない。
- profile比較では、`partially_evaluated` の推奨は暫定扱いにする。
- legacy `score_ocr.csv` は default profile の互換出力として維持する。
- `score_ocr_summary.json`、`score_ocr_profiles_summary.json`、`ocr_expected_coverage.md`、`ocr_roi_report.md` を合わせて読み、default OCR summary と profile summary を混同しない。
- M2では局所前処理とprofile評価を優先し、OCR方式刷新やROI座標定義の大変更には進まない。

## Output Expectations

`python -m tools.vision_poc` は以下を出力する。

- `data/vision_poc/results.csv`: 画像ごとの分類結果と各シグナルのスコア/真偽。
- `data/vision_poc/summary.json`: 集計結果。
- `data/vision_poc/misclassifications.md`: 誤検出、見逃し、`transition_countup_*` の扱いメモ。
- `data/vision_poc/rois/`: 主要ROIの切り出しPNG。

## Coding Notes

- Pythonコードは `pyproject.toml` の Ruff 設定に合わせる。
- 追加依存は必要最小限にし、PoC専用なら optional dependency に分ける。
- 固定しきい値を変更したら、`python -m tools.vision_poc` を実行して `result` / 非`result` / `transition_countup_*` の集計を確認する。
- 画像処理ロジックを共有化するときは、先にPoCの検証結果を崩さないテストを追加する。
- 保存境界、OCR対象、expected coverage、profile採用判断の仕様や読み方を変えた場合は、`docs/design/` と `tools/vision_poc/README.md` も更新する。
- 本番キャプチャAPI、実キャプチャデバイス依存コード、常駐監視ループ、非同期処理、DB保存、OCR方式刷新、ROI座標定義の大変更、duplicate key の本格差し替えは、それぞれ独立したフェーズとして扱う。

## Skills And Subagents

- 現時点ではプロジェクト専用SkillやSubagentは作らない。
- まずはこの `AGENTS.md` と `docs/design/` で運用ルールを育て、安定した反復作業になってからSkill化を検討する。
- confirmed-events OCR評価Skill: M2完了後、summary / coverage / profile report の読み方が安定した時点で検討する。
- OCR/profile調整レビューSkill: 前処理profileやROI別採用判断のレビュー観点が増えた時点で検討する。
- マスタ照合Skill: M5で曲名正規化、譜面候補絞り込み、曖昧一致の判断が独立して大きくなった時点で検討する。
- DB保存境界レビューSkill: M7/M8で保存可否、低確信度ログ、重複防止、個人スコアDB保存のレビュー観点が固まった時点で検討する。
- 実キャプチャSkill: M6以降、本番キャプチャAPIやmanifest互換dry-run出力の作業が独立して大きくなった時点で検討する。
- 追加する場合は、このファイルに用途、呼び出しタイミング、検証コマンドを追記する。
