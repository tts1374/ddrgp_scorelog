# 用語集

DDR GP scorelog の設計、PoC、テストで使う主要用語を定義する。似た名前の概念が多いため、実装やレビューではこの定義を基準にする。

この文書では、工程コード、入力field、画像feature、候補材料、正式保存値を分けて読む。工程コードは作業のまとまりを示すだけで、DB schema version、認識精度、保存可否を表さない。`M5jacket`、`M7title`、単独の `OCR` は曖昧な略称なので、新しいIssue・docs・Skillでは使わない。

## 読み方の基本

- `M5`、`M7` などはmilestone、`M5b` / `M5c` はその下位phase、`M5c-4` のような表記はphase内の作業単位を示す。
- `song_title`、`artist`、`jacket`、`score_digits` などは入力ROIまたは観測fieldの名前であり、そこから曲ID・譜面ID・正式保存値が確定したことを意味しない。
- `candidate`、`observation`、`feature`、`expected_value`、`match`、`recognized_digits` は、明示的に採用済みの正式値と区別する。
- status名やCSV列名は英語のまま契約語彙として扱う。似た日本語へ勝手に統合しない。

## 要件レベルの正式保存根拠

正式保存の要件、Issue、acceptance criteriaでは工程コードや実装クラス名を使わず、次の要件語彙を使う。source IDは根拠の役割と画像由来であることを表し、OCR、raw OCR、candidate、previewを正式根拠へ含めない。

| 要件語彙 | 対象 | 正式根拠source | 正式根拠にしないもの |
| --- | --- | --- | --- |
| `RESULT同定根拠` | `song_id`、`chart_id` | `result_identity_visual_evidence`による採用済み画像照合とmaster整合 | `identity_signal_*`、OCR文字列、candidate、expected、preview |
| `current-master-compatible jacket reference` | current GP masterの`song_id`、canonical title、canonical artistと完全一致するconfirmed jacket reference | catalogの画像featureを用いた`RESULT同定根拠` | master versionの一致だけでの採用、title/artist不一致、orphan、未確認、旧feature |
| `RESULT数値認識根拠` | `score`、`max_combo`、判定数、`ex_score` | `result_numeric_visual_evidence`による採用済み画像認識 | `recognized_digits`だけ、raw OCR、expected、`match`、preview |
| `RESULT状態認識根拠` | `rank`、`clear_type`、任意の`flare_rank` | `result_rank_visual_evidence`、`result_clear_type_visual_evidence`、`result_flare_rank_visual_evidence`。専用画像認識または正式な画像認識値からの規則導出を含む | rank/clear type OCR、animation表示、candidate、expected、preview |
| `capture event根拠` | `play_id`、`played_at`、`duplicate_key` | confirmed capture eventのmetadataとcapture UTC | 相対`timestamp_ms`、score/file由来のduplicate key |
| `正式保存可否` | formal playを既存workflowへ渡せるか | 上記根拠のsource、confidence、完全性、confirmed non-duplicate | 根拠不足、低confidence、ambiguous、missing、failed |

`M5 master match`、`M7a digit recognition`、`M7 result field recognition`、`M9 application/runtime`は、上記要件をどの実装が担当するかを示す対応名に留める。これらの工程名、`identity_signal_*`、`recognized_digits`をformal source IDや保存条件の代わりに使わない。

今後のIssue・PR・設計では、実装工程名ではなく次の要件名を優先する。

| 要件名 | 意味 | 接続する正式根拠 |
| --- | --- | --- |
| `RESULT数値・状態の正式画像認識` | RESULT画面の数字、rank、clear type、flare rankをapp-owned画像認識で採用すること | `RESULT数値認識根拠`、`RESULT状態認識根拠` |
| `RESULT同定・譜面の正式画像認識` | RESULT画面のジャケット、play style、difficulty、levelを画像認識し、current master/catalogと一意整合させること | `RESULT同定根拠`、`master_metadata` |
| `正式保存根拠の保存workflow接続` | confirmed non-duplicate RESULTの全根拠を既存正式DB保存workflowへ渡すこと | `capture event根拠`を含む全source/confidence/完全性 |
| `live RESULT identity retry` | live監視でconfirmedになったRESULTの`RESULT同定根拠`だけが一時的に未解決の間、同じcapture event IDを維持して後続frameを再評価すること | `RetryIdentity`は正式保存workflowへ未接続であることを示し、RESULT消失または8回目の試行で未解決として収束する |

## 工程コード

| 呼び方 | 正式な意味 | 主な対象・成果物 | この工程だけでは確定しないもの |
| --- | --- | --- | --- |
| `M0 input boundary` | frameを再現可能に受け取る入口 | `FrameInput`、manifest、timestamped、dry-run capture | result確定、OCR成功、DB保存 |
| `M1 event boundary` | result形状・継続・重複・遷移を分ける工程 | `result_candidate`、`confirmed_result`、`duplicate`、`rejected_transition` | 曲・譜面ID、数値、正式play |
| `M2 score OCR` | 数字ROIのTesseract OCRとprofile/expected coverageを評価する工程 | `score_ocr.*`、OCR profile、`evaluated` | 正式なスコア・判定数 |
| `M3 result field observation` | result画面の曲名・artist・譜面条件を後工程へ渡せる観測にする工程 | `song_title`、`artist`、`play_style`、`difficulty`、`level`、M3レポート | 曲名照合、曲ID・譜面ID、正式保存値 |
| `M4 master DB` | 楽曲・譜面のcanonical情報と照合対象を生成する工程 | `songs`、`charts`、`song_aliases`、`ddrgp-master.sqlite` | 入力画像の曲同定、個人スコア保存 |
| `M5 master match` | `RESULT同定根拠`へ進める前の曲・譜面候補と失敗理由を観測する実装工程 | title match、jacket match、`identity_signal_*` | 確定ID、保存OK、本番採用済み照合 |
| `M5b jacket reference catalog` | jacket参照featureをcurrent masterと一緒に安全に保持・読む基盤 | collector source `databases/jacket-catalog.sqlite`、binding済みruntime catalog、coverage、runtime loader | 正式個人スコアDB、画像原本の代替 |
| `M5c developer-only collector` | M5b catalogへ入れるjacket観測を収集・reviewする開発者専用工程 | collector、observation session、manual review、title/artist OCR評価 | 公開app、正式保存workflow、自動確定 |
| `M6 payload/evidence boundary` | 保存候補payloadと解析根拠・sourceを分離する工程 | save payload、analysis/source、候補材料 | 候補の正式値昇格 |
| `M7 save decision boundary` | 必須fieldの検証、保存前readiness、正式値変換の境界 | M7 readiness、save decision preview、formal evidence | previewだけでのDB保存 |
| `M7a digit recognition` | `RESULT数値認識根拠`へ進める前の数字ROIをテンプレート/bitmap比較で読むM7内の非OCR工程 | `recognized_digits`、digit review、Tesseract comparison | 正式なスコア・判定数、保存OK |
| `M7 result field recognition` | `RESULT状態認識根拠`へ進めるrank/clear_type/flare_rankを専用規則で認識する工程 | FAILED/E gate、score-derived rank、judgment-count clear type、independent flare badge | candidate/raw/previewのformal昇格 |
| `app-owned formal evidence bridge` | confirmed RESULT observationに明示された要件別source/confidenceを検査し、既存M8 formal save inputへ接続する境界 | `RESULT同定根拠`、`RESULT数値認識根拠`、`RESULT状態認識根拠`、`capture event根拠`、`unresolved`理由 | `identity_signal_*`、`recognized_digits`、candidate、expected、preview、known-resultの正式値昇格 |
| `M7 result-text feature` | resultのtitle/artist ROIからOCRなしの画像featureを作る補助工程 | `jacket-catalog.sqlite` の `result_text_features`、`m7_result_text_feature_master.*` 診断出力 | OCR文字列、曲ID確定、正式保存値 |
| `M8 formal personal score DB` | version 1正式DB、duplicate、transaction、明示単発保存を扱う工程 | `ddrgp-scores.sqlite`、formal save input | 候補材料の自動昇格、M8 preview DBの受入れ |
| `M9 application/runtime` | app package-owned runtimeでviewer、Windows capture、capture-save、監視UI、task trayを接続する工程 | WPF app、app-owned runtime、capture-save workflow | 新しい数字認識方式やDB schema |
| `M10 initial release` | 単一ユーザー向けの配布・依存固定・backup/restoreを固める工程 | installer/配布手順、lock file、運用docs | cloud運用、複数ユーザー、enterprise機能 |

## master DB inspection status

WPFが現在の環境の固定pathをread-onlyで検査した結果を表す。これはmaster DBの内容を正式個人スコアへ保存するstatusではなく、保存workflowを開始してよいかの入口状態である。

| status | 正式な意味 |
| --- | --- |
| `missing` | 現在の環境の固定pathにmaster DBが存在しない。正しいDBが固定pathへ用意されるまでcapture解析・正式保存を開始しない。 |
| `read不可` | pathはあるが、directory、SQLiteでない、アクセス権、I/Oなどの理由でread-only読込できない。 |
| `schema incompatible` | SQLiteとして読めるが、必須table、metadata、件数、source snapshot、master生成schemaの整合が現行runtimeと一致しない。 |
| `compatible` | 現行runtimeがread-onlyで検証でき、保存workflow開始前のmaster参照として利用できる。 |

master DB inspectionは起動時・保存開始時に行う。固定pathだけを再利用して、過去の保存結果、skip、拒否、失敗、候補をsavedへ昇格させるcheckpointは持たない。

## M10 local storage terms

- `development environment`: Debugで明示されたdevelopment root、またはDebugのcurrent directory／Debug出力directoryから親方向にsource checkout（`databases/`とScore Viewer project）が検出できる実行環境。既定DBはそのrootの`databases/`配下に置く。Releaseではdevelopment判定を行わない。
- `production environment`: Releaseの実行環境。既定DBは`%LOCALAPPDATA%\DDRGpScoreViewer\data\`配下に置き、repositoryのDBへfallbackしない。
- `app-owned runtime`: Score Viewer app packageが実行ロジックとruntime資材を所有するM9 runtime境界。Releaseではrepository root、repository内Python module、Python executable、Tesseractを探索・起動せず、packageまたは明示data pathだけから資材を解決する。
- `M5b jacket reference catalog`: `ddrgp-master.sqlite`とは別の`jacket-catalog.sqlite`。current jacket feature、M7 result-text feature、review historyを持つ参照catalogで、正式個人スコアDBや画像原本ではない。
- `evaluation DB`: M10-3が所有するdevelopment専用の評価SQLite。正式個人スコアDB、M4 master DB、M5b jacket reference catalogから分離し、WPF viewerは開かない。
- `formal score DB protection boundary`: 起動時はmaster/catalog検証後の固定score pathに限り、missing／0 byteの新規正式schema準備だけを既存file-preparation契約へ委譲し、通常の起動・閲覧・更新・評価処理では既存の非空正式個人スコアDBをread-only検証、上書き、migration、repairしない境界。正式writerの明示saveと確認済み個人スコアデータ復元だけが、既存の準備・transaction契約を使う明示的な変更操作である。
- `user settings`: WPF appの起動時監視、保存できない結果のローカル通知、既定プレイスタイル、起動時画面を保持する正式DB外のローカル設定。`user-settings.json`へ保存し、欠落・読込不能時は4項目すべてを初期値へ戻す。正式個人スコアDB、`plays`、保存境界は変更しない。
- `personal score data backup`: データ管理画面から明示的に作成・復元する、正式`plays`の履歴表示と自己ベスト算出に必要な個人プレー履歴だけのJSON。設定、master/catalog、jacket参照、source capture、解析ログ、診断ログを含まず、migration用のSQLite backupとは別契約とする。

`M5c` の下位phaseは次の意味で読む。

- `M5c-1`: current-only read-only review projection。
- `M5c-2`: current catalog manual review。
- `M5c-3a`: window capture lifecycle。preview/frameはmemory-only。
- `M5c-3b`: jacket observation session。stable observationとcheckpointを扱う。
- `M5c-3c`: current unresolved observation ingest。
- `M5c-4`: collectorが明示採用したartifactのtitle/artist OCR方式評価。
- `M5c-5`: unresolved candidate projection。候補表示でcatalog writerを呼ばない。
- `M5c-6`: title/artist OCR failure diagnostics。profile比較と失敗理由の診断。

## 曖昧になりやすい工程名

| 曖昧な呼び方 | 用語集で使う呼び方 | 読む範囲 |
| --- | --- | --- |
| `M5jacket` / `M5 jacket` | `M5 jacket match` | result `jacket` ROIとsong-select由来のjacket referenceを、chart候補集合内で比較するPoC。`M5b` catalogや`M5c` collectorとは別。 |
| `M5title` / `M5 title` | `M5 title match`、または `title OCR suffix` / `title line-hash` / `title image feature` | どの補助信号かを必ず明記する。M5 title matchはM3のresult `song_title`をM4へ照合する入口で、各補助信号は候補集合外から曲を拾わない。 |
| `M7title` / `M7 title` / `M7 jacket validation result title/artist feature master` | `M7 result-text feature` または `M7 result title/artist image feature` | result `song_title` / `artist` ROIから作るOCR-free画像feature。M3 result OCR、M5 title補助、M5c song-select OCR、M5b catalogのtitle hashとは別。照合用payloadはM5b catalogへ保存する。 |
| `jacket catalog` | `M5b jacket reference catalog` | collector sourceまたはbinding済みruntime catalogのcurrent reference、`result_text_features`、review状態。`data/`の診断出力や正式個人スコアDBではない。 |
| `title line hash` | `M5b/M5c catalog: title_line_hash` または `M7 result feature: title_linehash_rows` | 前者はsong-select `INFORMATION`欄のcatalog identity、後者はresult title feature payloadの行別値。同じ名前のfeatureとして流用しない。 |

`--m5-jacket-match` は現在の互換CLI入口であり、実行時にM5 jacket matchの出力とM7 result-text featureの診断出力を同時に生成する場合がある。`--m7-result-text-feature-catalog` を明示した場合だけ、acceptedなtitle/artist payloadをM5b catalogへ冪等保存する。CLI option名だけを見て、M7 result-text featureをM5 jacket featureや正式保存値と読み替えない。

## OCR、数字認識、画像featureの区別

`OCR` は画像を文字列へ変換する一般名であり、工程名ではない。対象を付けて次のように読む。

| 正式な呼び方 | 対象 | 方式・入口 | 出力の意味 |
| --- | --- | --- | --- |
| `M2 score OCR` | resultのスコア・判定数など数字ROI | Tesseract OCR、profile評価、互換 `score_ocr` 出力 | raw/normalized数字候補とexpectedとの比較。正式数値ではない。 |
| `M3 song/artist OCR` | resultの `song_title` / `artist` ROI | `--m3-song-artist-ocr`、OCR入口診断 | `ocr_raw`、`pre_normalized_text`、status、failure reason。曲名照合前の入口観測。 |
| `M5 title OCR suffix` | M5でjacket候補が曖昧なときのresult title文字列 | M3 OCR結果から `TYPE1/2/3` 等を候補集合内で再順位付け | `title_ocr_rerank_status`。候補観測であり、M5 `matched`や確定IDではない。 |
| `M5c title/artist OCR` | song-select observationのtitle/artist ROI | local Tesseract、`M5c-4` / `M5c-6` profile評価 | catalog/manual review用の候補評価。公開appや正式保存へ自動昇格しない。 |
| `M7a digit recognition` | `RESULT数値認識根拠`へ渡す前のresult可変桁数字ROI | template/bitmap比較。Tesseractとは別経路。 | `recognized_digits`、segment/status、expected/match。数字候補であり正式数値ではない。 |
| `M7 result-text feature` | resultのtitle/artist ROI | OCR-freeのluma/edge/dHash/line-hash等の画像feature | distance比較に再利用するpayloadとhash。文字列や正式IDではない。 |

`M3 song/artist OCR` と `M5c title/artist OCR` は、同じtitle/artistという言葉を含むが入力画面が異なる。前者はresult、後者はsong-select observationである。`M7 result-text feature` は文字列を読む処理ではないため、`M7title` をOCRの別名として扱わない。

## feature、catalog、candidateの区別

- `jacket ROI`: 画面から切り出したジャケット画像領域。画像原本や曲IDではない。
- `jacket feature`: jacket ROIを縮小画像、色、hash、距離比較用ベクトルなどへ変換した観測値。
- `feature hash`: feature payloadの同一性を示すhash。近似距離を計算するにはpayloadも必要で、hashだけで「似ている」とは判定しない。
- `feature master`: featureと参照ラベルをCSV/JSONへ並べた診断出力。M5の一時出力とM7 result-text featureのJSON/CSVは診断用で、照合再利用用のM7 payloadはM5b catalogへ保存する。
- `reference catalog`: current master、feature、review状態、historyをstrictに管理する永続的なローカルSQLite。現行ではM5b jacket reference catalogを指す。
- `candidate observation`: M5 `identity_signal_*`、M7a `recognized_digits`、M7 previewなど、後続レビューへ渡す材料。候補が一意でも正式値ではない。
- `formal value`: 採用済みsource、field別根拠、必要なvalidationを満たし、M8正式save inputへ明示的に配置された値。
- `flare_rank`: RESULT右側の独立badgeから認識する正式field。値は `I`〜`IX` / `EX` に限定し、認識不能時は `null` のまま保存を妨げない。`null` は「no-flareの証明」ではない。
- `RESULT状態認識根拠`: rank/clear_type/flare_rankの専用画像認識または正式な画像認識値からの規則導出が出すfield別根拠。rankはROIでFAILED/Eだけを判定し、通常rankはformal scoreから算出する。clear_typeは判定数から算出し、rank周囲のanimation表示やOCRを根拠にしない。`M7 result field recognition evidence`はこの要件の実装対応名である。

## OCR結果の読み方

- `ocr_raw` はOCR engineの生文字列。空でないことは正解や照合成功を意味しない。
- `pre_normalized_text` は改行と連続空白などを入口で整えた文字列。M5の曲名正規化、M4照合、保存値確定とは別段階。
- `normalized` は出力ごとに定義された比較用文字列。どの正規化を使ったかは各設計docの契約を優先する。
- `expected_value` はローカル評価用の期待値または診断ラベル。`match=true` は期待値との一致であり、正式値採用を意味しない。
- `confidence` はそのfield/方式の信頼度指標。閾値以上でもcandidateをformalへ自動昇格しない限り保存可否は決まらない。
- `missing_ocr`、`empty_ocr`、`ocr_failed`、`engine_unavailable`、`no_expected_value` は、OCR入口・engine・期待値不足を分ける語彙であり、マスタ照合失敗と混同しない。

## FrameInput

分類、イベント確定、OCR・数字認識・画像featureへ渡す1フレーム分の入力契約。

構成:

- `image_path`
- `timestamp_ms`
- `row`

metadata、timestamped、manifest、dry-run capture、将来の実キャプチャAPIを同じ境界で扱うためのPoC上の中心概念。

## manifest

フレーム列をCSVとして再実行可能にする入力形式。

必須列:

- `image_path`
- `timestamp_ms`

任意列:

- `screen_type`
- OCR期待値列
- 補助列

manifest mode で読み込む。timestamped と dry-run capture provider は manifest互換CSVを出す。

## dry-run capture provider

実キャプチャAPI導入前に、既存画像ディレクトリを capture provider の代替入力として扱うPoC入口。

特徴:

- 実デバイスには接続しない。
- ファイル名昇順で画像を読む。
- 単調増加する `timestamp_ms` を付ける。
- フレームを `data/` 配下へ保存する。
- manifest互換CSVを出す。

## metadata mode

`samples/screenshots/metadata.csv` を読み、キャプチャ時刻なしで分類評価するモード。

特徴:

- `timestamp_ms=None`
- `confirmation_mode=frames`

## timestamped mode

metadata と同じ画像列へ人工 timestamp を付けるモード。

特徴:

- `timestamp_ms` を人工生成する。
- `confirmation_mode=time`
- `frame_manifest.csv` を出す。
- metadata の期待値列を保持する。

## manifest mode

manifest CSVを読み込むモード。

特徴:

- `timestamp_ms` 必須。
- `confirmation_mode=time`
- timestamp の空、非整数、負数、非単調増加をエラーにする。

## screen_type

metadata または manifest 上の画面種別。

主な値:

- `result`
- `song_select`
- `gameplay`
- `menu_setup`
- `transition`

分類の期待値、評価集計、テストシナリオに使う。実キャプチャでは未知の場合があるため、空欄や `unknown` も許容する。

## result_shape_candidate

リザルト画面らしい形状を検出したか。

これは保存候補ではない。`transition_countup_*` は `result_shape_candidate=true` でも保存対象外にする。

## result_candidate

単発フレーム分類で保存候補に見えるか。

これは保存確定ではない。実キャプチャでは一瞬の誤検出や遷移フレームが混ざるため、継続条件を満たすまで保存しない。

## confirmed_result

継続条件を満たし、保存直前候補として確定したか。

metadata mode ではフレーム数ベース。timestamped、manifest、dry-run、将来キャプチャでは時間ベース。

## confirmed event ID

RESULT画面への遷移を1つのconfirmed eventとして扱うため、イベント境界で一度だけ発行する固有ID。同一RESULT画面の後続frame、再送、再処理では同じIDを引き継ぎ、RESULTSが消失してevent boundaryを抜けた後の別confirmed eventでは新しいIDを発行する。

正式DBの`duplicate_key`はこのIDを使う。曲・譜面・スコア・判定数・rank・clear typeから作るRESULT fingerprintは、同一画面内のアニメーション差をまとめるイベントグルーピング専用であり、正式DBの`duplicate_key`には使わない。

## confirmation_mode

保存確定の判定方式。

- `frames`: timestamp なし入力。フレーム数で継続を判定する。
- `time`: timestamp 付き入力。継続時間で判定する。

## event_type

`result_events.csv` で各フレームのイベント解釈を表す値。

- `none`: 未確定
- `confirmed`: 重複ではない保存候補
- `duplicate`: duplicate window 内の重複確定
- `rejected_transition`: 保存不可遷移

## confirmed-events

保存直前のOCR・数字認識・field抽出・画像feature評価対象。呼び名にOCRが残っていても、M7a digit recognitionやM7 result-text featureを除外する意味ではない。

対象条件:

```text
confirmed_result=true
duplicate=false
```

`--ocr-target confirmed-events` で使う。

## duplicate

同一リザルトが duplicate window 内で再確定したか。

`duplicate=true` の行は `confirmed_result=true` でも保存しない。

## duplicate_key

重複判定に使うキー。

現行PoCでは、ファイル名に `scoreXXXXXX` があれば `score:<digits>`、なければ `file:<filename>`。app-ownedの正式保存ではconfirmed event IDを使い、RESULT fingerprintや正式RESULT値をduplicate keyへ昇格しない。

## transition_countup_*

リザルト遷移中またはカウントアップ中を表すローカル素材の命名。

期待:

- `result_shape_candidate=true` でもよい。
- `result_candidate=false`
- `confirmed_result=false`
- `event_type=rejected_transition`
- 保存対象外
- confirmed-events OCR対象外

## expected value / expected columns

OCR、field抽出、M5/M7の評価に使う期待値または診断ラベル列。期待値との `match` はローカル評価結果であり、M4 canonical、M5候補、M7 formal value、M8正式DBの値を自動的に決めない。

例:

- `score`
- `expected_score`
- `max_combo`
- `expected_max_combo`
- `miss`
- `expected_miss`
- `ex_score`
- `expected_ex_score`

manifest や timestamped 出力で保持する。metadata由来の期待値は評価用の参照であり、実画面から採用済みの正式値とは分けて読む。

M3入口では、数字OCRとは別に曲・譜面情報ROIの期待値列も扱う。

例:

- `song_title` / `expected_song_title`
- `artist` / `expected_artist`
- `play_style` / `expected_play_style`
- `difficulty` / `expected_difficulty`
- `level` / `expected_level`
- `rank` / `expected_rank`

これらは `m3_metadata_expected_coverage.md` で confirmed-events 対象の列充足を見るための値であり、数字OCRの `ocr_expected_coverage.md` には含めない。`song_title` / `artist` はM3 result field観測、`play_style` / `difficulty` / `level` はM3 chart-field観測として読む。M5の照合入力になっても、曲ID・譜面IDの確定値にはならない。

## evaluated

対象ROIまたはfieldのすべての評価試行に期待値がある状態。`evaluated` は評価対象が揃っているという意味で、全件正解、方式採用、正式保存を意味しない。

## partially_evaluated

対象ROIまたはfieldの一部の評価試行に期待値があり、一部には期待値がない状態。coverageや採用判断は暫定扱いにする。

## no_expected_values

対象ROIまたはfieldの評価試行に期待値がない状態。OCR・抽出の成功扱いにせず、期待値不足として別に読む。

## confirmed-events boundary

M2以降のOCR・field抽出・M5/M7候補観測へ進める、保存直前イベントの最小対象条件。

```text
confirmed_result=true
duplicate=false
```

M0/M1で確定するのはこの解析対象境界であり、正式DBへの保存成功ではない。duplicate、rejected transition、未確定候補、non-resultをこの境界へ混ぜない。

## M7 save readiness / decision preview

M3、M5、M7aなどの候補材料を、M8正式保存の前に1件単位で束ねて不足やレビュー対象を示す状態。

- `ready_for_save_review` はPoC材料がレビュー入口まで揃った状態。
- `preview_save_candidate` はM8へ渡す候補材料が揃ったpreview状態。
- `needs_identity_review`、`needs_digit_review`、`blocked_readiness`、`missing_required_material` はレビューや材料不足を示す。
- どのstatusも、保存OK、DB保存成功、曲ID/譜面ID確定、数字の正式値確定を意味しない。

## formal save boundary

M8の明示的な正式保存入口。confirmed-eventsだけを対象にし、`RESULT同定根拠`、`RESULT数値認識根拠`、`RESULT状態認識根拠`、`capture event根拠`から構築したfieldごとの採用済みsource、formal play値、正式duplicate key、必要な時刻・master情報などをstrictに検証した `PersonalScoreDbSaveInput` だけを受け取る。`identity_signal_*`、`recognized_digits`、expected値、raw OCR、M8 preview rowは、そのまま正式値へ昇格しない。

正式DB保存の詳細は `docs/design/10_personal_score_db_schema.md` と `docs/design/05_storage_io_spec.md` を正本とする。

## Windows app automatic monitoring

Windows appの監視状態は、次の`MonitoringState`を正式名称として使う。`WaitingForGame`は対象windowの探索待ち、`ManuallyStopped`は同一app session中の自動再開抑止、`Blocked`はDBまたはruntime異常による自動開始抑止を表す。

| code | 正式な状態 | 意味 |
| --- | --- | --- |
| `Starting` | 監視開始中 | debounce済みの対象windowへ監視workerを接続中 |
| `WaitingForGame` | ゲーム待機中 | 対象windowの検出または消失後の再出現を待機中 |
| `Monitoring` | 監視中 | 対象windowを監視workerが処理中 |
| `ManuallyStopped` | 手動停止済み | 明示停止を受け、同一app session中の自動再開を抑止中 |
| `Blocked` | 監視開始不可 | DB検証またはruntime起動の失敗により自動開始を抑止中 |
| `ShuttingDown` | 終了処理中 | app終了要求後、新しい監視を受け付けずworkerを停止中 |

automatic monitoringの既定値は、1秒間隔、対象windowの2回連続検出、対象windowの2回連続消失である。単発の探索失敗は待機として扱い、DB異常、runtime異常、更新処理中、終了処理中とは区別する。
