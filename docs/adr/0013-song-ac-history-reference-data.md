# ADR 0013: 曲版のAC収録歴を共有参照データで管理する

## Status

Accepted

## Context

GP表示バージョンは曲版のAC初収録を表さない。現在GPでプレー可能な過去AC曲やカバーを、保存済みFLARE実績からアプリとWebで同じ規則により集計する必要がある。

## Decision

曲版のAC初収録歴とカテゴリをマスタ更新の参照データとして確認・管理し、アプリとWebへ同じ分類を渡す。現在GP対象の対応曲版について過去AC収録も含める。表示用バージョンと既存song/chart ID、正式個人スコアDBのplayを維持する。分類更新後は既存の再読込・取得で再集計する。

確認済みAC収録歴なしと資料不足・曲版対応不明を区別する。曲名だけの推測対応は行わない。根拠URL・確認日・理由は開発者の更新作業へ残す。公開アプリの集計時に外部資料を取得しない。

## Consequences

分類修正が個人の過去playの書換えを必要とせず、共有マスタ経由で双方へ届く。新曲追加時には曲版のAC収録歴を確認し、未解決結果を確認する開発者作業が必要になる。集計式・点数表・現行譜面レベルの規則は既存のまま維持する。

## Alternatives Considered

### 表示バージョンだけで分類する

GP表示の過去AC曲を分類できず、曲版に対応した収録歴を保持できない。

### 公開アプリとWebで個別に資料取得する

双方の分類が取得時点や資料対応の差へ依存する。確認済みの共有参照データを通常更新する方式を採用する。

## References

- [Issue #222](https://github.com/tts1374/ddrgp_scorelog/issues/222)
- [マスタ生成手順](../../master/README.md)
- [マスタ設計](../design/08_master_db_generation.md)
- [Web公開データ](../design/13_web_player_data.md)
- [参照データ更新の分離](0004-separate-application-and-reference-data-updates.md)
