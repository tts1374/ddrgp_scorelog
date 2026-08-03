# ddrgp_scorelog

DanceDanceRevolution GRAND PRIX のゲーム画面を解析し、十分に確認できたスコアだけをローカルDBへ保存・閲覧するWindows向け個人ツールです。

## Status

M9の監視・再起動・master DB再検証、M10のlocal storage・実機評価・VeloPack配布、reference DBのRelease取得境界を実装しています。保証範囲と既知制限はWindowsアプリREADMEへ記録しています。具体的な作業内容と受け入れ条件はGitHub Issuesを正本とし、このREADMEはmain branchで利用できる機能と開発入口を要約します。

main branchで利用できる主な機能:

- BEMANIWiki由来の楽曲・譜面マスタDB生成
- リザルト候補分類、confirmed event生成、数字ROI OCR、曲・譜面候補照合
- Debug buildの開発者向け領域からの1フレーム取得、連続取得、正式個人スコアDB version 1への単発保存
- 正式DBとマスタDBをread-onlyで開くWPFスコアビューア
- Release buildの通常画面は、`監視開始`／`監視停止`、固定path再検証、master DB read-only再検証に限定
- `監視開始` による `ddr-konaste` / client `1280x720` の対象window自動特定（該当1件だけ接続）、1秒ごとのRESULT/SCORE gateと候補のリアルタイム正式保存、監視のstop / exit
- developer-onlyのjacket catalog収集・manual review支援
- VeloPackによる未署名per-user installer、reference data setの同梱・GitHub Releases取得・安全なセット更新、Releaseログ、手動backup / restore

進行中の主な作業:

- M10: 初期版リリース準備（Issue #63）
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
dotnet build app\src\DDRGpScoreViewer\DDRGpScoreViewer.csproj --configuration Debug --no-restore
dotnet build app\src\DDRGpScoreViewer\DDRGpScoreViewer.csproj --configuration Release --no-restore
dotnet test app\tests\DDRGpScoreViewer.Tests\DDRGpScoreViewer.Tests.csproj --configuration Debug --no-restore
```

Debug buildだけが開発者向け操作のUIとcommand入口を含みます。Release buildではこれらを生成せず、通常の監視開始・停止だけを残します。

VeloPack releaseは、Git管理外のcurrent master DBとjacket catalogを用意したうえで次の1コマンドから再現します。成果物は`data/releases/<version>/`へ生成されます。

```powershell
.\app\packaging\Build-Release.ps1 -Version 0.1.0
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
