# 次PR作業仕様

`C:\work\ddrgp_scorelog` で作業してください。`AGENTS.md`、frame input・event/save boundary・正式個人スコアDB設計、WPF continuous capture session、M9 roadmapを読み、`.agents/skills/review-ddrgp-db-save-boundary/SKILL.md` を適用してください。既存のローカル画像、capture session出力、正式DB、backup、`data/`、`logs/`、生成物を保護してください。

## 推奨モデル

GPT-5.6 Sol

## 推論レベル

xhigh

capture、分類、confirmed event、認識候補、正式値昇格、duplicate、analysis artifact、正式DB transactionの境界を同時に扱い、現行schemaとmanual入口を維持する必要があるためです。

## 作業ブランチ

```powershell
codex/m9-capture-save-workflow
```

## Goal

明示開始したcontinuous capture sessionのframeを既存解析pipelineへ渡し、confirmed eventを1件ずつ既存正式保存workflowで処理します。十分な根拠が揃うeventだけを正式値へ昇格して保存し、duplicate、excluded、unresolved、解析失敗を成功保存と区別します。

このPRは `docs/implementation-roadmap.md` の「M9残り実行順」6項目中の4項目目です。監視dashboard・タスクトレイ、対象window自動探索・再接続、長時間運用、migrationには進みません。

## Deliverables

- continuous captureの保存済みframeを、取得順と `timestamp_ms` を維持して既存分類・confirmed event・identity・数字認識へ渡すapplication境界を追加する。
- 保存直前境界 `confirmed_result=true` かつ `duplicate=false` を唯一の通常保存候補入口として維持し、eventを1件ずつ直列処理する。
- M5 identityとM7a数字認識の既存採用済みprofile・confidence・完全性を明示検査し、全必須正式値の根拠が十分な場合だけversion 1 formal inputへ昇格するpure adapterを追加する。
- candidate、raw OCR、相対時刻、期待値、preview payloadを無条件に正式値へ転記しない。根拠不足は `unresolved` または低信頼度 `excluded` とし、play rowを作らない。
- reviewed formal inputを使う既存manual WPF入口とstrict loaderを維持し、自動入力とmanual入力の由来を混同しない。
- 既存workflow orchestrationを1eventにつき1回呼び、`saved`、DB duplicate、policy excluded、unresolved、invalid、artifact partial failure、DB拒否を既存statusのまま返す。
- source capture、analysis artifact、playの責務を維持し、正式DB transaction成功後だけviewerをread-only再読込する。
- session停止・capture失敗と、frame解析・正式値解決・DB保存失敗を別statusとして扱い、1eventの失敗で別eventを成功扱いへ丸めない。
- `app/README.md`、event/save boundary、正式DB schema、regression、roadmap docsを実績に同期する。

## Invariants

- `confirmed_result=true` かつ `duplicate=false` 以外を通常保存候補にしない。
- 正式個人スコアDB schema version 1、table/column、writer transaction、duplicate collision契約を変更しない。
- M8 preview DB・payload、OCR raw/normalized、expected values、relative `played_at_ms=0` を正式値として採用しない。
- duplicate、rejected transition、未確定result、低信頼度、必須値不足ではplay rowを作らない。
- analysis artifact、DB diagnostic、低信頼度ログ、source captureの責務を混同しない。
- single-frame capture、continuous session bundle、manifest mode、manual単発保存、read-only viewerの既存公開契約を維持する。
- screenshot、capture frame、実manifest、正式DB、analysis log実出力をGit管理しない。
- target window自動探索、自動再接続、background auto start、task tray、migration、backup、repairを実装しない。

## Validation

- 保存候補境界、正式値昇格pure adapter、workflow status mapping、duplicate、transaction rollback、viewer再読込の対象Python/.NET testを追加する。
- confirmed、duplicate、rejected transition、未確定、identity不足、数字不足、低confidence、saved、DB duplicate、artifact failure、DB拒否をfixtureで確認する。
- 正式値へcandidate/raw/expected/preview値が混入しないnegative testを追加する。
- .NET unit test、WPF build、関連Python test、条件付き全pytest、Ruff、compileall、`git diff --check` を実行する。
- 利用可能な実capture session manifestで保存直前までのdry-runを1回行い、対象event数とstatusを確認する。正式ローカルDBへの実保存は、破壊的操作なしで専用新規fixture DBへ限定できる場合だけ実行する。
- 分類・ROI・recognition thresholdを変更した場合は `python -m tools.vision_poc` を実行し、既存実capture評価とtransition_countup境界を確認する。

## Non-goals

- 監視状態dashboard、task tray、起動時自動監視、対象window自動探索・再接続
- 長時間soak、auto restart、installer、self-contained配布
- 正式DB schema変更、migration、backup、restore、repair
- OCR方式刷新、ROI座標定義の大変更、duplicate key方式の本格差し替え
- low-confidence画像やanalysis logのretention運用確定

## Acceptance Criteria

- continuous capture由来のconfirmed non-duplicate eventだけが、明示したconfidence・完全性policyを満たす場合に限り既存正式workflowへ1件ずつ渡る。
- saved playだけがtransaction後のviewerへ反映され、duplicate、excluded、unresolved、解析失敗、partial failure、DB拒否は成功表示されない。
- candidate/raw/expected/preview材料から正式値への暗黙昇格がなく、低信頼度または不足値でplay rowが作られない。
- 正式DB schema version 1、writer transaction、duplicate、source/analysis/play責務、manual入口が維持される。
- read-onlyレビューでmedium以上の未対応指摘がない。

完了後は次PR仕様へ更新し、今回変更だけをcommit、通常pushしてdraft PRを作成してください。
