# プロジェクト文書案内

このディレクトリは、GP Score Logの要求、現在の実装段階、利用手順、設計契約、画面仕様、検証記録を役割別に管理する。

GP Score Logは、DDR GRAND PRIXのリザルト画面をWindowsアプリが読み取り、必要な根拠を確認できたplayだけを正式個人スコアDBへ保存する。Phase 1の主要機能は実装済みで、現在はrelease quality closeout中である。現在地の詳細は[`implementation-roadmap.md`](implementation-roadmap.md)を参照する。

## 読者別の入口

| 読みたいこと | 正本 |
|---|---|
| インストール、通常操作、backup / restore、トラブル対応 | [`user-guide.md`](user-guide.md) |
| 製品が満たす要求 | [`requirements.md`](requirements.md) |
| milestoneの状態とreleaseまでの現在地 | [`implementation-roadmap.md`](implementation-roadmap.md) |
| 用語、pipeline、保存境界、DB、回帰条件 | [`design/README.md`](design/README.md) |
| 画面の情報、操作、表示条件 | [`wireframe/screen-spec.md`](wireframe/screen-spec.md) |
| 色、余白、component、状態表現 | [`wireframe/design-system.md`](wireframe/design-system.md) |
| Windowsアプリのbuild、runtime、package | [`../app/README.md`](../app/README.md) |
| 画像解析PoCの実行と出力 | [`../tools/vision_poc/README.md`](../tools/vision_poc/README.md) |
| M5c developer-only collector | [`../tools/jacket_catalog_collector/README.md`](../tools/jacket_catalog_collector/README.md) |
| master DB生成 | [`../master/README.md`](../master/README.md) |

利用者向けの説明では、内部のmilestoneコードや画像解析用語を前提にしない。開発資料で工程名、field名、status名を使う場合は[`design/00_glossary.md`](design/00_glossary.md)を正本とする。

## 現行システムの資料構成

### 画面取得から正式保存まで

Windowsアプリは対象windowをcaptureし、RESULT画面の確定、画像認識、master/catalogとの整合、formal evidenceの検査を順に行う。正式保存条件を満たした場合だけ、source capture、play、analysisをtransactionで正式個人スコアDBへ記録する。

- pipeline全体: [`design/01_pipeline_fsm.md`](design/01_pipeline_fsm.md)
- frame入力: [`design/02_frame_input_contract.md`](design/02_frame_input_contract.md)
- event確定と保存境界: [`design/03_event_and_save_boundary.md`](design/03_event_and_save_boundary.md)
- 正式個人スコアDB: [`design/10_personal_score_db_schema.md`](design/10_personal_score_db_schema.md)

### データと保存場所

M4 master DB、M5b jacket reference catalog、正式個人スコアDB、評価用DBは別file・別schema・別責務として扱う。local capture、解析artifact、log、生成DBはGit管理しない。

- 概念data model: [`design/04_data_model.md`](design/04_data_model.md)
- storageとI/O: [`design/05_storage_io_spec.md`](design/05_storage_io_spec.md)
- 回帰条件: [`design/06_regression_guard.md`](design/06_regression_guard.md)
- M4 master DB生成: [`design/08_master_db_generation.md`](design/08_master_db_generation.md)
- M5 master matchとcatalog: [`design/09_master_match_poc.md`](design/09_master_match_poc.md)

### 履歴と検証記録

ADR、完了済みchecklist、local素材のreview記録は、現在の操作や仕様ではなく決定・検証の根拠として読む。

- ADR index: [`adr/README.md`](adr/README.md)
- 完了checklist: [`checklists/`](checklists/)
- M3 chart-field実測記録: [`design/07_m3_chart_field_review.md`](design/07_m3_chart_field_review.md)
- M5 master match評価履歴: [`design/09_master_match_evaluation_history.md`](design/09_master_match_evaluation_history.md)
- collector UI改善の設計記録: [`wireframe/admin-and-collection-status.md`](wireframe/admin-and-collection-status.md)
- 初期sample収集条件: [`screenshot-collection.md`](screenshot-collection.md)
- docs棚卸し: [`document-inventory.md`](document-inventory.md)

## 正本の優先順位

同じ内容を複数資料が説明している場合は、対象に最も近い正本を優先する。

1. 利用者の操作と表示は`user-guide.md`と`wireframe/`。
2. milestoneの現在地は`implementation-roadmap.md`。
3. field、status、工程名は`design/00_glossary.md`。
4. 保存可否とformal evidenceは`design/03_event_and_save_boundary.md`。
5. 正式DB schemaとmigrationは`design/10_personal_score_db_schema.md`。
6. component固有の実行方法は各componentのREADME。

正本同士が矛盾する場合は、古い説明を別資料へ移して併記せず、実装契約と現在の挙動を確認して正本側を修正する。

## 更新ルール

- 公開操作、CLI、永続化形式、ユーザー手順、判定契約が変わった場合だけ、影響する正本を同じ変更で更新する。
- milestone状態が変わった場合は`implementation-roadmap.md`を更新する。
- 新しい工程名、field名、status名を追加した場合は`design/00_glossary.md`を更新する。
- 完了した検証や意思決定は履歴資料として保持し、現在の仕様説明へ混在させない。
- 用途を終えたtask prompt、mock、起動補助は参照元を確認し、現行資料へ統合できた場合だけ整理する。
