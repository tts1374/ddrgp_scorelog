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

`jacket_match_candidates.csv` は、confirmed-events の result `jacket` ROIを特徴量化し、`play_style` / `difficulty` / `level` で絞った候補song_idに紐づくローカル特徴量だけと比較する。列には候補曲数、候補譜面数、候補特徴量数、最上位候補、score、distance、特徴量参照元、上位候補一覧、期待曲名、期待song_id、期待song_idの距離、期待song_idの順位、最上位と次点songの距離差、title画像特徴量補助、title OCR suffix補助、title line-hash補助、後続保存判定へ渡すM5候補観測 `identity_signal_*`、`jacket_match_status`、`failure_reason` を出す。期待値由来の列はローカルmetadataを使った診断であり、保存可能判定ではない。

`jacket_match_status` は以下のPoC観測語彙とする。

- `matched`: 距離しきい値内で、近傍同点候補がない。PoC上の一意候補であり、保存可能や本番採用済み照合ではない。
- `ambiguous`: 距離しきい値内だが、別song_idに近傍候補がある。
- `not_found`: chart条件に候補がない、または最上位距離がしきい値を超える。
- `insufficient_input`: chart-field 3項目が候補絞り込みに足りない。
- `missing_feature`: resultジャケット特徴量または候補song_id側のローカル特徴量参照が足りない。

`jacket_match_status` はjacket特徴量単体の観測として維持する。title補助で候補が1件に見えても、`jacket_match_status=ambiguous` を `matched` へ昇格しない。後続保存判定へ渡す候補観測は別列 `identity_signal_*` に出す。

`identity_signal_status` はM5内の候補観測語彙であり、保存可能、曲ID/譜面ID確定、本番採用済み照合を意味しない。現時点の主な語彙は以下。

- `jacket_resolved_candidate`: jacket特徴量単体でPoC上の一意候補がある。
- `auxiliary_resolved_candidate`: jacketは曖昧なままだが、曖昧候補集合内で補助観測が候補を1件示した。
- `unresolved_ambiguous`: jacket曖昧候補を補助観測でも1件へ寄せられない。
- `unresolved_insufficient_input` / `unresolved_missing_feature` / `unresolved_not_found`: M5入力、特徴量参照、候補距離の不足により候補観測を出せない。

`identity_signal_source` は候補観測の出所を示す。優先順は `jacket_feature`、`title_linehash_dict`、`title_ocr_suffix`、`title_image_feature` とする。`title_linehash_exact_status` と `title_linehash_distance_status` は参考列に留め、`identity_signal_source` には使わない。

2026-07-05のローカル追加素材反映後は、`song_select` grid右上プレビュー由来の特徴量マスタが59件になり、confirmed-events 60件に対するジャケット照合は `matched=57`、`ambiguous=3`、`not_found=0`、`missing_feature=0` になった。ここでの `matched` は引き続きPoC上の一意候補であり、保存可能ではない。

残る `ambiguous` は、`osaka EVOLVED -毎度、おおきに！- (TYPE1/2/3)` の同一ジャケット3件である。`result_098_sp_basic_lv07_if_score972200.png` はファイル名とmetadataが `If` になっていたが、実画面表示は `桜 / Reven-G / SINGLE BASIC Lv7` だったためローカルmetadataを修正済み。その後、`桜` のsong_select grid/result素材を追加して近距離曖昧は解消した。osaka 3件は画像特徴量だけで無理に一意化せず、title画像特徴量またはtitle OCRで `TYPE1` / `TYPE2` / `TYPE3` を候補集合内だけ再順位付けする対象にする。

title / artist の画像特徴量を追加する場合も、候補集合外から曲を拾うためには使わない。基本順序は `play_style / difficulty / level` で候補集合を作り、ジャケット特徴量で狭め、残った曖昧候補だけを title 画像特徴量や title OCR で再順位付けする。artistは主キーではなく、矛盾チェックや弱い補助信号に留める。

初期の title 画像特徴量PoCでは、result `song_title` ROIを横長の濃淡サムネイル、エッジサムネイル、右側サフィックス寄りの濃淡/エッジサムネイル、dHashに変換する。参照はローカルmetadataの期待曲名を M4 `songs.title` へ一意解決できた result 素材から作る。比較時は同じ `organized_file` の参照を除外し、jacketで `ambiguous` になったsong_id集合内だけを対象にする。結果は `title_rerank_status`、title最上位候補、title距離、title参照元、title上位候補一覧として `jacket_match_candidates.csv` に出す。

`title_rerank_status=resolved_candidate` は、title画像特徴量が曖昧候補集合内の再順位付け候補を出したというPoC観測であり、`jacket_match_status` を `matched` に変えたり、曲ID/譜面ID確定やDB保存可能を意味したりしない。`missing_feature` はtitle参照不足、`ambiguous_candidate` はtitle画像でも近傍候補が残る状態として読む。

title OCR suffix補助は、`--m3-song-artist-ocr` で得た result `song_title` OCR文字列から `TYPE1` / `TYPE2` / `TYPE3` だけを抽出し、jacketで `ambiguous` になったsong_id集合内だけを再順位付けする。suffixが候補集合外の曲に対応しそうな場合でも、候補集合外から曲を拾わず `no_candidate_suffix_match` として観測する。`title_ocr_rerank_status=resolved_candidate` はsuffixが曖昧候補内の1曲に対応したというPoC観測であり、`jacket_match_status` を変えたり、曲ID/譜面ID確定やDB保存可能を意味したりしない。`missing_ocr` はOCR未実行またはOCR入口失敗、`no_suffix` はOCR文字列からTYPE suffixを取れない状態、`ambiguous_candidate` はsuffixでも候補が複数残る状態として読む。

2026-07-05のローカル確認では、osaka 3件の title OCR suffix 補助はすべて `no_suffix` だった。OCR文字列には `TYPE)`、`TYPED`、`TYPES` のようなsuffix末尾崩れが出ており、現行のM3 title OCR入口だけでは `TYPE1` / `TYPE2` / `TYPE3` を安定取得できない。これはOCR方式刷新の採用判断ではなく、次に song_select grid/detail 側のタイトル表示ROI参照やsuffix専用の小さな前処理を検討するための観測として扱う。

次のtitle補助は、result `song_title` ROIのline-hash方式に寄せる。これはinf-notebook系の固定UI文字認識を参考に、OCR文字列ではなく、固定ROIから抽出した文字画素のbit列を比較するPoCである。参照元はresult素材だけに限定し、song_select側のタイトル表示ROIは使わない。ローカルmetadataの期待曲名を M4 `songs.title` へ一意解決できた result素材から参照featureを作り、同じ `organized_file` の参照は比較から除外する。

title line-hashでは、result `song_title` ROIのうち曲名行だけを対象にし、白文字色域を固定しきい値で二値化して、行ごとのbit列を4bit単位でhex化する。inf-notebook 風に、参照素材から作った行hexキー辞書を主観測にする。距離比較型は互換の参考列として残し、候補参照同士で差が出るbitを重く見たHamming距離で順位付けする。

`jacket_match_candidates.csv` へ追加するline-hash観測列は、`title_linehash_candidate_feature_count`、`title_linehash_diff_bit_count`、`title_linehash_dict_status`、`title_linehash_dict_top_*`、`title_linehash_dict_top_candidates`、`title_linehash_exact_status`、`title_linehash_distance_status`、`title_linehash_top_*`、`title_linehash_top_candidates`、`title_linehash_rerank_reason` を基本とする。`title_linehash_dict_status=resolved_candidate` は、line-hash辞書が曖昧候補集合内の再順位付け候補を出したというM5観測であり、`jacket_match_status` を変えたり、曲ID/譜面ID確定やDB保存可能を意味したりしない。line-hashが候補集合外にありそうな曲名形状を示しても、候補集合外から曲を拾わない。line-hash辞書で候補が出た場合は `identity_signal_status=auxiliary_resolved_candidate` / `identity_signal_source=title_linehash_dict` として後続へ渡すが、これは低確信度の候補観測であり、保存判定では引き続きM7以降の集約ルールを待つ。

固定UI文字は最終的に汎用OCRより画像認識へ寄せる方針だが、スコア/判定数/EX SCORE のTesseract離脱や数字テンプレート認識は後続タスクに回す。M5の次作業では、まずtitle line-hashをjacket ambiguous候補内の補助信号として観測する。

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
