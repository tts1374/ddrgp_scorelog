# 次Goal: manual review運用の完成

PR #54で自動登録対象をcatalogへ反映するpipelineと、manual残件をODSへexportする処理を実装した。

一方、現状のmanual review ODSおよびアプリの「レビュー」画面は候補評価・診断情報を中心とした構成であり、実際のレビュー作業には適していない。

次PRでは、自動登録できなかったobservationを、人間または画像入力可能なAIが効率よく確認し、確定・reject・保留できるmanual review運用を完成させる。

このGoalが完了した時点で、レビュー領域は原則完了とする。候補診断、OCR分析、evidence viewer等は必要になった場合だけ別Goalとして扱う。

---

## Goal

以下の運用を成立させる。

1. レビュー画面でtitle ROIとartist ROIを確認する。
2. 曲である場合はMasterからtruth songを選択する。
3. 曲ではない画像や不正captureはrejectする。
4. 判断できないものは保留する。
5. 未レビュー対象を画像埋め込みODSへexportする。
6. 編集済みODSをimportし、レビュー下書きとして画面へ反映する。
7. importだけではcatalogを更新しない。
8. 画面の「一括反映」で確定・rejectをcatalogへ反映する。
9. 確定済み・reject済みの結果をレビュー済みタブで確認・訂正する。

---

## 基本方針

manual reviewの目的は、候補評価の詳細を分析することではない。

title ROIとartist ROIを見て、正しい曲を確定するか、曲ではないものとしてrejectできればよい。

レビュー画面およびODSで主要情報として扱う項目は以下に限定する。

- observation ID
- title ROI
- artist ROI
- status
- truth song ID
- notes

通常レビューでは以下を表示しない。

- jacket画像
- jacket top-1からtop-3
- jacket distance / margin / rank
- OCR raw / normalized / candidate
- hold reason
- candidate failure
- drift
- reference状態
- 詳細なconfirmation evidence

---

## 共通データモデル

| field | 内容 |
|---|---|
| `observation_id` | 対象observationの一意識別子 |
| `title_roi` | 曲名表示領域の画像 |
| `artist_roi` | アーティスト表示領域の画像 |
| `status` | レビュー下書き状態 |
| `truth_song_id` | Master上の確定song ID |
| `notes` | 任意メモ |

タイトルおよびアーティストの正本は自由入力値ではなく、`truth_song_id`からMasterを参照して取得する。

---

## status

### 未レビュータブ

| 表示 | 内部値 | truth_song_id | 一括反映 |
|---|---|---:|---|
| 未レビュー | `unreviewed` | 空欄 | 対象外 |
| 確定 | `confirmed` | 必須 | 曲として確定 |
| reject | `rejected` | 空欄 | rejectとして反映 |
| 保留 | `hold` | 空欄可 | 対象外 |

入力規則:

- `confirmed`では`truth_song_id`を必須とする。
- `rejected`では`truth_song_id`を空欄とする。
- truth songを選択した場合はstatusを`confirmed`へ自動変更してよい。
- statusを`rejected`へ変更した場合はtruth songを空欄にする。
- `unreviewed`と`hold`は一括反映しない。

### レビュー済みタブ

レビュー済みデータでは、現在値と変更予定値を分けて扱う。

- `current_status`
- `current_song_id`
- `draft_status`
- `draft_song_id`
- `notes`

変更予定の表示は以下とする。

| 表示 | 動作 |
|---|---|
| 変更なし | 現在値を維持 |
| 確定 | 指定songへ変更、またはrejectを曲として確定 |
| reject | 現在の確定をrejectへ変更 |
| 保留 | 変更を反映せず現在値を維持 |

レビュー済み側の`保留`は、現在の確定状態を未確定へ戻す意味ではない。

---

# レビュー画面

既存の「レビュー」タブを、`未レビュー`と`レビュー済み`の2タブ構成へ変更する。

## 未レビュータブ wireframe

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

### 表示対象

表示する:

- 未レビュー
- 保留
- 画面上で確定予定だが未反映
- 画面上でreject予定だが未反映
- ODS importで下書きが設定されたが未反映

表示しない:

- 自動登録済み
- manual確定済み
- reject反映済み

statusを画面上で変更しただけでは行を消さない。一括反映成功後に一覧から除外する。

### 件数

上部件数は画面上の下書き状態を表す。

```text
[未レビュー 93] [確定予定 20] [reject予定 4] [保留 3]
```

件数を選択してstatusで絞り込めるようにしてよい。

---

## レビュー済みタブ wireframe

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

### 表示内容

未レビュー側に加えて、以下を表示する。

- 現在のstatus
- 現在のtruth song
- 登録ルート
- 処理実行日時

登録ルートは内部値を人間向け表示へ変換する。

| 内部値 | 表示例 |
|---|---|
| `jacket_gate` | ジャケット自動判定 |
| `jacket_top3_title_ocr` | ジャケット＋曲名OCR |
| `ocr_title_artist_pair` | 曲名＋アーティストOCR |
| `manual_ui` | 手動レビュー |
| `manual_ods_import` | ODSレビュー |
| reject系 | reject |

### 一括反映

以下を処理する。

- 曲Aから曲Bへの変更
- 確定からrejectへの変更
- rejectから曲確定への変更
- notes変更

以下は変更しない。

- 変更なし
- 保留

反映成功後は行を消さず、現在値を更新して変更予定をクリアする。

レビュー済みタブへのODS export/importはこのGoalの必須要件にしない。

---

# truth song選択

truth songはMaster上のsongを検索できるプルダウンとする。

```text
[Grip & Break down !!                              ▼]

[検索欄                                              ]
Grip & Break down !! — SOUND HOLIC feat. Nana Takahashi
アイスクリームマジック — アーティスト名
蒼い衝動 ～for EXTREME～ — アーティスト名
蒼が消えるとき — アーティスト名
```

検索結果には最低限以下を表示する。

- canonical title
- canonical artist

song IDは内部値として保持し、通常表示では必須としない。同名曲等で区別が必要な場合だけ補助表示してよい。

検索対象:

- canonical title
- title alias
- canonical artist
- song ID

基本優先順位:

1. canonical title完全一致
2. alias完全一致
3. canonical title前方一致
4. canonical title部分一致
5. artist一致
6. song ID一致

曖昧なfuzzy matchingは必須にしない。

100件以上を連続処理できる操作性を優先し、キーボードで検索・選択できることが望ましい。

---

# 未レビューODS export

ODSはLibreOfficeだけでレビューを完結できる形式とする。

元画像を別途探す操作、observation IDから画像を逆引きする操作、画像を1件ずつ開く操作を前提としない。

## Manual Reviewシート

| observation_id | title_roi | artist_roi | status | truth_song_id | notes |
|---|---|---|---|---|---|

- `observation_id`: read-only
- `title_roi`: ODS内へ埋め込んだ画像、read-only
- `artist_roi`: ODS内へ埋め込んだ画像、read-only
- `status`: 編集可能
- `truth_song_id`: 編集可能
- `notes`: 編集可能

### 画像

- title ROIとartist ROIをODS内へ画像として埋め込む。
- jacket画像は埋め込まない。
- 人間および画像入力可能なAIが判読できるサイズを維持する。
- ファイルが過度に大きくなる場合は複数ODSへ分割してよい。
- 分割件数は固定せず、LibreOfficeで安定して閲覧・編集できることを基準とする。

### 出力しない列

- jacket top-1からtop-3
- distance / margin / rank
- OCR raw / normalized / candidate
- hold reason
- recommended song ID
- current review revision
- capture validity
- policy version
- confirmation evidence

## Master Songsシート

| song_id | title | artist |
|---|---|---|

現在のMasterに存在する全曲を含める。

レビュアーがtitleまたはartistで検索し、truth song IDをコピーできれば最低要件を満たす。複雑な数式やドロップダウンは必須にしない。

## Metadataシート

importに必要な最小情報だけを保持する。

- schema version
- export ID
- catalog version
- master version
- export日時
- export対象件数

catalog全体のbyte hash、全画像hash、全row state等は不要とする。

---

# 未レビューODS import

ODS importは編集内容をレビュー画面の下書きへ読み込む機能とする。

## import対象

- `observation_id`
- `status`
- `truth_song_id`
- `notes`

画像列はimportしない。

## 最低限の検証

- 対応するschema versionである。
- 必須sheetと必須columnが存在する。
- observation IDが重複していない。
- observation IDが現在のレビュー対象に存在する。
- statusが許可値である。
- `confirmed`ではtruth song IDが存在する。
- truth song IDが現在のMasterに存在する。
- `rejected`ではtruth song IDが空欄である。

catalogまたはMasterのversionがexport時から変わっていても、現在のobservationとtruth song IDが有効ならimport可能としてよい。version差だけを理由に全体拒否しない。

import成功後、status、truth song、notesを画面上へ反映する。importだけではcatalogを更新しない。

エラー時はobservation ID、行番号、理由を表示する。一部読込みか全体拒否かは実装を単純にできる方を選んでよいが、結果を明確に表示する。

---

# 一括反映の契約

catalogを変更する操作は画面の「一括反映」だけとする。

未レビュータブでは`confirmed`と`rejected`だけを反映し、`unreviewed`と`hold`は反映しない。

レビュー済みタブでは、変更予定がある行だけを反映する。

最低限、以下を検証する。

- observation IDがcatalogに存在する。
- observation IDが重複していない。
- `confirmed`にはtruth song IDが設定されている。
- truth song IDが現在のMasterに存在する。
- `rejected`にはtruth song IDが設定されていない。
- statusが許可値である。
- 既存確定情報を意図せず上書きしない。

対象を1つのtransactionで処理し、1件でも不正または書込み失敗があれば全件rollbackする。

維持する安全性:

- 単一transaction
- 入力不正時のrollback
- 既存確定情報の暗黙上書き防止
- 同じ内容の再反映に対する安全なno-op
- Masterに存在しないsong IDの拒否

必須にしない安全性:

- planファイルを手動で受け渡す二段階apply
- dry-runとapplyを別コマンドに分ける運用
- snapshot全画像の再hash
- catalog全体のlogical revision固定
- 非対象row変更による全体拒否
- DBファイルのbyte-level不変検証
- production service相当のTOCTOU guard
- 一括反映前後の全入力完全一致

---

# 状態遷移

```text
catalog上で未確定
        │
        ├── アプリ画面で編集 ───────────────┐
        │                                    │
        └── ODSへexport → ODS編集 → import ─┤
                                             │
                                      レビュー下書き
                                             │
                                          一括反映
                                             │
                      ┌──────────────────────┴──────────────────────┐
                      │                                             │
                confirmed反映                                 rejected反映
                      │                                             │
                レビュー済みタブ                             レビュー済みタブ
                      │                                             │
                      └──────────── 確認・訂正 ─────────────────────┘
```

重要な境界:

- exportはcatalogを変更しない。
- importはcatalogを変更しない。
- 画面上の編集はcatalogを変更しない。
- catalogを変更するのは一括反映だけである。
- 未レビュー側では反映成功後に一覧から除外する。
- レビュー済み側では反映成功後も一覧に残し、現在値を更新する。

---

# 既存レビュー画面からの変更

既存画面の候補再評価、candidate report、履歴、drift、referenced等を新しい通常レビュー画面へ残す必要はない。

新しい主要画面は以下へ置き換える。

- 未レビュー / レビュー済みタブ
- title ROI
- artist ROI
- statusまたは変更内容
- truth song
- notes
- 未レビュー側のexport/import
- 一括反映
- レビュー済み側の登録ルート / 処理実行日時

詳細・診断画面は優先度を下げ、このGoalの完了条件に含めない。既存機能の維持だけを目的として通常レビュー画面を複雑にしない。

---

# 実装優先順位

1. 共通レビュー下書きモデル
2. 未レビュータブ
3. truth song検索
4. 未レビュー一括反映
5. レビュー済みタブ
6. レビュー済み訂正反映
7. 画像埋め込みODS export
8. ODS import
9. 100件以上の連続処理を想定した操作性改善

レビュー画面、ODS export/import、一括反映は同じ利用フローに属するため、1つのPRでレビュー可能なら同時実装してよい。

catalog schemaの大規模再設計や診断機能再構築が必要なら分離する。

---

# 検証

## synthetic test

### 未レビュー

- 初期状態が`unreviewed`
- truth song選択で`confirmed`になる
- `rejected`への変更でtruth songが空になる
- `hold`と`unreviewed`が一括反映対象外になる
- 反映済みが未レビュー一覧から消える

### レビュー済み

- 自動登録、manual確定、reject済みが表示される
- 登録ルートと処理実行日時が表示される
- 曲Aから曲Bへ変更できる
- 確定からrejectへ変更できる
- rejectから確定へ変更できる
- 変更なしと保留は反映されない
- 反映後に現在値が更新され、変更予定がクリアされる

### truth song検索

- canonical title完全一致
- alias完全一致
- title部分一致
- artist検索
- song ID検索
- 存在しないsongを選択できない

### 一括反映

- confirmed正常反映
- rejected正常反映
- confirmed / rejected混在
- truth song未設定
- 存在しないtruth song ID
- rejectedでtruth song設定済み
- observation ID重複
- 既存確定行との競合
- 途中例外時rollback
- 同一内容の再反映が安全に処理される

### ODS export

- title ROIが埋め込まれる
- artist ROIが埋め込まれる
- observation IDが一致する
- status、truth song ID、notesが編集可能
- Master Songsシートが存在する
- Metadataシートが存在する
- 不要な診断列を含まない
- LibreOfficeで開けるODF構造である

### ODS import

- 正常なconfirmed / rejected / hold / unreviewed
- 不正status
- confirmedでtruth song ID欠損
- 存在しないtruth song ID
- rejectedでtruth song ID設定
- observation ID重複
- 未知observation ID
- 必須sheet / column欠損
- importだけではcatalogが変わらない

## 実データ確認

現在のmanual残件を使い、最低限以下を確認する。

- 全対象のtitle ROIとartist ROIが目視可能
- ODSだけでsong IDを特定できる
- ODS編集後にアプリへimportできる
- importだけではcatalogが変わらない
- confirmed / rejectedだけが一括反映される
- hold / unreviewedが残る
- 反映済みがレビュー済みタブに表示される
- 自動登録結果の登録ルートと処理実行日時を確認できる
- 確定済み・reject済みを訂正できる

LibreOffice本体で画像表示、行高、列幅、スクロール、編集、保存、再読込を確認する。

---

# 非要件

- jacket類似度精度改善
- OCR engine / profile変更
- fuzzy matching追加
- jacket top-3 UI
- candidate report UI
- confirmation evidence viewer
- drift診断画面
- review history専用画面
- GPT API直接連携
- ChatGPTへの自動アップロード
- 自動AI判定
- 一般ユーザー向け機能
- production service相当の承認フロー
- 全入力のbyte hash固定
- 全snapshot画像のTOCTOU検証
- DB byte-level failure injectionを通常受入条件にすること

---

# Goal完了条件

- 未レビュー / レビュー済みタブがある。
- title ROIとartist ROIを直接確認できる。
- statusを未レビュー、確定、reject、保留から選べる。
- truth songをMasterから検索して選べる。
- notesを編集できる。
- confirmed / rejectedだけを一括反映できる。
- importだけではcatalogが更新されない。
- 未レビュー側では反映済みが一覧から消える。
- レビュー済み側では現在値、登録ルート、処理実行日時を確認できる。
- 確定済み・reject済みを訂正できる。
- manual対象を画像埋め込みODSへexportできる。
- ODSにMaster Songsシートが含まれる。
- 編集済みODSをレビュー下書きとしてimportできる。
- 外部画像を個別に開かずODSだけでレビューできる。
- 現在のmanual残件を実際に処理できる。
- 100件以上のレビュー対象でも実用的に操作できる。
- Ruff、構文検査、対象pytest、`git diff --check`が成功する。
- DB、ODS、画像、生成物をGitへ含めない。

このGoal完了後、レビュー領域は原則完了とする。

---

# 前PR完了記録: production catalog自動登録 + manual ODS export

PR #53のread-only policyをproduction catalog writerへ接続した。

292 observationの実データ結果:

| route | 件数 |
|---|---:|
| `jacket_gate` | 249 |
| `jacket_top3_title_ocr` | 7 |
| `ocr_title_artist_pair` | 8 |
| auto合計 | 264 |
| manual ODS | 21 |
| capture mismatch reject | 7 |
| false decision | 0 |

validation copyへの初回applyは264件を更新し、同一plan再applyは264件すべてno-opとなった。

現行manual ODSは画像を直接確認しにくく、候補診断列が多く、Master Songsを同梱せず、実レビューには不十分である。次Goalでは現行形式との互換性より、実用的なmanual review運用への置換を優先する。

---

# 前PR完了記録: jacket自動登録policy read-only評価

PR #52のsnapshot mappingとcurrent ROI v2 jacket rankingを再利用し、292 observationを評価した。

| policy route | auto | correct | false |
|---|---:|---:|---:|
| `auto_jacket_gate` | 249 | 249 | 0 |
| `auto_jacket_top3_title_ocr` | 7 | 7 | 0 |
| `auto_ocr_title_artist_pair` | 8 | 8 | 0 |
| 合計 | 264 | 264 | 0 |

- confirmed coverage: 92.6316%
- manual review残件: 21
- capture mismatch reject: 7
- false decision: 0

この結果をPR #54のproduction catalog登録pipelineへ接続した。

---

# 運用上の判断

全曲収集を優先する。

現在の自動登録policyとcatalog登録pipelineを利用し、高信頼結果を順次catalogへ反映する。manual review対象の完全処理を待って次の収集へ進む必要はない。

ローカルで再生成またはバックアップ可能な開発用DBについて、操作性を大幅に損なうproduction service相当の承認・整合性契約を新たに追加しない。
