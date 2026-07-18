# DDR WORLD music snapshot collector (developer-only)

DDR WORLD公式収録曲一覧のHTMLとjacketを、一度だけローカル検証用snapshotへ保存する
独立したPython CLIです。通常のOCR評価、jacket catalog、master DB、正式保存workflowからは
呼び出しません。snapshot、HTML、画像、manifest、summaryはすべてGit管理外の`data/`へ出力し、
再配布やPR添付を想定しません。

## Network-free plan

実取得前に、最大リクエスト数、最低待機時間、出力先、上書き禁止を確認します。

```powershell
python -X utf8 -m tools.ddrworld_music_snapshot plan `
  --snapshot-id 20260718T120000Z `
  --page-count 26 `
  --estimated-songs 1300 `
  --delay-seconds 2
```

`estimated-songs`は計画用の上限です。実取得時は取得済みHTMLから見つけた一意なjacket URLだけを
順次取得します。既定値では26ページと最大1,300 jacketで最大1,326リクエスト、リクエスト間の
最低待機は2,650秒（約44分）です。通信時間は別途加わります。

## Explicit one-time fetch

本番取得は公式サイトへの外部アクセスを伴います。実行者が規約・負荷・出力先を確認した後だけ、
明示的な`fetch` commandと`--allow-network` optionを指定します。

```powershell
python -X utf8 -m tools.ddrworld_music_snapshot fetch `
  --allow-network `
  --snapshot-id 20260718T120000Z `
  --page-count 26 `
  --delay-seconds 2
```

HTTPはconcurrency 1、automatic retry 0、connect timeout 10秒、read timeout 30秒です。
各requestの完了後から次requestまで最低2秒待機します。timeoutはoptionで延長できますが、
delayを2秒未満にはできません。source origin、path、filter、filtertype、playmodeはcollector v1で
固定し、任意URLのcrawlerとしては動作しません。redirectは追跡しません。

## Output and publication boundary

既定出力は次の構成です。

```text
data/ddrworld_music_snapshot/<snapshot-id>/
  manifest.json
  pages/page-00.html ... page-25.html
  songs.jsonl
  jackets/<sha256>.<ext>
  summary.json
```

取得中は`<snapshot-id>.incomplete/`だけを使います。全pageと全一意jacket URLの取得・検証が
成功した場合だけ、directory renameで`<snapshot-id>/`へ公開します。page/画像取得、HTML解析、
content type、画像signatureのいずれかが失敗した場合は`.incomplete/`のまま残し、
`manifest.json`と`summary.json`を`status: incomplete`にします。既存の完成・未完成directoryが
1つでもあればnetwork access前に拒否し、resume、retry、追記、上書き、cleanupは行いません。

`songs.jsonl`はsource page、ページ内位置、official title/artist、jacket source URL、local path、
content type、byte size、SHA-256、失敗情報を保持します。`manifest.json`はsource条件、取得時刻、
collector version、request policy、page/image単位のHTTP statusと検証結果を保持します。
`summary.json`は件数、失敗、重複画像hashを集約します。同じhashを複数URLが返した場合は報告だけを
行い、同一曲や異常とは判定しません。同じcontentはhash pathへ1回だけ保存します。

HTMLは`#data_tbl tr.data`だけを対象にし、各行の`td.music_tit`、`td.artist_nam`、
`td.jk img[src]`を必須とします。page content typeはHTML、jacketはcontent typeとPNG/JPEG/GIF/WebP
signatureの一致を要求します。ODS、XLSX、catalog/master DB、既存capture、既存snapshotは読み書き
しません。

## Tests

実networkを使わず、synthetic HTMLとmock responseで解析、欠損、HTTP failure、画像検証、
duplicate hash、incomplete境界、上書き拒否、network opt-inを確認します。

```powershell
python -X utf8 -m pytest tests/test_ddrworld_music_snapshot.py -q
python -X utf8 -m ruff check tools/ddrworld_music_snapshot tests/test_ddrworld_music_snapshot.py
python -X utf8 -m compileall -q tools/ddrworld_music_snapshot
```

snapshotを使ったmaster対応付けやjacket照合評価は別実装単位です。このcollectorはDB反映、
OCR方式、ROI、INFORMATION gate、保存判定を変更しません。
