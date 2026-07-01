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

次にやること:

- confirmed-events を主評価対象として、`score_digits`、`max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss`、`ex_score` の精度を見る。
- `no_expected_values` のROIは成功扱いにせず、metadata期待値列を増やす。
- `partially_evaluated` は暫定判断として扱い、採用判断前に不足期待値を埋める。
- ROI座標の大変更ではなく、まずは局所前処理とprofile評価で改善する。
- default OCR summary と profile比較の読み分けを維持し、保存候補で弱いROIを `ocr_roi_report.md` から追う。
- 現ローカル確認では confirmed-events の主要ROIは expected coverage 上すべて `evaluated` だが、default 出力の `ex_score` に mismatch が残るため、`low-threshold` profile を採用候補として扱う条件を詰める。

完了条件:

- confirmed-events の主要数字ROIが十分な評価母数で `evaluated` になる。
- 保存候補イベントに対して必須数字項目の読み取り失敗理由を説明できる。
- OCR方式刷新なしで改善できる範囲と、次に方式変更が必要な範囲が分かれている。

### M3: 曲・譜面情報の読み取りPoC

目的は、スコアだけではなく、曲名、プレースタイル、難易度、レベルを保存候補として取り出すことです。

やること:

- 曲名ROI、プレースタイルROI、難易度ROI、レベルROIの切り出しを安定させる。
- OCR、テンプレート照合、色特徴の組み合わせを小さく比較する。
- SP/DP、難易度、レベルの読み取りを先に安定させる。
- 曲名はOCR生文字列、正規化文字列、候補一覧、確信度を出す。

完了条件:

- 保存候補イベントから曲・譜面照合に必要な最小情報を抽出できる。
- 低確信度時にDB保存へ進めないための理由をログ化できる。

### M4: マスタDB生成

目的は、BEMANIWiki の全曲リストから曲・譜面マスタDBを生成することです。

やること:

- BEMANIWiki のHTML取得と解析を実装する。
- `songs`、`charts`、`master_metadata`、`source_snapshots` のSQLite生成を行う。
- セル結合、注記、削除曲、限定曲、SP/DP片方のみ、CHALLENGEなしを扱う。
- GitHub Actionsで手動実行と定期実行を用意する。
- Releases成果物として配布する流れを作る。

完了条件:

- ローカルとCIでマスタDBを再現生成できる。
- 取得元構造変化を検出できる。
- スコア取得側が参照できる安定したDBスキーマがある。

### M5: マスタ照合PoC

目的は、OCR結果から曲と譜面を一意に特定できるか確認することです。

やること:

- 曲名OCR正規化を実装する。
- マスタDBに対するファジーマッチを実装する。
- SP/DP、難易度、レベルで候補を絞る。
- 一意に決まらない場合は保存不可にする。
- 候補一覧と照合スコアをログへ出す。

完了条件:

- 正常なリザルトで曲IDと譜面IDを一意に決められる。
- 曖昧または低確信度のケースを保存不可として扱える。

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
7. 実キャプチャAPIの最小接続へ進む。

## しばらく守る境界

- 本番キャプチャAPIに入るまでは、manifest互換出力を維持する。
- 実キャプチャAPI導入後もしばらく、実フレームをmanifestで再実行できる形に残す。
- 保存直前境界は `confirmed_result=true` かつ `duplicate=false` を維持する。
- `transition_countup_*` は `result_shape_candidate=true` でも保存対象外にする。
- DB保存、常駐監視、非同期処理、OCR方式刷新は、それぞれ独立したフェーズとして扱う。
- ローカル素材、`samples/screenshots/metadata.csv`、PoC出力、解析ログ、ローカルDBはGit管理しない。
