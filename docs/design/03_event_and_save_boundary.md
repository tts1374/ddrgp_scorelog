# イベントと保存境界

分類結果を時系列イベントとして解釈し、DB保存へ進めてよい行と保存してはいけない行を分ける設計です。現時点ではPython PoCの `result_events.csv` と `confirmed-events` OCR対象選定が正本です。

## 目的

- `result_candidate=true` だけで保存しない。
- 継続確認済みのリザルトだけを保存候補にする。
- duplicate と transition を保存対象外にする。
- OCR対象とDB保存候補の境界を一致させる。

## 主要概念

### `result_shape_candidate`

リザルト画面らしい形状検出。

例:

- RESULTSヘッダー
- 詳細リザルト枠
- ランク周辺やスコア周辺の形状

これは保存候補そのものではない。`transition_countup_*` はここが `true` でも保存対象外になり得る。

### `result_candidate`

単発フレーム分類で保存候補に見えるか。

これはOCR候補の参考母数ではあるが、保存確定ではない。実キャプチャでは一瞬の誤検出や遷移フレームが混ざるため、継続条件を満たすまで保存しない。

### `confirmed_result`

継続条件を満たし、保存直前候補として確定したか。

metadata mode:

- フレーム数ベース
- `confirmation_mode=frames`

timestamped / manifest / dry-run / 将来キャプチャ:

- 時間ベース
- `confirmation_mode=time`

### `duplicate`

同一リザルトが duplicate window 内で再確定したか。

`duplicate=true` の行は `confirmed_result=true` でも保存しない。

### `event_type`

イベントの読み分け。

- `none`: 未確定。保存しない。
- `confirmed`: 重複ではない保存候補。
- `duplicate`: duplicate window 内の重複確定。保存しない。
- `rejected_transition`: `transition_countup_*` など保存不可遷移。保存しない。

## 保存境界

DB保存へ進めてよい条件:

```text
confirmed_result=true
duplicate=false
```

同等に、現行PoCでは以下も保存候補として読める。

```text
event_type=confirmed
```

ただし将来 `event_type` が増える場合でも、保存境界の基本は `confirmed_result=true` かつ `duplicate=false` とする。

## OCR対象境界

`--ocr-target confirmed-events` は保存直前OCR相当の評価モードです。

対象:

```text
confirmed_result=true
duplicate=false
```

対象外:

- `result_candidate=true` だが未確定
- `duplicate=true`
- `event_type=rejected_transition`
- non-result

`--ocr-target result-candidate` は従来互換と副作用確認用であり、保存直前評価の成功扱いにはしない。

## transition_countup の扱い

`transition_countup_*` はリザルト形状が出ていても保存対象外とする。

期待されるイベント:

```text
result_shape_candidate=true
result_candidate=false
confirmed_result=false
event_type=rejected_transition
duplicate=false
```

この扱いは、カウントアップ中のスコアや判定数を誤保存しないための回帰ガードです。

## time-based confirmation

timestamp 付き入力では、フレーム数ではなく継続時間で確定する。

現行PoCの基準:

```text
CONFIRMED_RESULT_MIN_DURATION_MS = 1000
```

考え方:

- FPS固定に依存しない。
- フレーム欠落やFPS揺れがあっても、実時間で保存確定を判断する。
- timestamp が短時間に偏っている場合は、フレーム数が多くても確定しない。

## duplicate window

timestamp 付き入力では時間窓で重複判定する。

現行PoCの基準:

```text
DUPLICATE_WINDOW_MS = 90000
```

timestamp なし入力ではフレーム窓で重複判定する。

```text
DUPLICATE_WINDOW_FRAMES = 90
```

## 現行duplicate key

現行PoCでは簡易キーを使う。

- ファイル名に `scoreXXXXXX` があれば `score:<digits>`
- なければ `file:<filename>`

これは本格実装ではない。将来候補:

- score + 曲名 + 難易度
- score + 判定数
- perceptual hash
- マスタ照合後の正規化済み result id

## `result_events.csv` の読み方

保存候補確認では以下を見る。

- `event_type`
- `confirmed_result`
- `duplicate`
- `timestamp_ms`
- `candidate_duration_ms`
- `confirmation_mode`
- `reason`

`result_candidate=true` の行数だけで保存成功を判断しない。

## `result_events_summary.json` の読み方

見るべき値:

- `confirmed_count`: 重複ではない保存候補数
- `confirmed_result_count`: duplicate を含む確定フレーム数
- `duplicate_count`: 重複除外数
- `rejected_transition_count`: 遷移除外数
- `first_confirmed_timestamp_ms`: timestamp付き入力で最初に保存確定した時刻
- `confirmation_mode_counts`: `time` / `frames` の分布

## M0/M1で固定すること

- 保存境界は `confirmed_result=true` かつ `duplicate=false`。
- `transition_countup_*` は保存対象外。
- manifest / dry-run / 将来キャプチャは `confirmation_mode=time`。
- dry-run sequence scenario でも short result、sustained result、duplicate、`transition_countup_*` を同じ保存境界で確認する。
- duplicate key の本格差し替えは別フェーズ。
- DB保存はこの境界が固まってから実装する。
