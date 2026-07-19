# 次Goal: manual review運用の完成

自動登録できなかったobservationを、人間または画像入力可能なAIが効率よく確認し、確定・reject・保留できる運用を完成させる。

このGoal完了後、レビュー領域は原則完了とする。候補診断、OCR分析、evidence viewer等は必要になった場合だけ別Goalとする。

## Goal

1. title ROIとartist ROIをレビュー画面で確認できる。
2. 曲ならMasterからtruth songを選択できる。
3. 曲ではない画像や不正captureをrejectできる。
4. 判断不能なものを保留できる。
5. 未レビュー対象を画像埋め込みODSへexportできる。
6. 編集済みODSをimportし、画面上のレビュー下書きへ反映できる。
7. importだけではcatalogを更新しない。
8. 一括反映で確定・rejectだけをcatalogへ反映できる。
9. 確定済み・reject済みの結果をレビュー済みタブで確認・訂正できる。

## 非Goal

通常レビューでは以下を扱わない。

- jacket画像、jacket top-3、distance、margin、rank
- OCR raw / normalized / candidate
- hold reason、candidate failure、drift、reference状態
- 詳細なconfirmation evidence
- 候補再評価、候補report、専用診断画面
- GPT API連携、ChatGPTへの自動upload、自動AI判定
- planファイルを利用者が手動で受け渡す二段階apply
- snapshot全画像hash、catalog全体logical revision、DB byte-level不変検証

## 共通データモデル

| field | 内容 |
|---|---|
| `observation_id` | observationの一意識別子 |
| `title_roi` | 曲名表示領域の画像 |
| `artist_roi` | artist表示領域の画像 |
| `status` | レビュー下書き状態 |
| `truth_song_id` | Master上の確定song ID |
| `notes` | 任意メモ |

タイトルとartistは自由入力を正本にせず、`truth_song_id`からMasterを参照する。

## status

### 未レビュータブ

| 表示 | 内部値 | truth_song_id | 一括反映 |
|---|---|---:|---|
| 未レビュー | `unreviewed` | 空欄 | 対象外 |
| 確定 | `confirmed` | 必須 | 曲として確定 |
| reject | `rejected` | 空欄 | rejectとして反映 |
| 保留 | `hold` | 空欄可 | 対象外 |

- truth song選択時はstatusを`confirmed`へ変更してよい。
- `rejected`選択時はtruth songを空にする。
- `unreviewed`と`hold`は一括反映しない。

### レビュー済みタブ

現在値と変更予定値を分ける。

- `current_status`
- `current_song_id`
- `draft_status`
- `draft_song_id`
- `notes`

変更予定は`変更なし`、`確定`、`reject`、`保留`とする。レビュー済み側の`保留`は現在値を維持し、未確定へ戻す意味にはしない。

# レビュー画面

既存のレビュー画面を`未レビュー`と`レビュー済み`の2タブ構成へ変更する。

## 未レビュータブ

```text
|  未レビュー  |  レビュー済み  |

[ エクスポート ] [ インポート ] [ 一括反映 ]

[未レビュー 93] [確定予定 20] [reject予定 4] [保留 3]

┌──────────────┬──────────────┬──────────────┬────────────────────────────┬──────────────┐
│ title ROI    │ artist ROI   │ status       │ truth song                 │ notes        │
├──────────────┼──────────────┼──────────────┼────────────────────────────┼──────────────┤
│ 画像          │ 画像          │ [未レビュー ▼] │ [---                      ▼] │              │
│ 画像          │ 画像          │ [確定       ▼] │ [Grip & Break down !!     ▼] │              │
│ 画像          │ 画像          │ [reject     ▼] │ [---                      ▼] │ ROI不正      │
│ 画像          │ 画像          │ [保留       ▼] │ [---                      ▼] │ 判読困難     │
└──────────────┴──────────────┴──────────────┴────────────────────────────┴──────────────┘
```

表示対象:

- 未レビュー、保留
- 確定予定・reject予定だが未反映
- ODS importで下書きが設定された未反映行

表示しない:

- 自動登録済み
- manual確定済み
- reject反映済み

status変更だけでは行を消さず、一括反映成功後に除外する。

## レビュー済みタブ

```text
|  未レビュー  |  レビュー済み  |

[ 一括反映 ]

[変更なし 356] [変更予定 20] [reject予定 4] [保留 3]

┌──────────────┬──────────────┬────────────┬──────────────────────┬──────────────┬──────────────────────┬──────────────────┬────────────────────┬──────────────┐
│ title ROI    │ artist ROI   │ 現在の状態 │ 現在のtruth song     │ 変更内容     │ 変更後truth song     │ 登録ルート       │ 処理実行日時       │ notes        │
├──────────────┼──────────────┼────────────┼──────────────────────┼──────────────┼──────────────────────┼──────────────────┼────────────────────┼──────────────┤
│ 画像          │ 画像          │ 確定       │ Grip & Break down !! │ [変更なし ▼] │ [---              ▼] │ ジャケット自動判定 │ 2026-07-18 22:14   │              │
│ 画像          │ 画像          │ 確定       │ AAA                  │ [確定     ▼] │ [正しい曲名       ▼] │ 手動レビュー     │ 2026-07-18 22:20   │ 誤登録       │
│ 画像          │ 画像          │ 確定       │ BBB                  │ [reject   ▼] │ [---              ▼] │ 曲名＋artist OCR │ 2026-07-18 22:21   │ 曲画面ではない│
│ 画像          │ 画像          │ reject     │ ---                  │ [確定     ▼] │ [正しい曲名       ▼] │ 手動レビュー     │ 2026-07-18 22:24   │ 判定修正     │
└──────────────┴──────────────┴────────────┴──────────────────────┴──────────────┴──────────────────────┴──────────────────┴────────────────────┴──────────────┘
```

追加表示:

- 現在のstatusとtruth song
- 登録ルート
- 処理実行日時

一括反映対象:

- 曲Aから曲Bへの変更
- 確定からrejectへの変更
- rejectから曲確定への変更
- notes変更

`変更なし`と`保留`は反映しない。反映成功後は行を残し、現在値を更新して変更予定をクリアする。

レビュー済み側のODS export/importは必須にしない。

# truth song選択

Masterを検索できるプルダウンとする。

```text
[Grip & Break down !!                              ▼]

[検索欄                                              ]
Grip & Break down !! — SOUND HOLIC feat. Nana Takahashi
アイスクリームマジック — アーティスト名
蒼い衝動 ～for EXTREME～ — アーティスト名
```

- 表示: canonical title、canonical artist
- 内部値: song ID
- 検索対象: canonical title、title alias、canonical artist、song ID
- 基本順位: title完全一致、alias完全一致、title前方一致、title部分一致、artist一致、song ID一致
- fuzzy matchingは必須にしない。

# ODS export

## Manual Reviewシート

| observation_id | title_roi | artist_roi | status | truth_song_id | notes |
|---|---|---|---|---|---|

- title ROIとartist ROIをODS内へ画像として埋め込む。
- 外部画像リンクやobservation IDからの逆引きを前提にしない。
- `status`、`truth_song_id`、`notes`だけを編集可能にする。
- ファイルが大きくなる場合は複数ODSへ分割してよい。

## Master Songsシート

| song_id | title | artist |
|---|---|---|

現在のMaster全曲を含め、LibreOffice上で検索・フィルタできる形式にする。

## Metadataシート

importに必要な最小情報だけを持つ。

- schema version
- export ID
- catalog version
- master version
- export日時
- 対象件数

全画像hashやcatalog全row stateは不要。

# ODS import

- importは`status`、`truth_song_id`、`notes`をレビュー下書きへ反映するだけとする。
- importだけではcatalog、history、確定状態を更新しない。
- 画像列はimportしない。

最低限の検証:

- 対応schemaと必須sheet・column
- observation IDの重複と存在
- statusの許可値
- `confirmed`のtruth song ID必須とMaster存在確認
- `rejected`のtruth song ID空欄

catalogまたはMasterのversion差だけでは拒否せず、現在のobservationとsong IDが有効なら読み込んでよい。

エラー時は行番号、observation ID、理由を示し、一部読込か全体拒否かを明示する。

# 一括反映

catalogを変更する操作は一括反映だけとする。

- `confirmed`と`rejected`を1 transactionで処理する。
- observation、status、song ID、既存確定との競合を検証する。
- 1件でも不正なら対象全体をrollbackし、該当行を示す。
- 既存確定情報を暗黙に上書きしない。
- 同じ内容の再反映は安全なno-opにする。

未レビュー側は成功後に一覧から除外する。レビュー済み側は行を残して現在値を更新する。

# 実装優先順位

1. レビュー下書きのデータモデル
2. 未レビュー／レビュー済みタブとROI表示
3. status編集とtruth song検索
4. 一括反映
5. 画像埋め込みODS export
6. ODS import
7. 操作性改善

同じデータモデルと利用フローに収まり、レビュー可能な規模なら1 PRで実装してよい。診断機能再設計や大規模schema migrationは分離する。

# 必須検証

対象責務のtestを優先し、全testは影響範囲を限定できない場合だけ実行する。

最低限:

- status遷移と下書き保持
- truth song検索の完全一致・alias・部分一致・artist・song ID
- confirmed / rejected / 混在一括反映
- 不正song ID、欠損、重複、既存確定競合、rollback、再反映no-op
- 反映後のタブ別表示挙動
- ROI画像埋め込みODS、Master Songs、Metadata
- ODS正常importと各入力エラー
- importだけではcatalog不変
- 現在のmanual実データでLibreOffice表示・編集・保存・再読込
- Ruff、構文検査、対象pytest、`git diff --check`

# Goal完了条件

- 未レビュー／レビュー済みの2タブが動作する。
- title ROIとartist ROIを直接確認できる。
- status、truth song、notesを編集できる。
- confirmedとrejectedを一括反映できる。
- 反映済み結果を確認・訂正でき、登録ルートと処理日時を確認できる。
- 画像埋め込みODSをexportし、Master Songsを参照できる。
- 編集済みODSを下書きとしてimportできる。
- importだけではcatalogが変わらない。
- 現在のmanual残件を実際に処理できる。
- DB、ODS、画像、生成物をGitへ含めない。
