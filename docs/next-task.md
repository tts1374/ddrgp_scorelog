# 次PR作業仕様

`C:\work\ddrgp_scorelog` で作業してください。`AGENTS.md` と保存境界Skillを読み、既存のローカルDB、backup、`data/`、`logs/`、生成物を保護してください。

## 推奨モデル

GPT-5.6 Sol

## 推論レベル

high

既存のmanifest/manual境界、正式保存入力、transaction、viewerのread-only境界を維持したまま、最初の縦断経路を1つに限定して接続するためです。

## 作業ブランチ

```powershell
codex/m9-manual-save-viewer-vertical-slice
```

## Goal

既存のstrictな正式保存入力JSONを明示的に選び、正式個人スコアDB version 1へ既存workflowで単発保存し、その結果を最小WPF viewerで再読込して確認できるmanual縦断sliceを追加します。

## Deliverables

- WPFアプリから既存version 1 workflow入力JSONと保存先v1 DBをユーザーが明示選択できる入口を追加する。
- Python側の既存strict loader、adapter、analysis artifact orchestration、file saveを再実装せず、境界が明確な単発process/API adapterで1回だけ呼ぶ。
- ready、excluded、duplicate、unresolved、invalid、DB拒否、artifact partial successをユーザー向け状態へ写像する。
- transaction完了したreadyだけviewer履歴へ反映し、excluded/duplicateを成功playとして表示しない。
- 成功後は同じread-only repositoryでDBを再読込し、保存playを履歴・詳細・自己ベストへ反映する。
- compatible DB、unresolved、duplicate、invalid input、preview/unknown DB、workflow失敗を一時DB/fixtureでテストする。
- `app/README.md`、保存境界design docs、roadmapを同期する。

## Invariants

- 正式DB schema versionを1から変更しない。
- version 2 schema、supported transition、migration SQL、schema writerを設計・実装しない。
- 候補材料、M5 identity signal、M7a recognized digits、相対時刻を正式値へ暗黙昇格しない。
- 保存直前境界 `confirmed_result=true` かつ `duplicate=false` を維持する。
- source capture、任意play、analysisを既存の1 transactionで書き、別writerを作らない。
- unresolved/invalid/DB拒否ではDB、artifact、`data/`、`logs/`を新たに変更しない。
- duplicate/excludedはplay rowを作らず、`play_id=null` を成功playとして扱わない。
- viewerの通常閲覧は引き続きread-onlyとし、閲覧操作から保存を暗黙実行しない。
- 既存Python CLI/API契約と終了コードを変えない。

## Validation

.NET build/test、対象Python workflow/saveテスト、全Pythonテスト、Ruff、compileall、`git diff --check` を実行してください。画像処理を変更しない限りVision PoCは省略します。

## Non-goals

- manifestやPoC runnerからの自動保存
- 自動キャプチャ、常駐監視、タスクトレイ
- save inputのUI編集、候補値の自動補完
- migration、backup、restore、repair
- master DB更新、検索、絞り込み、グラフ
- installer、self-contained配布

## Acceptance Criteria

- 明示選択したvalid workflow inputから既存境界で1件を正式v1 DBへ保存し、同じアプリでread-only再表示できる。
- unresolved、invalid、preview/unknown DBは副作用なしで拒否される。
- duplicate/excludedはplay履歴へ追加されず、状態と理由をユーザーが確認できる。
- artifact生成後のDB失敗を保存成功へ丸めず、既存workflowのpartial success契約を維持する。
- 既存viewer操作だけではDB hashが変わらない。
- read-onlyレビューでmedium以上の未対応指摘がない。

完了後は次PR仕様へ更新し、今回変更だけをcommit、通常pushしてdraft PRを作成してください。
