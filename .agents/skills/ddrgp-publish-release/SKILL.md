---
name: ddrgp-publish-release
description: DDRGP scorelogのRelease readiness監査、version指定のVeloPack release candidate生成、GitHub Release公開、公開後検証を安全に行う。リリース可能かの確認、package作成、tag・GitHub Release・asset公開、公開済みReleaseの検証を明示依頼されたときに使う。通常実装、CI失敗の修正、PR review、Issue作成、リリース方針のアイデア整理だけには使わない。
---

# DDRGP Publish Release

DDRGP scorelogのReleaseを、監査、candidate生成、公開、公開後確認の順で扱う。ルートと`app/`の`AGENTS.md`を常に優先し、既存のREADME、build script、検証scriptを正本として再利用する。

## 1. Select The Mode

依頼文から次のmodeを1つ選ぶ。曖昧な場合は`READINESS_AUDIT`を選び、公開しない。

- `READINESS_AUDIT`: リリース可能性、手順、残課題、既存Release状態を確認する。local package生成、tag、GitHub Release、asset uploadは行わない。
- `BUILD_CANDIDATE`: 明示された`X.Y.Z`からlocal packageを生成・検証する。GitHub上のtag、Release、assetは変更しない。
- `PUBLISH`: 明示された`X.Y.Z`をbuildし、tagとGitHub Releaseを公開して公開後検証まで行う。「公開して」「GitHub Releaseを作成して」などの外部公開指示を必須とする。
- `POST_RELEASE_VERIFY`: 既存の明示versionまたはRelease URLをGitHub上でread-only検証する。修正、asset差替え、Release削除は行わない。local installer smokeは明示依頼がある場合だけ行う。

`BUILD_CANDIDATE`または`PUBLISH`でversionがない場合は、推測せず1問だけ確認する。`PUBLISH`の明示がない依頼を、package生成から公開へ拡張しない。

## 2. Load The Release Contract

操作前に次を読む。

1. `AGENTS.md`と`app/AGENTS.md`。
2. `docs/implementation-roadmap.md`のCurrent PhaseとM10 Initial Release。
3. `README.md`のRelease build入口。
4. `app/README.md`のRelease package生成・公開、更新、初回導入、reference data set、backup / restore、既知制限、release停止条件。
5. `app/packaging/Build-Release.ps1`。
6. `app/tests/VerifyReleaseBuild.ps1`、`VerifyReleaseRuntime.ps1`、`VerifyVeloPackInstall.ps1`。
7. `.github/workflows/ci.yml`。

指定されたRelease Issueまたは承認済みchecklistがある場合だけ追加契約として読む。READMEやscriptと矛盾する場合は、公開挙動を推測で選ばず`NEEDS_DECISION`で停止する。

## 3. Apply Global Safety Gates

全modeで次を守る。

- screenshot、実入力、解析log、local DB、`data/`、`logs/`、release生成物をGit対象へ入れない。
- 既存の未コミット変更、local DB、既存release生成物を保護する。
- Release failureをこのSkill内で通常実装修正へ広げない。原因と別Issue候補を報告して停止する。
- force push、既存tagの移動・削除、公開済みReleaseやassetの削除・置換を行わない。
- code signing、複数channel、任意version選択、自動rollback、schema migrationを追加しない。
- secret、token、local pathをRelease notesや公開assetへ含めない。
- 同一versionのtagまたはReleaseが存在する場合は内容を比較する。一致すれば冪等に完了報告し、不一致なら変更せず停止する。

`BUILD_CANDIDATE`と`PUBLISH`では、build前に`data/release-build/<version>/`と`data/releases/<version>/`の存在を確認する。`Build-Release.ps1`はこの2つを再作成するため、既存出力があれば明示許可なしに実行しない。

## 4. Run Readiness Audit

`READINESS_AUDIT`では次を確認して、変更せず報告する。

1. 現在branch、HEAD、`origin/main`、worktree状態、release対象候補commitを確認する。
2. 対象commitのGitHub Actions必須jobを確認する。未実行、失敗、対象SHA不一致を成功扱いにしない。
3. required master DBとbinding済みruntime catalogの存在、Git管理外であること、対応versionを確認する。DB内容を変更しない。
4. `app/README.md`の現在のrelease停止条件をhard gateとして列挙し、各項目を`ready`、`not_ready`、`unverified`へ分類する。
5. package、installer、公開asset、Release notes、公開後確認の未実施項目を分ける。
6. 最終判定を`READY`、`NOT_READY`、`NEEDS_VERIFICATION`のいずれかで返す。

local test未実施をGitHub Actions成功で代替した、またはその逆であると暗黙判断しない。実施済み事実と未実施を分ける。

## 5. Build And Verify A Candidate

`BUILD_CANDIDATE`と`PUBLISH`では次を順に実行する。

1. versionが`X.Y.Z`、tagが`vX.Y.Z`であることを固定する。
2. worktreeがcleanで、対象HEADが`origin/main`と一致することを確認する。異なるcommitの公開が必要なら停止して判断を求める。
3. 対象SHAの必須GitHub Actionsが成功していることを確認する。
4. localとremoteに同名tagがなく、GitHubに同version Releaseがないことを確認する。既存の場合はGlobal Safety Gatesの冪等判定へ戻る。
5. master DBとcatalogをread-onlyで検査するため、まず`Build-Release.ps1 -Version X.Y.Z -ValidateInputsOnly`を実行する。必要なら正本READMEに従って明示pathを渡す。
6. 既存version出力がないことを再確認し、`Build-Release.ps1 -Version X.Y.Z`を実行する。
7. build script内のRelease build検証とruntime smokeが成功したことを確認する。
8. 生成されたSetupを指定して`VerifyVeloPackInstall.ps1 -SetupPath <Setup.exe>`を実行する。
9. `app/README.md`が要求するVeloPack assetとreference data setの3 assetがすべて存在することを確認する。
10. 公開予定assetごとにfile名、byte数、SHA-256を記録する。
11. `git status --short`とdiffを確認し、tracked差分やlocal data混入がないことを確認する。

いずれかが失敗した場合はcandidateを完成扱いにせず、`PUBLISH`でも公開へ進まない。

## 6. Publish Safely

`PUBLISH`だけがこのsectionを実行する。

1. target SHA、`vX.Y.Z`、全assetの記録を再確認する。
2. 前Releaseからtarget SHAまでのユーザー影響を要約し、Release notesを作る。target commit、全公開assetのSHA-256、未署名installer、保証環境、既知制限、導入・更新時のデータ保持を現在の`app/README.md`に合わせて記載する。
3. GitHubのdraft Releaseをtarget SHAの`vX.Y.Z`で作成し、Setup、full package、`RELEASES`、`assets.win.json`、`releases.win.json`とreference data setの3 assetを添付する。
4. draft上のtag、target SHA、asset名、byte数をlocal記録と照合する。不足、重複、不一致があれば公開せずdraftのまま停止する。
5. すべて一致した場合だけstable Releaseとして公開する。prereleaseや別channelへ変更しない。

GitHub connectorまたは`gh`を使用する前に、対象repositoryと認証先をread-onlyで確認する。途中失敗時にdraft、tag、assetを独断で削除しない。

## 7. Verify After Publication

`PUBLISH`と`POST_RELEASE_VERIFY`では次を確認する。

1. 公開Releaseのtag、target SHA、stable状態、Release notes、asset一覧を確認する。
2. public downloadしたassetのfile名、byte数、SHA-256をlocal記録と照合する。
3. GitHub latest Release APIでreference data setの3 assetが同じReleaseから解決されることを確認する。
4. VeloPack feedに`releases.win.json`とfull packageが存在することを確認する。
5. `PUBLISH`では、実施可能ならdownload済みSetupへ`VerifyVeloPackInstall.ps1`を実行する。`POST_RELEASE_VERIFY`では明示依頼がある場合だけ実行する。未実施なら理由と残るリスクを明示する。
6. 初回Releaseでは既存versionからのupdate確認を要求しない。後続Releaseで旧version環境がある場合だけ、ユーザー操作のupdate確認結果を記録する。

公開後の不一致を見つけても、既存Releaseを削除・上書きしない。影響、利用者データの安全性、推奨する修正版versionまたは公開停止判断を報告し、明示指示を待つ。

## 8. Report

次を簡潔に報告する。

- mode、version、tag、target commit。
- readiness判定と各hard gate。
- 実行したbuild・test・installer smokeと結果。
- master/catalogの対応確認結果。
- asset名、byte数、SHA-256。
- GitHub Release URLと公開状態。公開していないmodeではその旨。
- post-release確認結果。
- 未実施項目、Issue仕様との差異、別Issue候補。
- commit、push、tag、Release、asset uploadの各実施有無。

成功条件を満たさない場合は、部分成功を`released`へ丸めない。
