# Release Readiness

build前条件の監査とversion選定で読む。まず次の正本の関連箇所を確認する。

- `docs/implementation-roadmap.md`のCurrent PhaseとM10 Initial Release。
- `README.md`のRelease build入口、`app/README.md`のrelease停止条件、reference data set、既知制限。
- `app/packaging/Build-Release.ps1`の入力path・master/catalog検査契約。
- `.github/workflows/ci.yml`の対象SHAで必須のjob。

指定Release Issueまたは承認済みchecklistがある場合だけ追加契約として読む。正本と矛盾する公開挙動は推測で選ばず`NEEDS_DECISION`とする。candidateのscript本文やpublish/post手順は、監査だけで全件読まない。

## Version Selection

明示versionがあればそれを優先する。versionがなく`BUILD_CANDIDATE`または`PUBLISH`へ進む場合は、次の全条件を満たすときだけ自動決定する。

1. GitHubの最新stable Releaseが`vX.Y.Z`形式であり、そのtagが解決できる。
2. latest tagのtargetから今回のtarget SHAまでに1件以上のcommit差分があり、target SHAがlatest tagの子孫である。
3. README、Issue、承認済みchecklistに別のversion指定またはversioning方針がない。
4. 差分に`BREAKING CHANGE`、互換性を壊すCLI・永続化形式・schema・installer identity・update channel変更、必須migrationなど、major/minor判断を必要とする兆候がない。
5. 自動決定する`X.Y.(Z+1)`にlocal/remote tag、GitHub Release、`data/release-build/<version>/`、`data/releases/<version>/`の競合がない。

全条件を満たす場合だけ次patch version `X.Y.(Z+1)`とtag `vX.Y.(Z+1)`を採用し、latest tagからtarget SHAまでの差分をRelease notesの対象とする。major/minorは自動決定しない。初回Release、非SemVer tag、破壊的変更の疑い、version競合、targetがlatest tagの子孫でない場合は`NEEDS_DECISION`で停止し、versionを1問だけ確認する。差分が0件ならpackageやReleaseを作らず`NO_RELEASE_NEEDED`で終了する。

## Audit

`READINESS_AUDIT`では次を確認して、変更せず報告する。

1. 現在branch、HEAD、`origin/main`、worktree状態、release対象候補commitを確認する。
2. 対象commitのGitHub Actions必須jobを確認する。未実行、失敗、対象SHA不一致を成功扱いにしない。
3. required master DBとbinding済みruntime catalogの存在、Git管理外であること、対応versionを確認する。DB内容を変更しない。
4. GitHubの最新stable Releaseとtag targetを確認し、tag targetから対象commitまでの差分と上記のversion自動決定条件を評価する。
5. installer smoke環境を次のように分類する。
   - 同じ`packId`の本番install root、Start Menu shortcut、uninstall登録のいずれかがある場合は`installer smoke未実施`とし、smokeを実行せず既存環境を変更しない。
   - cleanな使い捨てWindows環境が実際に利用できる場合だけ`installer smoke実行可能`とする。
   - `installer smoke未実施`は情報項目であり、それだけをreadiness failureにしない。初回Releaseやinstaller関連変更でも、対象SHAの必須GitHub Actionsと[candidateの代替検証](candidate.md)をhard gateとする。
   - latest tagからtarget SHAまでのinstaller identity、package生成、shortcut、install / update / uninstall lifecycleの変更有無を分類する。変更がある場合は、変更責務に対応する自動testとpackage検証を必須にする。testやSkillだけの変更はshipped behaviorを変えるかdiffで判断する。
   - installer smoke未実施の理由と、install / update / uninstallの実機回帰が未確認である残存リスクをRelease notesの技術情報とtask報告へ明記する。
6. `app/README.md`の現在のrelease停止条件のうち、build前に検証可能な項目をhard gateとして列挙し、各項目を`ready`、`not_ready`、`unverified`へ分類する。
7. package、installer、公開asset、Release notes、公開後確認はdownstream stageの検証項目として分け、未生成であることだけを`unverified` hard gateまたは失敗にしない。
8. build前条件がすべて満たされれば`READY_TO_BUILD`、失敗があれば`NOT_READY_TO_BUILD`、実行中CIなど結果待ちなら`NEEDS_VERIFICATION`、versionまたは契約判断が必要なら`NEEDS_DECISION`を返す。installer smoke環境未確保だけを停止理由にしない。latest tagから差分がなければ`NO_RELEASE_NEEDED`を返す。
9. `READY_TO_BUILD`かつ自動遷移が許可される依頼では、versionを固定して`BUILD_CANDIDATE`へ進む。

local test未実施をGitHub Actions成功で代替した、またはその逆であると暗黙判断しない。実施済み事実と未実施を分ける。
