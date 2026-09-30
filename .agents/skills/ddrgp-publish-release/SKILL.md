---
name: ddrgp-publish-release
description: DDRGPのrelease readiness監査、candidate生成、明示許可されたGitHub Release公開、公開済みRelease検証を依頼されたときに使う。通常実装・CI修正は対象外。
---

# DDRGP Publish Release

ルートと`app/AGENTS.md`を適用し、現在の明示的なユーザー指示をSkill guidanceより優先する。README・既存scriptを正本として再利用する。既にcontextで確認済みの同一指示は再読込しない。

## Select And Route

開始modeを選ぶ前に、依頼で指定された入力から対象version/Release URLと操作制約を確認する。その結果からmodeを1つ選び、なお曖昧なら`READINESS_AUDIT`から始める。詳細は到達したphaseで読む。

| Mode | 許可範囲と読むreference |
| --- | --- |
| `READINESS_AUDIT` | build前入力・commit・CI・停止条件・既存Releaseをread-only監査。[readiness.md](references/readiness.md)だけから開始。package生成・公開はこのphaseでは行わない。 |
| `BUILD_CANDIDATE` | 明示または安全に自動選定したversionのlocal package生成・検証。[readiness.md](references/readiness.md)で前提を確認し、[candidate.md](references/candidate.md)へ進む。GitHub tag/Release/assetは変更しない。 |
| `PUBLISH` | 元の依頼に「公開して」「GitHub Releaseを作成して」等の明示公開指示がある場合だけ。readiness・candidate成功後に[publish.md](references/publish.md)、成功後に[post-release.md](references/post-release.md)を読む。 |
| `POST_RELEASE_VERIFY` | 明示version/Release URLのread-only確認。[post-release.md](references/post-release.md)だけを読む。修正、asset差替え、Release削除は行わない。installer smokeは明示依頼と安全な環境が揃った場合だけ。 |

「公開問題ないか」「release readinessを確認して」は監査から開始する。「監査だけ」「read-only」「buildしない」の指定があれば`READY_TO_BUILD`でもそこで終了する。それ以外は`READY_TO_BUILD`から`BUILD_CANDIDATE`へ同じ実行内で自動遷移してよい。

`READY_TO_PUBLISH`は公開権限ではない。元の依頼に外部公開指示がなければcandidate完成で終了する。許可済みの公開について再承認は求めない。`PUBLISH`成功後は公開後検証まで完了する。

`NOT_READY_TO_BUILD`、`NOT_READY_TO_PUBLISH`、`NEEDS_VERIFICATION`、`NEEDS_DECISION`では自動遷移しない。latest tagから差分がなければ`NO_RELEASE_NEEDED`とする。

## Shared Safety Gates

全modeで次を守る。

- screenshot、実入力、解析log、local DB、`data/`、`logs/`、release生成物をGit対象へ入れない。
- 既存の未コミット変更、local DB、既存release生成物を保護する。
- Release failureをこのSkill内で通常実装修正へ広げない。原因と別Issue候補を報告して停止する。
- force push、既存tagの移動・削除、公開済みReleaseやassetの削除・置換を行わない。
- code signing、複数channel、任意version選択、自動rollback、schema migrationを追加しない。
- secret、token、local pathをRelease notesや公開assetへ含めない。
- 同一versionのtagまたはReleaseが存在する場合は内容を比較する。一致すれば冪等に完了報告し、不一致なら変更せず停止する。
- VeloPack installer smokeは同じ`packId`の本番install root、Start Menu shortcut、uninstall登録を上書き・削除し得る。`--installto`で一時pathを指定しても分離されたと判断しない。いずれかが存在するPCでは実行せず、既存環境を削除・退避しない。
- cleanな使い捨てWindows環境は通常利用できない前提とし、installer smokeは補助検証として扱う。環境を用意できないこと自体を、初回Releaseやinstaller変更を含めて`NEEDS_VERIFICATION`、`NOT_READY_TO_BUILD`、`NOT_READY_TO_PUBLISH`の理由にしない。代わりにreadinessとcandidateの自動検証を必須とし、未実施理由と残存リスクを記録する。

`BUILD_CANDIDATE`と`PUBLISH`では、build前に`data/release-build/<version>/`と`data/releases/<version>/`の存在を確認する。`Build-Release.ps1`はこの2つを再作成するため、既存出力があれば明示許可なしに実行しない。

契約変更または未許可の操作が必要な場合は、許可済みの独立作業を済ませてから必要な人間判断を求める。Skillが停止を要求した場合は、読んだfileのlinkと該当指示を引用し、実際の停止理由を説明する。

## Report

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
