# 次PR作業仕様

`C:\work\ddrgp_scorelog` で作業してください。`AGENTS.md`、frame input設計、Vision PoCの分類・identity・digit recognition契約、M9 roadmapを読み、既存のローカル画像、capture出力、DB、backup、`data/`、`logs/`、生成物を保護してください。

## 推奨モデル

GPT-5.6 Sol

## 推論レベル

high

実window capture固有の解像度、DPI、枠・影、色差を既存分類・ROI・identity・数字認識へ局所的に吸収し、正式値昇格や保存接続へ範囲拡大しないためです。

## 作業ブランチ

```powershell
codex/m9-real-capture-recognition-quality
```

## Goal

前PRのWPF `1フレーム取得` が出力する `data/windows_capture/capture-*/frame_manifest.csv` を既存manifest modeで再実行できる状態を固定し、DDR GRAND PRIXの実captureを分類、曲・譜面同定、主要数字認識へ安全に投入できる品質まで局所改善します。

このPRは `docs/implementation-roadmap.md` の「M9残り実行順」6項目中の2項目目です。連続capture、正式保存workflow接続、監視UI・タスクトレイ、長時間運用には進みません。

## Deliverables

- WPF single-frame capture manifestを既存 `--sequence-mode manifest` で再実行する手順と対象テストを固定する。
- ローカル実captureについて、window内容の実画像領域、解像度、幅・高さ、DPI/scale、枠や影の影響を記録し、既存1280x720 ROI基準へ渡す前処理境界を明確にする。
- result、song select、gameplay/menu/transitionの実captureを使い、`result_shape_candidate` と `result_candidate` の誤判定を確認して必要最小限の分類補正を行う。
- confirmed resultの実captureについて、既存の曲・譜面identity候補と `score_digits`、`max_combo`、judgments、`ex_score` を評価し、局所前処理または既存profile選択で改善する。
- ローカル実captureに期待値がない場合は評価templateを `data/` 配下へ生成し、`no_expected_values` を成功扱いにしない。採用判断に使う対象は `evaluated` にする。
- manifestのcapture補助列、path解決、`timestamp_ms`、`confirmation_mode=time`、expected columns保持を壊さない回帰テストを追加する。
- `app/README.md`、`tools/vision_poc/README.md`、frame input・regression・roadmap docsを実績に同期する。

## Invariants

- 実captureの認識結果を正式値、正式save input、DB saveへ自動接続しない。
- 保存直前境界 `confirmed_result=true` かつ `duplicate=false` を変更しない。
- 正式DB schema version 1、writer、duplicate、analysis artifact、manual save/viewer縦断sliceを変更しない。
- WPF picker、単発capture、atomic writerの公開契約を必要なく変更しない。
- 既存manifest/timestampedの必須列、時刻単位、path解決、expected columns保持契約を壊さない。
- screenshot画像、capture metadata、期待値CSV、PoC出力をGit管理しない。
- ROI座標定義の全面変更、OCR方式刷新、identity方式刷新は行わず、実capture差分に必要な局所補正を優先する。
- 常駐監視、連続capture、background loop、task tray、auto restartを実装しない。

## Validation

- 変更箇所の対象Pythonテスト、WPF capture manifest互換の.NETテスト、Ruff、compileall、`git diff --check` を実行する。
- 分類、ROI、OCR、identity、profile評価、PoC runnerに変更が入るため、`python -m tools.vision_poc` と実capture manifestの対象再実行を行う。
- result、非result、`transition_countup_*` の既存ローカル集計を確認し、実capture向け補正で既存評価を退行させない。
- 実captureの期待値カバレッジとROI reportを確認し、`partially_evaluated` / `no_expected_values` を採用成功へ丸めない。
- 実window画像が不足し自動取得もできない場合だけ、必要なcapture操作を `ユーザー対応が必要` として具体化する。

## Non-goals

- 連続capture、FPS制御、監視loop
- captureからの自動解析起動や自動保存
- 正式save input生成、DB保存、migration、backup、restore、repair
- 曲・譜面identity方式の全面刷新、OCR engine差し替え、ROI座標定義の全面変更
- task tray、起動時自動監視、対象window自動再接続
- audio capture、video recording、installer、self-contained配布

## Acceptance Criteria

- WPF single-frame captureの1行manifestを既存manifest modeで読み、capture補助列を保持したまま処理できる。
- 代表的な実capture result/non-resultで分類結果と主要ROIを評価でき、既存ローカル評価に回帰がない。
- confirmed resultの曲・譜面identityと主要数字ROIについて、期待値付き評価結果と未解決理由を区別して確認できる。
- 実capture向け補正が候補材料を正式値へ昇格させず、workflow、正式DB、artifact、viewer履歴を変更しない。
- read-onlyレビューでmedium以上の未対応指摘がない。

完了後は次PR仕様へ更新し、今回変更だけをcommit、通常pushしてdraft PRを作成してください。
