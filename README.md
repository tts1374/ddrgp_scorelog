# ddrgp_scorelog

DanceDanceRevolution GRAND PRIX のゲーム画面を解析し、十分に確認できたスコアだけをローカルDBへ保存・閲覧するWindows向け個人ツールです。

## Status

M9の監視・再起動・master DB再検証と、M10-2のlocal storage境界を実装しています。実DDR GRAND PRIX windowを使う長時間soakなどの残存リスクはWindowsアプリREADMEへ記録しています。具体的な作業内容と受け入れ条件はGitHub Issuesを正本とし、このREADMEはmain branchで利用できる機能と開発入口を要約します。

main branchで利用できる主な機能:

- BEMANIWiki由来の楽曲・譜面マスタDB生成
- リザルト候補分類、confirmed event生成、数字ROI OCR、曲・譜面候補照合
- 正式個人スコアDB version 1への明示的な単発保存
- 正式DBとマスタDBをread-onlyで開くWPFスコアビューア
- Windows Graphics Captureによる1フレーム取得、連続取得、取得後の保存workflow
- 監視のstart / stop / exit、対象window再選択、環境ごとの固定path再検証、master DB read-only再検証
- developer-onlyのjacket catalog収集・manual review支援

進行中の主な作業:

- M10: 初期版リリース準備（Issue #63）
- M10-1: Python / NuGet依存関係の固定（Issue #66）
- M10前提のmanual reviewデータ整備（Issues #55〜#60）

## Safety Boundaries

- `confirmed_result=true`かつ`duplicate=false`だけを通常の保存候補とする。
- candidate、OCR raw、期待値、preview材料を正式値へ暗黙昇格させない。
- 不完全な解析結果、DB不整合、subprocess失敗を保存成功へ丸めない。
- マスタDB、正式個人スコアDB、解析出力、画像原本を分離する。
- ローカルDB、スクリーンショット、実入力、解析ログ、生成物をGit管理しない。

## Repository Layout

```text
.
├─ app/                         # Windows WPFアプリ
├─ docs/                        # 要求・ロードマップ・設計・ADR
├─ master/                      # 楽曲・譜面マスタDB生成
├─ samples/                     # スクリーンショット収集ルールとmetadata例
├─ tests/                       # Python側の回帰テスト
├─ tools/vision_poc/            # 画面分類、OCR、保存workflow
├─ tools/jacket_catalog_collector/ # developer-only収集・review UI
└─ pyproject.toml               # Python依存と開発ツール定義
```

## Development

### Python

Python 3.13とuvを使用します。`uv.lock`を正本にして、固定済みの依存から環境を構築します。
Pythonの検証はCIと同じUTF-8明示実行だけを採用し、Windowsのlocale依存実行は検証結果に使いません。

```powershell
$env:PYTHONUTF8 = "1"
uv sync --frozen --extra dev --extra vision
uv run ruff check tools\vision_poc pyproject.toml tests
uv run python -m compileall master tools\vision_poc
uv run pytest tests
```

### Windows app

.NET 10 SDKとWindows 11を使用します。

```powershell
dotnet restore app\tests\DDRGpScoreViewer.Tests\DDRGpScoreViewer.Tests.csproj --locked-mode
dotnet build app\src\DDRGpScoreViewer\DDRGpScoreViewer.csproj --no-restore
dotnet test app\tests\DDRGpScoreViewer.Tests\DDRGpScoreViewer.Tests.csproj --no-restore
```

詳細な実行・操作手順は[WindowsアプリREADME](app/README.md)を参照してください。

### Dependency updates

依存を意図的に更新するときだけmanifestとlock fileを同じ変更へ含めます。Pythonは`pyproject.toml`を変更して`uv lock`を実行し、NuGetは対象`.csproj`の`PackageReference`を変更して、locked modeを一時的に無効にしたrestoreで`packages.lock.json`を更新します。更新後は次のlocked検証を実行します。

```powershell
uv lock
uv sync --frozen --extra dev --extra vision
dotnet restore app\tests\DDRGpScoreViewer.Tests\DDRGpScoreViewer.Tests.csproj -p:RestoreLockedMode=false
dotnet restore app\tests\DDRGpScoreViewer.Tests\DDRGpScoreViewer.Tests.csproj --locked-mode
```

特定Python packageだけを更新する場合は`uv lock --upgrade-package PACKAGE_NAME`を使います。Dependabotの更新PRは自動mergeせず、lock差分とCI結果を確認します。

## Documents

- [要求定義](docs/requirements.md)
- [実装ロードマップ](docs/implementation-roadmap.md)
- [設計資料](docs/design/)
- [画面解析PoCツール](tools/vision_poc/README.md)
- [マスタDB生成](master/README.md)
- [Windowsアプリ](app/README.md)
- [jacket catalog collector](tools/jacket_catalog_collector/README.md)

## Development Workflow

- GitHub Issueを作業契約とする。
- 原則として1 Issueを1 PRで実装する。
- `AGENTS.md`と対象directoryのnested `AGENTS.md`を適用する。
- PRはGitHub Actionsの対象job成功後にmergeする。
- 実装中に見つけた別課題は現在のPRへ混入させず、別Issue候補として記録する。
