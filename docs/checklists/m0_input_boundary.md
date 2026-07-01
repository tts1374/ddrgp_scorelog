# M0 入力境界チェックリスト

M0「画像解析PoCの入力境界を固める」を完了扱いにするためのチェックリストです。

## 入力モード

- [x] metadata mode が存在する。
- [x] timestamped mode が存在する。
- [x] manifest mode が存在する。
- [x] dry-run capture provider が存在する。
- [x] timestamped mode が manifest互換CSVを出力する。
- [x] dry-run capture provider が manifest互換CSVを出力する。
- [x] 複数 `screen_type` 混在の dry-run sequence scenario がある。

## FrameInput契約

- [x] `image_path` を分類とROI切り出しに使える。
- [x] `timestamp_ms` を time-based confirmation に渡せる。
- [x] `row` に `organized_file` と `screen_type` を保持できる。
- [x] `row` にOCR期待値列を保持できる。
- [x] manifest 再読込で任意列を保持できる。

## timestamp

- [x] `timestamp_ms` は0以上の整数として読む。
- [x] `timestamp_ms` 空欄をエラーにする。
- [x] `timestamp_ms` 非整数をエラーにする。
- [x] `timestamp_ms` 負数をエラーにする。
- [x] `timestamp_ms` 非単調増加をエラーにする。
- [x] dry-run capture provider が単調増加 timestamp を付ける。
- [x] 高fps丸めで同値になり得る場合に直前値 + 1ms へ補正する。

## manifest互換

- [x] 最小manifest `image_path,timestamp_ms` を読める。
- [x] `screen_type` 付きmanifestを読める。
- [x] expected columns付きmanifestを読める。
- [x] timestamped mode の生成manifestを manifest mode で読める。
- [x] dry-run capture provider の生成manifestを manifest mode で読める。
- [x] 複数 `screen_type` 混在の生成manifestを manifest mode で読める。

## confirmation

- [x] metadata mode は `confirmation_mode=frames`。
- [x] timestamped mode は `confirmation_mode=time`。
- [x] manifest mode は `confirmation_mode=time`。
- [x] dry-run capture provider 由来manifestは `confirmation_mode=time`。
- [x] short duration の result candidate は確定しない。
- [x] sustained result candidate は確定する。
- [x] 複数screen_type混在シナリオで short / sustained / duplicate / transition を同時に確認する。

## 保存境界

- [x] `result_candidate=true` だけでは保存対象にしない。
- [x] confirmed-events は `confirmed_result=true` かつ `duplicate=false` のみを対象にする。
- [x] duplicate は confirmed-events OCR対象外。
- [x] unconfirmed は confirmed-events OCR対象外。
- [x] `transition_countup_*` は confirmed-events OCR対象外。
- [x] 複数screen_type混在シナリオで confirmed-events 対象を回帰確認する。

## transition_countup

- [x] `transition_countup_*` は `result_shape_candidate=true` でも保存対象外。
- [x] `transition_countup_*` は `event_type=rejected_transition`。
- [x] `transition_countup_*` は `confirmed_result=false`。
- [x] 複数screen_type混在シナリオ内で `transition_countup_*` 相当を確認する。

## expected coverage

- [x] expected columns があるROIは評価に使う。
- [x] expected columns がないROIは `no_expected_values`。
- [x] 一部だけ期待値があるROIは `partially_evaluated`。
- [x] `no_expected_values` は成功扱いにしない。
- [x] timestamped manifest が expected columns を保持する。
- [x] dry-run sequence scenario で expected columns の保持を確認する。

## 検証コマンド

- [x] `python -m pytest tests`
- [x] `python -m ruff check tools\vision_poc pyproject.toml`
- [x] `python -m ruff check tests`
- [x] `python -m compileall tools\vision_poc`
- [x] `python -m tools.vision_poc --sequence-mode timestamped --ocr-target confirmed-events --ocr-rois all --ocr-profile all`
- [x] `python -m tools.vision_poc --sequence-mode manifest --frame-manifest data\vision_poc_timestamped\frame_manifest.csv --frame-root samples\screenshots --ocr-target confirmed-events --ocr-rois all --ocr-profile all`
- [x] dry-run capture provider の生成manifestを manifest mode で再読込する。
- [x] 複数screen_type混在 dry-run sequence の生成manifestを manifest mode で再読込する。

## M0完了条件

M0は以下を満たしたら完了扱いにする。

- [x] 複数screen_type混在のdry-run/manifestシナリオで保存境界を確認できる。
- [x] 生成manifestが既存manifest modeで読める。
- [x] `timestamp_ms` の単調増加と `confirmation_mode=time` が維持されている。
- [x] confirmed-events の保存境界が維持されている。
- [x] `transition_countup_*` が保存対象外として維持されている。
- [x] expected columns / `no_expected_values` / `partially_evaluated` の扱いが壊れていない。
- [x] 本番キャプチャAPI、常駐監視、非同期処理、DB保存に踏み込んでいない。
