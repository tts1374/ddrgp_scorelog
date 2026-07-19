# Application Agent Rules

このファイルは `app/` 配下の実装・検証に追加適用する。ユーザー向けの機能、操作、制限は `README.md` と指定Issueを事実源とし、ここではM9/M10のローカルアプリに必要な実装水準を定める。

## Product Context

- このアプリは単一ユーザーがローカルPCで使用する個人開発の趣味ツールである。
- 氏名、住所、認証情報などの個人情報を保存せず、商用サービス、組織運用、複数利用者、複数端末の同時利用を前提にしない。
- 既存の安全機構はIssueの目的に不要でも壊さない。簡略化や削除は明示scopeがある場合だけ行う。

## Required Safety

- 誤認識、不完全な解析結果、不正入力を正式スコアDBへ保存しない。
- 正常保存済みのスコアを通常の閲覧、監視開始・停止、アプリ終了・再起動で失わない。
- 解析失敗、入力不足、DB不整合、subprocess失敗を成功扱いへ丸めない。
- 監視開始・停止、window再選択、アプリ終了・再起動という通常運用から復帰できる。
- 利用者がエラー理由と必要な対処を確認できる。
- 保存先、設定、backup、復旧手順は個人利用として再現可能にする。

## Proportional Implementation

- Acceptance criteriaを満たす既存コードの局所変更を優先し、将来拡張だけを目的とした新しいlayer、service、interface、plugin、設定を追加しない。
- 単一ユーザー、単一PC、通常は単一app processからの利用を前提とする。
- Issueに明記されない限り、次を実装しない。
  - 複数ユーザー・複数端末対応
  - 複数process writerの完全な競合制御
  - enterprise向け権限管理や監査証跡
  - 無停止migration、自動rollback framework、汎用crash recovery
  - telemetry、remote monitoring、中央管理
  - cryptographicな改ざん検知
  - あらゆるOS障害、disk failure、電源断からの自動復旧
  - 未観測のedge caseに対する包括的な防御layer
  - 理論上の全状態組合せを網羅するtest

## Higher-Risk Changes

次は個人利用でも比較的厳格に扱う。ただし、指定Issueに必要な不変条件と失敗経路の範囲だけを実装・検証する。

- 正式スコアDBへの自動保存
- duplicate判定と同一playの再投入
- schema変更とmigration
- DB、設定、backup、ユーザーデータの削除・上書き
- installer、updater、uninstaller
- OS起動時の自動実行
- 外部networkへの送信
- 認証情報や秘密情報の取扱い

正式DBへの複数row更新は既存transaction境界を使い、失敗を部分的な保存成功へ丸めない。既存DBの自動repair、未指定migration、互換layerは追加しない。

## Validation

- 変更責務の対象testと、直接依存する現実的な正常系・失敗系を確認する。
- UI変更は主要操作とエラー表示を確認する。全画面・全状態の網羅的なGUI testを既定にしない。
- 共通DB schema、writer、capture lifecycle、共通状態管理、installer設定など影響範囲が広い変更、または対象testで予期しない回帰が出た場合だけ広いtestを実行する。
- 実機、GUI、installer確認がAcceptance criteriaに含まれる場合は実施し、実行できない場合は未実施理由と残るリスクを報告する。
- ローカルDB、画像、設定、生成物をGitへ混入させない。
