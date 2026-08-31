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
  --estimated-songs 1300 `
  --delay-seconds 2
```

`estimated-songs`は計画用の上限です。実取得時は取得済みHTMLから見つけた一意なjacket URLだけを
順次取得します。pageはoffset 0から連番で取得し、正常な公式pageで楽曲行が0件になった時点で
終端とします。終端確認を含むpage requestは100回を安全上限とし、既定の計画では最大1,400
リクエスト、リクエスト間の最低待機は2,798秒（約46分38秒）です。通信時間は別途加わります。

## Explicit fixed-root fetch

本番取得は公式サイトへの外部アクセスを伴います。実行者が規約・負荷・出力先を確認した後だけ、
明示的な`fetch` commandと`--allow-network` optionを指定します。通常のcollector画面はこの
fixed-root modeを使用し、snapshot IDや取得条件を画面から変更させません。

```powershell
python -X utf8 -m tools.ddrworld_music_snapshot fetch `
  --allow-network `
  --fixed-output `
  --output-root data/ddrworld_music_snapshot `
  --incomplete-root data/ddrworld_music_snapshot.incomplete `
  --delay-seconds 2
```

HTTPはconcurrency 1、automatic retry 0、connect timeout 10秒、read timeout 30秒です。
各requestの完了後から次requestまで最低2秒待機します。delayとtimeoutは有限値だけを許可し、
timeoutは正値のoptionで延長できますが、delayを2秒未満にはできません。source origin、path、
filter、filtertype、playmodeはcollector v1で
固定し、任意URLのcrawlerとしては動作しません。redirectは追跡しません。

fixed-root modeではoffset 0から連番で取得します。楽曲行があるpageだけを保存し、正常な公式
pageで楽曲行が0件になったpageを終端確認として扱います。終端pageは`pages/`、楽曲数、page
数へ含めません。HTTP、content type、公式page構造、楽曲行解析の失敗は空pageとみなさず、
100回以内に終端を確認できない場合と同じく取得を失敗扱いにして既存の固定snapshotを維持します。
offset 0の空pageも有効なsnapshotの終端にはなりません。`manifest.json`の`pagination`へ安全上限、
終端offset、終端pageの検証結果を記録します。

## Output and publication boundary

`--fixed-output`での出力は次の固定構成です。`snapshot-id`は内部の取得識別子として
`manifest.json`と`summary.json`へ記録しますが、ディレクトリ名には使いません。

```text
data/ddrworld_music_snapshot/
  manifest.json
  pages/page-00.html ... page-(楽曲ページ).html
  songs.jsonl
  jackets/<sha256>.<ext>
  summary.json
```

取得中は固定rootの隣にある`data/ddrworld_music_snapshot.incomplete/`だけを使います。
全楽曲page、終端確認、全一意jacket URLの取得・検証が成功し、必須ファイル、status、件数を検証した場合だけ、
directory renameで固定rootへ公開します。page/画像取得、HTML解析、content type、画像signatureの
いずれかが失敗した場合は`.incomplete/`のまま残し、`manifest.json`と`summary.json`を
`status: incomplete`にします。既存の完成rootは公開直前まで保持し、network access前に
不完全なrootを上書きしません。次回実行時は前回の`.incomplete/`を破棄して最初から取得します。

画面からのキャンセルはrequest境界で停止し、固定rootを公開しません。キャンセル時の診断は
`.incomplete/`へ`status: cancelled`として残し、次回実行時に破棄します。

`songs.jsonl`はsource page、ページ内位置、official title/artist、jacket source URL、local path、
content type、byte size、SHA-256、失敗情報を保持します。`manifest.json`はsource条件、取得時刻、
collector version、request policy、page/image単位のHTTP statusと検証結果を保持します。
`summary.json`は件数、失敗、重複画像hashを集約します。新しく生成するsnapshotの
`stored_jacket_count`は保存されたhash pathの実数であり、同じhashを複数URLが返した場合は
報告だけを行い、同一曲や異常とは判定しません。同じcontentはhash pathへ1回だけ保存します。
初期配置する旧v1 snapshotは成功したimage record数をこのfieldに保持する場合があるため、
validatorは旧形式も読み込み、表示時の保存画像数はmanifestの一意local pathから算出します。

HTMLは確認済みの公式構造である`table.table-ui`または既存snapshot形式の`table#data_tbl`だけを
対象にします。現行形式では各楽曲行の`td.chart img.left-image.large[src]`、`.music-title`、
`.artist`、旧形式では`td.jk img[src]`、`td.music_tit`、`td.artist_nam`を必須とします。pageに
楽曲行がない場合も、確認済みの公式tableが存在し、想定外の行がないことを確認できた場合だけ
正常な終端とします。page content typeはHTML、jacketはcontent typeとPNG/JPEG/GIF/WebP signature
の一致を要求します。ODS、XLSX、catalog/master DB、既存capture、既存snapshotは読み書きしません。

## Tests

実networkを使わず、synthetic HTMLとmock responseで動的page取得、空page終端、構造・解析・HTTP
failure、画像検証、duplicate hash、安全上限、incomplete境界、pathの包含関係、上書き拒否、network
opt-inを確認します。

```powershell
python -X utf8 -m pytest tests/test_ddrworld_music_snapshot.py -q
python -X utf8 -m ruff check tools/ddrworld_music_snapshot tests/test_ddrworld_music_snapshot.py
python -X utf8 -m compileall -q tools/ddrworld_music_snapshot
```

完成済みsnapshotをcurrent ROI v2のgrid jacket truthへnetworkなしで照合する後続評価は、
`tools/ddrworld_snapshot_evaluation/README.md`を参照してください。collector自体は評価、
master/catalog対応付け、DB反映を行いません。

snapshotを使ったmaster対応付けやjacket照合評価は別実装単位です。このcollectorはDB反映、
OCR方式、ROI、INFORMATION gate、保存判定を変更しません。
