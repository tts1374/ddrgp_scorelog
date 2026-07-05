# M5 マスタ照合PoC設計

M5では、M3保存候補レポートの観測値をM4マスタDBへ照合し、曲・譜面候補の絞り込み結果を観察する。ここでの結果はDB保存可否の最終判断ではなく、M7以降の保存判定へ渡すための低確信度語彙と候補情報である。

## 入力

M5 PoCの入力は以下に限定する。

- M3保存候補行: `confirmed_result=true` かつ `duplicate=false` から作った `m3_save_candidate_summary.csv` 相当の行。
- M4マスタDB: `songs` / `charts` を持つ `ddrgp-master.sqlite`。
- `song_title_extracted_value`: 曲名OCR入口の生文字列。
- `play_style_extracted_value` / `difficulty_extracted_value` / `level_extracted_value`: M3 chart-fieldのPoC観測値。

M3の `ready` はM5へ渡せる観測値という意味に留める。曲ID、譜面ID、マスタ曲名への一意照合成功やDB保存可能を意味しない。

## 最小正規化

曲名OCR文字列はPoC入口として以下だけを行う。

- Unicode NFKC正規化。
- 大小文字をcasefoldで寄せる。
- 空白を除去する。
- ASCII句読点と代表的な日本語括弧・句読点を除去する。
- OCR文字列にartistや余分な記号が後続混入するケースを観察するため、正規化後のマスタ曲名が5文字以上でOCR正規化文字列に含まれる場合は、曲名包含一致として類似度を最大扱いにする。

この正規化と包含一致はM5最小入口であり、読み仮名、別名、版権表記差、OCR誤読辞書、曲名固有ルール、artistを一意主キーにする処理はまだ扱わない。

## 候補絞り込み

まず `play_style` / `difficulty` / `level` で `charts` を絞り、該当chartとsongを候補集合にする。曲名類似度は標準ライブラリの `difflib.SequenceMatcher` を使い、正規化済みOCR文字列とマスタ曲名の正規化文字列を比較する。artist混入の観察用として、5文字以上のマスタ曲名がOCR文字列内にそのまま含まれる場合は包含一致を優先する。

現時点の既定しきい値は `0.92`。外部依存は増やさない。

## match_status

- `matched`: しきい値以上の最上位候補が1件だけある。PoC上の一意候補であり、保存可能や本番採用済み照合ではない。
- `ambiguous`: しきい値以上の最上位候補が同点で複数ある。
- `not_found`: chart条件に候補がない、または最上位スコアがしきい値未満。
- `insufficient_input`: `song_title` または chart-field 3項目がM5入力として足りない。

`ambiguous`、`not_found`、`insufficient_input` は、M7以降で保存不可理由や低確信度ログへ渡す観測語彙として扱う。

## 出力

`--m5-master-match` はPoC出力先に以下を生成する。

- `master_match_candidates.csv`
- `master_match_summary.json`
- `master_match_report.md`

各行には、OCR入力文字列、正規化文字列、chart-field条件、候補曲数、候補譜面数、最上位候補、score、上位候補一覧、`match_status`、`failure_reason` を出す。上位候補一覧は失敗代表の観察用であり、保存可能判定ではない。生成物は `data/` 配下に置き、Git管理しない。

## 次の主信号候補: song_select grid ジャケット特徴量

曲名OCRは、artist混入、記号崩れ、日本語タイトルの崩れ、空OCRが残るため、曲ID確定の主信号としては弱い。次のM5 PoCでは、`play_style` / `difficulty` / `level` で譜面候補を絞ったうえで、ジャケットROI特徴量を曲候補確定の主信号候補として扱う。

初回は全曲ジャケット画像取得ではなく、ローカル `song_select` の grid 画面から得られる右上の大きい選択中ジャケットプレビューを使い、`song_id` に紐づくローカル特徴量マスタを育てる。grid内の小ジャケットセル検出は初回スコープ外にし、右上プレビューROIだけを対象にする。detail画面は必須入力にせず、後続で必要になった場合の追加対象にする。

想定する初回データフローは以下。

```text
song_select grid
  -> 右上選択中ジャケットプレビューROIを切り出す
  -> metadata の song_title / expected_song_title を M4 songs.title へ照合する
  -> 一意に song_id が決まる場合だけ jacket feature master へ登録する

result confirmed-events
  -> result ジャケットROIを特徴量化する
  -> play_style / difficulty / level で charts を絞る
  -> 候補 song_id の jacket feature と比較する
  -> 一意に近い候補だけ PoC 上の matched とする
```

特徴量マスタはまず `data/` 配下のCSV/JSONとして出力し、Git管理しない。metadata実体もGit管理しないため、ラベルが不足している `song_select` 行は `data/` 配下のテンプレートCSVへ出し、人がローカルmetadataへ転記して再実行する運用にする。

初回特徴量は新規依存を増やさず、Pillow / numpy の範囲で、縮小RGBサムネイル、色ヒストグラム、dHash系の軽量特徴を比較する。しきい値や距離はPoC上の観察値であり、DB保存可能判定ではない。

`--m5-jacket-match` は以下を生成する。

- `jacket_feature_master.csv`
- `jacket_feature_master_summary.json`
- `jacket_feature_label_template.csv`
- `jacket_match_candidates.csv`
- `jacket_match_summary.json`
- `jacket_match_report.md`

`jacket_feature_master.csv` は、`screen_type=song_select` かつ grid 画面の右上選択中ジャケットプレビューを特徴量化し、metadata の `song_title` / `expected_song_title` を M4 `songs.title` へ一意照合できた行だけを `accepted` として出す。ラベルが空のgrid行は `jacket_feature_label_template.csv` へ出し、metadata実体は更新しない。

`jacket_match_candidates.csv` は、confirmed-events の result `jacket` ROIを特徴量化し、`play_style` / `difficulty` / `level` で絞った候補song_idに紐づくローカル特徴量だけと比較する。列には候補曲数、候補譜面数、候補特徴量数、最上位候補、score、distance、特徴量参照元、上位候補一覧、`jacket_match_status`、`failure_reason` を出す。

`jacket_match_status` は以下のPoC観測語彙とする。

- `matched`: 距離しきい値内で、近傍同点候補がない。PoC上の一意候補であり、保存可能や本番採用済み照合ではない。
- `ambiguous`: 距離しきい値内だが、別song_idに近傍候補がある。
- `not_found`: chart条件に候補がない、または最上位距離がしきい値を超える。
- `insufficient_input`: chart-field 3項目が候補絞り込みに足りない。
- `missing_feature`: resultジャケット特徴量または候補song_id側のローカル特徴量参照が足りない。

全曲ジャケット画像取得、配布可否、画像キャッシュ方針は別フェーズとして残す。ジャケット特徴量を入れる場合も、初回は保存成功判定ではなく、低確信度ログへ渡す観測値として始める。

## スコープ外

- OCR方式刷新。
- ROI座標定義の大変更。
- artistを一意照合主キーにすること。
- 全曲ジャケット画像取得ツールの実装。
- grid内の小ジャケットセル検出。
- 曖昧一致をDB保存可能として扱うこと。
- 個人スコアDB保存。
- duplicate key の本格差し替え。
