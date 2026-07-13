# 次PR作業仕様

`C:\work\ddrgp_scorelog` で作業してください。`AGENTS.md`、frame input設計、Vision PoCのmanifest契約を読み、既存のローカル画像、DB、backup、`data/`、`logs/`、生成物を保護してください。

## 推奨モデル

GPT-5.6 Sol

## 推論レベル

high

Windows Graphics Capture、WPFのUI thread、GPU resource解放、既存manifest互換性を、保存や常駐監視へ範囲拡大せず接続するためです。

## 作業ブランチ

```powershell
codex/m9-windows-capture-manifest-slice
```

## Goal

ユーザーがWPFアプリから対象windowを明示選択し、Windows Graphics Captureで1フレームだけ取得して、既存manifest入力へ再利用できるローカル画像とmetadataを `data/` 配下へ安全に出力する最小capture sliceを追加します。

## Deliverables

- WPFアプリに、ユーザー操作で開始する対象window pickerと1フレーム取得入口を追加する。
- capture実装をUI、Windows Graphics Capture adapter、出力writerの境界へ分け、テスト可能なinterfaceを設ける。
- 取得時刻、画像path、幅、高さ、capture sourceの最小metadataを、既存timestamped/manifest入力へ変換可能な形で出力する。
- 出力先を `data/` 配下の新規directoryへ限定し、既存ファイルを上書きしない。
- picker cancel、対象window終了、0x0/resize、device lost、access拒否、write失敗をユーザー向け状態へ写像する。
- frame pool、capture session、D3D device、streamを成功・失敗・cancelの全経路で解放する。
- capture APIを使わないfixture/fake中心の.NETテストと、可能な範囲のWindows integration境界テストを追加する。
- `app/README.md`、`docs/design/02_frame_input_contract.md`、storage/regression docs、roadmapを同期する。

## Invariants

- capture結果を分類、OCR、identity解決、正式保存入力、DB saveへ自動接続しない。
- 保存直前境界 `confirmed_result=true` かつ `duplicate=false` を変更しない。
- 正式DB schema version 1、writer、duplicate、analysis artifact、manual save/viewer縦断sliceを変更しない。
- 既存manifest/timestampedの列、時刻単位、path解決、expected columns保持契約を壊さない。
- screenshot画像と実capture metadataはGit管理せず、`data/` 配下のローカル生成物に留める。
- 常駐監視、連続capture、background loop、task tray、auto restartを実装しない。
- capture失敗やcancelで空画像、部分manifest、temp fileを残さない。
- 通常viewer閲覧とmanual保存はcaptureを暗黙実行しない。

## Validation

.NET build/test、capture writer/manifest互換の対象Pythonテスト、Ruff、compileall、`git diff --check` を実行してください。画像分類・ROI・OCR・confirmed-events生成を変更しない限り `python -m tools.vision_poc` は省略します。実window pickerの自動テストができない場合はfixture検証を完了し、必要な目視確認を `ユーザー対応が必要` として具体的に記載してください。

## Non-goals

- 連続capture、FPS制御、監視loop
- 分類、ROI、OCR、曲・譜面同定の精度変更
- manifestからの自動保存、正式save input生成
- DB保存、migration、backup、restore、repair
- task tray、起動時自動監視、対象window自動再接続
- audio capture、video recording、installer、self-contained配布

## Acceptance Criteria

- 明示pickerで選んだwindowから1フレームを取得し、新規 `data/` 出力へ画像とmetadataをatomicに残せる。
- 出力を既存manifest/timestamped入力へ渡せることをfixtureで確認できる。
- cancel、対象消失、capture/write失敗で既存ファイルを変更せず、部分生成物を残さない。
- capture完了後にGPU/capture resourceが残らず、同一processで再度明示captureできる。
- captureだけではVision PoC、workflow、正式DB、artifact、viewer履歴が変化しない。
- read-onlyレビューでmedium以上の未対応指摘がない。

完了後は次PR仕様へ更新し、今回変更だけをcommit、通常pushしてdraft PRを作成してください。
