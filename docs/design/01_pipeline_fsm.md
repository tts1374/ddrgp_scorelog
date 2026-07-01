# パイプラインFSM設計

DDR GRAND PRIX の画面入力から、保存候補判定、OCR、マスタ照合、DB保存またはスキップまでの状態遷移を定義する。現時点では画像解析PoCの状態名と保存境界を固定するための設計メモであり、本番キャプチャAPI、常駐監視、非同期処理、DB保存の実装仕様ではない。

## 目的

- 単発フレーム分類と保存確定を分離する。
- リザルト形状、保存候補、確定、重複、遷移除外を同じ語彙で扱う。
- 実キャプチャAPI導入後も dry-run manifest で同じ状態遷移を再現できるようにする。
- DB保存へ進む前に、保存してよいイベントと保存してはいけないイベントを一意に分ける。

## 入力状態

### `NO_SOURCE`

入力元がない状態。PoCでは該当しない。本番常駐アプリでは DDR GRAND PRIX ウィンドウ未検出、キャプチャ不可、権限不足などを含む。

### `FRAME_SOURCE_READY`

フレーム入力元が準備できている状態。

PoCでの入力元:

- metadata mode
- timestamped mode
- manifest mode
- dry-run capture provider

将来の入力元:

- Windows Graphics Capture API provider

## フレーム処理状態

### `FRAME_RECEIVED`

1フレーム分の入力を受け取った状態。PoCでは `FrameInput` がこの境界を表す。

必要な情報:

- `image_path`
- `timestamp_ms`
- `row`

`timestamp_ms` がある入力は time-based confirmation、ない入力は frame-based confirmation として扱う。

### `CLASSIFIED_NON_RESULT`

リザルト候補ではない状態。

条件:

- `result_candidate=false`
- `result_shape_candidate=false`

保存対象外。直前まで result candidate streak があれば streak をリセットする。

### `RESULT_SHAPE_DETECTED`

リザルトらしい形状は検出したが、保存候補としてはまだ採用しない状態。

代表例:

- `transition_countup_*`
- カウントアップ中
- 詳細リザルト枠やRESULTSヘッダーは見えるが、スコア周辺の保存条件が不足しているフレーム

`transition_countup_*` は `result_shape_candidate=true` でも保存対象外とする。

### `RESULT_CANDIDATE`

単発フレーム分類で保存候補に見える状態。

条件:

- `result_candidate=true`

この状態だけでは保存しない。継続時間または継続フレーム数を見て `CONFIRMED_RESULT` に進める。

### `CONFIRMED_RESULT`

保存直前候補として確定した状態。

metadata mode:

- `result_candidate=true` が `CONFIRMED_RESULT_MIN_FRAMES` 以上継続
- `confirmation_mode=frames`

timestamped / manifest / dry-run / 将来キャプチャ:

- `result_candidate=true` が `CONFIRMED_RESULT_MIN_DURATION_MS` 以上継続
- `confirmation_mode=time`

この状態でも duplicate の場合は保存しない。

### `DUPLICATE_RESULT`

同一リザルトが duplicate window 内で再確定した状態。

PoCの現行キー:

- ファイル名に `scoreXXXXXX` があれば `score:<digits>`
- それ以外は `file:<filename>`

これはローカルPoC用の簡易キーであり、曲名、譜面、判定数、画像ハッシュを使う本格実装へ将来差し替える。

### `REJECTED_TRANSITION`

リザルト形状はあるが保存不可の遷移として除外した状態。

現行代表:

- `transition_countup_*`

`confirmed_result=false` のまま扱う。OCR対象にもDB保存対象にも進めない。

## 解析状態

### `OCR_READY`

保存直前OCRへ進める状態。

条件:

- `confirmed_result=true`
- `duplicate=false`

PoCでは `--ocr-target confirmed-events` がこの境界を使う。

### `OCR_EVALUATED`

OCRを実行し、ROIごとの結果と期待値カバレッジを出した状態。

評価状態:

- `evaluated`
- `partially_evaluated`
- `no_expected_values`

`no_expected_values` は成功扱いにしない。`partially_evaluated` は暫定判断として扱い、採用前に期待値不足を埋める。

### `MASTER_MATCH_READY`

曲名、プレースタイル、難易度、レベルなどが抽出され、マスタ照合へ進める状態。現時点では未実装。

### `SAVE_READY`

OCR、マスタ照合、重複判定、信頼度判定をすべて満たし、DB保存してよい状態。現時点では未実装。

### `SAVED`

個人スコアDBに1プレー1レコードで保存した状態。現時点では未実装。

### `SKIPPED`

DB保存しない状態。

代表理由:

- non-result
- unconfirmed result candidate
- rejected transition
- duplicate
- OCR failed
- no expected values in evaluation
- low confidence
- master match ambiguous
- master match failed

本番では失敗画像と解析ログを残す。

## 状態遷移概要

```text
NO_SOURCE
  -> FRAME_SOURCE_READY
  -> FRAME_RECEIVED
  -> CLASSIFIED_NON_RESULT
  -> SKIPPED

FRAME_RECEIVED
  -> RESULT_SHAPE_DETECTED
  -> REJECTED_TRANSITION
  -> SKIPPED

FRAME_RECEIVED
  -> RESULT_CANDIDATE
  -> CONFIRMED_RESULT
  -> DUPLICATE_RESULT
  -> SKIPPED

FRAME_RECEIVED
  -> RESULT_CANDIDATE
  -> CONFIRMED_RESULT
  -> OCR_READY
  -> OCR_EVALUATED
  -> MASTER_MATCH_READY
  -> SAVE_READY
  -> SAVED
```

## 現在のPoCで検証する範囲

- `FRAME_RECEIVED`
- `CLASSIFIED_NON_RESULT`
- `RESULT_SHAPE_DETECTED`
- `RESULT_CANDIDATE`
- `CONFIRMED_RESULT`
- `DUPLICATE_RESULT`
- `REJECTED_TRANSITION`
- `OCR_READY`
- `OCR_EVALUATED`

## まだ実装しない範囲

- 本番キャプチャAPI
- 常駐監視ループ
- 非同期処理
- マスタ照合
- DB保存
- 失敗画像と本番ログ保存
- WindowsアプリUI
