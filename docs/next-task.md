# 次PR作業仕様

`C:\work\ddrgp_scorelog` で作業してください。`AGENTS.md`、frame input設計、WPF single-frame capture実装、今回の実capture認識実績、M9 roadmapを読み、既存のローカル画像、capture session出力、DB、backup、`data/`、`logs/`、生成物を保護してください。

## 推奨モデル

GPT-5.6 Sol

## 推論レベル

high

Windows Graphics Captureのsession寿命、停止競合、resize・target closed・device lost時のresource解放を扱いながら、解析・正式保存へ範囲拡大しないためです。

## 作業ブランチ

```powershell
codex/m9-continuous-capture-session
```

## Goal

ユーザーが明示選択した1つのDDR GRAND PRIX windowに対し、開始から停止まで連続してフレームを取得し、既存manifest modeで再実行できるsession outputとして `data/` 配下へ安全に残します。

このPRは `docs/implementation-roadmap.md` の「M9残り実行順」6項目中の3項目目です。分類・identity・数字認識の自動起動、正式保存workflow接続、監視UI・タスクトレイ、長時間運用には進みません。

## Deliverables

- WPFに、明示pickerで選んだwindowへの連続capture開始と明示停止を追加する。
- session中は同じcapture item、D3D11 device、frame pool、capture sessionを責務境界どおり所有し、停止・失敗時に一度だけ解放する。
- frameを時系列順で `data/windows_capture/session-<UTC>-<unique>/` 配下へ残し、strictly increasingな `timestamp_ms` を持つmanifestを生成する。
- session outputはstagingから公開し、既存outputを上書きせず、部分frameや不完全manifestを成功sessionとして見せない。
- resize、対象window終了、picker cancel、access拒否、device lost、明示停止、二重開始、停止中再操作を区別して扱う。
- resize後の自動追従を行うかsession停止にするかは既存single-frameの `Resized` 契約と安全性を基準に決め、テストとREADMEへ固定する。
- 連続sessionの生成manifestを既存 `--sequence-mode manifest` で読み、補助列、path解決、時刻単位、`confirmation_mode=time` を壊さない回帰テストを追加する。
- `app/README.md`、frame input・regression・roadmap docsを実績に同期する。

## Invariants

- capture sessionから分類、OCR、identity、confirmed event、正式save input、DB saveを自動起動しない。
- 保存直前境界 `confirmed_result=true` かつ `duplicate=false` を変更しない。
- 正式DB schema version 1、writer、duplicate、analysis artifact、manual save/viewer縦断sliceを変更しない。
- single-frame picker、単発capture、atomic writerの既存公開契約を維持する。
- manifestの必須列、ミリ秒時刻、directory相対path、任意列保持を壊さない。
- screenshot画像、session frame、capture metadata、manifest実出力、PoC出力をGit管理しない。
- 対象window自動探索、自動再接続、background auto start、task tray、auto restartを実装しない。

## Validation

- 変更箇所の.NET unit test、WPF build、manifest互換の対象Pythonテスト、Ruff、compileall、`git diff --check` を実行する。
- session開始・複数frame・明示停止、cancel、resize、target closed、device lost、writer失敗、二重開始、停止冪等性、resource解放をfake adapter/fixtureで確認する。
- 利用可能な実windowで短いsessionを1回実行し、複数frame、timestamp単調増加、manifest再読込を確認する。
- 分類・ROI・OCRロジックを変更しない限り `python -m tools.vision_poc` 全体は省略できる。その場合は理由と残るリスクを完了報告へ記載する。
- 実window確認が自動化できない場合だけ、必要な操作を `ユーザー対応が必要` として具体化する。

## Non-goals

- capture frameの自動分類、OCR、identity、confirmed event生成
- 正式save input生成、DB保存、duplicate接続、migration、backup、restore、repair
- 監視状態dashboard、task tray、起動時自動監視、対象window自動再接続
- 長時間soak test、auto restart、installer、self-contained配布
- audio capture、video encoding、録画ファイル生成

## Acceptance Criteria

- 明示選択windowから複数frameを取得し、明示停止後にmanifest互換session bundleとして安全に公開できる。
- session manifestの `timestamp_ms` がstrictly increasingで、既存manifest modeが補助列を保持して `confirmation_mode=time` で読める。
- cancel、停止、resize、対象終了、device lost、write failureでresourceとstagingを残さず、成功sessionへ丸めない。
- capture sessionが解析、正式値昇格、workflow、正式DB、viewer履歴を変更しない。
- read-onlyレビューでmedium以上の未対応指摘がない。

完了後は次PR仕様へ更新し、今回変更だけをcommit、通常pushしてdraft PRを作成してください。
