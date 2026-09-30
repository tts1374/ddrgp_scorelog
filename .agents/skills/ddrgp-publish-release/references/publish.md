# Publish Release

明示的な外部公開許可とreadiness・candidate成功が揃った場合に読む。`README.md`、`app/README.md`、利用ガイドの現在の導入・更新・制限をRelease notesの正本にする。

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

成功後は[post-release.md](post-release.md)へ進む。
