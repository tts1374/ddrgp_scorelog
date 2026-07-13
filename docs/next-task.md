# 次PR作業仕様

`C:\work\ddrgp_scorelog` で作業してください。`AGENTS.md` と保存境界Skillを読み、既存のローカルDB、backup、`data/`、`logs/`、生成物を保護してください。

## 推奨モデル

GPT-5.6 Sol

## 推論レベル

high

正式DB version 1を変更せず、保存済みスコアがユーザー価値として見える最小ビューアを作るためです。

## 作業ブランチ

```powershell
codex/m9-minimal-score-viewer
```

## Goal

正式個人スコアDB version 1をread-onlyで開き、保存済みプレー履歴、プレー詳細、譜面別自己ベストを確認できる最小WPFスコアビューアを追加します。

## UI仕様の参照順

実装前に次をすべて確認してください。

1. `docs/wireframe/screen-spec.md`: 表示情報、画面状態、操作、文言、画面遷移の正本
2. `docs/wireframe/design-system.md`: 色トークン、余白、タイポグラフィ、コンポーネント、アクセシビリティの正本
3. `docs/wireframe/wireframe1.png`: サイドバー、ホーム、自己ベスト、譜面詳細、プレー履歴の配置と情報優先順位の参考
4. `docs/wireframe/wireframe2.png`: 要確認、全体遷移、共通凡例の配置と情報優先順位の参考

文書と画像が矛盾する場合は `screen-spec.md`、`design-system.md`、PNGの順で優先します。PNGのピクセル完全一致、画像内の誤字・不自然な値・仕様外要素の再現は求めません。

wireframeはアプリ全体の将来像を含みますが、今回実装するのは下記DeliverablesとAcceptance Criteriaに必要な共通レイアウト、プレー履歴、プレー詳細、自己ベスト表示だけです。ホームの全KPI、グラフ、要確認、設定、データ管理、自動記録状態、常駐機能へ範囲を広げません。

## Deliverables

- `app/` にC# / .NET 10 / WPFの最小プロジェクトを作成する。
- wireframeのテーマトークンと共通table/badge/empty/error stateを、後続画面でも再利用できるWPF resource/componentとして実装する。
- ユーザーが明示選択したv1 DBと生成済みマスタDBをread-onlyで開き、それぞれのidentity/versionを検査する。
- `song_id` / `chart_id` でマスタDBを参照し、`plays` の新しい順の履歴一覧に、日時、曲名、SP/DP、難易度、レベル、score、rank、clear typeを表示する。
- 選択プレーの判定数、MAX COMBO、EX SCORE、保存日時を詳細表示する。
- 保存済み全履歴から譜面別自己ベストをqueryで算出し、履歴rowを変更せず表示する。
- compatible DB、空履歴、マスタ参照欠落、拒否DB、読取失敗をfixtureまたは一時DBでテストする。
- `app/README.md`、利用手順、設計docs、roadmapを同期する。

## Invariants

- 正式DB schema versionを1から変更しない。
- version 2 schema、supported transition、migration SQL、schema writerを設計・実装しない。
- viewerはDBをread-onlyで開き、schema初期化、save、migration、backup、repairを実行しない。
- マスタ参照が欠ける履歴も失わず、IDと参照欠落状態を表示する。
- 自己ベストは全履歴から算出し、自己ベスト専用rowやtableを作らない。
- preview/unknown/identity mismatch/newer unsupported/partial stateを表示対象DBとして受け入れない。
- 既存Python CLI/API契約と終了コードを変えない。

## Validation

.NET build/test、対象Pythonテスト、全Pythonテスト、Ruff、compileall、`git diff --check` を実行してください。画像処理を変更しない限りVision PoCは省略します。

## Non-goals

- DBへの書込み、save、migration、backup、restore、repair
- 自動キャプチャ、常駐監視、タスクトレイ、ROI調整
- マスタDB更新、検索、絞り込み、グラフ、統計高度化
- OCR、画像分類、解析pipelineとの自動接続
- installer、self-contained配布

## Acceptance Criteria

- compatibleなv1 DBの全プレー履歴を、マスタDB由来の曲・譜面表示と詳細つきでread-only閲覧できる。
- 譜面別自己ベストが保存済み履歴から正しく算出される。
- viewer操作の前後でDB内容hashが変わらない。
- 非compatible DBを変更せず、理由を表示して拒否する。
- `screen-spec.md`の対象画面仕様と`design-system.md`のトークンを満たし、PNGと同等の情報優先順位を目視確認できる。
- read-onlyレビューでmedium以上の未対応指摘がない。

完了後は次PR仕様へ更新し、今回変更だけをcommit、通常pushしてdraft PRを作成してください。
