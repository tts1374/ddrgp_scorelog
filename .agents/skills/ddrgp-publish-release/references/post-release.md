# Verify Published Release

公開直後、または既存Releaseのread-only確認で読む。`README.md`と`app/README.md`のasset一覧、reference data set、VeloPack feed、導入・更新・既知制限の関連箇所を契約にする。明示依頼があり、cleanな使い捨て環境でinstaller smokeを実行する場合だけ`app/tests/VerifyVeloPackInstall.ps1`を追加で読む。

公開済みReleaseの確認だけでは、roadmap、candidate build script、build/runtime検証scriptを事前に全件読まない。公開時のlocal asset記録がなければhash照合を未検証とし、推測で一致扱いにしない。

`PUBLISH`と`POST_RELEASE_VERIFY`では次を確認する。

1. 公開Releaseのtag、target SHA、stable状態、Release notes、asset一覧を確認する。
2. public downloadしたassetのfile名、byte数、SHA-256をlocal記録と照合する。
3. GitHub latest Release APIでreference data setの3 assetが同じReleaseから解決されることを確認する。
4. VeloPack feedに`releases.win.json`とfull packageが存在することを確認する。
5. `PUBLISH`では、cleanな使い捨てWindows環境が確保済みの場合だけdownload済みSetupへ`VerifyVeloPackInstall.ps1`を実行する。本番install root、Start Menu shortcut、uninstall登録があるPCでは実行しない。installer smoke未実施で公開した場合は、未実施理由と残るリスクがRelease notesに記載されていることを確認する。`POST_RELEASE_VERIFY`では明示依頼があり、かつclean環境の場合だけ実行する。
6. 初回Releaseでは既存versionからのupdate確認を要求しない。後続Releaseで旧version環境がある場合だけ、ユーザー操作のupdate確認結果を記録する。

公開後の不一致を見つけても、既存Releaseを削除・上書きしない。影響、利用者データの安全性、推奨する修正版versionまたは公開停止判断を報告し、明示指示を待つ。
