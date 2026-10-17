# ADR 0014: 正式保存日時のUTC正規化と通常ISO・SQLite UTCの互換境界

## Status

Accepted

このdecisionはIssue #238で確定した後続実装契約である。writer、reader、日時順query・表示へのコード適用は後続作業とする。

## Context

Python/C#の正式writerは、日時parserが受理した文字列を未変換で保存する。parser間の受理形式・小数秒精度の違いに加え、SQLite queryと表示側の解釈範囲も一致しない。writerへ渡る前の日時型変換は、不足要素の補完や精度の変更を隠し得る。

正式保存の入力を一意に検証でき、保存した時点をbackup・復元・query・表示が共有する必要がある。一方、SQLite生成の保存日時は既存履歴と通常backupに含まれる。日時parserが過去に受理できた全表記を継続保守することと、通常の履歴を引き継ぐことは分けて判断する。

## Decision

- 新規のプレー日時・取得日時は、完全な暦日・時刻・timezoneを明示した通常ISO日時だけを受け付け、表す時点を変えずUTC canonicalへ正規化する。Python/C#に同じ境界を適用する。
- 小数秒は両経路で7桁まで保持し、保存を7桁固定とする。検証前の日時型変換による補完、丸め、切捨てを受理の根拠にしない。
- 正式DBの3 tableの作成日時はSQLite生成のoffsetなしUTCを維持する。取得・プレー時刻と保存時刻の役割を分ける。
- 既存値・v1/v2 backupの保証対象は通常ISO日時と、保存日時に限定したSQLite UTCとする。受理したbackupの日時は原文復元し、新規canonicalの強制や一括書換えは行わない。
- 保証対象外のbackupは全体をDB変更前に拒否する。既存のtransaction、duplicate、source capture / analysis logの保持を継承する。
- query・表示は保証対象の旧表記と新canonicalを同じ時点として解釈する。保存可能・復元可能であることだけを、時系列比較の正しさの根拠にしない。

形式、範囲、項目ごとの受理条件と失敗結果は[ストレージI/O仕様](../design/05_storage_io_spec.md#formal-timestamps)、現行実測と後続回帰条件は[正式個人スコアDB仕様](../design/10_personal_score_db_schema.md#formal-timestamp-validation)を正本とする。

## Consequences

- 通常の過去履歴とSQLite保存日時を引き継ぎつつ、新規保存の時点と精度を共通化できる。
- 復元の受理範囲は現行readerより狭まる。過去のparserだけが受理した特殊形式を含むbackupは保証対象外となる。
- 新旧の表記がDBに混在するため、query・表示は精度とtimezoneを考慮した比較が必要になる。
- schema、SQLite default、backup format versionを変更せずに契約を固定できる。既存値の自動repair・削除を正規化へ混ぜない。

## Alternatives Considered

### 保存日時も含めcanonical表記だけに限定する

consumerの形式数は減るが、通常の旧保存日時まで復元できなくなり、既存DBの移行や履歴を引き継がない運用が必要になる。通常履歴の継続利用に対して変更が大きい。

### 過去のparserの全受理形式をconsumerへ拡張する

広い復元互換性を維持できるが、parser間で異なる精度解釈や独自形式の時点変換をquery・表示まで継続保守することになる。通常ISOとSQLite UTCに保証境界を限定する。

## References

- [Issue #238](https://github.com/tts1374/ddrgp_scorelog/issues/238)
- [Issue #233](https://github.com/tts1374/ddrgp_scorelog/issues/233) / [PR #237](https://github.com/tts1374/ddrgp_scorelog/pull/237): 復元の事前検証と現行互換性
- [ADR 0003: DB責務の分離と正式個人スコアDBの保護](0003-database-responsibility-and-protection.md)
- [ストレージI/O仕様](../design/05_storage_io_spec.md#formal-timestamps)
- [正式個人スコアDB仕様](../design/10_personal_score_db_schema.md#formal-timestamp-validation)
