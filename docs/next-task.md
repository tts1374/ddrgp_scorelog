# 現在PR: DDR WORLD公式music snapshot取得

DDR WORLD公式収録曲一覧のHTMLとjacketを、一度だけローカルsnapshotへ取得するdeveloper-only
Python CLIを追加する。snapshot評価、master/catalog対応付け、DB反映、OCR方式、ROI、
INFORMATION gate、正式保存判定はこのPRへ含めない。

## 今回の完了範囲

- `python -m tools.ddrworld_music_snapshot plan`で、network accessなしに想定page/jacket request数、
  最低待機時間、出力先、上書き禁止を確認できる。
- `fetch --allow-network`を同時指定した場合だけ公式sourceへ接続する。
- sourceはDDR WORLD music listのHTTPS origin/pathと`filter=7`、`filtertype=0`、
  `playmode=2`へ固定し、offsetは既知の0～25以内だけを許可する。
- HTTPはconcurrency 1、request間最低2秒、automatic retry 0、redirectなし、既定connect timeout
  10秒/read timeout 30秒とする。
- HTMLの各曲からsource page、ページ内位置、official title/artist、jacket URLを抽出する。
- page HTTP status/content type/空response、title/artist/jacket URL欠損、off-origin jacket URL、
  jacket HTTP status/content type/signatureをstrictに検査する。
- jacketはSHA-256 pathへ保存し、URL別のmetadataと同一画像hashをreportする。同一hashだけを理由に
  同一曲や異常とは判定しない。
- 取得中は`<snapshot-id>.incomplete/`だけを使い、全取得・検証成功時だけ
  `<snapshot-id>/`へdirectory renameする。失敗snapshotは`status: incomplete`で分離する。
- 既存の完成/未完成snapshotがあればnetwork access前に拒否し、上書き、追記、retry、cleanupを
  自動実行しない。
- mock responseとsynthetic HTMLだけを使うtestで正常、欠損、HTTP failure、content type/signature
  mismatch、duplicate hash、incomplete publication、既存出力、network opt-inを固定する。

## Local data boundary

出力はGit管理外の`data/ddrworld_music_snapshot/<snapshot-id>/`とし、次を保持する。

- `manifest.json`
- `pages/page-00.html`～`page-25.html`
- `songs.jsonl`
- `jackets/<sha256>.<ext>`
- `summary.json`

公式HTML、jacket、snapshot metadataはローカル検証だけに使い、Git、PR、artifact、Releaseへ
添付・再配布しない。既存ODS/XLSX、master/catalog DB、capture画像、監査成果物は読み書きしない。

## 実取得gate

collector実装、mock test、少数pageのread-only構造確認まではこのPRで実施する。26 pageと全jacketの
一括取得は、実行直前に想定request数、最低待機時間、出力先、上書きしないことをユーザーへ提示し、
明示確認を得るまで実行しない。規約上の自動取得許諾は未確認のため、確認前の本番取得をPR完了条件に
しない。

# 次PR候補: 保存済みsnapshot評価

次の実装単位は完成済みsnapshotだけを読み、networkへ接続しない。

- official title/artistをM4 masterへ対応付ける。
- DDR WORLD未収録、DDR GP専用、表記差、対応不明を分離する。
- 整合済みconfirmed 285件を基準にjacket照合を評価する。
- top-1、top-k、1位と2位の差、precision、coverage、誤一致、判定保留をreportする。
- ODS/XLSX、catalog/master DB、manual reviewへ自動反映しない。

その次の実装単位で、正常285件と明確なcapture mismatch 7件を使ってINFORMATION検出を評価する。
座標や閾値を根拠なく固定せず、snapshot取得・snapshot評価PRへ混ぜない。
