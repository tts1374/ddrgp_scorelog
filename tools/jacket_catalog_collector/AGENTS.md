# Jacket Catalog Collector Agent Rules

このファイルは `tools/jacket_catalog_collector/` 配下の実装・検証に追加適用する。コマンド、UI操作、機能説明は `README.md` を事実源とし、ここではdeveloper-onlyツールとして維持する境界と実装水準を定める。

## Product Context And Risk Level

- このcollectorは開発者本人がローカルPCで使用するdeveloper-onlyツールである。単一開発者、単一process、明示操作を基本とし、公開アプリ、複数利用者、複数端末、個人情報処理を前提にしない。
- 処理を再実行でき、既存の入力・DB・画像を失わず、失敗理由を開発者が確認できる状態を優先する。
- 既存の安全機構はIssueの目的に不要でも壊さない。簡略化や削除は明示scopeがある場合だけ行う。

## Required Boundaries

- developer-only collectorを公開 `DDRGpScoreViewer`、installer、Release、GitHub Actions artifact、正式保存workflow、正式個人スコアDBへ接続しない。
- capture、crop、feature、checkpoint、catalog/master DBはlocal dataとし、Git、通常stdout、公開logへ混入させない。物理削除、retention cleanup、既存DB修復は明示scopeなしに行わない。
- Python subprocessのstdoutは契約データ、stderrはdiagnosticとして分離し、非0終了や不正な契約出力を成功扱いへ丸めない。
- window候補を自動選択・自動開始・暗黙再選択しない。capture開始とdisk保存は開発者の明示操作を必要とする。
- candidate、expected song、OCR rawを明示選択や確定結果へ暗黙昇格させない。
- catalogの複数row更新は既存writerのtransaction境界を使い、途中失敗を部分成功へ丸めない。既存fileやDBを明示なしに上書きしない。
- UIは既存のPython projectionとwriter契約を再利用し、SQLite schemaや判定logicをC#側へ重複実装しない。

## Proportional Implementation

Issueに明記されない限り、次を新規導入または一般化しない。

- 複数process間の完全な競合制御
- 全入力fileのhash固定や包括的なTOCTOU対策
- byte-identicalな生成物保証
- 汎用的なresume / retry frameworkやcrash recovery protocol
- append-only audit framework
- 将来version向けmigration frameworkや互換layer
- 全失敗点へのfailure injection
- 理論上の全状態組合せを網羅するtest
- 1用途のためのplugin化、抽象interface、汎用pipeline

既存実装にこれらの機構がある場合は、変更責務に関係する契約だけを維持する。checklistの存在だけを理由に、未変更経路へ同種の機構を追加しない。

## Existing Contract Handling

- capture lifecycleを変更する場合は、二重開始防止、bounded queue、停止後frameの破棄、resource解放という既存の基本契約を維持する。ただし未変更の失敗経路を包括的に再設計しない。
- artifact、checkpoint、resume/retryを変更する場合は、現在利用中のversionと再実行可能性を守る。新しいversion、migration、receipt protocolはIssueに明示された場合だけ追加する。
- schema、manual review、auto-confirm、observation ingestの境界は、指定Issue、関連する親Issue、設計docsに従う。Issueと設計docsが矛盾する場合は推測で仕様を拡張せず、最小限の判断を報告する。
- derived表示や診断処理でpersisted status、revision、historyを変更しない。

## Validation Focus

- 変更責務の正常系、主要な入力不備、現実に発生しやすい途中失敗、確認済み回帰を対象testで確認する。
- cancel、duplicate、stale state、queue満杯、publish failureなどは、今回変更した責務に直接関係する場合だけ追加確認する。
- 全testは共通schema、writer、projection契約、共通fixtureなど影響範囲が広い変更、または対象testで予期しない回帰が出た場合に実行する。
- local DB、画像、artifact、checkpoint、生成物が意図せず変更・Git追加されていないことを確認する。
- Python/C# error分類やprojection schemaを変えた場合はproducerと実際のconsumer、fixture、公開手順を同期する。内部実装だけならdocs更新を増やさない。
