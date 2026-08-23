# M5 master match評価履歴

> Status: 履歴資料。M5 jacket matchの信号選定に使ったlocal評価と判断を保存する。現行契約は[`09_master_match_poc.md`](09_master_match_poc.md)を正本とする。

## 2026-07-05 local評価

`song_select` grid右上プレビュー由来の特徴量参照59件を使い、confirmed-events 60件を評価した。結果は`matched=57`、`ambiguous=3`、`not_found=0`、`missing_feature=0`だった。この`matched`はdeveloper評価上の一意候補であり、正式保存判定ではない。

曖昧な3件は`osaka EVOLVED -毎度、おおきに！- (TYPE1/2/3)`で、同一jacketを共有していた。画像特徴量だけで一意化せず、jacket候補集合内でtitle側の分岐情報を使う対象と判断した。

`result_098_sp_basic_lv07_if_score972200.png`はファイル名とmetadataが`If`だった一方、実画面は`桜 / Reven-G / SINGLE BASIC Lv7`だったため、local metadataを修正した。`桜`のsong-select grid/result素材を追加すると近距離曖昧は解消した。この結果から、参照不足と同一jacket分岐を別のfailureとして扱う方針を維持した。

## title補助の比較

初期のtitle画像特徴量は、result `song_title` ROIの濃淡・edge thumbnail、suffix寄りthumbnail、dHashを比較した。同じ`organized_file`の参照を除外し、jacketで曖昧になったsong ID集合内だけを再順位付けした。

title OCR suffix補助は`TYPE1` / `TYPE2` / `TYPE3`の抽出を試した。local確認では3件とも`no_suffix`で、`TYPE)`、`TYPED`、`TYPES`のような崩れが観測されたため、安定した分岐信号には採用しなかった。

続いてresult `song_title` ROIの白文字を二値化し、行ごとのbit列を比較するline-hashを評価した。参照辞書による一致を主観測、重み付きHamming距離を補助観測とし、候補集合内で解決した場合だけ`identity_signal_status=composite_resolved_candidate`、`identity_signal_source=title_linehash_dict`として後続へ渡す形に収束した。

この評価を経て、現行の順序はchart fieldで候補集合を作り、jacket特徴量で絞り、title、必要な場合のartistを同じ候補集合内で適用する形となった。スコア、判定数、EX SCOREはM9 app-owned runtimeのdigit recognitionへ移行している。
