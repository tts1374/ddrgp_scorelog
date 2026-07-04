# ストレージI/O仕様

ローカル素材、PoC出力、manifest、ログ、DB、将来の本番保存物の置き場とGit管理方針を定義する。AGENTS.md のプロジェクトルールを設計資料として補強する。

## 基本方針

- スクリーンショット画像はGit管理しない。
- `samples/screenshots/metadata.csv` はGit管理しない。
- PoC出力は原則 `data/` 配下へ出す。
- 解析ログは原則 `logs/` 配下へ出す。
- ローカルDBはGit管理しない。
- 既存のローカル素材や生成物を削除・移動するときは、目的と対象を明確にする。

## Git管理するもの

- Python PoCコード
- テストコード
- README
- `docs/`
- サンプル用の空READMEや例示CSV
- 設計資料
- CI設定
- 将来のアプリコード

## Git管理しないもの

- `samples/screenshots/organized/`
- `samples/screenshots/metadata.csv`
- `data/`
- `logs/`
- ローカルDB
- 実キャプチャ画像
- 失敗時キャプチャ画像
- OCR前処理画像
- PoC解析ログ

## 入力素材

### スクリーンショット

配置:

```text
samples/screenshots/organized/
```

用途:

- 分類評価
- ROI確認
- OCR前処理確認
- regression fixture

Git管理しない。

### metadata

配置:

```text
samples/screenshots/metadata.csv
```

用途:

- `organized_file`
- `screen_type`
- score expected values
- judgment expected values

Git管理しない。列定義はREADMEや設計資料で管理する。

### metadata example

配置:

```text
samples/metadata.example.csv
```

用途:

- 入力列の例示。

Git管理してよい。

## PoC出力

### metadata mode

既定:

```text
data/vision_poc/
```

主な出力:

- `results.csv`
- `summary.json`
- `misclassifications.md`
- `result_events.csv`
- `result_events_summary.json`
- `score_ocr.csv`
- `score_ocr_summary.json`
- `ocr_roi_report.md`
- `ocr_expected_coverage.md`
- `ocr_expected_template.csv`
- `rois/`
- `ocr/`

### timestamped mode

既定:

```text
data/vision_poc_timestamped/
```

追加出力:

- `frame_manifest.csv`

### manifest mode

既定:

```text
data/vision_poc_manifest/
```

用途:

- timestamped または dry-run の manifest 再読込結果。

### dry-run capture provider

既定:

```text
data/vision_poc_capture_dry_run/
```

主な出力:

- `frames/`
- `frame_manifest.csv`

制約:

- `--capture-dry-run-output` は `data/` 配下に限定する。

## manifest

manifest はフレーム列を再実行可能にするCSV。

最小列:

- `image_path`
- `timestamp_ms`

任意列:

- `screen_type`
- expected columns
- 補助列

Git管理しない。ただし仕様と例はdocsに書く。

## ログ

PoCログ:

```text
logs/
```

本番アプリログ候補:

```text
%LOCALAPPDATA%/ddrgp_scorelog/logs/
```

本番失敗画像候補:

```text
%LOCALAPPDATA%/ddrgp_scorelog/failed-captures/
```

最終パスは未決。

## ローカルDB

マスタDB候補:

```text
ddrgp-master.sqlite
```

M4初期実装のローカル生成先:

```text
data/master/ddrgp-master.sqlite
```

個人スコアDB候補:

```text
ddrgp-scores.sqlite
```

開発中に生成したDB、取得元HTML snapshot、解析ログはGit管理しない。配布用マスタDBはGitHub Releases成果物として扱う。

## 削除・移動のルール

削除または移動前に確認すること:

- 対象がローカル素材か生成物か。
- 再生成可能か。
- metadata と画像の対応が壊れないか。
- `data/` や `logs/` の掃除で十分か。

原則:

- コード変更のついでにローカル素材を削除しない。
- PoC出力の削除は目的を明確にして行う。
- Git管理外ファイルはコミット対象にしない。

## 今後決めること

- 本番アプリの正式なローカルデータ保存先
- 失敗画像の保存期間と掃除方法
- ログローテーション
- DBバックアップ方針
- manifest dry-run 出力を本番でも残す期間
