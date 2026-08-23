# 設計資料

このディレクトリは、GP Score Logの用語、pipeline、入力、event確定、正式保存、データ、I/O、回帰条件を固定する設計資料である。プロジェクト全体の文書案内は[`../README.md`](../README.md)を参照する。

工程コード、field名、status名は[`00_glossary.md`](00_glossary.md)を正本とする。対象や工程を省略せず、`M5 jacket match`、`M5b jacket reference catalog`、`M5c developer-only collector`、`M7 result-text feature`、`M7a digit recognition`のように記載する。`OCR`も対象fieldと工程を付けて読む。

## 共通して最初に読む資料

1. [`00_glossary.md`](00_glossary.md): 用語と工程の意味
2. [`01_pipeline_fsm.md`](01_pipeline_fsm.md): 画面取得から正式保存までの全体像
3. [`03_event_and_save_boundary.md`](03_event_and_save_boundary.md): confirmed event、formal evidence、保存可否
4. [`06_regression_guard.md`](06_regression_guard.md): 維持すべき拒否条件と回帰確認

## 責務別の正本

| 責務 | 正本 | 読み方 |
|---|---|---|
| frame入力、manifest、capture | [`02_frame_input_contract.md`](02_frame_input_contract.md) | `FrameInput`と入力modeの契約 |
| event確定、duplicate、正式保存境界 | [`03_event_and_save_boundary.md`](03_event_and_save_boundary.md) | candidateをformal値へ暗黙昇格させない境界 |
| 概念data model | [`04_data_model.md`](04_data_model.md) | DB間の責務と値の由来 |
| local storage、artifact、log、migration I/O | [`05_storage_io_spec.md`](05_storage_io_spec.md) | file配置と副作用境界 |
| regression | [`06_regression_guard.md`](06_regression_guard.md) | 変更時に壊してはいけない挙動 |
| M4 master DB | [`08_master_db_generation.md`](08_master_db_generation.md) | 生成元、schema、配布境界 |
| M5 master match、M5b catalog、M5c collector | [`09_master_match_poc.md`](09_master_match_poc.md) | 候補観測、runtime参照、developer-only整備 |
| M8 formal personal score DB | [`10_personal_score_db_schema.md`](10_personal_score_db_schema.md) | 現行schema、transaction、migration、viewer境界 |

SQLiteの正確なtable、column、index、metadata、migrationは、M4 master DBでは`08_master_db_generation.md`、正式個人スコアDBでは`10_personal_score_db_schema.md`を優先する。`04_data_model.md`は概念上の責務を説明する。

## 実行方法と利用者向け資料

- 通常の導入と操作: [`../user-guide.md`](../user-guide.md)
- Windowsアプリのbuild、runtime、package: [`../../app/README.md`](../../app/README.md)
- 画像解析PoCのcommandと出力: [`../../tools/vision_poc/README.md`](../../tools/vision_poc/README.md)
- M5c developer-only collector: [`../../tools/jacket_catalog_collector/README.md`](../../tools/jacket_catalog_collector/README.md)

## 履歴資料

[`07_m3_chart_field_review.md`](07_m3_chart_field_review.md)は、Git管理しないlocal `metadata.csv`と画像素材に対するM3実測結果を保持する。M5 jacket matchの信号選定過程は[`09_master_match_evaluation_history.md`](09_master_match_evaluation_history.md)に残す。いずれも現在の一般仕様としてではなく、期待値修正と採用判断の根拠として読む。

architecture decisionの一覧は[`../adr/README.md`](../adr/README.md)、完了済みM0の検収結果は[`../checklists/m0_input_boundary.md`](../checklists/m0_input_boundary.md)に残す。

## 更新ルール

- `result_candidate`、`confirmed_result`、`duplicate`、confirmed-eventsの意味を変えた場合は`00_glossary.md`、`01_pipeline_fsm.md`、`03_event_and_save_boundary.md`、`06_regression_guard.md`を確認する。
- manifest、capture、`FrameInput`を変えた場合は`02_frame_input_contract.md`と`05_storage_io_spec.md`を確認する。
- DB責務、保存先、transaction、migrationを変えた場合は`04_data_model.md`、`05_storage_io_spec.md`、`10_personal_score_db_schema.md`を確認する。
- M4 master DB生成を変えた場合は`08_master_db_generation.md`を更新する。
- M5 master matchのstatus、M5b catalog、M5c developer-only collectorを変えた場合は`09_master_match_poc.md`と該当component READMEを確認する。
- 公開挙動や利用者の操作が変わらない内部実装だけの変更では、説明を増やすためだけにdocsを更新しない。
