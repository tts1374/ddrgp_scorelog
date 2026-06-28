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

## Skills And Subagents

- 現時点ではプロジェクト専用SkillやSubagentは作らない。
- OCR、実キャプチャ、DB保存、レビュー観点が独立して大きくなった時点で追加を検討する。
- 追加する場合は、このファイルに用途、呼び出しタイミング、検証コマンドを追記する。
