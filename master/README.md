# マスタ取得機能

BEMANIWiki の DanceDanceRevolution GRAND PRIX 全曲リストを取得し、配布用SQLiteマスタDBを生成する機能をここに実装する。

## Source

https://bemaniwiki.com/?DanceDanceRevolution+GRAND+PRIX/%E5%85%A8%E6%9B%B2%E3%83%AA%E3%82%B9%E3%83%88

## Planned Output

```text
ddrgp-master.sqlite
```

## Planned Tables

- `songs`
- `charts`
- `master_metadata`
- `source_snapshots`

## Implementation Notes

- PythonでHTMLを取得する。
- BeautifulSoup/lxmlでテーブルを解析する。
- セル結合、注記、削除曲、限定曲、SP/DP差分を扱う。
- 生成物はGitHub Releasesで配布する。

