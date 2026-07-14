# AGENTS.md

このリポジトリで作業するエージェント向けのプロジェクト固有ルールです。一般的な実装方針より、このファイルを優先してください。

## Project Rules

- スクリーンショット画像、`samples/screenshots/metadata.csv`、PoC出力、解析ログ、実入力JSON、ローカルDBはGit管理しない。
- 生成物は原則として `data/` または `logs/` 配下へ出力する。
- 既存のローカル素材や生成物を削除・移動するときは、目的と対象を明確にする。
- 画像解析PoCは軽量に保ち、まずローカルで再現できる1コマンド実行を優先する。
- 仕様や判定方針を変えた場合は、関連する `docs/` または `tools/*/README.md` も更新する。

## Pull Request Scope

- 指定された作業は、1つのレビュー可能かつmerge可能なPRとして完成させるまで自走する。
- 作業開始時に、依頼内容、`docs/next-task.md`、関連設計docsから、PRの目的、成果物、完了条件、スコープ外を確認する。
- 既存資料から一意に判断できる事項は、ユーザーへ再確認しない。
- 主実装後も、必要なテスト、README・設計docs同期、実装担当と独立したread-only branch diffレビュー、修正後の再レビュー、検証、diff確認まで続行する。
- テスト失敗、lint失敗、fixture不足、今回変更による文書不整合は、安全に解決できる限りユーザーへ戻さず修正する。
- 追加変更は、次をすべて満たす場合だけ同じPRへ含める。
  - 現在のPR目的とタイトルのまま説明できる。
  - 現在の完了条件を満たすために直接必要である。
  - 同じ検証セットで確認できる。
  - 一緒にmerge・revertするのが自然である。
  - 独立した仕様判断、新機能、公開契約変更を必要としない。
- 独立した次機能、別マイルストーン、無関係なリファクタリング、別成果は実装せず、`docs/next-task.md` の後続候補へ送る。
- 現在のPRの完了条件を満たした後、次PR相当の作業へ自動的に進まない。
- `docs/next-task.md` の更新だけ、または確認結果の記録だけで作業完了扱いにしない。

## Authorization And Approval Boundaries

このリポジトリの通常タスクでは、次を事前許可された操作として扱う。

- リポジトリ内ファイルの読取りと編集
- 非破壊的なローカルコマンド、テスト、lint、build、diagnosticの実行
- 今回の変更だけを含むローカルコミット
- 指定された `codex/*` ブランチへの通常push
- 指定ブランチからのdraft PR作成

次は許可されていない。必要な場合は停止して確認する。

- `main` への直接push
- force-push
- PRのmerge
- tag、release、公開成果物の作成
- issue、既存PR、外部サービスへの書込み
- migration、データ削除、既存DB修復などの破壊的操作
- 認証情報、秘密情報、費用発生を伴う操作

## Human Action Requests

ユーザーによる操作や判断が必要な場合は、進捗報告へ埋め込まず、`ユーザー対応が必要` という見出しで明示する。

対象は次に限定する。

- 既存仕様から決定できないプロダクト判断
- 公開CLI、正式DB schema、保存形式、互換性の非互換変更
- migration、データ削除、破壊的操作
- 実機、GUI、目視、実キャプチャ環境での確認
- 認証情報、アカウント、外部サービス操作
- 現在のPRと独立した機能へ進む必要
- 設計docsと実装の重大な矛盾

各依頼には次を含める。

- `必須/任意`
- `実施タイミング`
- `目的`
- `具体的な手順またはコマンド`
- `期待される結果`
- `エージェントへ返してほしい内容`
- `未実施の場合の影響`

任意確認で現在のPRを止めない。ユーザー作業がない場合は、完了報告に `現時点でユーザー対応はありません` と記載する。

## Model And Reasoning Guidance

- `docs/next-task.md` には、次チャット向けの `推奨モデル` と `推論レベル` を分けて記載する。
- モデルと推論レベルはCodexの実行設定であり、本文だけでは自動切替されない。
- 通常の実装、テスト追加、README・設計docs同期、既存契約に沿った回帰修正は `GPT-5.6 Sol / medium` を基準とする。
- `GPT-5.6 Terra` は、代表的な作業で必要品質を維持できる場合の性能とコストのバランス候補とする。
- `GPT-5.6 Luna` は、高頻度、定型、低リスクで、遅延やコストを重視する作業に限定する。
- `high` または `xhigh` は、正式DB schema、migration、保存境界、transaction、並行性、難しい原因調査、複数設計文書の矛盾解消、最終監査などで、代表的な作業における追加推論の品質向上が確認できる場合に使う。
- `max` は最も難しい品質優先作業に限定し、可能なら `xhigh` と品質、遅延、コストを比較する。
- 推論レベルを上げることを、タスク範囲を広げる理由にしない。
- モデル名や推論レベルを、アプリ本体のAPI model ID、依存関係、実装設定へ混入させない。

## Validation Policy

- 実装中は、変更箇所に直接関係する最小の対象テストを優先する。
- PR完成前に、変更した責務とその直接依存を対象とする影響範囲テストを実行する。
- コード変更を含むPRでは、原則としてRuff、構文検査、`git diff --check` を実行する。
- 全テストは常に必須とはしない。次の場合にPR完成前に1回実行する。
  - 共通runner、CLI option解析、共通loaderを変更した。
  - 正式DB schema、writer、transaction、duplicate処理を変更した。
  - 共通fixtureまたは複数モジュールへ影響するhelperを変更した。
  - 対象テストで予期しない回帰が見つかった。
  - 依存関係、`pyproject.toml`、共通テスト設定を変更した。
  - read-onlyレビューで影響範囲が想定より広いと判断した。
  - マイルストーンの区切り、または明示的な全体検証要求がある。
- `python -m tools.vision_poc` は、次に影響する変更時だけ実行する。
  - 画像分類
  - ROI
  - OCRまたはdigit recognition
  - profile評価
  - confirmed-events生成
  - PoC runner全体
  - PoC出力集計またはreport生成
  - 画像処理に影響する共通コード
- 条件付きの全テストやVision PoCを省略した場合は、完了報告に省略理由と残るリスクを記載する。
- テスト件数の増減自体を失敗とみなさない。既存テストの意図しない削除、skip、失敗がないことを確認する。
- `pytest_chalice` 由来の `pkg_resources` deprecated warningは、既知warningとしてテスト失敗と区別する。

## Development Commands

基本検証:

```powershell
python -m ruff check tools\vision_poc pyproject.toml tests
python -m compileall master tools\vision_poc
git diff --check
```

Vision PoC変更時:

```powershell
python -m tools.vision_poc
```

PoC用の画像処理依存がない環境では次を使う。ただし、現状のフラット構成では `python -m pip install -e ".[dev]"` がsetuptoolsの自動パッケージ探索で失敗する可能性がある。

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
- `transition_countup_*` はリザルト形状が出ていても保存不可候補として扱い、通常の非リザルト遷移と区別する。

## Vision PoC Policy

- 初期PoCは `docs/vision-poc-prep.md` に従う。
- ROI座標は1280x720基準で定義し、実画像サイズへ線形スケールする。
- OCR本番精度より先に、ROI切り出し、候補分類、ログ、評価集計を安定させる。
- `result_shape_candidate` と `result_candidate` を分ける。
- `result_shape_candidate` はリザルト画面らしい形状検出を表す。
- `result_candidate` は保存処理や数字OCRへ進める候補を表す。
- 初期分類はOCRなしで、RESULTSヘッダー、詳細リザルト枠、スコア周辺、ランク周辺の色・エッジ・明度特徴を使う。
- 数字OCRへ進む前に、主要ROIの切り出し画像を `data/vision_poc/rois/` で目視確認できる状態を保つ。

## Confirmed Events OCR Policy

- `confirmed-events` は保存直前OCR相当の評価対象で、条件は `confirmed_result=true` かつ `duplicate=false`。
- OCR対象境界とOCR成功条件を分けて扱う。
- duplicate、`event_type=rejected_transition`、未確定 `result_candidate`、non-result は対象外に保つ。
- timestamped、manifest、dry-run由来の入力では `confirmation_mode=time` を維持する。
- timestampedが生成するmanifestとmanifest再読込では、metadata由来のexpected columnsを保持する。
- `evaluated` は対象ROIの全OCR試行に期待値がある状態。
- `partially_evaluated` は暫定状態とし、採用判断前に不足期待値を埋める。
- `no_expected_values` は成功扱いにせず、`reference_profiles` は目視参考に留める。
- legacy `score_ocr.csv` はdefault profileの互換出力として維持する。
- `score_ocr_summary.json`、`score_ocr_profiles_summary.json`、`ocr_expected_coverage.md`、`ocr_roi_report.md` を合わせて読む。
- M2では局所前処理とprofile評価を優先し、OCR方式刷新やROI座標定義の大変更には進まない。

## Output Expectations

`python -m tools.vision_poc` は次を出力する。

- `data/vision_poc/results.csv`
- `data/vision_poc/summary.json`
- `data/vision_poc/misclassifications.md`
- `data/vision_poc/rois/`

## Coding Notes

- Pythonコードは `pyproject.toml` のRuff設定に合わせる。
- 追加依存は必要最小限にし、PoC専用ならoptional dependencyへ分ける。
- 固定しきい値を変更したら `python -m tools.vision_poc` を実行し、result、非result、`transition_countup_*` の集計を確認する。
- 画像処理ロジックを共有化するときは、先に既存PoCを守るテストを追加する。
- 保存境界、OCR対象、expected coverage、profile採用判断を変えた場合は、関連する `docs/design/` と `tools/vision_poc/README.md` を更新する。
- 本番キャプチャAPI、実キャプチャデバイス依存コード、常駐監視、非同期処理、DB保存、OCR方式刷新、ROI座標定義の大変更、duplicate key本格差し替えは独立フェーズとして扱う。

## Skills And Review

- M7/M8の保存判定、正式個人スコアDB、duplicate、DB diagnostic、低信頼度ログ、`source_captures`、`analysis_logs` を変更またはレビューするときは、`.agents/skills/review-ddrgp-db-save-boundary/SKILL.md` を使う。
- Skillを変更したら次を実行する。

```powershell
python -X utf8 "$env:USERPROFILE\.codex\skills\.system\skill-creator\scripts\quick_validate.py" ".agents\skills\review-ddrgp-db-save-boundary"
```

- commit・push・GitHubレビュー依頼の前に、base branchとのbranch diff全体を専用reviewerまたはread-onlyサブエージェントでレビューする。利用可能なら `/review` を使い、実装担当と同じ文脈のセルフレビューだけで完了扱いにしない。
- レビューでは、変更責務に該当する正常系、空/欠損、重複、競合、再投入、旧version、部分失敗、option混在を境界条件マトリクスとして列挙し、各行の期待結果と回帰testの有無を確認する。該当しない軸は理由を示して除外してよい。
- schema、field、status、option、永続化形式を変更した場合は、書く側だけでなく、それを読む全consumer、集計、再読込、互換経路を検索し、旧値・旧versionを現行値として誤解しないことを確認する。
- validation、duplicate、skip、互換性拒否、option不整合などの早期returnを追加・変更した場合は、returnより前に起こり得る入力読込、directory/file作成、DB準備・書込み、log/artifact出力を列挙し、拒否時に副作用がないtestを置く。
- 重大度medium以上の指摘は、現在のPR範囲内なら修正して再検証し、修正後のdiffをもう一度read-onlyレビューする。範囲外なら次PR候補へ送る。
- read-onlyサブエージェントは、diffレビュー、テストギャップ調査、READMEとCLIの整合確認に限って必要時に使う。
- 実装変更は原則として親エージェントが統合して行う。
- 新しいプロジェクト専用SkillやSubagentは、対象作業が独立した反復手順として安定してから追加する。

## Completion Report

完了報告には次を含める。

- 変更した責務と主要ファイル
- 維持した不変条件
- 実行した検証と結果
- 省略した条件付き検証、その理由、残るリスク
- セルフレビュー結果
- 未解決事項と次PR候補
- コミットSHA、push先branch、作成したPR
- `ユーザー対応が必要` または `現時点でユーザー対応はありません`
