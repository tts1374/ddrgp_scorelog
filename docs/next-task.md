# 次PR作業仕様

`C:\work\ddrgp_scorelog` で作業してください。`AGENTS.md`、`docs/implementation-roadmap.md` の M5b、M5 jacket match設計・実装・既存coverage出力、frame input/capture保存方針を読み、既存のローカル画像、capture session、特徴量、DB、`data/`、`logs/`、生成物を保護してください。

## 推奨モデル

GPT-5.6 Sol

## 推論レベル

high

ローカル非共有素材、M4 master identity、1:N参照、再投入冪等性、master更新時のorphan、誤自動確定0件を同時に扱うためです。正式個人スコアDBや保存workflowは変更しないため、通常はxhighまで上げません。

## 作業ブランチ

```powershell
codex/m5b-local-jacket-reference-catalog
```

## Goal

song select grid由来のローカルjacket、title、artist観測をM4 masterへ安全に紐付け、GPプレー可能な全songの参照状態を機械的に確認できるローカルjacket参照カタログを追加します。同じcaptureの再投入で参照を増やさず、複数jacket参照、要レビュー、未収集、未解決、master更新時のorphanを区別します。

このPRはM9の正式保存workflow接続後、監視UI・task trayへ進む前の独立M5bです。正式保存workflow、WPF監視UI、正式個人スコアDB schema、ゲーム操作自動化、公式サイト画像scrapingには進みません。

## Deliverables

- ローカルcatalog schemaとversioned loader/writerを追加し、master version、`song_id`、review status、feature extractor version、source image hash、jacket `thumbnail_rgb`、histogram、dHash相当、title/artist観測、作成・更新時刻を保持する。
- catalogは `data/` 配下の明示pathだけへ生成し、正式個人スコアDB、M8 preview DB、master DB、通常analysis logと相互受入れしない。
- songとjacket referenceを1:Nにし、同一source hash/同一captureの再投入を冪等に扱う。別jacketは同一songへ追加できる。
- canonical title + artist完全一致、一意alias一致など既存masterから一意と証明できる条件だけをauto-confirmする。複数候補、低confidence、OCR/抽出失敗は候補と理由を保持して `needs_review` / `unresolved` にする。
- 対象master versionの `grand_prix_play_available=true` 全songを分母に、`referenced`、`needs_review`、`uncollected`、`unresolved`、`orphaned` を集計するcoverage JSON/CSV/Markdownを追加する。
- capture済み対象song全体を分母としたauto-confirm rate、理由別失敗件数、既知誤確定監査結果を出す。失敗行を分母から除外しない。
- master version変更時に、存在しないsong_id、GP対象外化、identity変更、再レビュー対象をread-onlyで検出し、自動で別songへ付け替えない。
- 生画像を削除したfixture状態でも、永続特徴量から既存M5 jacket照合を再実行できるapplication入口と回帰testを追加する。
- `tools/vision_poc/README.md`、M5/master match設計、storage I/O、regression、roadmap docsを実績に同期する。

## Invariants

- capture、crop、16x16 RGBを含む特徴量catalog、review結果、ローカルcatalog DB/JSONはGit、CI artifact、Release、通常ログへ含めない。
- ユーザーの明示操作なしにゲームを操作せず、対象window自動探索、song select自動巡回、公式サイト画像scrapingを行わない。
- expected title/song、近傍候補、OCR raw、曖昧aliasを正式なcatalog `song_id` へ寄せない。
- jacket参照なし、曖昧、未解決ではtitle/artistだけのruntime fallbackを行わず、正式playへ昇格しない。
- M4 master DBはread-only、正式個人スコアDB schema version 1、capture save workflow、manual入口、duplicate、transactionを変更しない。
- `source_captures`、`plays`、`analysis_logs`、DB diagnostic、jacket catalogの責務を混同しない。
- 生captureとcropは自動削除しない。cleanup、retention確定、配布をこのPRへ含めない。

## Validation

- catalog schema/version、safe path、create/open、1:N、同一capture冪等性、複数reference、strict loader、非catalog/破損catalog拒否を対象testで固定する。
- exact canonical、title+artist、unique alias、ambiguous alias、複数候補、OCR/feature不足、master song不存在、GP対象外、master version更新、orphanをfixtureで確認する。
- coverage分母、status counts、auto-confirm rate、理由別件数、誤確定監査、再実行安定性をfixtureで確認する。
- 画像削除後の永続特徴量照合と、既存M5 jacket match結果・候補境界の回帰を確認する。
- 利用可能なローカルsong select素材でcatalog build/coverageを1回実行し、素材と生成catalogをGit対象へ入れない。
- 関連Python test、条件付き全pytest、Ruff、compileall、`git diff --check` を実行する。WPF/.NETを変更しない場合は.NET test/buildを省略し、理由を報告する。
- jacket crop/feature extractor/identity matchを変更するため `python -m tools.vision_poc` の関連実行を行い、confirmed-events、duplicate、unconfirmed境界と既存実capture代表を確認する。

## Non-goals

- WPF監視dashboard、task tray、対象window自動探索・再接続、長時間soak
- ゲーム操作自動化、song select自動巡回、公式サイト画像scraping
- jacket/capture/特徴量catalogの配布、Git/CI/Releaseへの格納
- 正式個人スコアDB schema、migration、backup、repair、保存workflow変更
- OCR方式刷新、ROI座標定義の大変更、正式値昇格policy変更
- 生画像の自動cleanup、retention運用確定

## Acceptance Criteria

- GPプレー可能な全songを `referenced` / `needs_review` / `uncollected` / `unresolved` のいずれかで数え、orphanを別集計できる。
- capture済み対象song全体に対するauto-confirm rateが90%以上で、95%未満なら理由別件数と改善候補を確認できる。既知の誤自動確定は0件である。
- auto-confirmは安全な一意条件だけで行い、曖昧候補を近傍の別曲へ寄せない。
- 同一入力再実行でreference件数が増えず、同一songの複数有効referenceとmaster更新時のorphan/再レビューを検査できる。
- 生画像削除後も永続特徴量からM5 jacket照合を再実行できる。
- ローカル素材・catalog・生成物がGit管理対象へ混入せず、read-onlyレビューでmedium以上の未対応指摘がない。

## ユーザー対応が必要になり得る項目

ローカル素材だけで90%の分母を満たせない場合に限り、開発者本人によるsong select gridの手動巡回captureが必要です。実施が必要になった時点で `AGENTS.md` の形式に従い、必須/任意、手順、期待結果、返してほしい内容、未実施の影響を明示してください。ゲーム操作の自動化で代替しないでください。

完了後は次PR仕様へ更新し、今回変更だけをcommit、通常pushしてdraft PRを作成してください。
