# 回帰ガード

今後の実装で壊してはいけない挙動を整理する。テスト、README、PoC実行確認の基準として使う。

## 入力契約

- manifest mode は `image_path,timestamp_ms` の最小CSVを読める。
- manifest mode は `screen_type` がなくても動く。
- manifest mode は任意の expected columns を保持する。
- `timestamp_ms` は空、非整数、負数、非単調増加をエラーにする。
- `image_path` 空欄と存在しない画像パスはエラーにする。
- dry-run capture provider はファイル名昇順にフレームを供給する。
- dry-run capture provider は `timestamp_ms` を単調増加させる。
- dry-run capture provider の出力先は `data/` 配下に限定する。
- dry-run capture provider の生成manifestは manifest mode で読める。
- dry-run sequence scenario は複数 `screen_type`、OCR期待値列、補助列を保持したmanifestを生成できる。
- dry-run sequence scenario の生成manifestは manifest mode で読める。

## confirmation

- metadata mode は `confirmation_mode=frames`。
- timestamped mode は `confirmation_mode=time`。
- manifest mode は `confirmation_mode=time`。
- dry-run capture provider 由来のmanifest再読込は `confirmation_mode=time`。
- timestamp付き入力では継続フレーム数だけで確定しない。
- 短時間に偏った連続フレームは、枚数が多くても確定しない。
- 十分な継続時間を満たした result candidate は確定する。
- 複数screen_type混在シナリオでも short result は未確定、sustained result は確定、duplicate result は `duplicate=true` になる。

## 保存境界

- `result_candidate=true` だけでは保存対象にしない。
- 保存対象は `confirmed_result=true` かつ `duplicate=false` だけ。
- 現行PoCでは `event_type=confirmed` は保存候補として扱える。
- 将来 `event_type` が増えても、基本境界は `confirmed_result=true` かつ `duplicate=false`。
- `event_type=duplicate` は保存しない。
- `event_type=rejected_transition` は保存しない。
- `event_type=none` は保存しない。
- duplicate の行は `confirmed_result=true` でも保存しない。
- 未確定の `result_candidate=true` は保存しない。
- `result_shape_candidate=true` だけでは保存しない。
- 現行 `duplicate_key` はファイル名由来のローカルPoC簡易キーのまま維持し、M1では本格キーへ差し替えない。

## transition_countup

- `transition_countup_*` は保存対象外。
- `transition_countup_*` は `result_shape_candidate=true` でもよい。
- `transition_countup_*` は `result_candidate=false` を期待する。
- `transition_countup_*` は `event_type=rejected_transition` として扱う。
- `transition_countup_*` は confirmed-events OCR対象外。

## OCR対象

- `--ocr-target result-candidate` は従来互換として `result_candidate=true` を対象にする。
- `--ocr-target confirmed-events` は `confirmed_result=true` かつ `duplicate=false` のみを対象にする。
- confirmed-events は保存直前OCR相当の評価として扱う。
- duplicate は confirmed-events OCR対象外。
- rejected transition は confirmed-events OCR対象外。
- 未確定候補は confirmed-events OCR対象外。
- dry-run sequence scenario の confirmed-events OCR対象は `confirmed_result=true` かつ `duplicate=false` のみ。
- `score_ocr_summary.json` では duplicate と rejected transition を `skipped_duplicate_count` / `skipped_rejected_transition_count` で読み分けられる。

## expected coverage

- confirmed-events 対象で expected columns が全試行にあるROIは `evaluated` として扱う。
- confirmed-events 対象で expected columns が一部だけあるROIは `partially_evaluated` として扱う。
- confirmed-events 対象で expected columns がないROIは `no_expected_values` として扱う。
- `no_expected_values` はOCR成功扱いにしない。
- `partially_evaluated` は暫定評価として扱う。
- `evaluated` / `partially_evaluated` / `no_expected_values` の意味を変えない。
- timestamped が生成する manifest は metadata の期待値列を保持する。
- manifest 再実行でも expected coverage の読み替えを維持する。
- profile比較では `no_expected_values` の `reference_profiles` は目視参考に留め、採用根拠にしない。
- profile比較では `partially_evaluated` の推奨は暫定扱いにし、期待値不足を埋めてから判断する。
- profile比較では `evaluated` かつ `recommended_profiles` があるROIだけを `recommendation_readiness=adoption_candidate` として読む。
- profile比較では `no_expected_values` の `recommended_profiles` を空に保ち、`recommendation_readiness=reference_only` として読む。
- profile比較では `partially_evaluated` の推奨を `recommendation_readiness=tentative` として読む。
- `ex_score` の `low-threshold` は confirmed-events 対象かつ expected coverage が `evaluated` の場合だけ採用候補として読む。
- `score_ocr_profiles_summary.json` と `ocr_roi_report.md` では default profile と推奨profileの差を確認できる補助情報を維持する。

## OCR出力互換

- `score_ocr.csv` の既存列を維持する。
- profile比較を有効にしても legacy `score_ocr.csv` は default profile の互換出力として維持する。
- OCRエンジンがない環境でもPoCは落とさず、`engine_unavailable` として記録する。
- 前処理画像を保存して目視確認できる状態を維持する。

## ROI方針

- ROI座標は 1280x720 基準。
- 実画像サイズへ線形スケールする。
- OCR前処理改善のために、まずは局所前処理で試す。
- ROI座標定義の大変更は別フェーズとして扱う。

## Git管理

- スクリーンショット画像をコミットしない。
- `samples/screenshots/metadata.csv` をコミットしない。
- `data/` をコミットしない。
- `logs/` をコミットしない。
- ローカルDBをコミットしない。
- PoC出力や解析ログをコミットしない。

## 代表検証コマンド

```powershell
python -m ruff check tools\vision_poc pyproject.toml
python -m ruff check tests
python -m compileall tools\vision_poc
python -m pytest tests
python -m tools.vision_poc --sequence-mode timestamped --ocr-target confirmed-events --ocr-rois all --ocr-profile all
python -m tools.vision_poc --sequence-mode manifest --frame-manifest data\vision_poc_timestamped\frame_manifest.csv --frame-root samples\screenshots --ocr-target confirmed-events --ocr-rois all --ocr-profile all
```

dry-run capture provider 入口を変更した場合は、生成manifestを manifest mode で再読込するコマンドも実行する。
dry-run sequence scenario 入口を変更した場合も、生成manifestを manifest mode で再読込するコマンドを実行する。

## 実装を分けるべきもの

以下はM0/M1のついでに混ぜない。

- 本番キャプチャAPI
- 実キャプチャデバイス依存コード
- 常駐監視ループ
- 非同期処理
- DB保存
- OCR方式刷新
- duplicate key の本格差し替え
- ROI座標定義の大変更
