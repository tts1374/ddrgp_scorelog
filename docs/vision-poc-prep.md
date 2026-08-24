# 画像解析PoCのローカル評価準備

この文書は、`tools/vision_poc`をlocal screenshot素材で再現・評価するための準備をまとめる。command、option、生成物の詳細は[`../tools/vision_poc/README.md`](../tools/vision_poc/README.md)を正本とする。

通常のRelease runtimeはWindowsアプリ内のapp-owned画像認識を使い、Python、Tesseract、repository内のlocal screenshotやtemplateを探索しない。Vision PoCは、分類、ROI、候補観測、confirmed-events境界を検証するdeveloper向け経路である。

## 正本

| 内容 | 正本 |
|---|---|
| 用語と工程名 | [`design/00_glossary.md`](design/00_glossary.md) |
| pipeline全体 | [`design/01_pipeline_fsm.md`](design/01_pipeline_fsm.md) |
| `FrameInput`とmanifest | [`design/02_frame_input_contract.md`](design/02_frame_input_contract.md) |
| event確定と保存境界 | [`design/03_event_and_save_boundary.md`](design/03_event_and_save_boundary.md) |
| 出力場所 | [`design/05_storage_io_spec.md`](design/05_storage_io_spec.md) |
| 回帰条件 | [`design/06_regression_guard.md`](design/06_regression_guard.md) |
| commandと出力の読み方 | [`../tools/vision_poc/README.md`](../tools/vision_poc/README.md) |

## local素材

次の素材はlocal専用で、Git管理しない。

- `samples/screenshots/organized/`配下のPNG
- `samples/screenshots/metadata.csv`
- chart-field、digit、jacketなどのlocal template
- 実capture画像とmanifest

基本評価は1280x720のDDR GRAND PRIX画面を使う。ROIは`tools/vision_poc/runner.py`の`ROI_DEFINITIONS`と関連testを実装上の正本とし、入力画像サイズに合わせて既存規則でscaleする。

### screenshot分類

`metadata.csv`の`screen_type`は、分類評価の期待値として次を使う。

| `screen_type` | 期待する扱い |
|---|---|
| `result` | `result_candidate=true`の正例 |
| `transition` | RESULT遷移やcountupを含む非保存例 |
| `song_select` | 選曲画面の非保存例 |
| `gameplay` | play中画面の非保存例 |
| `menu_setup` | menu、待機、設定画面の非保存例 |

`transition_countup_*`はRESULT形状を持っていても、`event_type=rejected_transition`、`confirmed_result=false`として評価する。

### metadata

`metadata.csv`には、少なくとも画像を特定する列と`screen_type`を用意する。OCR、M3 result field observation、M7a digit recognitionを評価する場合は、対象fieldのexpected列をlocal素材の画面表示と照合して追加する。

expected値は評価用であり、formal play値や正式保存根拠へ転記しない。local素材のreview結果を共有する必要がある場合は、画像や`metadata.csv`本体ではなく、必要な判断だけをGit管理可能なreview記録へ残す。

## 評価の実行

repository rootで次を実行する。

```powershell
python -X utf8 -m tools.vision_poc
```

既定のmetadata modeは`data/vision_poc/`へ出力する。timestamped、manifest、dry-run、各評価optionのcommandと出力先は`tools/vision_poc/README.md`に従う。

local素材がない環境では、素材依存の評価だけを未実施として扱う。代替画像の生成やGit追加は行わない。

## 結果の確認

最低限、次を確認する。

- `summary.json`: result/non-result分類数と全体summary
- `misclassifications.md`: 誤分類代表
- `result_events.csv`: `confirmed_result`、`event_type`、`duplicate`、`confirmation_mode`
- `result_events_summary.json`: confirmed、duplicate、rejected transitionの集計
- `rois/`: 現在のROI切り出し

対象工程を実行した場合だけ、対応するOCR、M3、M5、M7、M8のreportを追加確認する。候補status、`recognized_digits`、expected一致、preview statusは評価材料であり、正式DB保存成功として読まない。

## event境界

- `result_shape_candidate`: RESULTらしい形状の検出。保存候補とは限らない。
- `result_candidate`: 単発frameの保存候補。保存確定ではない。
- `confirmed_result`: 継続条件を満たした保存直前event。duplicateになり得る。
- confirmed-events対象: `confirmed_result=true`かつ`duplicate=false`。

timestamped、manifest、dry-runでは`confirmation_mode=time`と単調増加する`timestamp_ms`を維持する。metadata modeのframe-based confirmationと混同しない。

Vision PoCの`score:` / `file:`形式の`duplicate_key`はlocal評価専用である。WindowsアプリのRESULT groupingや正式DBのduplicate keyとは別の境界として扱う。

## 変更時の確認

分類、ROI、event確定、OCR・数字認識、出力契約を変更した場合は、変更責務に対応するtestと`tools/vision_poc/AGENTS.md`のValidationを実行する。

確認結果では次を分けて報告する。

- 実行したlocal素材とmode
- result、non-result、rejected transition、duplicateの集計
- expected coverageと認識status
- 実行できなかった素材依存検証
- `data/`、`logs/`、local素材がGit差分へ入っていないこと
