---
name: ddrgp-publish-release
description: DDRGP scorelogのRelease readiness監査、最新stable Releaseとの差分に基づく次patch versionの選定、VeloPack release candidate生成、GitHub Release公開、公開後検証を安全に行う。リリース可能かの確認、package作成、tag・GitHub Release・asset公開、公開済みReleaseの検証を依頼されたときに使う。通常実装、CI失敗の修正、PR review、Issue作成、リリース方針のアイデア整理だけには使わない。
---

# DDRGP Publish Release

DDRGP scorelogのReleaseを、監査、candidate生成、公開、公開後確認の順で扱う。ルートと`app/`の`AGENTS.md`を常に優先し、既存のREADME、build script、検証scriptを正本として再利用する。

## 1. Select The Mode

依頼文から開始modeを1つ選ぶ。曖昧な場合は`READINESS_AUDIT`から始め、後述の自動遷移条件を満たす場合だけ次stageへ進む。

- `READINESS_AUDIT`: build前のrelease入力、対象commit、CI、停止条件、既存Release状態をread-onlyで確認する。このstage単体ではlocal package生成、tag、GitHub Release、asset uploadを行わない。
- `BUILD_CANDIDATE`: 明示versionまたは安全に自動決定した`X.Y.Z`からlocal packageを生成・検証する。GitHub上のtag、Release、assetは変更しない。
- `PUBLISH`: readiness監査とcandidate検証を通過したversionを、tagとGitHub Releaseへ公開して公開後検証まで行う。「公開して」「GitHub Releaseを作成して」など、元の依頼での外部公開指示を必須とする。
- `POST_RELEASE_VERIFY`: 既存の明示versionまたはRelease URLをGitHub上でread-only検証する。修正、asset差替え、Release削除は行わない。local installer smokeは明示依頼がある場合だけ行う。

「公開問題ないか確認して」「release readinessを確認して」のような依頼は`READINESS_AUDIT`から開始する。利用者が「監査だけ」「read-only」「buildしない」と明示した場合は、`READY_TO_BUILD`でもそこで停止する。それ以外は`READY_TO_BUILD`から`BUILD_CANDIDATE`へ同じ実行内で自動遷移してよい。

`READY_TO_PUBLISH`という状態だけを公開権限と解釈しない。元の依頼に`PUBLISH`の明示がなければcandidate完成で停止する。

## 2. Resolve Version And Stage Transitions

明示versionがあればそれを優先する。versionがなく`BUILD_CANDIDATE`または`PUBLISH`へ進む場合は、次の全条件を満たすときだけ自動決定する。

1. GitHubの最新stable Releaseが`vX.Y.Z`形式であり、そのtagが解決できる。
2. latest tagのtargetから今回のtarget SHAまでに1件以上のcommit差分があり、target SHAがlatest tagの子孫である。
3. README、Issue、承認済みchecklistに別のversion指定またはversioning方針がない。
4. 差分に`BREAKING CHANGE`、互換性を壊すCLI・永続化形式・schema・installer identity・update channel変更、必須migrationなど、major/minor判断を必要とする兆候がない。
5. 自動決定する`X.Y.(Z+1)`にlocal/remote tag、GitHub Release、`data/release-build/<version>/`、`data/releases/<version>/`の競合がない。

全条件を満たす場合だけ次patch version `X.Y.(Z+1)`とtag `vX.Y.(Z+1)`を採用し、latest tagからtarget SHAまでの差分をRelease notesの対象とする。major/minorは自動決定しない。初回Release、非SemVer tag、破壊的変更の疑い、version競合、targetがlatest tagの子孫でない場合は`NEEDS_DECISION`で停止し、versionを1問だけ確認する。差分が0件ならpackageやReleaseを作らず`NO_RELEASE_NEEDED`で終了する。

stageは次の順で遷移する。

1. `READINESS_AUDIT`が`READY_TO_BUILD`で、read-only指定がなければ`BUILD_CANDIDATE`へ進む。
2. `BUILD_CANDIDATE`が`READY_TO_PUBLISH`で、元の依頼に外部公開指示があれば`PUBLISH`へ進む。
3. `PUBLISH`成功後に`POST_RELEASE_VERIFY`相当の確認を行う。

`NOT_READY_TO_BUILD`、`NOT_READY_TO_PUBLISH`、`NEEDS_VERIFICATION`、`NEEDS_DECISION`では自動遷移しない。

## 3. Load The Release Contract

操作前に次を読む。

1. `AGENTS.md`と`app/AGENTS.md`。
2. `docs/implementation-roadmap.md`のCurrent PhaseとM10 Initial Release。
3. `README.md`のRelease build入口。
4. `app/README.md`のRelease package生成・公開、更新、初回導入、reference data set、backup / restore、既知制限、release停止条件。
5. `app/packaging/Build-Release.ps1`。
6. `app/tests/VerifyReleaseBuild.ps1`、`VerifyReleaseRuntime.ps1`、`VerifyVeloPackInstall.ps1`。
7. `.github/workflows/ci.yml`。

指定されたRelease Issueまたは承認済みchecklistがある場合だけ追加契約として読む。READMEやscriptと矛盾する場合は、公開挙動を推測で選ばず`NEEDS_DECISION`で停止する。

## 4. Apply Global Safety Gates

全modeで次を守る。

- screenshot、実入力、解析log、local DB、`data/`、`logs/`、release生成物をGit対象へ入れない。
- 既存の未コミット変更、local DB、既存release生成物を保護する。
- Release failureをこのSkill内で通常実装修正へ広げない。原因と別Issue候補を報告して停止する。
- force push、既存tagの移動・削除、公開済みReleaseやassetの削除・置換を行わない。
- code signing、複数channel、任意version選択、自動rollback、schema migrationを追加しない。
- secret、token、local pathをRelease notesや公開assetへ含めない。
- 同一versionのtagまたはReleaseが存在する場合は内容を比較する。一致すれば冪等に完了報告し、不一致なら変更せず停止する。
- VeloPack installer smokeは同じ`packId`の本番install root、Start Menu shortcut、uninstall登録を上書き・削除し得る。`--installto`で一時pathを指定しても分離されたと判断しない。いずれかが存在するPCでは実行せず、既存環境を削除・退避しない。
- cleanな使い捨てWindows環境は通常利用できない前提とし、installer smokeは補助検証として扱う。環境を用意できないこと自体を、初回Releaseやinstaller変更を含めて`NEEDS_VERIFICATION`、`NOT_READY_TO_BUILD`、`NOT_READY_TO_PUBLISH`の理由にしない。代わりにSection 5と6の自動検証を必須とし、未実施理由と残存リスクを記録する。

`BUILD_CANDIDATE`と`PUBLISH`では、build前に`data/release-build/<version>/`と`data/releases/<version>/`の存在を確認する。`Build-Release.ps1`はこの2つを再作成するため、既存出力があれば明示許可なしに実行しない。

## 5. Run Readiness Audit

`READINESS_AUDIT`では次を確認して、変更せず報告する。

1. 現在branch、HEAD、`origin/main`、worktree状態、release対象候補commitを確認する。
2. 対象commitのGitHub Actions必須jobを確認する。未実行、失敗、対象SHA不一致を成功扱いにしない。
3. required master DBとbinding済みruntime catalogの存在、Git管理外であること、対応versionを確認する。DB内容を変更しない。
4. GitHubの最新stable Releaseとtag targetを確認し、tag targetから対象commitまでの差分とSection 2のversion自動決定条件を評価する。
5. installer smoke環境を次のように分類する。
   - 同じ`packId`の本番install root、Start Menu shortcut、uninstall登録のいずれかがある場合は`installer smoke未実施`とし、smokeを実行せず既存環境を変更しない。
   - cleanな使い捨てWindows環境が実際に利用できる場合だけ`installer smoke実行可能`とする。
   - `installer smoke未実施`は情報項目であり、それだけをreadiness failureにしない。初回Releaseやinstaller関連変更でも、対象SHAの必須GitHub ActionsとSection 6の代替検証をhard gateとする。
   - latest tagからtarget SHAまでのinstaller identity、package生成、shortcut、install / update / uninstall lifecycleの変更有無を分類する。変更がある場合は、変更責務に対応する自動testとpackage検証を必須にする。testやSkillだけの変更はshipped behaviorを変えるかdiffで判断する。
   - installer smoke未実施の理由と、install / update / uninstallの実機回帰が未確認である残存リスクをRelease notesの技術情報とtask報告へ明記する。
6. `app/README.md`の現在のrelease停止条件のうち、build前に検証可能な項目をhard gateとして列挙し、各項目を`ready`、`not_ready`、`unverified`へ分類する。
7. package、installer、公開asset、Release notes、公開後確認はdownstream stageの検証項目として分け、未生成であることだけを`unverified` hard gateまたは失敗にしない。
8. build前条件がすべて満たされれば`READY_TO_BUILD`、失敗があれば`NOT_READY_TO_BUILD`、実行中CIなど結果待ちなら`NEEDS_VERIFICATION`、versionまたは契約判断が必要なら`NEEDS_DECISION`を返す。installer smoke環境未確保だけを停止理由にしない。latest tagから差分がなければ`NO_RELEASE_NEEDED`を返す。
9. `READY_TO_BUILD`かつ自動遷移が許可される依頼では、versionを固定して`BUILD_CANDIDATE`へ進む。

local test未実施をGitHub Actions成功で代替した、またはその逆であると暗黙判断しない。実施済み事実と未実施を分ける。

## 6. Build And Verify A Candidate

`BUILD_CANDIDATE`と`PUBLISH`では次を順に実行する。

1. 明示versionまたはSection 2で自動決定したversionが`X.Y.Z`、tagが`vX.Y.Z`であることを固定し、versionの決定根拠を記録する。
2. worktreeがcleanで、対象HEADが`origin/main`と一致することを確認する。異なるcommitの公開が必要なら停止して判断を求める。
3. 対象SHAの必須GitHub Actionsが成功していることを確認する。
4. localとremoteに同名tagがなく、GitHubに同version Releaseがないことを確認する。既存の場合はGlobal Safety Gatesの冪等判定へ戻る。
5. master DBとcatalogをread-onlyで検査するため、まず`Build-Release.ps1 -Version X.Y.Z -ValidateInputsOnly`を実行する。必要なら正本READMEに従って明示pathを渡す。
6. 既存version出力がないことを再確認し、`Build-Release.ps1 -Version X.Y.Z`を実行する。
7. build script内のRelease build検証とruntime smokeが成功したことを確認する。
8. installer smokeを次の分岐で扱う。
   - cleanな使い捨てWindows環境が利用できる場合は、生成されたSetupを指定して`VerifyVeloPackInstall.ps1 -SetupPath <Setup.exe>`を実行する。
   - 同じ`packId`の本番install root、Start Menu shortcut、uninstall登録を検出した場合はsmokeを実行せず、既存環境を削除・退避しない。
   - smokeを実行しない場合は、build script内のRelease build検証、repository外runtime smoke、`VerifyReleasePackage.ps1`、対象SHAの必須GitHub Actionsの成功を代替hard gateとし、未実施理由と残存リスクを記録して次へ進む。
   - installer identity、package生成、shortcut、install / update / uninstall lifecycleに変更がある場合は、その変更責務に対応する自動testが対象SHAで成功していることと、生成assetのidentity・version・構成がREADME契約に一致することも確認する。必要な自動testまたはpackage検証が失敗・欠落している場合は`NOT_READY_TO_PUBLISH`で停止する。
9. `app/README.md`が要求するVeloPack assetとreference data setの3 assetがすべて存在することを確認する。
10. 公開予定assetごとにfile名、byte数、SHA-256を記録する。
11. `git status --short`とdiffを確認し、tracked差分やlocal data混入がないことを確認する。

必須検証のいずれかが失敗した場合はcandidateを完成扱いにせず、`PUBLISH`でも公開へ進まない。理由と残存リスクを記録したinstaller smoke未実施は失敗として扱わない。

すべて成功した場合は`READY_TO_PUBLISH`と判定する。元の依頼に外部公開指示がなければ、公開せずcandidateの場所とasset記録を報告して停止する。

## 7. Publish Safely

`PUBLISH`だけがこのsectionを実行する。

1. target SHA、`vX.Y.Z`、全assetの記録を再確認する。
2. 前Releaseからtarget SHAまでのユーザー影響を要約し、次の順でRelease notesを作る。
   - 利用者向けの日本語を先に置き、変更点を画面・操作・結果への影響として説明する。class名、test名、CI用語を見出しや主説明に使わない。
   - 初回installと既存versionからの更新方法を分け、利用者がdownloadするSetup名を明記する。
   - 正式個人スコアDB、設定、reference DB、ログの保持、未署名installerの警告、保証環境、既知制限、利用ガイドへの導線を現在の`README.md`と`app/README.md`に合わせて記載する。
   - target commit、検証結果、全公開assetのbyte数とSHA-256は`<details>`内の「技術情報」へ分離する。
   - installer smokeを省略した場合は、clean環境を利用できないため未実施であることと、install / update / uninstallの実機回帰が未確認である残存リスクを同じ技術情報へ明記する。
   - 実施後に副作用、不一致、未確認事項が判明した検証を成功実績として記載しない。利用者の判断に不要な内部検証の列挙を避ける。
3. GitHubのdraft Releaseをtarget SHAの`vX.Y.Z`で作成し、Setup、full package、`RELEASES`、`assets.win.json`、`releases.win.json`とreference data setの3 assetを添付する。
4. draft上のtag、target SHA、asset名、byte数をlocal記録と照合する。不足、重複、不一致があれば公開せずdraftのまま停止する。
5. すべて一致した場合だけstable Releaseとして公開する。prereleaseや別channelへ変更しない。

GitHub connectorまたは`gh`を使用する前に、対象repositoryと認証先をread-onlyで確認する。途中失敗時にdraft、tag、assetを独断で削除しない。

## 8. Verify After Publication

`PUBLISH`と`POST_RELEASE_VERIFY`では次を確認する。

1. 公開Releaseのtag、target SHA、stable状態、Release notes、asset一覧を確認する。
2. public downloadしたassetのfile名、byte数、SHA-256をlocal記録と照合する。
3. GitHub latest Release APIでreference data setの3 assetが同じReleaseから解決されることを確認する。
4. VeloPack feedに`releases.win.json`とfull packageが存在することを確認する。
5. `PUBLISH`では、cleanな使い捨てWindows環境が確保済みの場合だけdownload済みSetupへ`VerifyVeloPackInstall.ps1`を実行する。本番install root、Start Menu shortcut、uninstall登録があるPCでは実行しない。installer smoke未実施で公開した場合は、未実施理由と残るリスクがRelease notesに記載されていることを確認する。`POST_RELEASE_VERIFY`では明示依頼があり、かつclean環境の場合だけ実行する。
6. 初回Releaseでは既存versionからのupdate確認を要求しない。後続Releaseで旧version環境がある場合だけ、ユーザー操作のupdate確認結果を記録する。

公開後の不一致を見つけても、既存Releaseを削除・上書きしない。影響、利用者データの安全性、推奨する修正版versionまたは公開停止判断を報告し、明示指示を待つ。

## 9. Report

次を簡潔に報告する。

- 開始mode、実行したstage遷移、version、tag、target commit、versionの明示／自動決定根拠。
- `READY_TO_BUILD`、`READY_TO_PUBLISH`、`NOT_READY_TO_BUILD`、`NOT_READY_TO_PUBLISH`、`NEEDS_VERIFICATION`、`NEEDS_DECISION`、`NO_RELEASE_NEEDED`または`RELEASED`の最終判定と各hard gate。
- 実行したbuild・test・installer smokeと結果。installer smokeを実施しなかった場合は、未実施理由、代替検証、残存リスク。
- master/catalogの対応確認結果。
- asset名、byte数、SHA-256。
- GitHub Release URLと公開状態。公開していないmodeではその旨。
- post-release確認結果。
- 未実施項目、Issue仕様との差異、別Issue候補。
- commit、push、tag、Release、asset uploadの各実施有無。

成功条件を満たさない場合は、部分成功を`released`へ丸めない。
