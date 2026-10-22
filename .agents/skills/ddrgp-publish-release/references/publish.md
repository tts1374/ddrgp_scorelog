# Publish Release

明示的な外部公開許可とreadiness・candidate成功が揃った場合に読む。`README.md`、`app/README.md`、利用ガイドの現在の導入・更新・制限をRelease notesの正本にする。

以下の公開操作は`PUBLISH`だけが実行する。文章基準は告知文の作成・修正にも適用する。

1. target SHA、`vX.Y.Z`、全assetの記録を再確認する。
2. 前Releaseからtarget SHAまでのユーザー影響を要約し、次の順でRelease notesを作る。
   - 高校生・非技術者が、開発知識や事前説明なしで分かる平易な日本語を使う。変更点は利用者が画面・操作・結果で実感できる内容に絞り、何が見やすくなったか、何ができるかを具体的に書く。
   - 内部変更は記載しない。実装、認証・保存処理、形式の互換性、依存関係などの説明を、専門語の言い換えや「改善しました」だけの項目として残さない。利用者に伝える具体的な変化がなければ省く。
   - 初回installと既存versionからの更新方法を分け、利用者がdownloadするSetup名を明記する。
   - 正式個人スコアDB、設定、reference DB、ログの保持、未署名installerの警告、保証環境、既知制限、利用ガイドへの導線を現在の`README.md`と`app/README.md`に合わせて記載する。
   - target commit、必要な検証結果、全公開assetのbyte数とSHA-256は`<details>`内の確認情報へ分離する。これは公開物の識別・検証用であり、内部変更を列挙する場所にしない。検証の説明や既知制限も平易に書く。
   - installer smokeを省略した場合は、clean環境を利用できないため未実施であることと、install / update / uninstallの実機回帰が未確認である残存リスクを同じ確認情報へ明記する。
   - 実施後に副作用、不一致、未確認事項が判明した検証を成功実績として記載しない。利用者の判断に不要な内部検証の列挙を避ける。
3. GitHubのdraft Releaseをtarget SHAの`vX.Y.Z`で作成し、Setup、full package、`RELEASES`、`assets.win.json`、`releases.win.json`とreference data setの3 assetを添付する。
4. draft上のtag、target SHA、asset名、byte数をlocal記録と照合する。不足、重複、不一致があれば公開せずdraftのまま停止する。
5. すべて一致した場合だけstable Releaseとして公開する。prereleaseや別channelへ変更しない。

GitHub connectorまたは`gh`を使用する前に、対象repositoryと認証先をread-onlyで確認する。途中失敗時にdraft、tag、assetを独断で削除しない。

成功後は[post-release.md](post-release.md)へ進む。

## X告知文（依頼された場合）

高校生・非技術者に分かる平易な文で、利用者が実感できる変更だけを載せる。内部変更は記載しない。URLは元の長さによらず半角23文字として扱い、本文・改行・hashtagも含めてXの文字数計算で上限内に収める。日本語中心の「140文字」は全角140文字相当（日本語2・半角1、URL23の加重合計280以内）を基準とし、ユーザーが別の数え方・上限を指定した場合はそれを優先する。実際に数え、超える場合は変更点を絞る。報告時は単純な文字数とXの加重文字数を区別する。数え方は[X公式の文字数説明](https://docs.x.com/fundamentals/counting-characters)を参照する。現在のユーザー指定がなければ、次の形式を使う。

```text
DDR GP スコアログ vX.Y.Zをリリースしました。

今回のアップデート
・利用者向けの変更点
・利用者向けの変更点
リリースページ:
対象versionのGitHub Release URL
#GP_ScoreLog
```

versionとURLは対象Releaseにそろえる。告知文作成はXへの投稿許可を含まない。
