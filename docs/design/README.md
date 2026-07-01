# 設計資料

このディレクトリは、実装ロードマップを進めるときにブレやすい状態、入力契約、保存境界、データ、I/O、回帰ガードを固定するための設計資料です。

## 読む順番

初めて読む場合は以下の順で読む。

1. `00_glossary.md`
2. `01_pipeline_fsm.md`
3. `02_frame_input_contract.md`
4. `03_event_and_save_boundary.md`
5. `06_regression_guard.md`
6. `04_data_model.md`
7. `05_storage_io_spec.md`

## M0/M1で主に使う資料

M0「画像解析PoCの入力境界」とM1「保存直前イベントの仕様」では、以下を正本として扱う。

- `00_glossary.md`: 用語の意味
- `01_pipeline_fsm.md`: 入力から保存候補までの状態
- `02_frame_input_contract.md`: `FrameInput`、manifest、dry-run capture の契約
- `03_event_and_save_boundary.md`: confirmed-events と保存境界
- `06_regression_guard.md`: 壊してはいけない挙動

## 後工程で主に使う資料

M4以降のマスタDB、M7以降の保存判定、M8以降のDB保存では、以下を更新しながら使う。

- `04_data_model.md`
- `05_storage_io_spec.md`

## 役割分担

`docs/implementation-roadmap.md` は「いつ何をやるか」をまとめる。  
`docs/design/` は「各境界がどう振る舞うべきか」をまとめる。  
`tools/vision_poc/README.md` は「現在のPoCコマンドと出力の使い方」をまとめる。

## 更新ルール

- 実装で状態名、CSV列、保存境界、出力場所を変えた場合は関連する設計資料も更新する。
- `result_candidate`、`confirmed_result`、`duplicate`、confirmed-events の意味を変える場合は `00_glossary.md` と `03_event_and_save_boundary.md` を更新する。
- manifest や dry-run capture の契約を変える場合は `02_frame_input_contract.md` と `06_regression_guard.md` を更新する。
- DBや保存先を決めた場合は `04_data_model.md` と `05_storage_io_spec.md` を更新する。
