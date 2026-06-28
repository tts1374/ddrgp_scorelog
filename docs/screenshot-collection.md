# スクリーンショット収集マトリクス

画面解析PoCでは、リザルト画面だけでなく、保存対象外の画面をネガティブサンプルとして収集する。

## 撮影条件

- PNG推奨。
- リサイズ・圧縮なし。
- DDR GPの画面全体を含める。
- マウスカーソル、通知、配信オーバーレイは映さない。
- 同じリザルトを2〜3秒間隔で2枚撮るケースを少し含める。

## 推奨フォルダ

```text
samples/screenshots/
  result/
  song_select/
  gameplay/
  transition/
  metadata.csv
```

スクリーンショット画像と `metadata.csv` はローカル素材として扱い、初期設定ではGit管理対象外にしている。

## リザルト画面

| 観点 | 欲しいパターン | 目安 |
|---|---|---:|
| プレースタイル | SINGLE / DOUBLE | 各5枚以上 |
| 難易度 | BEGINNER / BASIC / DIFFICULT / EXPERT / CHALLENGE | 各2枚以上 |
| 曲名 | 日本語、英語、記号入り、長い曲名、似た名前の曲 | 各2〜3枚 |
| スコア帯 | 1,000,000近辺、990k台、900k台、800k台以下 | 各2枚 |
| ランク/クリア | AAA系、AA/A系、低ランク、FAILED相当、FULL COMBO系 | 可能な範囲で各1〜2枚 |
| 判定数 | 0を含む、1桁、2桁、3桁以上が混ざる結果 | 10枚程度に分散 |
| レベル帯 | 低難度、中難度、高難度 | 各3枚以上 |

## ネガティブサンプル

| 画面 | 目的 | 目安 |
|---|---|---:|
| 選曲中 | リザルトと誤判定しない確認 | 10〜20枚 |
| プレー中 | スコア/数字が出ていても保存しない確認 | 10〜20枚 |
| リザルト遷移直後 | 表示途中の誤読防止 | 3〜5枚 |
| リザルト退出直前 | 重複保存/画面遷移検出確認 | 3〜5枚 |
| ロード/待機画面 | 無関係画面の除外 | 3〜5枚 |

## ファイル名ルール

```text
YYYYMMDD_style_difficulty_level_score_shorttitle_001.png
```

例:

```text
20260628_SP_EXPERT_15_987650_songname_001.png
20260628_DP_CHALLENGE_17_1000000_songname_002.png
```

## metadata.csv

可能なら `samples/screenshots/metadata.csv` を作成する。

列:

```text
file,screen_type,style,difficulty,level,song_title,score,note
```

記入例は `samples/metadata.example.csv` を参照。

