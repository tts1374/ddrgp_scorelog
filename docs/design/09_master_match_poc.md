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

## 将来の補助信号: ジャケット特徴量

曲名OCRとchart-fieldだけで `not_found` や `ambiguous` が残る場合、ジャケットROIの特徴量を補助信号として使う余地がある。特に、曲名OCRが空になるケース、OCR文字列にartistやノイズが混ざるケース、同じ `play_style` / `difficulty` / `level` で候補曲数が多いケースでは、ジャケット類似度が低確信度候補の順位付けやtie-breakerになる可能性がある。

ただし、M5初期ではジャケット特徴量を実装しない。先に曲名OCR文字列の失敗パターン、最小正規化、上位候補レポートを整理し、どのケースで追加信号が必要かを分ける。ジャケット特徴量を入れる場合も、保存成功判定ではなく補助観測値として始める。

ジャケット特徴量を実用的に試すには、M4マスタDBとは別に全曲ジャケット画像を取得・更新・検証する専用ツールが必要になる。想定する専用ツールの責務は以下。

- 曲ごとのジャケット画像取得元、取得日時、content hashを記録する。
- 取得画像をGit管理せず `data/` または配布artifactへ出力する。
- `song_id` と画像を対応付けるmanifestを生成する。
- 画像取得失敗、差し替わり、重複、低解像度などを検査レポートに出す。
- ジャケットROI特徴量と全曲ジャケット特徴量の比較を、曲名OCR照合とは別レポートとして出す。

全曲ジャケット取得元、利用条件、キャッシュ方針、配布可否は未決。画像取得ツールはM5の曲名OCRクリーニングを一段進めた後、独立したフェーズとして扱う。

## スコープ外

- OCR方式刷新。
- ROI座標定義の大変更。
- artistを一意照合主キーにすること。
- ジャケット特徴量照合の実装。
- 全曲ジャケット画像取得ツールの実装。
- 曖昧一致をDB保存可能として扱うこと。
- 個人スコアDB保存。
- duplicate key の本格差し替え。
