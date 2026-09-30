# Release Candidate

readiness通過後のlocal package生成・検証で読む。`app/README.md`のpackage生成、更新、初回導入、reference data set、backup / restoreとasset契約、`app/packaging/Build-Release.ps1`、`app/tests/VerifyReleaseBuild.ps1`、`VerifyReleaseRuntime.ps1`、`VerifyReleasePackage.ps1`を確認する。installer smokeを安全に実行できる場合だけ`VerifyVeloPackInstall.ps1`の手順も読む。

## Build And Verify

`BUILD_CANDIDATE`と`PUBLISH`では次を順に実行する。

1. 明示versionまたはreadinessのversion選定で自動決定したversionが`X.Y.Z`、tagが`vX.Y.Z`であることを固定し、versionの決定根拠を記録する。
2. worktreeがcleanで、対象HEADが`origin/main`と一致することを確認する。異なるcommitの公開が必要なら停止して判断を求める。
3. 対象SHAの必須GitHub Actionsが成功していることを確認する。
4. localとremoteに同名tagがなく、GitHubに同version Releaseがないことを確認する。既存の場合は`SKILL.md`の共通gateの冪等判定へ戻る。
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
