# Vision PoC Agent Rules

このファイルは `tools/vision_poc/` 配下の実装・検証に追加適用する。人間向けの使い方と機能説明は `README.md` を事実源とし、ここではエージェントが守る境界だけを定める。

## Local Screenshot Assets

- `samples/screenshots/organized/`、`samples/screenshots/metadata.csv`、digit templateなどの画像素材はローカル専用でGit管理しない。
- `metadata.csv` の `organized_file` と `screen_type` を分類評価の正解にする。
- `screen_type=result` は `result_candidate=true`、`song_select`、`gameplay`、`menu_setup`、`transition` は `result_candidate=false` を期待する。
- `transition_countup_*` はリザルト形状が出ても保存不可候補とし、`result_shape_candidate=true` になり得るが `result_candidate=false`、`transition_kind=countup`、`event_type=rejected_transition` として通常遷移と区別する。
- metadataや画像がない環境では、ローカル素材が必要なtestだけをskip対象とし、生成・代替・Git追加しない。

## Classification And ROI

- 初期PoCは `docs/vision-poc-prep.md` に従う。
- ROIは1280x720基準で定義し、実画像サイズへ線形scaleする。ROI座標定義の大変更は独立phaseとする。
- OCR本番精度より先にROI切り出し、候補分類、log、評価集計を安定させ、主要ROIを `data/vision_poc/rois/` で目視できる状態に保つ。
- `result_shape_candidate` はリザルトらしい形状検出、`result_candidate` は保存処理や数字OCRへ進める単発候補であり、保存確定そのものではない。
- 初期分類はOCRに依存せず、RESULTS header、詳細result枠、score周辺、rank周辺の色・edge・明度特徴を使う。

## Confirmed Events And OCR

- 保存直前OCR相当の対象境界は `confirmed_result=true` かつ `duplicate=false` とする。duplicate、`event_type=rejected_transition`、未確定 `result_candidate`、non-resultを含めない。
- OCR対象境界とOCR成功条件を分ける。`no_expected_values` は成功扱いにせず、`partially_evaluated` は暫定状態として不足期待値を埋めてから採否を判断する。
- timestamped、manifest、dry-run由来の入力とmanifest再読込では `confirmation_mode=time` を維持し、metadata由来のexpected columnsを保持する。
- `evaluated` は対象ROIの全OCR試行に期待値がある状態とする。`reference_profiles` は目視参考であり、採用根拠に昇格させない。
- profile採用は `score_ocr_summary.json`、`score_ocr_profiles_summary.json`、`ocr_expected_coverage.md`、`ocr_roi_report.md` を合わせて判断する。
- legacy `score_ocr.csv` はdefault profileの互換出力として維持する。M2では局所前処理とprofile評価を優先し、OCR方式刷新やROI座標定義の大変更へ進まない。

## Outputs And Documentation

- 生成物は `data/vision_poc/` または明示した `data/` 配下の別outputへ出し、Git管理しない。
- 基本出力の `results.csv`、`summary.json`、`misclassifications.md`、`rois/` を維持する。confirmed-events/OCRを変更するときはREADME記載のevent、coverage、profile、ROI report出力もconsumerと合わせて確認する。
- 保存境界、OCR対象、expected coverage、profile採用判断を変えた場合は、関連する `docs/design/` と `tools/vision_poc/README.md` を同期する。
- 固定しきい値を変更した場合はPoCを実行し、result、non-result、`transition_countup_*` の集計を確認する。
- 画像処理logicを共有化するときは、先に既存PoCを守るtestを追加する。
- 本番capture API、device依存、常駐監視、async処理、DB保存、OCR方式刷新、ROI大変更、duplicate key本格差し替えは独立phaseとする。

## Validation

画像分類、ROI、OCR/digit recognition、profile評価、confirmed-events、PoC runner、出力集計/report、または画像処理へ影響する共通コードを変更した場合だけ、通常の対象・影響範囲testに加えて次を実行する。

```powershell
python -X utf8 -m ruff check tools\vision_poc pyproject.toml tests
python -X utf8 -m compileall master tools\vision_poc
python -X utf8 -m tools.vision_poc
```

画像処理依存がない環境では、必要に応じて次を使う。現状のflat構成では `python -m pip install -e ".[dev]"` がsetuptoolsの自動package探索で失敗し得るため、Vision optional dependencyを使う。

```powershell
python -X utf8 -m pip install -e ".[vision]"
python -X utf8 -m pip install --user "ruff>=0.9.0"
```

`pytest_chalice` 由来の `pkg_resources` deprecated warningは既知warningとしてtest failureと区別する。PoCを省略した場合は、変更が上記実行条件に該当しない理由と残るリスクを報告する。
