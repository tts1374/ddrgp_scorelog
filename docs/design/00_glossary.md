# 用語集

DDR GP scorelog の設計、PoC、テストで使う主要用語を定義する。似た名前の概念が多いため、実装やレビューではこの定義を基準にする。

## FrameInput

分類、イベント確定、OCRへ渡す1フレーム分の入力契約。

構成:

- `image_path`
- `timestamp_ms`
- `row`

metadata、timestamped、manifest、dry-run capture、将来の実キャプチャAPIを同じ境界で扱うためのPoC上の中心概念。

## manifest

フレーム列をCSVとして再実行可能にする入力形式。

必須列:

- `image_path`
- `timestamp_ms`

任意列:

- `screen_type`
- OCR期待値列
- 補助列

manifest mode で読み込む。timestamped と dry-run capture provider は manifest互換CSVを出す。

## dry-run capture provider

実キャプチャAPI導入前に、既存画像ディレクトリを capture provider の代替入力として扱うPoC入口。

特徴:

- 実デバイスには接続しない。
- ファイル名昇順で画像を読む。
- 単調増加する `timestamp_ms` を付ける。
- フレームを `data/` 配下へ保存する。
- manifest互換CSVを出す。

## metadata mode

`samples/screenshots/metadata.csv` を読み、キャプチャ時刻なしで分類評価するモード。

特徴:

- `timestamp_ms=None`
- `confirmation_mode=frames`

## timestamped mode

metadata と同じ画像列へ人工 timestamp を付けるモード。

特徴:

- `timestamp_ms` を人工生成する。
- `confirmation_mode=time`
- `frame_manifest.csv` を出す。
- metadata の期待値列を保持する。

## manifest mode

manifest CSVを読み込むモード。

特徴:

- `timestamp_ms` 必須。
- `confirmation_mode=time`
- timestamp の空、非整数、負数、非単調増加をエラーにする。

## screen_type

metadata または manifest 上の画面種別。

主な値:

- `result`
- `song_select`
- `gameplay`
- `menu_setup`
- `transition`

分類の期待値、評価集計、テストシナリオに使う。実キャプチャでは未知の場合があるため、空欄や `unknown` も許容する。

## result_shape_candidate

リザルト画面らしい形状を検出したか。

これは保存候補ではない。`transition_countup_*` は `result_shape_candidate=true` でも保存対象外にする。

## result_candidate

単発フレーム分類で保存候補に見えるか。

これは保存確定ではない。実キャプチャでは一瞬の誤検出や遷移フレームが混ざるため、継続条件を満たすまで保存しない。

## confirmed_result

継続条件を満たし、保存直前候補として確定したか。

metadata mode ではフレーム数ベース。timestamped、manifest、dry-run、将来キャプチャでは時間ベース。

## confirmation_mode

保存確定の判定方式。

- `frames`: timestamp なし入力。フレーム数で継続を判定する。
- `time`: timestamp 付き入力。継続時間で判定する。

## event_type

`result_events.csv` で各フレームのイベント解釈を表す値。

- `none`: 未確定
- `confirmed`: 重複ではない保存候補
- `duplicate`: duplicate window 内の重複確定
- `rejected_transition`: 保存不可遷移

## confirmed-events

保存直前OCR相当の評価対象。

対象条件:

```text
confirmed_result=true
duplicate=false
```

`--ocr-target confirmed-events` で使う。

## duplicate

同一リザルトが duplicate window 内で再確定したか。

`duplicate=true` の行は `confirmed_result=true` でも保存しない。

## duplicate_key

重複判定に使うキー。

現行PoCでは、ファイル名に `scoreXXXXXX` があれば `score:<digits>`、なければ `file:<filename>`。本格実装では曲、譜面、スコア、判定数、画像ハッシュなどを使う方式へ差し替える。

## transition_countup_*

リザルト遷移中またはカウントアップ中を表すローカル素材の命名。

期待:

- `result_shape_candidate=true` でもよい。
- `result_candidate=false`
- `confirmed_result=false`
- `event_type=rejected_transition`
- 保存対象外
- confirmed-events OCR対象外

## expected columns

OCR評価に使う期待値列。

例:

- `score`
- `expected_score`
- `max_combo`
- `expected_max_combo`
- `miss`
- `expected_miss`
- `ex_score`
- `expected_ex_score`

manifest や timestamped 出力で保持する。

## evaluated

対象ROIのすべてのOCR試行に期待値がある状態。

## partially_evaluated

対象ROIの一部のOCR試行に期待値があり、一部には期待値がない状態。採用判断は暫定扱いにする。

## no_expected_values

対象ROIのOCR試行に期待値がない状態。OCR成功扱いにしない。

## 保存境界

DB保存へ進めてよい最小条件。

```text
confirmed_result=true
duplicate=false
```

M0/M1ではDB保存自体は実装しないが、この境界を崩さない。
