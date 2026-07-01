# ADR 0001: 初期PoC境界の固定

## Status

Accepted

## Context

DDR GP scorelog は最終的に Windows常駐アプリ、実キャプチャAPI、OCR、マスタ照合、DB保存をつなぐ必要がある。一方で、現時点では本番キャプチャ、常駐監視、非同期処理、DB保存まで同時に進めると、誤保存防止やイベント確定の判断が不安定になる。

そのため、M0/M1では画像解析PoCの入力境界、manifest互換、保存直前イベント境界を先に固定する。

## Decision 1: manifest互換のFrameInput境界を維持する

metadata、timestamped、manifest、dry-run capture、将来の実キャプチャAPIは、`FrameInput` 相当の境界へ接続する。

PoCでの `FrameInput` は以下を持つ。

- `image_path`
- `timestamp_ms`
- `row`

`row` は `organized_file`、`screen_type`、OCR期待値列、補助列を保持する。実キャプチャAPI導入後もしばらく manifest互換 dry-run 出力を維持し、実フレームを同じCSV契約で再実行できるようにする。

### Consequences

- 実キャプチャ入力の不具合と、分類/OCR/イベント確定の不具合を切り分けやすい。
- timestamped や dry-run の出力を manifest mode で回帰確認できる。
- manifest の列互換を壊す変更は設計資料とテスト更新が必要になる。

## Decision 2: 保存境界は confirmed-events に寄せる

DB保存へ進めてよい最小条件は以下とする。

```text
confirmed_result=true
duplicate=false
```

PoCでは `--ocr-target confirmed-events` がこの境界を使う。`result_candidate=true` だけでは保存対象にしない。

### Consequences

- 単発誤検出や短すぎる result candidate を保存しない。
- duplicate は `confirmed_result=true` でも保存しない。
- `transition_countup_*` は `result_shape_candidate=true` でも保存しない。
- DB保存実装前でも「保存してよいイベント行」をテストできる。

## Decision 3: ローカル素材と生成物はGit管理しない

以下はGit管理しない。

- スクリーンショット画像
- `samples/screenshots/metadata.csv`
- `data/`
- `logs/`
- PoC出力
- 解析ログ
- ローカルDB

コード、README、docs、テスト、例示CSVはGit管理する。

### Consequences

- ローカル素材に依存する検証は、存在しない環境でskipまたは生成fixtureを使う必要がある。
- PoC出力は再生成可能なものとして扱う。
- 実行結果を共有したい場合は、生成物そのものではなく要約、設計資料、テスト、または必要な例示だけをコミットする。

## Decision 4: M0/M1では本番実装に踏み込まない

M0/M1では以下をスコープ外にする。

- 本番キャプチャAPI
- 実キャプチャデバイス依存コード
- 常駐監視ループ
- 非同期処理
- DB保存
- OCR方式刷新
- duplicate key の本格差し替え
- ROI座標定義の大変更

### Consequences

- 入力契約と保存境界を小さく安定させられる。
- 本番キャプチャやDB保存は、M0/M1の回帰ガードを通った後に進める。
- 近い作業は dry-run sequence scenario と保存直前イベント仕様の固定になる。

## References

- `docs/implementation-roadmap.md`
- `docs/design/00_glossary.md`
- `docs/design/02_frame_input_contract.md`
- `docs/design/03_event_and_save_boundary.md`
- `docs/design/05_storage_io_spec.md`
- `docs/design/06_regression_guard.md`
