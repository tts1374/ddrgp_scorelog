# 実装ロードマップ

DDR GRAND PRIX のリザルト画面からスコア情報を自動取得し、ローカルDBへ保存する最終系までの実装計画です。詳細な要求は `docs/requirements.md`、画面解析PoCの個別方針は `docs/vision-poc-prep.md` と `tools/vision_poc/README.md`、状態や契約の設計は `docs/design/` を参照します。

## 最終系

Windows常駐アプリが DDR GRAND PRIX のゲームウィンドウを直接キャプチャし、リザルト画面だけを検出して、曲・譜面・スコア・ランク・判定数を解析する。解析結果が十分に信頼でき、同一リザルトの重複ではない場合だけ、個人スコアDBへ1プレー1レコードとして自動保存する。低確信度、不一致、解析失敗、重複疑いはDBへ保存せず、画像とログを残して後から検証できるようにする。

最終系で分離して持つものは以下です。

- マスタDB: BEMANIWiki 由来の曲・譜面情報。個人スコアとは分離する。
- 個人スコアDB: 保存済みプレー履歴。マスタDB更新で上書きしない。
- 解析ログ: 保存成功、保存スキップ、低確信度、例外を追跡する。
- 失敗時画像: 解析失敗や低確信度の原因確認用に保存する。

## 現在地

現在は画像解析PoC、マスタDB生成、マスタ照合PoCを経て、M8の正式個人スコアDB version 1へ安全に1件保存できる境界まで実装済みです。DB基盤の追加高度化はいったん止め、次は保存済みスコアを閲覧できる最小ビューアへ進みます。その後、既存PoCからv1 DBへの縦断接続、実キャプチャ、常駐監視の順で最終系へ近づけます。

完了済みの主な足場:

- 固定ROIによるリザルト候補分類
- `result_shape_candidate` と `result_candidate` の分離
- `transition_countup_*` をリザルト形状検出済みでも保存不可候補として扱うイベント化
- timestamp なし metadata mode と timestamp 付き mode の分離
- `FrameInput` 契約による入力境界
- manifest mode による `image_path,timestamp_ms` CSV入力
- `timestamp_ms` 単調増加検証
- `confirmation_mode=time` による time-based confirmation
- `confirmed_result=true` かつ `duplicate=false` を confirmed-events の保存直前境界として扱う方針
- duplicate window のPoC
- score digits と判定数ROI、EX SCORE のOCR前処理PoC
- OCR profile比較PoC
- `evaluated` / `partially_evaluated` / `no_expected_values` による期待値カバレッジ整理
- dry-run capture provider PoC
- dry-run provider から `data/` 配下へフレーム保存し、manifest互換CSVを出力する入口
- 複数 `screen_type` 混在の dry-run sequence scenario
- non-result、短すぎる result、確定 result、duplicate、`transition_countup_*` を同じ時系列で確認する回帰テスト

まだ未着手またはPoC止まりの主な領域:

- 本番キャプチャAPI
- 実キャプチャデバイス依存コード
- 常駐監視ループ
- 非同期処理
- 曲名、プレースタイル、難易度、レベルの本格OCR
- preview材料と明示的な正式値を分離して正式保存入力を組み立てるpure adapter（実装済み。runner/CLI未接続）
- 明示指定された新規/0 byte/compatible正式個人スコアDBへの単発実ファイル保存（実装済み。runner/CLI未接続）
- 低確信度ログと失敗画像の本番保存
- Windows常駐アプリUI
- 配布形態

## 直近MVP

常駐監視や実キャプチャの前に、既存v1 DBをread-onlyで閲覧できる最小WPFスコアビューアを最初のユーザー価値とする。

```text
正式個人スコアDB v1
  -> compatible read-only open
  -> マスタDBread-only参照
  -> 曲・譜面情報つき全プレー履歴一覧
  -> プレー詳細
  -> 譜面別自己ベスト集計
```

ビューアの次に、`manifest/manual -> confirmed event -> 解析済み正式値 -> v1 DB -> viewer` の縦断経路を接続する。最小ビューアは生成済みマスタDBをread-only参照して曲・譜面情報を表示するが、自動キャプチャ、常駐、マスタDB更新、検索、グラフは同時実装しない。

## マイルストーン

### M0: 画像解析PoCの入力境界を固める

目的は、実キャプチャに進む前に「フレーム列を受け取り、時刻付きで分類・確定・OCR対象選定できる」契約を安定させることです。

完了済み:

- metadata mode
- timestamped mode
- manifest mode
- dry-run capture provider
- manifest互換出力

残作業:

- M0範囲では、ローカル素材差分が出た場合に同じ検証コマンドで確認する。
- 次はM1として保存直前イベント仕様をさらに固定する。

完了条件:

- 生成manifestが `--sequence-mode manifest` で読める。
- `timestamp_ms` が単調増加する。
- manifest mode で `confirmation_mode=time` が維持される。
- `transition_countup_*` が保存対象外のまま維持される。
- expected columns と `no_expected_values` / `partially_evaluated` の扱いが壊れていない。

### M1: 保存直前イベントの仕様を固める

目的は、DB保存前のイベント境界をPython PoC内で明確にすることです。

やること:

- `result_events.csv` の列意味を仕様として固定する。
- 保存候補を `confirmed_result=true` かつ `duplicate=false` に固定する。
- 現行PoCでは `event_type=confirmed` も保存候補として読めるが、将来 `event_type` が増えても基本境界は `confirmed_result=true` かつ `duplicate=false` とする。
- `duplicate_key` は現行PoCの簡易実装であることを明記し、本格差し替えポイントを整理する。
- time-based confirmation のしきい値をサンプルで再確認する。
- `transition_countup_*` は `result_shape_candidate=true` でも保存対象外にする方針を維持する。

完了条件:

- 保存対象、重複、遷移除外、未確定の区別がテストとREADMEで一致している。
- duplicate、`rejected_transition`、`none`、未確定 `result_candidate` が保存対象外であることをテストで確認できる。
- DB保存実装がなくても「保存してよいイベント行」を一意に判定できる。

### M2: 数字OCRの保存候補品質を上げる

目的は、保存候補イベントに対してスコアと判定数を十分な精度で読める状態にすることです。

現在地:

- `confirmed-events` を保存直前OCR相当の主評価対象として使う。
- timestamped と manifest 再読込の両方で `confirmation_mode=time` と expected columns 保持を確認する。
- `evaluated` / `partially_evaluated` / `no_expected_values` でROI別の期待値カバレッジを読む。
- `no_expected_values` はOCR成功扱いにせず、profile比較の `reference_profiles` も目視参考に留める。
- duplicate、未確定候補、`rejected_transition` は confirmed-events OCR対象外のまま維持する。
- default summary と profile summary の読み分けを固定し、`score_ocr_summary.json` は default profile の現行弱点、`score_ocr_profiles_summary.json` は profile採用候補の比較として読む。
- 現ローカル確認では `ex_score` の default は 4件中1 match / 3 mismatch だが、`low-threshold` は 4件中4 match で、confirmed-events かつ `evaluated` の場合だけ採用候補として読める。
- ローカル検証用に、各 result 画像を non-result reset 後の2連続フレームとして並べ、result間を duplicate window より長く離した manifest を `--make-m2-expanded-manifest` で `data/` 配下へ再生成できる。既存の保存境界を保ったまま `ex_score` の confirmed-events 母数を16件へ増やせ、この確認では default は 4 match / 11 mismatch / 1 empty、`low-threshold` は 16 match / 0 mismatch / 0 empty で、`recommendation_readiness=adoption_candidate` を維持している。
- 同じ拡張manifestで主要数字ROI全体を16件評価すると、`score_digits`、`max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss` は default profile が16 match / 0 mismatch / 0 emptyで、現時点では追加調整なしで採用候補として固定できる。`ex_score` だけ default が弱く、`low-threshold` が16 match / 0 mismatch / 0 emptyの単独採用候補になる。

次にやること:

- confirmed-events を主評価対象として、標準4件評価と拡張16件評価の両方で `score_digits`、`max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss`、`ex_score` の精度を継続確認する。
- `no_expected_values` のROIは成功扱いにせず、metadata期待値列を増やす。
- `partially_evaluated` は暫定判断として扱い、採用判断前に不足期待値を埋める。
- ROI座標の大変更ではなく、まずは局所前処理とprofile評価で改善する。
- default OCR summary と profile比較の読み分けを維持し、保存候補で弱いROIを `ocr_roi_report.md` の default counts / recommended counts / delta から追う。
- `ex_score` 以外の主要数字ROIは拡張16件評価で default 16/16 match を確認済みなので、次は実キャプチャ前の追加素材や期待値列増加時に同じ読み方で再確認する。`ex_score` は引き続き `low-threshold` 採用候補として扱い、本番処理への直結は別フェーズに残す。

完了条件:

- confirmed-events の主要数字ROIが十分な評価母数で `evaluated` になる。
- 保存候補イベントに対して必須数字項目の読み取り失敗理由を説明できる。
- OCR方式刷新なしで改善できる範囲と、次に方式変更が必要な範囲が分かれている。

### M3: 曲・譜面情報の読み取りPoC

Status: Completed on 2026-07-04.

目的は、スコアだけではなく、曲名ROIのOCR入口結果、プレースタイル、難易度、レベルを保存候補ごとの観測値として取り出すことです。M3の成果物は照合済みデータではなく、M5のマスタ照合PoCへ渡すOCR生文字列、譜面3項目のPoC抽出候補、失敗理由です。

やること:

- 曲名ROI、プレースタイルROI、難易度ROI、レベルROIの切り出しを安定させる。
- OCR、テンプレート照合、色特徴の組み合わせを小さく比較する。
- SP/DP、難易度、レベルの読み取りを先に安定させる。
- 曲名はOCR生文字列と入口失敗理由を出す。正規化文字列、候補一覧、照合確信度はM5のマスタ照合PoCへ残す。

M3内マイルストーン:

1. M3-1: 期待値と評価セットの整理
   - metadata期待値はROI目視を正にする。
   - ファイル名由来baselineは、古いラベルやドリフト検出の参考として扱う。
   - `roi-template-nearest` の同分布 leave-one-out 診断を再確認する。
   - 同分布診断を採用済みテンプレート照合やマスタ照合の成功扱いにしない。
2. M3-2: chart-field評価分割
   - confirmed-events result ROIを、参照用と評価用に分ける。
   - `chart_field_templates/` と評価対象を混同しない。
   - `play_style` / `difficulty` / `level` の外部検証に近い評価形を作る。
   - 参照を `chart_field_templates/` のみに限定した `roi-template-holdout` レポートを、同分布 leave-one-out 診断とは別に読む。
3. M3-3: chart-field採用候補の仕様化
   - `play_style` / `difficulty` / `level` について、採用候補にする extractor と失敗理由を決める。
   - `filename-baseline`、`roi-feature-nearest-centroid`、`roi-template-nearest` の読み分けをREADME、docs、testsで固定する。
   - 低確信度、参照不足、期待値不足の failure_reason を保存前判断へ渡せる語彙に寄せる。
   - 初回ローカル holdout では `play_style` を `roi-template-holdout` の `adoption_candidate` として読め、`difficulty` と `level` は `needs_template_references` だった。
   - ローカル37テンプレート配置後は `play_style`、`difficulty`、`level` がすべて confirmed-events 60/60 match の `adoption_candidate` になった。
   - `m3_chart_field_adoption_candidates_summary.json` と `m3_chart_field_adoption_candidates.md` は採用候補レビューであり、本番採用済みテンプレート照合、OCR、マスタ照合の成功扱いにしない。
4. M3-4: 曲名・artist ROIの入口
   - `song_title` / `artist` のOCR生文字列と正規化前文字列を出す。
   - 長い曲名、日本語、記号、2行表示、artist切れを代表ケースとして見る。
   - マスタ照合、ファジーマッチ、曲名正規化の本格実装にはまだ進まない。
   - `--m3-song-artist-ocr` で confirmed-events 対象の `m3_song_artist_ocr.csv`、summary、Markdownを出し、`engine_unavailable` / `ocr_failed` / `empty_ocr` / `no_expected_value` を保存前判断に近い失敗理由として観察できる入口を追加済み。
5. M3-5: 保存候補向けのM3集約レポート
   - confirmed-eventsごとに、曲名、artist、`play_style`、`difficulty`、`level` の抽出状態を一覧化する。
   - `m3_save_candidate_summary.csv`、summary、Markdownで `ready`、`missing_reference`、`ocr_unavailable`、`ocr_failed`、`empty_ocr`、`no_expected_value`、`not_adopted` を保存前判断に近い状態として出す入口を追加済み。
   - `play_style` の `ready` は M3-3 の採用候補、`song_title` / `artist` の `ready` は M3-4 OCR入口観察であり、DB保存可能やマスタ照合成功として扱わない。
   - DB保存やマスタ照合はM4/M5以降へ残す。
6. M3-6: 保存候補ブロッカーの代表整理
   - M3-5集約から、未ready fieldを status / failure reason ごとに代表化する。
   - `m3_save_candidate_blockers_summary.json` と Markdownで、代表 `organized_file`、期待値、抽出値、extractor、`roi_path` を出す入口を追加済み。
   - 対象は confirmed-events 境界だけに限定し、duplicate、`rejected_transition`、未確定候補、non-result は含めない。
   - 代表整理はレビュー補助であり、DB保存可否判定、マスタ照合、ファジーマッチ、曲名正規化には進まない。
7. M3-7: 保存前ブロッカー解消順の整理
   - M3-5集約の未ready fieldから、追加すべきローカルテンプレート参照ラベル、参照追加後の再確認、曲名/artist OCR入口の次手を分ける。
   - `m3_save_candidate_blocker_resolution_plan.json` と Markdownで、解消順、必要ラベル、代表 `organized_file`、期待値、抽出値、extractor、`roi_path` を出す入口を追加済み。
   - テンプレート画像、OCR画像、PoC出力はGit管理せず、必要ラベルと判断だけをdocsに残す。
   - 解消順整理はレビュー補助であり、DB保存可否判定、マスタ照合、ファジーマッチ、曲名正規化には進まない。
8. M3-8: chart-field採用候補の最終整理
   - ローカル37テンプレート配置後の `play_style` / `difficulty` / `level` 60/60 match を、PoC上の採用候補としてdocsとテストで固定する。
   - M3-5集約では chart-field 3項目を `ready` として扱えるが、DB保存可能、本番採用済みテンプレート照合、マスタ照合の成功ではない。
   - M3の残りは `song_title empty_ocr` / `artist empty_ocr` のOCR入口代表失敗の整理に絞る。
9. M3-9: song_title / artist OCR入口失敗代表の整理
   - `m3_song_artist_ocr_entry_failures_summary.json` と Markdown で、M3-4 OCR入口行の `engine_unavailable` / `ocr_failed` / `empty_ocr` だけを入口失敗代表として整理する。
   - `song_title` は主要項目、`artist` は左右切れがある補助項目として別々に読み、同じ改善対象として混ぜない。
   - ローカル確認では `song_title empty_ocr=2`、`artist empty_ocr=22` だが、これはOCR入口観察であり、曲名正規化、ファジーマッチ、マスタ照合、DB保存可否判定へは進まない。

M3で「読めた」と扱ってよい範囲:

- `song_title`: OCR入口が空ではない、または入口失敗理由を説明できる状態。
- `play_style` / `difficulty` / `level`: confirmed-events 60件でPoC上の抽出候補を出せる状態。
- `artist`: 曲名照合の主キーではなく、左右切れがある補助観測値。

M3で「読めた」と扱わない範囲:

- 曲ID、譜面ID、マスタ曲名への一意照合。
- 曲名正規化、ファジーマッチ、候補一覧、照合スコア、照合確信度。
- M5で照合した結果、M3のOCR文字列が違っていたと判明する可能性の排除。

M3完了判断:

- M3はM5へ渡す観測値と失敗理由をそろえた段階で完了とする。
- `play_style` / `difficulty` / `level` はPoC上の抽出候補として扱い、採用済みテンプレート照合やマスタ照合の成功とは扱わない。
- `song_title` / `artist` はOCR入口結果と入口失敗代表を出すところまでで止め、前処理改善や正規化は別フェーズへ回す。
- 以降の曲ID・譜面ID特定はM4のマスタDB生成後、M5のマスタ照合PoCで扱う。

完了条件:

- 保存候補イベントから、M5の曲・譜面照合PoCへ渡す観測値と失敗理由を出せる。
- 低確信度時にDB保存へ進めないための理由候補をM3レポート上で説明できる。
- confirmed-events対象から `play_style` / `difficulty` / `level` のPoC抽出候補を安定して出せる。
- 曲名は少なくともOCR生文字列と入口失敗理由を出せる。
- duplicate、`transition_countup_*`、未確定候補、non-result はM3評価対象外のまま維持される。
- README、docs、testsで、何を成功扱いにしてよいかが固定されている。
- 曲名・譜面の正規化済み照合成功はM5まで未確定として扱う。

### M4: マスタDB生成

目的は、BEMANIWiki の全曲リストから曲・譜面マスタDBを生成することです。

現在地:

- `master` パッケージに、BEMANIWiki風HTMLの楽曲リスト表を解析して `songs` / `charts` / `master_metadata` / `source_snapshots` を持つSQLite DBを生成する初期入口を追加済み。
- 2026-07-04時点の取得元URLと2段ヘッダ構造を確認し、`docs/design/08_master_db_generation.md` に初期スキーマと境界を整理済み。
- 通常テストはネットワークに依存せず、小さなHTML fixtureでセル結合、注記付きレベル、削除/限定/パック記号、SP/DP片方のみ、CHALLENGEなし、同名曲・同アーティスト、複数バージョン表を固定する。
- 注記付きレベルは raw 表記を保持し、整数 level は最初に現れる数字列から取得する。同一 `chart_id` の譜面行が食い違う場合は生成失敗として扱う。
- 実HTML取得では `data/master/ddrgp-master.sqlite` に 1282 songs / 9594 charts を生成できることを確認済み。ただし生成DBはGit管理しない。
- GitHub Actions の `build-master-db.yml` で、手動・週次実行、fixtureテスト、実HTML生成、metadata件数検査、artifactアップロードを行える。

やること:

- GitHub Actions artifactの生成結果を確認し、取得元構造変化の検出が十分かを見る。
- Releases成果物として配布する流れを作る。
- 配布互換性が必要になった段階で `song_id` / `chart_id` の互換方針を固定する。

完了条件:

- ローカルとCIでマスタDBを再現生成できる。
- 取得元構造変化を検出できる。
- スコア取得側が参照できる安定したDBスキーマがある。

### M5: マスタ照合PoC

目的は、M3保存候補の曲・譜面観測値をM4マスタDBへ照合し、M7以降の保存判定へ渡す曲同定候補観測と失敗理由を出すことです。M5の `matched` や `identity_signal_*` はPoC上の候補観測であり、保存OK、曲ID/譜面ID確定、本番採用済み照合ではありません。

現在地:

- `tools.vision_poc --m5-master-match` で、M3保存候補行とM4マスタDBを使う最小入口を追加済み。
- 曲名OCR文字列はNFKC、casefold、空白除去、代表的な句読点除去だけの最小正規化を行う。
- `play_style` / `difficulty` / `level` で `charts` を絞り、候補曲数、候補譜面数、最上位候補、score、`match_status`、`failure_reason` を `master_match_candidates.csv` / summary / Markdownへ出す。
- `matched` はPoC上の一意候補であり、DB保存可能や本番採用済み照合ではない。
- `--m5-jacket-match` で、`song_select` grid右上プレビュー由来のローカルjacket特徴量マスタ、通常候補60件の `jacket_match_candidates.csv`、duplicate / unconfirmed を含む `jacket_match_diagnostics.csv` を出す。
- M4公式canonicalと `song_aliases` を使い、`RЁVOLUTIФN` / `RËVOLUTIФN` のような表記差を候補集合外から曲を拾わずに吸収する。
- `jacket_reference_coverage.csv` と `jacket_reference_diagnostics_coverage.csv` で、chart-field条件の候補song_idごとにローカルjacket参照の有無を確認できる。
- `jacket_match_summary.json` は照合PoC信号、`jacket_reference_coverage_summary.json` は参照素材カバレッジ診断として読み分ける。`expected_missing_feature` / `expected_not_in_chart_candidates` / `expected_unresolved` はレビュー材料であり、保存候補昇格やGP対象外曲復帰には使わない。

M5完了時点で固定すること:

- 通常候補は `confirmed_result=true` かつ `duplicate=false` だけに限定する。
- 診断出力は duplicate / unconfirmed を観察できるが、保存候補や保存可否判定として扱わない。
- `jacket_match_status=matched` はPoC上の一意候補であり、保存可能ではない。
- `identity_signal_status=jacket_resolved_candidate` / `composite_resolved_candidate` は後続保存判定へ渡す候補観測であり、曲ID/譜面ID確定ではない。
- `missing_feature` はjacket照合行のローカル参照不足、`expected_missing_feature` は期待曲側の参照カバレッジ診断として分けて読む。
- 参照不足時に、近傍の別曲へ寄せて解消扱いにしない。
- title画像特徴量、title OCR、title line-hashは、jacket ambiguous候補集合内の再順位付けだけに使い、候補集合外から曲を拾わない。
- スコア/判定数のTesseract離脱、保存OK/NG、低信頼度ログ、個人スコアDB保存はM5に含めない。

完了条件:

- M4 DBを入力に、通常M5、M5 jacket、診断M5、参照coverageを再生成できる。
- `jacket_match_candidates.csv` で expected song、expected song_id、expected song resolution、official availability、expected distance、expected rank、top margin、`identity_signal_*` を確認できる。
- `jacket_reference_coverage.csv` で候補song_idごとの参照有無と、期待曲側の `expected_missing_feature` / `expected_not_in_chart_candidates` / `expected_unresolved` を確認できる。
- `docs/design/09_master_match_poc.md` と `tools/vision_poc/README.md` が、通常候補、診断出力、coverage summary の読み分けを説明している。
- fixtureテストが、coverage語彙、代表CSV、診断coverage出力名をネットワーク、画像、`metadata.csv` なしで固定している。
- 生成DB、PoC出力、OCR画像、`metadata.csv`、ローカル素材、ローカルDBをGit管理していない。
- この条件を満たした後、次フェーズは M7a「スコア系数字認識のOCR脱却」または M6「本番キャプチャAPIの最小接続」へ切り分ける。

### M5b: ローカルjacket参照カタログ基盤

Status: Completed on 2026-07-14.

目的は、M5 jacket照合をGPプレー可能な全曲へ広げるため、song select grid由来のjacket、title、artist観測をM4マスタへ安全に紐付けて永続化するlocal catalog基盤を固定することです。実用的なcapture、review UI、master更新、約1200曲の収集workflowはM5cへ分けます。

固定する方針:

- M5bの入力はlocal observation CSVとし、主要ROI抽出、同一観測の重複排除、M4マスタ照合、永続特徴量、coverageを固定する。capture、title / artist取得、review UIはM5cで扱う。
- 利用範囲は開発者本人のローカル利用に限定し、capture、crop、特徴量カタログを第三者へ共有しない。Git、GitHub Actions artifact、Releaseにも含めない。
- 対象は、対象master versionで `grand_prix_play_available=true` となる全songとする。未収集、参照済み、要レビュー、未解決を分けてcoverageを集計する。
- 生の全画面captureとjacket cropはレビュー完了までローカル保持できるようにし、レビュー後の削除は明示cleanupとする。自動削除は初期範囲に含めない。
- 現行M5互換のjacket照合に必要な `thumbnail_rgb`、histogram、dHash相当、feature extractor version、source image hash、`song_id`、master version、review statusをローカル参照カタログへ永続保存する。16x16 RGB縮小値を含む特徴量も画像由来の非共有データとして扱う。
- songとjacket referenceは1:Nとし、同一songに複数の有効参照を保持できるようにする。同一captureの再投入では参照件数を増やさない。
- canonical title + artist完全一致、一意alias一致など安全な一意照合だけを自動確定し、複数候補、低確度、取得失敗は候補と理由を付けて人手レビューへ送る。
- 自動確定率、既知誤確定、理由別件数を同じcapture観測分母で計測できるようにする。90%以上の必須目標と95%以上の努力目標は実用収集を行うM5cの運用完了条件とし、率のために一意性条件を緩めない。
- runtime identityはjacketを主信号とし、chart fieldsで候補を絞った後にjacket照合する。jacket参照なし、曖昧、未解決ではtitle / artistだけのfallbackを行わず、正式playへ昇格しない。
- 参照カタログは正式個人スコアDB、`source_captures`、`analysis_logs`、`plays` と分離し、M5 identity候補観測を供給する責務だけを持つ。

実績:

- 専用identity/schema versionを持つlocal SQLite catalog、strict create/open、captureとsource hash + 解決identityの冪等性、song 1:N、同一画像bytesを共有する別songの保持、`image_kind` と全768 thumbnail・histogram・dHash永続化を追加した。kind訂正時は同じreferenceの特徴量を再計算する。
- canonical title + artist完全一致と一意alias完全一致だけをauto-confirmし、曖昧・artist不一致・観測/feature失敗を候補理由付きでレビュー状態へ残す。
- 全GP songの4状態coverage、候補songのneeds-review投影、候補なし未割当観測、master drift/orphan、capture分母のauto-confirm rateとknown-false auditをJSON/CSV/Markdownへ出す。
- `--m5-jacket-catalog` からcurrent masterと現行extractor versionで有効な永続特徴量だけを既存M5 jacket照合へ供給し、旧extractor混入を防ぎ、参照生画像削除後の再実行を固定した。
- catalog、特徴量、review、coverageは `data/` 配下のローカル非共有物のまま、正式保存workflow・正式個人スコアDB schema・WPF監視UIを変更していない。

完了条件:

- local observation CSVからstrict catalogを再現生成し、GPプレー可能な全songを参照済み、要レビュー、未収集、未解決のいずれかへ機械的に分類できる。
- 自動確定率、理由別件数、監査済み/未監査、既知誤確定を同じdeduplicated observation集合から確認できる。
- fixture監査で既知の誤確定がなく、曖昧候補を近傍の別曲へ寄せて解消扱いにしない。
- 同一入力の再実行、複数jacket reference、master version更新時のorphan・再レビュー対象を検査できる。
- 生画像削除後も永続特徴量からM5 jacket照合を再実行できる。
- capture、crop、特徴量、レビュー結果、ローカルカタログがGit管理、CI artifact、Release、通常ログへ混入しない。

### M5c: 開発者専用jacket catalog collector

Status: In progress. M5c-1 completed on 2026-07-14; M5c-2 completed on 2026-07-15; M5c-3a completed on 2026-07-15; M5c-3b以降は未着手。

目的は、M5b catalogを約1200曲の手作業画像保存・CSV記入に依存せず運用するため、公開WPF appと分離した開発者専用collectorを追加することです。開発者はsong select gridを手動巡回し、ツールがmaster更新、coverage、review、capture、jacket安定検出、観測生成、M4照合を担当します。ゲーム操作は自動化しません。

固定する方針:

- collectorは `tools/` 配下の独立appとし、公開 `DDRGpScoreViewer`、M9 monitoring/tray、installer、Releaseへ含めない。
- Wiki譜面表と公式収録曲一覧からのmaster生成・inspectionは既存M4 builderを再利用し、staging検証成功後の明示操作だけでatomic publishする。
- master version/hash、GP対象曲、catalog identity/schema、参照済み、要レビュー、未収集、未解決、orphan、旧extractorを1つの開発者画面で確認できるようにする。
- 曖昧referenceは元capture/crop、観測title/artist、候補、理由を表示し、将来の手動確定を `auto_confirmed` と異なるprovenanceで保持する。
- 将来のcollection sessionは開始時のmaster version/hashとfeature extractor versionを固定し、中断・再開と冪等再投入を前提にする。
- DDR GP window自動特定はdeveloper-only collectorで先行評価してよいが、候補根拠と誤検出を観測可能にし、ゲームへの入力、focus操作、grid自動巡回は行わない。
- 同一画像hashだけで全観測をskipせず、同じ画像bytesを共有する別songを別referenceとして保持するM5b境界を維持する。
- capture、crop、特徴量、review、master/catalog DB、source snapshotはlocal dataとし、Git、CI artifact、Release、通常公開logへ含めない。

保留事項:

- title/artist取得方式、OCR精度、auto-confirm閾値、jacketと文字領域の更新ずれ対策
- 完了: `manual_confirmed`、`rejected`、review/reassignment historyを持つcatalog v2と、v1を不変に保つcopy-on-write移行
- reject、取り消し、完全削除、source image削除を分ける操作契約
- 完了: window候補条件、preview付き初回確認、strict identity再検査、handle消失・再起動後の明示再選択契約
- 完了: memory-only raw frame ring buffer、drop観測、capture resource lifecycle。採用frame、診断frame、crop保持、明示cleanup policyはM5c-3b以降
- catalog referenceとlocal source capture/cropを再表示可能に結ぶlocator、retention、欠損時表示の契約

実行順:

1. M5c-1: 完了。`tools/jacket_catalog_collector/` に独立developer app/testを追加し、既存M4 master build/inspectをstaging + atomic publishで実行する。M5b catalogはPythonのversion 1 read-only projectionを介してcoverage/review queueを表示し、catalog mutation、capture、OCRは行わない。
2. M5c-2: 完了。catalog v2、strict validator、v1からのcopy-on-write移行、revision/action IDによる競合・再投入契約、append-only history、手動confirm/reassign/reject/reopenを追加した。projection v2とcollectorは明示GP song検索・確認・history表示を提供し、runtimeはcurrent master/GP/current extractorを満たすauto/manual referenceだけを読む。
3. M5c-3a: 完了。DDR GP window候補検出、preview付き確認、strict identity再検査、Windows Graphics Captureの明示開始・停止、bounded memory-only raw frame ring buffer、window/resource lifecycleを追加した。catalog観測生成は行わない。
4. M5c-3b: jacket ROI変化/安定検出、同一preview制御、session checkpoint、中断・再開、観測自動生成、catalog投入を追加する。実装前にcapture lifecycleとsession永続化が同じ検証セットで扱えるか再確認し、必要ならさらに分割する。
5. M5c-4: 実captureでtitle/artist取得を評価し、採用条件を満たす方式だけauto-confirmへ接続する。未採用・不一致はreviewへ残す。

完了条件:

- 開発者がCSVを通常編集せず、master更新、coverage確認、曖昧review、手動紐付け、grid手動巡回によるcollectionを独立appで実施できる。
- capture済み対象song全体を分母にauto-confirm率90%以上、95%以上を努力目標として確認でき、既知誤自動確定0件を優先する。
- 中断・再開、同一session/frame再投入、同じ画像を共有する別song、master/extractor driftを安全に扱える。
- 公開app、正式保存workflow、正式個人スコアDB、ゲーム操作へ接続せず、local dataを公開成果物へ混入させない。

M5c-1で固定した境界:

- 既存非空masterはnetwork/build前にinspectionし、新規/0 byte/compatible targetはinspection済みstagingだけを明示publishする。失敗・取消ではtarget、temporary file、新規の空parentを残さない。
- UIはcatalog schemaを直接解釈せず、Pythonがstrict read-only validationとcoverage/review projectionを担当する。C#はversion 1 JSONを未知field/statusも含めstrictに読むが、reason文字列はopaqueに保持する。
- master/catalog選択と表示はDB byteを変更せず、旧extractor、master drift、orphan、候補なし未割当観測を成功へ丸めない。
- developer projectは公開 `app/` のproject/resource/Releaseへ依存せず、通常solutionやinstallerへ追加しない。

M5c-2で固定した境界:

- catalog v1はimmutable sourceとし、別pathのv2 stagingをstrict検証した後だけexclusive publishする。既存target、失敗、取消、競合ではsource/targetを変更しない。
- v2 mutationはexpected revision/status/songとaction IDを要求し、current row更新とappend-only historyを1 transactionにする。同一ID・同一payloadは冪等、異なるpayloadとstale stateは副作用なしで拒否する。
- manual confirm/reassignはcurrent masterのGP対象songを明示選択した場合だけ行い、candidate、expected、OCR rawを暗黙昇格しない。reject/reopenはevidenceを物理削除しない。
- projection v1 fixtureはread-only互換として残し、producer v2はcatalog v1をmigration-required/read-only、catalog v2をmanual-review capableとしてstrictに投影する。
- capture、window探索、title/artist OCR、物理削除、公開app、正式保存workflow、正式個人スコアDBは変更しない。

M5c-3aで固定した境界:

- title/process由来の候補はhandle、PID、process start、title/class、client size、visible/minimized snapshotとpreview/根拠を表示するが、自動選択・自動開始しない。
- capture開始前とframe受領時にidentity/size/stateを再検査し、stale候補、handle再利用、resize、最小化、対象終了では暗黙再選択・再開始しない。
- WGC native frame queueとimmutable PNG ring bufferはboundedとし、満杯時のdropを表示する。通常停止、取消、対象終了、device loss、例外、collector終了ではin-flight callback後にresourceを1回だけ解放する。
- frame/preview/diagnosticはmemory-onlyで、disk、catalog、観測、OCR、公開app、正式保存workflowへ接続しない。

### M6: 本番キャプチャAPIの最小接続

目的は、dry-run provider の入力元だけを実キャプチャへ差し替えることです。

やること:

- Windows Graphics Capture API を第一候補として調査・実装する。
- DDR GRAND PRIX ウィンドウを特定する。
- 取得フレームへ単調増加する `timestamp_ms` を付ける。
- しばらく manifest互換 dry-run 出力を維持する。
- 実キャプチャフレームを `FrameInput` 相当の契約へ接続する。

完了条件:

- 実キャプチャフレームを保存し、manifest modeで再実行できる。
- 実キャプチャ入力でも time-based confirmation が動く。
- 本番DB保存なしで、分類・OCR・イベント確定まで検証できる。

### M7: 保存判定と低確信度ログ

目的は、自動保存してよい結果と保存しない結果を明確に分けることです。

やること:

- 保存候補イベントに対して必須項目の解析結果を集約する。
- スコア、判定数、曲ID、譜面ID、ランクなどの信頼度をまとめる。
- 低確信度、OCR失敗、マスタ照合失敗、重複疑いの理由をJSONログにする。
- 保存失敗時画像とログの保存先を決める。

現在地:

- `m7_save_readiness_review.csv`、`m7_save_readiness_review.json`、`m7_save_readiness_review.md` で、M3保存候補材料、M7a数字材料、任意のM5 jacket候補観測を confirmed-events 1件単位に束ねる保存判定前レビュー入口を追加済み。
- 入力は `m3_save_candidate_summary_rows`、`m7a_digit_save_candidate_summary_rows`、任意の `jacket_match_rows` とし、duplicate、`rejected_transition`、未確定候補、non-result は対象外のまま維持する。
- readiness status は `ready_for_save_review`、`blocked_m3_material`、`blocked_digit_review`、`blocked_identity_signal`、`missing_required_material`。`ready_for_save_review` は保存判定へ進むためのPoC材料が揃った状態であり、保存OK、DB保存成功、曲ID/譜面ID確定ではない。
- `--m5-jacket-match` 実行時は `identity_signal_*` / `jacket_match_status` を参照列として取り込み、`identity_signal_status=jacket_resolved_candidate` / `composite_resolved_candidate` だけをM5側レビュー可能材料として扱う。M5 identity材料がレビュー可能な場合は `song_title` / `artist` OCR不足だけでは `blocked_m3_material` にせず、元のM3 blockerは `m3_blocking_fields`、M7保存前レビュー上のM3 blockerは `m7_m3_blocking_fields` として分ける。M5未実行時は従来どおりM3 + M7a材料だけでレビューする。
- `m7_save_decision_preview.csv`、`m7_save_decision_preview.json`、`m7_save_decision_preview.md` で、`m7_save_readiness_review_rows` を入力にした保存判定プレビュー入口を追加済み。
- preview status は `preview_save_candidate`、`blocked_readiness`、`needs_identity_review`、`needs_digit_review`、`missing_required_material`。`preview_save_candidate` はM8へ渡す候補材料が揃ったプレビュー状態であり、保存OK、DB保存成功、曲ID/譜面ID確定ではない。
- プレビューではM5未実行の `ready_for_save_review` 行も `needs_identity_review` として止め、`m5_not_run`、`m5_identity_not_reviewable`、`identity_signal_id_missing` を `preview_reason` と代表で読み分ける。`identity_signal_song_id` / `identity_signal_chart_id` は候補観測としてだけ出す。M7aの `recognized_digits`、`expected_value`、`match`、`failure_reason` も保存値確定ではなくレビュー材料として出す。
- `m7_save_decision_preview.json` / Markdown は、`preview_save_candidate` の M5 source、jacket status、identity signal status の件数と代表、`needs_identity_review` の理由別代表、`needs_digit_review` のROI別代表を出す。これはM8へ渡す前の診断補助であり、DB保存可否判定ではない。
- `m8_save_payload_preview.csv`、`m8_save_payload_preview.json`、`m8_save_payload_preview.md` で、M7 preview行から将来DB保存へ渡すならどの材料になるかをdry-run payloadとして確認する入口を追加済み。入力は `m7_save_decision_preview_rows` で、`preview_save_candidate` 以外は `unsupported_preview_status` としてpayload材料から除外する。
- M8 dry-run status は `payload_ready`、`missing_identity_candidate`、`missing_digit_value`、`unsupported_preview_status`。`payload_ready` はM8本実装前の仮payload材料が揃った状態であり、DB保存可能、保存成功、曲ID/譜面ID確定、保存値確定ではない。
- `m8_planned_play_records.csv`、`m8_planned_play_records.json`、`m8_planned_play_records.md` で、`payload_ready` 行だけを個人スコアDB `plays` 相当の最小row contractへ変換する入口を追加済み。`unsupported_preview_status`、`missing_identity_candidate`、`missing_digit_value` は保存予定レコードへ変換しない。
- `m8_score_db_write_preview.csv`、`m8_score_db_write_preview.json`、`m8_score_db_write_preview.md` で、保存予定レコードだけを新規 in-memory SQLite `plays` へinsertするdry-run入口を追加済み。非ready payloadは上流の planned records で止まり、write previewへ進まない。previewスキーマ識別として `schema_version=1` をsummary/reportへ出し、SQLite側は `PRAGMA user_version=1` にする。
- `--m8-score-db-output data\...\ddrgp-scores.sqlite` を明示した場合だけ、保存予定レコードを `data/` 配下の新規SQLiteファイルへinsertするfile output previewを追加済み。出力DBはGit管理せず、`inserted_to_file_preview` は本番保存成功、曲ID/譜面ID確定、保存値確定として扱わない。
- 最小 `plays` スキーマとinsert境界は in-memory SQLite fixtureと明示オプション付きfile output previewで固定している。`song_id` / `chart_id` はM5候補観測、数字列はM7a候補値のまま扱う。
- 現時点では本番insert、低確信度ログ本番仕様、保存値本番確定には進んでいない。

完了条件:

- 保存する、保存しない、重複として捨てる、低確信度としてログ保存する、を機械的に判定できる。
- 誤保存を避けるための失敗側ログが残る。

### M7a: スコア系数字認識のOCR脱却

目的は、個人スコアDBへ保存する数値項目を、Tesseract OCR依存からテンプレート/画像特徴ベースのPoCへ切り出すことです。曲・譜面同定を扱うM5とは分け、DB保存を実装するM8より前に、保存値として使う数字の読み取り方式と失敗理由を固定します。

現在地:

- `--m7a-digit-recognition` で、confirmed-events 境界だけを対象にした非OCR数字認識PoCの最小入口を追加済み。
- 初期対象は既定で `score_digits`。`--m7a-digit-rois all` で判定数ROIや `ex_score` へ広げられるが、採用判断はまだ行わない。
- 桁分割した前景maskを、ローカル `digit_templates/<roi>/<digit>.png`、共有グループ、または `<root>/<digit>.png` のbitmapテンプレートと比較する。テンプレート画像はローカル素材でGit管理しない。判定数系は `digit_templates/judgment_counts/`、`max_combo` / `ex_score` 系は `digit_templates/combo_ex_score/` を共有候補として試せる。
- `score_digits` は0から1,000,000までの可変桁表示を前提にし、カンマや背景ノイズを除いた大きな数字成分だけを左から読む。1桁から7桁までを固定6桁へ寄せない。
- `max_combo` はROI左側ラベルや下線を除き、右側数字領域の前景コンポーネントを分割する。テンプレート不足時でも `segment_count_counts` と `expected_digit_length_counts` で分割数と期待桁数を確認できる。
- 出力は `m7a_digit_recognition.csv`、`m7a_digit_recognition_summary.json`、`m7a_digit_recognition_report.md`。既存 `score_ocr.csv` / `score_ocr_summary.json` は壊さず、同じ実行にOCR結果がある場合だけ `tesseract_comparison` で比較する。
- status は `recognized`、`ambiguous`、`missing_reference`、`failed_segmentation`、`not_evaluated` を分ける。これは保存値候補の読み取り材料であり、保存OK/NG判定やDB保存ではない。
- 2026-07-07時点のローカル `score_digits` テンプレート配置後は、confirmed-events 60件で M7a が60/60 `recognized` / match。Tesseract比較ありの実行では、Tesseract側の余分な桁または先頭誤読との差分が3件、OCR未取得が1件だった。これはM7aの保存値候補観測であり、保存OK判定ではない。
- 2026-07-07時点のローカル `max_combo` テンプレート配置後は、`score_digits` と `max_combo` の2 ROIで confirmed-events 60件ずつ、合計120/120 `recognized` / match。テンプレート配置前でも `max_combo` は `missing_reference` のまま分割分布が期待桁数分布と一致する。
- 2026-07-07時点のローカル `marvelous` テンプレート配置後は、`score_digits`、`max_combo`、`marvelous` の3 ROIで confirmed-events 60件ずつ、合計180/180 `recognized` / match。テンプレート配置前でも `marvelous` は `missing_reference` のまま分割分布が期待桁数分布と一致する。
- 2026-07-07時点のローカル `perfect` テンプレート配置後は、`score_digits`、`max_combo`、`marvelous`、`perfect` の4 ROIで confirmed-events 60件ずつ、合計240/240 `recognized` / match。テンプレート配置前でも `perfect` は `missing_reference` のまま分割分布が期待桁数分布と一致する。
- `great`、`good`、`miss` を非OCR認識対象へ広げ、右側数字領域を分割・認識できるfixture、テンプレート不足時の分割数診断、共有 `judgment_counts` テンプレートだけで読めるfixtureを追加済み。
- `max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss` は4桁fixtureでも右側数字領域を分割・認識できる。`marvelous`、`perfect`、`great`、`good`、`miss` は共有 `judgment_counts` テンプレートだけでも認識できるfixtureを追加済み。
- 2026-07-07時点のローカル `good` テンプレート配置後は、`score_digits`、`max_combo`、`marvelous`、`perfect`、`great`、`good` の6 ROIで confirmed-events 60件ずつ、合計360/360 `recognized` / match。`good` は左側ラベル由来成分を避けるため、右側数字領域へのfocusを `great` より少し強くしている。
- 2026-07-07時点の共有 `judgment_counts` テンプレート確認でも、同じ6 ROI合計360/360 `recognized` / match。
- 2026-07-08時点のローカル `miss` ROI別テンプレート配置後は、`score_digits`、`max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss` の7 ROIで confirmed-events 60件ずつ、合計420/420 `recognized` / match。判定数ROIは明るい青背景を数字扱いしないよう高明度かつチャンネル差が大きい成分を除外している。`miss` はさらに右側数字領域へのfocus、白数字向けの明度 + チャンネル差mask、最小高さをROI別に絞っている。共有 `judgment_counts` だけでは `miss` が58/60 `ambiguous`、2/60 `recognized` になるため、ROI別テンプレートを使う。
- 2026-07-08時点の `ex_score` M7a確認では、右側数字領域へのfocusとcomponent分割、既存 `max_combo` テンプレートfallbackにより、ROI別 `ex_score` テンプレートなしで confirmed-events 60/60 `recognized` / match になった。`score_digits` から `ex_score` までの8 ROI合計は480/480 `recognized` / match。分割診断は `segment_count_counts={1:1,3:30,4:29}`、`expected_digit_length_counts={1:1,3:30,4:29}`。この確認ではローカル `metadata.csv` の `result_087_sp_basic_lv06_888_score986610.png` の `ex_score` をROI表示どおり `593` に修正している。
- `m7a_digit_save_candidate_summary.csv`、`m7a_digit_save_candidate_summary.json`、`m7a_digit_save_candidate_summary.md` で、confirmed-events 保存候補1件につき1行のM7a横持ち集約を追加済み。選択ROIごとに `recognized_digits`、`status`、`failure_reason`、`match`、`confidence`、`distance` を読み、`aggregate_status` は `all_digits_recognized` / `needs_digit_review` / `no_digit_rois` に留める。これはM8向けの数値読み取り材料であり、保存OK/NG判定やDB保存ではない。
- `m7a_digit_save_candidate_review.json` と `m7a_digit_save_candidate_review.md` で、横持ち集約の `needs_digit_review` 行をROI別 status / failure reason ごとに代表化する補助レポートを追加済み。代表には `organized_file`、ROI名、`recognized_digits`、`expected_value`、`status`、`failure_reason`、`match`、`confidence`、`distance`、`segment_count` を含め、`missing_reference`、`ambiguous`、`failed_segmentation`、`not_evaluated` の読み分けを助ける。これも保存判定やDB保存ではない。
- `m7a_tesseract_comparison_review.json` と `m7a_tesseract_comparison_review.md` で、同じ実行内のM7a数字認識結果と既存Tesseract OCR結果の比較差分を代表化する補助レポートを追加済み。既存 `m7a_digit_recognition_summary.json` の `tesseract_comparison` counts は維持し、`same_normalized`、`different_normalized`、`tesseract_unavailable`、`m7a_unavailable` の代表から、Tesseract差分や未取得理由をROI別に確認する。これも保存判定、DB保存、OCR方式刷新ではない。

やること:

- ローカル `score_digits` テンプレート配置済み環境では、追加素材で1桁から7桁までの実画面サンプルが増えたときに同じ可変桁分割で再確認する。
- `score_digits` のテンプレート余白、桁分割、距離しきい値を継続レビューし、過剰な `ambiguous` や誤認識があれば最小限で調整する。
- M7aの数字読み取り材料は、M7保存判定前レビューの数字側入力として `m7a_digit_save_candidate_summary.*`、`m7a_digit_save_candidate_review.*`、`m7a_tesseract_comparison_review.*` を合わせて確認する。
- 出力は `data/` 配下に置き、テンプレート素材やローカル画像はGit管理しない。
- fixtureテストで、正規化、桁分割、テンプレート選択、失敗理由の基本動作を継続確認する。

完了条件:

- confirmed-events対象で保存値候補になる数字ROIを評価できる。
- OCR非依存の認識候補、信頼度、失敗理由を出せる。
- Tesseract既存結果との差分をsummaryで確認できる。
- mismatch / ambiguous / missing_reference / failed_segmentation を区別できる。
- 保存判定M7やDB保存M8と混同せず、M8へ渡す数値読み取り材料として文書化されている。

### M8: 個人スコアDB保存

目的は、保存候補をローカルの個人スコアDBへ1プレー1レコードで保存することです。

現在地:

- DB insertの前段として、M8 dry-run payload previewを追加済み。
- `m8_save_payload_preview.*` は `m7_save_decision_preview_rows` を入力にし、`preview_save_candidate` だけをpayload候補として扱う。
- `payload_ready` は候補IDとM7a数字列が揃ったdry-run状態であり、保存OKやDB保存成功ではない。
- `preview_save_candidate` 以外は `excluded_preview_status_counts` と代表で読み、payload材料へ昇格しない。
- `m8_planned_play_records.*` は `payload_ready` だけを `plays` 最小列へ写す保存予定レコードプレビューで、DB保存成功、曲ID/譜面ID確定、保存値確定ではない。
- `m8_score_db_write_preview.*` は保存予定レコードだけを新規 in-memory SQLite `plays` へinsertするdry-runプレビューで、insert対象件数、insert後件数、除外件数、代表行、`schema_version=1` を確認する。実ファイルDBは生成せず、本番DB保存成功として扱わない。
- `--m8-score-db-output data\...\ddrgp-scores.sqlite` は、明示した場合だけ保存予定レコードを `data/` 配下の新規SQLiteファイルへinsertするfile output preview。`m8_score_db_file_output_preview.*` は出力DBへのinsert件数と `schema_version=1` の確認であり、本番DB保存成功として扱わない。SQLite側は `PRAGMA user_version=1` を設定する。
- `plays` の最小スキーマとinsert境界は in-memory SQLite fixtureと明示オプション付きfile output previewで検証し、生成したローカルDBファイルはGit管理しない。
- file output previewでは、実DB readback由来の `database_schema_version`、`database_preview_metadata`、`database_plays_row_count`、`database_plays_schema_columns` と、metadata / row count / schema の一致診断をJSONとMarkdown reportの両方で確認できる。
- metadata mismatch、row count mismatch、schema mismatch の負例は実SQLite readback経路で固定済みで、Markdown report上にも mismatch reason がそのまま出ることをfixtureで確認する。
- 2026-07-09時点でM8 previewは、`m8_save_payload_preview.*`、`m8_planned_play_records.*`、`m8_score_db_write_preview.*`、明示file output preview、readback診断までを一区切りの完了範囲として扱う。
- 2026-07-10時点で、正式 `ddrgp-scores.sqlite` の初期schema contractを `tools/vision_poc/personal_score_db_schema.py` と `docs/design/10_personal_score_db_schema.md` に追加した。正式 `plays`、`analysis_logs`、`source_captures`、`score_db_metadata`、`schema_migrations` の責務を分け、M8 preview DBを正式個人スコアDBとして拒否する軽量テストを追加済み。これは本番insert実装ではない。
- 2026-07-10時点で、正式DB検査用の `inspect_personal_score_db_schema()` と `assert_personal_score_db_compatible()` を追加し、空DB、未知DB、M8 preview DB、metadata identity mismatch、必須table欠落、`user_version` mismatch の拒否理由と `migration_plan_status` をテストで固定した。これは互換チェックの入口であり、自動migrationや本番insertではない。
- 2026-07-10時点で、正式DBオープン前段として `initialize_personal_score_db_if_empty()` と `prepare_personal_score_db_for_write()` を追加した。空DBだけ正式初期schemaを作成し、compatible DBは変更せず、M8 preview DB、unknown DB、metadata identity mismatch、manual migration候補は自動変更しないことをテストで固定した。これは既存DB migrationや本番insertではない。
- 2026-07-10時点で、正式DBファイルパス境界として `prepare_personal_score_db_file_for_write(path)` を追加した。新規ファイルと0 byte空ファイルだけ正式初期schemaへ進め、既存compatible DBは変更せず、M8 preview DB、unknown DB、metadata identity mismatch、manual migration候補、非SQLiteファイル、ディレクトリを拒否して自動変更しないことをテストで固定した。これは `--m8-score-db-output` のpreview出力とは別であり、本番insertや既定自動保存ではない。
- 2026-07-10時点で、正式DB inspection / file preparation result をJSON風dictとMarkdownへ投影するdiagnostic関数を追加した。compatible、空DB、M8 preview DB、unknown DB、manual migration候補について、path、`migration_plan_status`、`migration_plan_reason`、拒否理由、必須table欠落、metadata identity、ファイル準備summaryを人間が読める形で固定した。これはCLI表示前の足場であり、本番insert、自動migration、低信頼度ログ本番保存ではない。
- 2026-07-10時点で、正式DB diagnosticを `python -m tools.vision_poc --personal-score-db-diagnostic <path>` からMarkdownまたはJSON風dictで標準出力へ出せるようにした。既定inspect modeは読み取り専用、`--personal-score-db-diagnostic-mode prepare-write` は新規DBファイルまたは0 byte空ファイルだけ正式初期schemaへ進める。compatible、空DB初期化、M8 preview拒否、unknown拒否、manual migration required、非SQLiteファイル、ディレクトリ拒否をCLI経由テストで固定した。これは本番insert、自動migration、既定自動保存、低信頼度ログ本番保存ではない。
- 2026-07-10時点で、正式DB diagnosticに `--personal-score-db-diagnostic-output <path>` を追加し、標準出力と同じMarkdown/JSON診断を `data/` 配下へ保存できるようにした。Markdownは `.md` / `.markdown`、JSONは `.json` に限定し、formatと拡張子の不一致や `data/` 外出力は拒否する。これは診断ファイル生成だけであり、本番insert、自動migration、既定自動保存、`logs/` 連携、低信頼度ログ本番保存ではない。
- 2026-07-10時点で、正式DB diagnosticに `--personal-score-db-diagnostic-log-output <path>` を追加し、診断1回につき1行のJSONLを `logs/` 配下へappendできるようにした。ログレコードはdiagnostic dict、mode、format、exit code相当status、対象DB path、diagnostic output pathを持ち、必須keyと `diagnostic.is_compatible` / exit code / status の整合をappend前に検査する。`.jsonl` 以外や `logs/` 外指定はDB準備より前に拒否する。これは診断ログ入口だけであり、本番insert、自動migration、既定自動保存、低信頼度ログ本番保存、source capture保存ではない。
- 2026-07-11時点で、`PersonalScoreDbSaveInput` と `write_personal_score_db_save()` を追加した。timezone付き時刻、master version、rank/clear type、正式duplicate key、参照整合を検査し、正常保存はsource/play/analysis、duplicate/低信頼度はsource/analysisだけを1 transactionでin-memory正式DBへinsertする。入力拒否とrollbackもfixtureで固定済み。これは確定済み入力からの最小縦断であり、previewからの自動昇格、実ファイル既定保存、CLI保存ではない。
- 2026-07-11時点で、`adapt_personal_score_db_save_input()` を追加した。M8 payload/planned rowは候補材料としてだけ受け取り、正式時刻、master version、ID、数字、rank/clear type、正式duplicate keyを別の明示入力として要求する。不足・不正は `unresolved`、duplicate/低信頼度/その他skipは `play=None` の `excluded`、全条件を満たす場合だけ `ready` とするpure contractをfixtureで固定した。runner/CLI、実ファイル保存、候補値の自動昇格には接続していない。
- 2026-07-11時点で、`save_personal_score_db_file(db_path, adapter_input)` を追加した。adapterをDB準備より先に評価し、unresolvedはファイル/親ディレクトリを作らず理由を返す。readyはsource/play/analysis、excludedはsource/analysisだけを、明示された新規/0 byte/compatible正式DBへ既存writerで記録する。preview/unknown/identity mismatch/manual migration/non-SQLite/directory拒否とrollbackをfixtureで固定し、通常runner/CLI、既定自動保存、自動migrationには接続していない。
- 2026-07-11時点で、`--personal-score-db-save-input` と `--personal-score-db-save-database` の必須ペアを追加した。`input_schema_version=1` のUTF-8 JSONを厳格に読み、候補材料を正式playへ昇格せず、ready/excludedだけ単発保存する。通常PoC、timestamped/manifest runner、既定path、diagnostic/低信頼度ログ自動出力には接続していない。
- 2026-07-11時点で、正式DB保存直前のduplicate preflightを追加した。レビュー済み明示 `duplicate_key` が既存playと衝突した場合は2件目のplayを作らず、新しいsource captureと `skipped/duplicate/duplicate_key_already_saved` analysisだけを同じtransactionで記録し、Python API/CLIは `excluded/written/play_id=null` を返す。完全同一ID再送の冪等化と並行writer制御は未実装で、UNIQUE制約とrollbackを維持する。
- 2026-07-11時点で、`--personal-score-db-save-input-validate` を追加した。既存strict loaderとadapterだけを各1回実行し、ready/excluded/unresolved/invalidをJSONと終了コードで返す。DB pathを受け取らず、DB、`data/`、`logs/`、diagnostic outputを作成・変更しない。readyはsave input構築可能だけを意味し、DB互換性、DB内duplicate、並行writer、実保存は保証しない。
- 2026-07-11時点で、`--personal-score-db-save-input-template` を追加した。`data/` 配下の新規JSONへschema version 1の空review templateを生成し、既存loader互換と未編集時 `unresolved` を固定した。候補値、preview、metadata、DBを読まず、正式値の自動生成、validationや保存への自動連鎖は行わない。
- 2026-07-11時点で、`--personal-score-db-save-input-validate-output` をvalidation inputとの必須ペアとして追加した。標準出力と同じvalidation投影だけを `data/` 配下の新規JSONへ固定形式で保存し、正式値・候補材料・DB情報を記録しない。従来validationの副作用なし、status/終了コード、strict loader/adapter各1回を維持する。
- 2026-07-12時点で、template生成、未編集時のunresolved receipt、人手編集相当、ready receipt、明示saveを既存CLI入口だけで順に実行するE2E fixtureを追加した。READMEにも同じ6段階のPowerShell手順を固定し、receiptは承認・認可・保存成功証明・save入力ではなく、save CLIが要求・消費しないことを明記した。

やること:

- 2026-07-12時点で、低信頼度/error analysisのversion 1詳細JSON、`logs/analysis_details/` と `logs/analysis_failures/` の相対path境界、7日/30日/期限なしのretention metadataをpure contractとfixtureで固定した。さらに明示API/CLIから新規 `logs/analysis_details/**/*.json` へ1件だけatomic生成できるようにし、既存output、unsafe path、option混在を副作用前に拒否した。failure image生成、DB insert、save連鎖は行わない。
- 2026-07-12時点で、現行CLIを独立のまま維持する単発明示orchestration API/CLIを実装した。入力/adapter、共有ID/status/path、DB互換性と早期duplicate、artifact atomic publish/reuse、既存file saveの順とし、低信頼度/errorだけartifact必須とする。DB失敗時はartifactを保持して同一payloadだけ再利用し、partial successを保存成功へ丸めない。
- 正式個人スコアDBのmigration方針、backup前提、互換version遷移を設計する。
- 2026-07-12時点で、正式個人スコアDBのmigration/backup/version遷移をpure contractとfixture matrixで固定した。preview/unknown/identity mismatch/newer unsupported/partial stateを拒否し、verified backupをsource transactionより前に必須化し、migration履歴・metadata・`PRAGMA user_version` の更新順とrollback、dry-run/明示確認/status/終了コードを定義した。実DB migration/backup writerやversion 2 schemaは未実装である。
- 2026-07-12時点で、既存schema inspectionとpure migration contractを合成するread-only status/dry-run CLIを追加した。DB path、target version、明示backup pathを必須とし、JSON/Markdownへ状態、理由、version、backup path検査、予定step、終了コードを表示する。preview/unknown/identity mismatch/newer/partialを拒否し、DB、backup、`data/`、`logs/`を変更しない。
- 2026-07-12時点で、compatibleな現行正式DBからverified backupを明示的に1件作成する独立API/CLIを追加した。新規pathのexclusive create、SQLite source snapshot copy、flush、read-only再open、integrity、formal identity/version/history/row count照合を行い、失敗時は不完全な新規backupだけを除去する。migration、source変更、自動restoreには接続していない。
- 初回リリースまでは正式個人スコアDBをversion 1に固定し、version 2 schema、supported transition、migration SQL、schema writerには進まない。M8の追加監査やdiagnostic高度化を独立した直近PRにせず、次はv1 DBをread-onlyで見る最小スコアビューアへ進む。

完了条件:

- 同一リザルトを複数回保存しない。
- マスタDB更新後も過去履歴が参照できる。
- 保存成功と保存スキップをログで追える。

### M9: Windows常駐アプリ

目的は、ユーザーが実運用できるアプリにまとめることです。

やること:

- 2026-07-13時点で第1段階の最小WPFビューアを追加した。正式v1 DBと生成済みマスタDBをread-onlyで検査し、全履歴、選択プレー詳細、全履歴query由来の譜面別自己ベスト、参照欠落、空・拒否・読取失敗状態を表示する。viewer前後のDB hash不変をfixtureで固定し、save、migration、backup、repair、自動記録には接続していない。
- 2026-07-13時点で第2段階のmanual縦断sliceを追加した。明示選択したstrict workflow入力と正式v1 DBを既存Python orchestrationで1回だけ処理し、saved playだけ同じread-only repositoryで履歴・詳細・自己ベストへ再反映する。excluded / duplicate / unresolved / invalid / DB拒否 / artifact partial successをUIで区別し、候補材料の昇格、自動保存、常駐監視には進んでいない。
- 2026-07-13時点で第3段階のsingle-frame capture sliceを追加した。Windows pickerで明示選択したwindowからWindows Graphics Captureで1フレームだけ取得し、画像、既存manifest互換CSV、最小metadataを `data/windows_capture/` の一意directoryへatomicに出力する。主要失敗状態とresource解放を独立境界で扱い、連続capture、解析、自動保存、常駐監視には接続していない。
- 2026-07-13時点で第4段階の実capture認識品質を確認した。WPF manifestを既存manifest modeで再実行し、1280x720実capture 5枚で分類5/5、confirmed result 1件、M7a主要数字8/8、chart-field 3/3、master/jacket match各1/1を期待値付きで確認した。曲名ROIをartist行から局所分離し、空読み時だけ従来ROIへ戻す。既存 `low-threshold` profileでEX SCOREの実capture差分を吸収した。候補材料を正式値やDB保存へ昇格していない。
- 2026-07-13時点で第5段階の連続capture sessionを追加した。明示選択windowを明示停止まで同じWindows Graphics Capture resourceで取得し、strictly increasing timestampのmanifest互換bundleをstagingからatomicに公開する。resize、target closed、device lost、write失敗は部分sessionを破棄し、解析、自動保存、常駐監視には接続していない。
- 2026-07-14時点で第6段階の正式保存workflow接続を追加した。明示した `連続取得・保存` だけが完成session manifestを既存分類・confirmed event・M5/M7a候補へ渡し、eventを直列に既存正式workflowで処理する。採用済みfield根拠、confidence、完全性が揃わないeventは `unresolved` に保ち、DB duplicate・excluded・解析/DB失敗をsavedへ丸めない。transaction済みplayだけread-only再読込する。現行pipelineの候補値を正式値へ暗黙昇格せず、常駐監視・task trayには進んでいない。
- 第7段階としてM5b jacket参照カタログ基盤を追加済み。実用収集は独立M5cへ分離する。
- タスクトレイ常駐を実装する。
- 監視状態、対象ウィンドウ状態、最新保存結果を表示する。
- マスタDB更新状態を表示する。
- ROI調整画面、失敗ログ一覧、保存済み履歴の簡易一覧を段階的に追加する。

M9残り実行順（PR #21 merge後、原則1項目1PR）:

1. 完了: Windows Graphics Captureで、ユーザーが明示選択した任意windowから1フレームを取得し、既存manifest互換のローカル入力として `data/` 配下へ安全に残す。DDR GRAND PRIXの自動特定、連続capture、解析、保存には進まない。
2. 完了: 実capture画像をmanifestで再実行できる状態を固定し、曲・譜面同定と数字認識を実capture投入可能な品質へ上げる。認識結果を正式値へ暗黙昇格させない。
3. 完了: 明示選択したwindowに対する連続capture sessionを追加し、resize、対象終了、再選択、device lost、resource解放を扱う。監視結果からの自動保存はまだ行わない。
4. 完了: capture、分類、confirmed event、既存正式保存workflowを接続し、保存成功、duplicate、excluded、解析失敗を既存境界のまま1件ずつ処理する。
5. 監視状態、対象window状態、最新保存結果、保存skip、解析失敗ログをWPFへ統合し、タスクトレイから開始・停止・状態確認できるようにする。
6. マスタDB更新状態、長時間動作、再起動・再接続、resource leak、失敗復旧を検証し、M9完了条件を満たす運用状態へ固める。installer、配布、精度保証値の確定はM10へ残す。

4項目目の正式保存workflow接続後、5項目目の監視UI・タスクトレイへ進む前に、M5b「ローカルjacket参照カタログ整備」を独立PRとして差し込む。M5bでは正式保存workflow、監視UI、正式個人スコアDB schemaを変更しない。

M9監視UIはDraft PR #27で独立して進行中である。M5cは最新mainから別branchで開始し、公開WPF appやM9監視ファイルを変更せず、M5c-1を先にmerge可能な単位へ分ける。M5c-1とPR #27の後続merge順は固定せず、後からmergeするbranchが最新mainを取り込み、M5c milestoneとM9残り作業の両方を残してdocs競合を解消する。M5c-2以降とM9残り作業も独立trackとして進める。

この6項目はM9を約6PRで完了させるための基準順であり、M5bは追加の独立PRとして扱う。各PRは現在の目的、完了条件、検証セットでmerge可能な単位に保ち、独立した次項目へ同じPR内で進まない。実測で安全に統合・分割する必要が出た場合も、capture、認識品質、正式保存、jacket参照カタログ、常駐監視の責務境界は混ぜない。M10の初期版リリース準備は、この後さらに2から3PRを目安とする。

完了条件:

- ユーザー操作で起動し、DDR GRAND PRIX 起動中に監視状態を確認できる。
- 保存成功、保存スキップ、解析失敗をUIとログで確認できる。

### M10: 初期版リリース準備

目的は、ローカルで継続利用できる初期版として固めることです。

やること:

- 精度保証対象の解像度・ウィンドウサイズを定義する。
- 実機スクリーンショット検証セットで成功率と誤保存を確認する。
- ローカルデータ保存先を確定する。
- 配布形態を決める。
- READMEと運用手順を整える。

完了条件:

- 実機検証セットで自動保存成功率95%以上を目標に近づける。
- 誤保存0件を目標として、低確信度は保存せずログと画像が残る。
- マスタDBと個人スコアDBが分離されている。
- GitHub ActionsでマスタDBを生成できる。

## 近い順の推奨作業

1. M5c-1として、独立developer app、master build/update、read-only coverage/review queueを追加する。
2. Draft PR #27のM9監視UIとM5c-1は独立trackとしてレビュー・mergeし、相互の完了待ちを必須にしない。
3. M5c-2以降のmanual review、capture、collection、title/artist評価と、M9の長時間運用・失敗復旧をそれぞれ独立PRで進める。
4. M9とM5cの完了後にM10の実機検証と配布準備へ進む。

各チャットの具体的な次PR仕様は `docs/next-task.md` を優先し、上記順序と矛盾する古い候補へ戻らない。

## しばらく守る境界

- 本番キャプチャAPIに入るまでは、manifest互換出力を維持する。
- 実キャプチャAPI導入後もしばらく、実フレームをmanifestで再実行できる形に残す。
- 保存直前境界は `confirmed_result=true` かつ `duplicate=false` を維持する。
- `transition_countup_*` は `result_shape_candidate=true` でも保存対象外にする。
- DB保存、常駐監視、非同期処理、スコア系数字認識のOCR脱却は、それぞれ独立したフェーズとして扱う。
- developer-only collectorのwindow候補自動検出は公開appの自動探索採用を意味せず、実測結果と誤検出を分けて評価する。ゲームへの入力・focus操作・grid自動巡回は行わない。
- ローカル素材、`samples/screenshots/metadata.csv`、PoC出力、解析ログ、ローカルDBはGit管理しない。
