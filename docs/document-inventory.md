# docs棚卸し

確認日: 2026-08-23

この文書は、`docs/`配下の資料について、現行の役割、陳腐化リスク、次の扱いを整理した監査記録である。製品仕様や設計契約の正本ではない。整備を完了した資料は分類と対応状況を更新する。

## 判定方法

最終更新日の古さだけでは判定せず、次を確認した。

- `README.md`、`AGENTS.md`、他のdocs、実装からの参照
- `docs/implementation-roadmap.md`が示す現在の実装段階
- 文書内の「未実装」「将来」「未決事項」と現在コードの整合
- 現行仕様、履歴記録、作業用資料、視覚参考のどれを担うか

分類は次の意味で使う。

| 分類 | 意味 |
|---|---|
| 現行 | 現在の正本または正本を支える資料として維持する |
| 要更新 | 現行情報と実装前・旧phaseの記述が混在しており、現在状態から再構成する |
| 履歴 | 意思決定、完了checklist、実測結果として保持し、現行仕様と区別する |
| 整理候補 | 参照がない、用途が終了している、または`docs/`以外が適切。削除・移動・現行資料への統合を判断する |

## 優先対応

### 優先度A: 第1バッチ対応済み

| ファイル | 観測した状態 | 次の扱い |
|---|---|---|
| `docs/design/01_pipeline_fsm.md` | `MASTER_MATCH_READY`、`SAVE_READY`、`SAVED`を未実装とする記述と、M10まで実装済みの現状が一致しなかった | Windows capture、app-owned画像認識、formal evidence、正式保存statusを含む現行pipelineへ再構成済み |
| `docs/design/04_data_model.md` | 解決済みのschema、migration、duplicate境界が未決事項に残っていた | 現行の正本関係、formal duplicate、残るcleanup境界へ更新済み |
| `docs/vision-poc-prep.md` | 実装前の手順が中心だったが、`tools/vision_poc/AGENTS.md`から現在も参照されていた | local素材を使う現在の評価準備へ再構成し、参照元の役割説明も更新済み |
| `docs/design/README.md` | 読む順番がM0/M1中心で、現在の責務別入口が弱かった | 設計正本を責務別に案内するindexへ再構成済み |

### 優先度B: 第2バッチ対応済み

| ファイル | 観測した状態 | 次の扱い |
|---|---|---|
| `docs/design/02_frame_input_contract.md` | 実装済みのWPF capture契約と「将来の実capture API」が同居していた | developer `FrameInput`とapp-owned captureの現行共通契約へ更新済み |
| `docs/design/05_storage_io_spec.md` | 評価入出力、現行アプリ保存、migration、release更新契約が追記型で集積していた | 現行storage契約へ直し、自動cleanupとrotationの未導入境界を明示済み |
| `docs/design/09_master_match_poc.md` | current runtime、catalog、collector契約とlocal評価経緯が混在していた | 現行責務を先頭で整理し、評価経緯を独立した履歴資料へ分離済み |
| `docs/design/10_personal_score_db_schema.md` | 現行version 3契約と解決済みの未決事項が同居していた | schema version、migration、ID・duplicate・formal evidence境界を現行化済み |
| `docs/wireframe/admin-and-collection-status.md` | Issue #75の判断と完了条件を保持していた | collector READMEとmockを現行正本として案内する履歴資料へ位置づけ済み |

### 優先度C: 最終判定・整理済み

| 対象 | 最終判定 | 根拠と処置 |
|---|---|---|
| `docs/task-prompts/m0_dry_run_sequence_scenario.md` | 削除 | 完了済みM0の固定branch付きtask prompt。現行契約はFrameInput・回帰資料に反映済み |
| `docs/wireframe/chart-detail-mock.html` | 現行維持 | Issue #127で実装された楽曲・譜面詳細画面の視覚参考。`screen-spec.md`から導線を追加 |
| `docs/wireframe/wireframe1.png` | 削除 | 現行画面構成と異なる初期総合案で参照なし。画面正本と個別mockで代替可能 |
| `docs/wireframe/wireframe2.png` | 削除 | 現行画面構成と異なる初期総合案で参照なし。画面正本と個別mockで代替可能 |
| `docs/wireframes/manual-review-55-56.svg` | 削除 | 完了Issue向けの初期visualで参照なし。現行collector README、mock、実装が責務を保持 |
| `docs/launch-jacket_catalog.ps1` | 移設 | `tools/jacket_catalog_collector/`へ移し、配置場所基準で起動できるPowerShell launcherへ更新 |
| `docs/launch-jacket_catalog.bat` | 移設 | 同じcollector directoryへダブルクリック用launcherとして移動 |

履歴資料は内容を書き換えず、先頭に履歴・検収記録としての位置づけと現行正本への導線を追加した。launcherはcollector directoryへ移設し、削除したtracked fileはGit履歴から復元できる。

## 全ファイル一覧

### 要求・進捗・利用者向け資料

| ファイル | 分類 | 現在の役割と次の扱い |
|---|---|---|
| `docs/README.md` | 現行 | プロジェクト全体の文書案内。読者別入口、正本の優先順位、更新ルールを管理する |
| `docs/requirements.md` | 現行 | 製品要求の正本。実装済みの外部挙動との整合を維持する |
| `docs/implementation-roadmap.md` | 現行 | current phaseとrelease readinessの正本。release状態の変化時に更新する |
| `docs/user-guide.md` | 現行 | 通常利用手順の正本。root/app READMEから参照されている |
| `docs/screenshot-collection.md` | 履歴 | 初期のsample収集方針と観察記録。現行評価準備とcapture契約への導線を明示済み |
| `docs/vision-poc-prep.md` | 現行 | local screenshot素材、基本実行、結果確認、event境界の評価準備 |

### 設計資料

| ファイル | 分類 | 現在の役割と次の扱い |
|---|---|---|
| `docs/design/README.md` | 現行 | 設計資料を共通入口と責務別正本へ案内するindex |
| `docs/design/00_glossary.md` | 現行 | milestoneコード、field名、status名の正本 |
| `docs/design/01_pipeline_fsm.md` | 現行 | Windowsアプリとdeveloper評価経路を含むpipeline全体 |
| `docs/design/02_frame_input_contract.md` | 現行 | developer FrameInputとWPF captureに共通する入力・manifest契約 |
| `docs/design/03_event_and_save_boundary.md` | 現行 | event確定と正式保存境界の正本。実測履歴は必要な根拠として保持する |
| `docs/design/04_data_model.md` | 現行 | 概念data modelとDB間の責務。正確なschemaはM4/M8資料へ委譲する |
| `docs/design/05_storage_io_spec.md` | 現行 | storage、artifact、migration、application/reference data更新境界 |
| `docs/design/06_regression_guard.md` | 現行 | 回帰防止契約。互換性・拒否条件の記録を保持する |
| `docs/design/07_m3_chart_field_review.md` | 履歴 | Git管理しないlocal素材のM3実測記録。複数の現行資料から参照されるため保持する |
| `docs/design/08_master_db_generation.md` | 現行 | M4 master DB生成契約 |
| `docs/design/09_master_match_poc.md` | 現行 | M5 master match、M5b catalog、M5c collectorの責務と現行契約 |
| `docs/design/09_master_match_evaluation_history.md` | 履歴 | M5 jacket matchのlocal評価、曖昧候補、title補助選定の記録 |
| `docs/design/10_personal_score_db_schema.md` | 現行 | 正式個人スコアDB schema version 3、migration、保存・viewer境界 |

### ADR・checklist・task資料

| ファイル | 分類 | 現在の役割と次の扱い |
|---|---|---|
| `docs/adr/0001-foundational-poc-boundaries.md` | 履歴 | Accepted ADR。決定時点のContextを保持し、現行設計への参照を補う |
| `docs/adr/README.md` | 現行 | ADR番号、Status、対象decision、主要正本のindex |
| `docs/adr/0002-app-owned-formal-save-boundary.md` | 履歴 | app-owned recognition、formal evidence、confirmed event identityのAccepted decision |
| `docs/adr/0003-database-responsibility-and-protection.md` | 履歴 | DB責務分離と正式個人スコアDB保護のAccepted decision |
| `docs/adr/0004-separate-application-and-reference-data-updates.md` | 履歴 | application packageとreference data set更新分離のAccepted decision |
| `docs/checklists/m0_input_boundary.md` | 履歴 | 完了済みM0の検収記録。現行入力契約と回帰条件への導線を明示済み |

### 画面仕様・mock・visual asset

| ファイル | 分類 | 現在の役割と次の扱い |
|---|---|---|
| `docs/wireframe/screen-spec.md` | 現行 | 画面情報、操作、表示条件の正本 |
| `docs/wireframe/design-system.md` | 現行 | visual表現とcomponentの正本 |
| `docs/wireframe/home-mock.html` | 現行 | home画面の視覚参考。関連mockから参照される |
| `docs/wireframe/chart-bests-mock.html` | 現行 | 自己ベスト画面の視覚参考。screen specと関連mockから参照される |
| `docs/wireframe/chart-detail-mock.html` | 現行 | 楽曲・譜面詳細画面の視覚参考。screen specから参照する |
| `docs/wireframe/play-history-mock.html` | 現行 | play履歴画面の視覚参考。関連mockから参照される |
| `docs/wireframe/settings-mock.html` | 現行 | 設定画面の視覚参考 |
| `docs/wireframe/data-management-mock.html` | 現行 | data管理画面の視覚参考 |
| `docs/wireframe/jacket-catalog-collector-mock.html` | 現行 | developer collector UIの正本としてtool READMEから参照される |
| `docs/wireframe/admin-and-collection-status.md` | 履歴 | Issue #75のcollector UI判断と完了条件。現行正本への導線を明示済み |
| `docs/wireframe/screen-mock-shared.css` | 現行 | 複数mockの共有style |

## 集計

| 分類 | 件数 |
|---|---:|
| 現行 | 27 |
| 要更新 | 0 |
| 履歴 | 9 |
| 整理候補 | 0 |
| 合計 | 36 |

## 次回棚卸しの契機

公開操作、永続化契約、主要画面構成、milestone状態が変わったとき、または参照のない資料が追加されたときに再度棚卸しする。
