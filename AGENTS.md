# AGENTS.md

このリポジトリで作業するエージェント向けのプロジェクト固有ルールです。一般的な実装方針より、このファイルに書かれた内容を優先してください。

## Project Rules

- スクリーンショット画像、`samples/screenshots/metadata.csv`、PoC出力、解析ログ、ローカルDBはGit管理しない。
- 生成物は原則として `data/` または `logs/` 配下へ出力する。
- 既存のローカル素材や生成物を削除・移動するときは、目的と対象を明確にしてから行う。
- 画像解析PoCは軽量に保ち、まずはローカルで再現できる1コマンド実行を優先する。
- 仕様や判定方針を変えた場合は、関連する `docs/` または `tools/*/README.md` も更新する。

## Progress Guard

- 後続作業を依頼された場合、原則としてコード、テスト、workflow、CLI、README以外の仕様反映など、実行可能な成果物変更を1つ以上進める。
- `docs/next-task.md` の更新だけ、または確認結果の記録だけで作業完了扱いにしない。
- 主作業が外部条件でブロックされた場合は、ブロック理由を明記し、同じマイルストーン内で進められる次の実装・テスト・検証強化へ切り替える。
- 実装に入らずdocs整理だけで終える必要がある場合は、コミット前にその理由を明示する。
- `docs/next-task.md` は、実装・検証が終わった後の引き継ぎ更新として扱う。

## Human Action Requests

- ユーザーによる操作、目視確認、実機確認、外部サービス上の操作、判断または情報提供が必要になった場合は、進捗報告や完了報告の中に埋め込まず、`ユーザー対応が必要` という見出しで明示する。
- 各依頼には、`必須/任意`、`実施タイミング`、`目的`、`具体的な手順またはコマンド`、`期待される結果`、`エージェントへ返してほしい内容`、`未実施の場合の影響` を含める。
- 現在の作業を止める必須対応と、後で実施できる確認を区別する。必須対応でない場合は、ユーザーの返答を待たずに安全な範囲の作業を続ける。
- エージェントがリポジトリ内で安全に実行できる作業を、ユーザーへ転嫁しない。実機、GUI、アカウント、秘密情報、人間の意味判断など、エージェントだけでは完了できない作業に限定して依頼する。
- マイルストーン完了報告や次チャットへの引き継ぎでユーザー作業がない場合も、`現時点でユーザー対応はありません` と明記する。

## Model And Reasoning Guidance

- `docs/next-task.md` には、次チャット向けの `推奨モデル` と `推論レベル` を分けて記載する。
- モデルと推論レベルはCodexのモデルピッカーでユーザーが選ぶ実行設定であり、`AGENTS.md` や `docs/next-task.md` の本文だけでは自動切替されない。
- 次チャットの既定推奨は `GPT-5.6 Sol / high` とする。保存境界、DB schema、OCR採用判断、複数設計文書にまたがる変更など、品質優先の作業に使う。
- 既存方針に沿う小規模な実装、テスト追加、README/docs更新は `GPT-5.6 Terra / high` でもよい。
- `GPT-5.6 Luna` は、限定的な検索、定型確認、低リスクの単純変更に限る。後続作業全体の既定にはしない。
- `max` は、広範な設計変更、難しい原因調査、最終レビューなど、追加推論の価値が明確な場合だけ推奨する。通常の後続作業は `high` を維持する。
- 開始時の選択モデルが推奨と異なっても、エージェントはモデルを自動変更できない。相違だけを理由に作業を止めず、品質上の懸念がある場合は高リスクな判断へ入る前にユーザーへ明示する。
- モデル名や推論レベルはCodex実行環境の指定として扱い、アプリ本体のAPI model ID、依存関係、実装設定へ混入させない。

## Development Commands

基本検証:

```powershell
python -m tools.vision_poc
python -m ruff check tools\vision_poc pyproject.toml
python -m compileall tools\vision_poc
```

PoC用の画像処理依存がない環境では以下を使う。ただし、現状のフラット構成では `python -m pip install -e ".[dev]"` が setuptools の自動パッケージ探索で失敗する可能性がある。

```powershell
python -m pip install -e ".[vision]"
python -m pip install --user "ruff>=0.9.0"
```

## Screenshot Assets

- `samples/screenshots/organized/` はローカル素材置き場で、Git管理対象外。
- `samples/screenshots/metadata.csv` はローカル評価の正解データで、Git管理対象外。
- `metadata.csv` の `organized_file` と `screen_type` を分類評価の基準にする。
- `screen_type=result` は `result_candidate=true` を期待する。
- `screen_type=song_select`、`gameplay`、`menu_setup`、`transition` は `result_candidate=false` を期待する。
- `transition_countup_*` はリザルト形状が出ていても保存不可候補として扱い、ログ上で通常の非リザルト遷移と区別する。

## Vision PoC Policy

- 初期PoCは `docs/vision-poc-prep.md` の方針に従う。
- ROI座標は 1280x720 基準で定義し、実画像サイズへ線形スケールする。
- OCR本番精度より先に、ROI切り出し、候補分類、ログ、評価集計を安定させる。
- `result_shape_candidate` と `result_candidate` を分ける。
- `result_shape_candidate` はリザルト画面らしい形状検出を表す。
- `result_candidate` は保存処理や数字OCRへ進める候補を表す。
- 初期分類はOCRなしで、RESULTSヘッダー、詳細リザルト枠、スコア周辺、ランク周辺の色・エッジ・明度特徴を使う。
- 数字OCRへ進む前に、主要ROIの切り出し画像を `data/vision_poc/rois/` で目視確認できる状態を保つ。

## Confirmed Events OCR Policy

- `confirmed-events` は保存直前OCR相当の評価対象で、条件は `confirmed_result=true` かつ `duplicate=false`。
- OCR対象境界とOCR成功条件は別に読む。`confirmed-events` 対象であることは、DB保存成功やOCR成功を意味しない。
- duplicate、`event_type=rejected_transition`、未確定 `result_candidate`、non-result は confirmed-events OCR対象外に保つ。
- timestamped / manifest / dry-run 由来の入力では `confirmation_mode=time` を維持する。
- timestamped が生成する manifest と manifest 再読込では、metadata由来の expected columns を保持する。
- `evaluated` は対象ROIの全OCR試行に期待値がある状態。
- `partially_evaluated` は一部だけ期待値がある暫定状態。採用判断前に不足期待値を埋める。
- `no_expected_values` は期待値がない状態。OCR文字列が出ていても成功扱いにしない。
- profile比較では、`no_expected_values` の `reference_profiles` は目視参考に留め、採用根拠にしない。
- profile比較では、`partially_evaluated` の推奨は暫定扱いにする。
- legacy `score_ocr.csv` は default profile の互換出力として維持する。
- `score_ocr_summary.json`、`score_ocr_profiles_summary.json`、`ocr_expected_coverage.md`、`ocr_roi_report.md` を合わせて読み、default OCR summary と profile summary を混同しない。
- M2では局所前処理とprofile評価を優先し、OCR方式刷新やROI座標定義の大変更には進まない。

## Output Expectations

`python -m tools.vision_poc` は以下を出力する。

- `data/vision_poc/results.csv`: 画像ごとの分類結果と各シグナルのスコア/真偽。
- `data/vision_poc/summary.json`: 集計結果。
- `data/vision_poc/misclassifications.md`: 誤検出、見逃し、`transition_countup_*` の扱いメモ。
- `data/vision_poc/rois/`: 主要ROIの切り出しPNG。

## Coding Notes

- Pythonコードは `pyproject.toml` の Ruff 設定に合わせる。
- 追加依存は必要最小限にし、PoC専用なら optional dependency に分ける。
- 固定しきい値を変更したら、`python -m tools.vision_poc` を実行して `result` / 非`result` / `transition_countup_*` の集計を確認する。
- 画像処理ロジックを共有化するときは、先にPoCの検証結果を崩さないテストを追加する。
- 保存境界、OCR対象、expected coverage、profile採用判断の仕様や読み方を変えた場合は、`docs/design/` と `tools/vision_poc/README.md` も更新する。
- 本番キャプチャAPI、実キャプチャデバイス依存コード、常駐監視ループ、非同期処理、DB保存、OCR方式刷新、ROI座標定義の大変更、duplicate key の本格差し替えは、それぞれ独立したフェーズとして扱う。

## Skills And Subagents

- DB保存境界レビューSkillとして `.agents/skills/review-ddrgp-db-save-boundary/SKILL.md` を使う。
- M7/M8の保存判定、正式個人スコアDB schema/保存入力、duplicate、DB diagnostic、低信頼度ログ、`source_captures`、`analysis_logs` を変更またはレビューするときに呼び出す。
- Skillを変更したら、`python -X utf8 "$env:USERPROFILE\.codex\skills\.system\skill-creator\scripts\quick_validate.py" ".agents\skills\review-ddrgp-db-save-boundary"` で構造を検証する。
- 追加のプロジェクト専用SkillやSubagentは、対象作業が独立した反復手順として安定してから作る。
- confirmed-events OCR評価Skill: M2完了後、summary / coverage / profile report の読み方が安定した時点で検討する。
- OCR/profile調整レビューSkill: 前処理profileやROI別採用判断のレビュー観点が増えた時点で検討する。
- マスタ照合Skill: M5で曲名正規化、譜面候補絞り込み、曖昧一致の判断が独立して大きくなった時点で検討する。
- DB保存境界レビューSkill: M7/M8で固まったレビュー観点を初版Skillへ反映済み。実運用で不足した境界だけを小さく追記する。
- 実キャプチャSkill: M6以降、本番キャプチャAPIやmanifest互換dry-run出力の作業が独立して大きくなった時点で検討する。
- 追加する場合は、このファイルに用途、呼び出しタイミング、検証コマンドを追記する。
