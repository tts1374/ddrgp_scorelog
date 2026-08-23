# パイプラインFSM設計

DDR GRAND PRIXのwindow取得から、RESULT画面の確定、画像認識、master/catalog整合、formal evidence検査、正式個人スコアDBへの保存または非保存までを共通の境界で説明する。

通常のRelease runtimeはWindowsアプリ内のapp-owned実装を使う。`tools/vision_poc`はlocal素材で分類、ROI、候補観測、confirmed-events境界を再現・評価するdeveloper向け経路であり、PoC出力を正式値として保存しない。

## 目的

- 単発frameのRESULTらしさと、継続条件を満たしたconfirmed eventを分離する。
- `confirmed_result=true`でもduplicateならplayを作らない。
- candidate、OCR raw、expected値、preview材料とformal evidenceを分離する。
- `saved`、duplicate、excluded、unresolved、解析失敗、DB拒否を同じ成功状態へ丸めない。
- Windows captureとmanifest replayが、同じevent確定・保存境界を検証できるようにする。

## 実行経路

### Windowsアプリ

Windowsアプリは、検出または明示選択したDDR GRAND PRIX windowをcaptureする。M4 master DBとM5b jacket reference catalogを別々にread-only検査し、両方がcompatibleな場合だけRESULT解析と正式保存workflowへ進む。

RESULT画面ではapp-owned画像認識が数字、状態、ジャケット、譜面条件を評価する。current master/catalogと一意に整合し、fieldごとのformal evidenceが揃ったconfirmed eventだけを`app-owned formal evidence bridge`から正式保存入口へ渡す。

### Vision PoCとoffline replay

developer向け経路は次の入力を`FrameInput`境界へ揃える。

- metadata mode
- timestamped mode
- manifest mode
- dry-run capture provider
- capture-only manifestのoffline replay

この経路は分類、confirmed-events、M3/M5/M7候補観測、M8 preview、formal workflowの結合確認に使う。candidate材料やpreview rowは、正式保存根拠として採用しない。

## 入力状態

### `NO_SOURCE`

frame入力元がない状態。Windowsアプリでは対象window待ち、明示停止、DB/runtime異常による開始抑止を区別し、`docs/design/00_glossary.md`の`MonitoringState`を使う。

### `FRAME_SOURCE_READY`

frame入力元と、処理開始に必要なruntime dataが準備できた状態。

- Windowsアプリ: capture対象window、compatibleなM4 master DB、compatibleなM5b jacket reference catalog
- developer経路: metadata、timestamped、manifest、dry-runまたはoffline replay入力

### `FRAME_RECEIVED`

1frame分の入力を受け取った状態。PoCでは`FrameInput`、Windowsアプリではapp-owned capture frameがこの境界を担う。timestampを持つ入力はtime-based confirmation、metadata modeはframe-based confirmationとして評価できる。

## frame分類とevent確定

### `CLASSIFIED_NON_RESULT`

RESULT候補ではないframe。`result_shape_candidate=false`かつ`result_candidate=false`で、candidate streakをresetする。

### `RESULT_SHAPE_DETECTED`

RESULTに似た形状を検出したが、保存候補には採用しないframe。カウントアップ中などは`result_shape_candidate=true`になり得るが、`result_candidate=false`を維持する。

### `RESULT_CANDIDATE`

単発frame分類でRESULT保存候補に見える状態。継続時間または継続frame数を満たすまで保存処理へ進めない。

### `CONFIRMED_RESULT`

継続条件を満たした保存直前event。`confirmed_result=true`だけでは正式保存可能を意味せず、duplicate判定、画像認識、master/catalog整合、formal evidence検査が後続する。

### `DUPLICATE_RESULT`

同一eventまたは正式DBのduplicateとしてplayを追加しない状態。

- Vision PoCの`score:` / `file:`形式はlocal分類評価専用の簡易`duplicate_key`である。
- WindowsアプリのRESULT fingerprintは同じ画面のframe groupingにだけ使う。
- 正式`duplicate_key`はconfirmed capture event IDから構築し、正式DB保存直前にも既存`plays`と照合する。

正式duplicate collisionではplayを作らず、source captureとduplicate理由を持つanalysisを同じtransactionで記録する。

### `REJECTED_TRANSITION`

RESULT形状はあるが保存不可の遷移として除外した状態。`confirmed_result=false`を維持し、正式保存へ進めない。

## 解析と正式保存

### `OCR_READY` / `OCR_EVALUATED`

Vision PoCでconfirmed-eventsを対象にOCRやM7a digit recognitionを評価する境界。`confirmed_result=true`かつ`duplicate=false`だけを対象にする。

`evaluated`、`partially_evaluated`、`no_expected_values`は評価coverageであり、formal evidenceやDB保存成功を表さない。通常のRelease runtimeはPython/Tesseract経路を呼ばず、app-owned画像認識を使う。

### `MASTER_MATCH_READY`

曲・譜面候補とcurrent master/catalogを照合できる材料が揃った概念上の境界。Windowsアプリでは`RESULT同定・譜面の正式画像認識`が一意整合した場合だけ`RESULT同定根拠`として採用する。M5候補観測やOCR文字列だけではこの境界をformal値として通過しない。

### `SAVE_READY`

`RESULT同定根拠`、`RESULT数値認識根拠`、`RESULT状態認識根拠`、`capture event根拠`からformal play値を構築し、strict validationが成功した状態。正式保存入口は`PersonalScoreDbSaveInput`だけを受け取る。

### `SAVED`

正式DB transactionが完了し、`workflow_status=saved`、`written=true`、非nullの`play_id`が揃った状態。この組だけをplay履歴へ表示する。

### `SKIPPED`

playを追加しない終端状態の総称。外部へは原因を次のstatusに分けて返す。

| status | 意味 |
|---|---|
| `duplicate` | 同一eventまたは既存正式playとの重複 |
| `excluded` | 保存方針上playを作らず、source/analysisを記録できた |
| `unresolved` | formal evidenceまたは必須値が揃わず、正式保存入力を構築できない |
| `analysis_failed` | RESULT解析またはworkflow処理に失敗した |
| `db_rejected` | 正式DBの準備・互換性・書込み境界で拒否された |

これらを`saved`へ昇格させない。`unresolved`やDB拒否では、各境界の契約に従い既存DBを変更しない。

## 状態遷移概要

```text
NO_SOURCE
  -> FRAME_SOURCE_READY
  -> FRAME_RECEIVED

FRAME_RECEIVED
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

CONFIRMED_RESULT
  -> RESULT画像認識
  -> master/catalog整合
  -> app-owned formal evidence bridge
      -> unresolved / excluded
      -> SAVE_READY
          -> SAVED
          -> db_rejected / analysis_failed
```

## 正本の分担

- `FrameInput`、manifest、capture: [`02_frame_input_contract.md`](02_frame_input_contract.md)
- confirmed event、formal evidence、duplicate: [`03_event_and_save_boundary.md`](03_event_and_save_boundary.md)
- status、工程名: [`00_glossary.md`](00_glossary.md)
- storageとartifact順序: [`05_storage_io_spec.md`](05_storage_io_spec.md)
- regression条件: [`06_regression_guard.md`](06_regression_guard.md)
- 正式DB schema、transaction、migration: [`10_personal_score_db_schema.md`](10_personal_score_db_schema.md)
