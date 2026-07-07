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

現在は実キャプチャAPIやDB保存へ入る前の画像解析PoC段階です。

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
- マスタDB生成
- マスタ照合
- 個人スコアDB保存
- 低確信度ログと失敗画像の本番保存
- Windows常駐アプリUI
- 配布形態

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

完了条件:

- 保存する、保存しない、重複として捨てる、低確信度としてログ保存する、を機械的に判定できる。
- 誤保存を避けるための失敗側ログが残る。

### M7a: スコア系数字認識のOCR脱却

目的は、個人スコアDBへ保存する数値項目を、Tesseract OCR依存からテンプレート/画像特徴ベースのPoCへ切り出すことです。曲・譜面同定を扱うM5とは分け、DB保存を実装するM8より前に、保存値として使う数字の読み取り方式と失敗理由を固定します。

やること:

- confirmed-eventsだけを対象にする。
- `score_digits`、`max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss`、`ex_score` の数字ROIを対象にする。
- Tesseractではなく、テンプレート、桁分割、画像特徴などのOCR非依存方式で数字候補を出す。
- 既存Tesseract出力と比較できるsummaryを出す。
- ROIごとに `recognized` / `ambiguous` / `missing_reference` / `failed_segmentation` などの失敗理由を出す。
- 出力は `data/` 配下に置き、テンプレート素材やローカル画像はGit管理しない。
- fixtureテストで、正規化、桁分割、テンプレート選択、失敗理由の基本動作を確認する。

完了条件:

- confirmed-events対象で保存値候補になる数字ROIを評価できる。
- OCR非依存の認識候補、信頼度、失敗理由を出せる。
- Tesseract既存結果との差分をsummaryで確認できる。
- mismatch / ambiguous / missing_reference / failed_segmentation を区別できる。
- 保存判定M7やDB保存M8と混同せず、M8へ渡す数値読み取り材料として文書化されている。

### M8: 個人スコアDB保存

目的は、保存候補をローカルの個人スコアDBへ1プレー1レコードで保存することです。

やること:

- `ddrgp-scores.sqlite` のスキーマを定義する。
- `plays` テーブルを実装する。
- マスタDBバージョン、曲ID、譜面ID、OCR結果、スコア、判定数、画像ハッシュ、解析確信度を保存する。
- 重複保存防止をDB保存直前にも適用する。
- マイグレーション方針を決める。

完了条件:

- 同一リザルトを複数回保存しない。
- マスタDB更新後も過去履歴が参照できる。
- 保存成功と保存スキップをログで追える。

### M9: Windows常駐アプリ

目的は、ユーザーが実運用できるアプリにまとめることです。

やること:

- WPFを第一候補として最小UIを作る。
- タスクトレイ常駐を実装する。
- 監視状態、対象ウィンドウ状態、最新保存結果を表示する。
- マスタDB更新状態を表示する。
- ROI調整画面、失敗ログ一覧、保存済み履歴の簡易一覧を段階的に追加する。

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

1. dry-run capture sequence scenario を追加する。
2. 保存直前イベント境界の仕様とテストを固める。
3. confirmed-events の数字OCR評価母数を増やす。
4. 曲・譜面情報ROIの抽出PoCへ進む。
5. マスタDB生成を始める。
6. マスタ照合PoCを作る。
7. M5の参照カバレッジ明示と完了判定を固める。
8. M7aとしてスコア系数字認識のOCR脱却PoCを切る。
9. 実キャプチャAPIの最小接続、保存判定、個人スコアDB保存へ進む。

## しばらく守る境界

- 本番キャプチャAPIに入るまでは、manifest互換出力を維持する。
- 実キャプチャAPI導入後もしばらく、実フレームをmanifestで再実行できる形に残す。
- 保存直前境界は `confirmed_result=true` かつ `duplicate=false` を維持する。
- `transition_countup_*` は `result_shape_candidate=true` でも保存対象外にする。
- DB保存、常駐監視、非同期処理、スコア系数字認識のOCR脱却は、それぞれ独立したフェーズとして扱う。
- ローカル素材、`samples/screenshots/metadata.csv`、PoC出力、解析ログ、ローカルDBはGit管理しない。
