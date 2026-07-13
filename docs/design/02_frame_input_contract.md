# FrameInput契約

画像解析PoCで分類、イベント確定、OCRへ渡す1フレーム分の入力契約を定義する。実キャプチャAPI導入時は、入力元だけを差し替え、この契約以降の分類、確定、OCR対象選定を維持する。

## 目的

- metadata、timestamped、manifest、dry-run capture、将来の実キャプチャAPIを同じ境界で扱う。
- `timestamp_ms` の単調増加を保証し、time-based confirmation を壊さない。
- manifest expected columns を保持し、OCR評価カバレッジを壊さない。
- dry-run 出力を manifest mode で再実行できるようにする。

## 現行データ構造

Python PoCでは `FrameInput` が入力境界です。

```text
FrameInput
  row: dict[str, str]
  image_path: Path
  timestamp_ms: int | None
```

### `image_path`

分類とROI切り出しに使う画像ファイルのパス。

要件:

- 既存ファイルを指す。
- manifest mode では `image_path` 列から解決する。
- dry-run capture provider では `data/.../frames/` 配下へ保存したフレームを指す。

### `timestamp_ms`

フレーム取得時刻をミリ秒で表す値。

要件:

- timestamp 付き入力では必須。
- 0以上の整数。
- 入力順に strictly increasing とする。
- 同値や逆行は manifest 読み込み時にエラーにする。

`timestamp_ms=None` の入力は frame-based confirmation を使う。現時点では metadata mode が該当する。

### `row`

metadata互換の任意列を保持する辞書。

必須扱い:

- `organized_file`
- `screen_type`

任意列:

- `score`
- `expected_score`
- `max_combo`
- `expected_max_combo`
- `marvelous`
- `expected_marvelous`
- `perfect`
- `expected_perfect`
- `great`
- `expected_great`
- `good`
- `expected_good`
- `miss`
- `expected_miss`
- `ex_score`
- `expected_ex_score`
- 将来の補助列

任意列は読み捨てずに保持する。これにより timestamped が生成した manifest を manifest mode で読み直しても expected columns を使った評価が維持される。

## 入力モード

### metadata mode

目的:

- 既存ローカル評価素材による分類精度確認。

特徴:

- `samples/screenshots/metadata.csv` を読む。
- `timestamp_ms=None`。
- `confirmation_mode=frames`。
- `organized_file` は `samples/screenshots` から解決する。

### timestamped mode

目的:

- metadata と同じ画像列へ人工 timestamp を付けて time-based confirmation を確認する。

特徴:

- `timestamp_ms` を `timestamp-start-ms` と `timestamp-interval-ms` から生成する。
- `confirmation_mode=time`。
- `data/vision_poc_timestamped/frame_manifest.csv` を生成する。
- metadata の期待値列を manifest に保持する。

### manifest mode

目的:

- 実フレーム列入力PoC。
- timestamped や dry-run capture provider の出力を再実行する。

必須列:

- `image_path`
- `timestamp_ms`

任意列:

- `screen_type`
- OCR期待値列
- 補助列

検証:

- `image_path` 空欄はエラー。
- 画像ファイル不存在はエラー。
- `timestamp_ms` 空欄、非整数、負数はエラー。
- `timestamp_ms` 非単調増加はエラー。

### dry-run capture provider

目的:

- 実キャプチャAPI導入前に、既存画像ディレクトリを capture provider の代替入力として扱う。

特徴:

- 実デバイスには接続しない。
- `--frame-root` 直下の画像をファイル名昇順で読む。
- `timestamp_ms` を `--fps` から生成する。
- 丸めで同値が出る場合は直前値 + 1ms に補正する。
- フレームを `data/.../frames/` 配下へ保存する。
- manifest互換 `frame_manifest.csv` を出す。
- `--capture-dry-run-output` は `data/` 配下に限定する。

### dry-run sequence scenario

目的:

- 複数 `screen_type` が混在する時系列を、実キャプチャ前に manifest mode で再現する。
- short result、sustained result、duplicate、`transition_countup_*` を同じ入力列で確認する。

特徴:

- 入力CSVは既存manifestと同じ `image_path,timestamp_ms` を必須列にする。
- `screen_type`、OCR期待値列、補助列を任意列として保持する。
- `read_frame_manifest()` の検証を使い、timestampの空、非整数、負数、非単調増加をエラーにする。
- 画像を `data/.../frames/` 配下へコピーし、manifest互換 `frame_manifest.csv` を出す。
- 生成manifestは `--sequence-mode manifest` でそのまま読める。
- 本番キャプチャAPI、実デバイス、常駐監視、非同期処理、DB保存、OCR刷新は含まない。

### WPF single-frame capture

目的:

- ユーザーがWindows pickerで明示選択したwindowから1フレームだけ取得する。
- 実captureを既存manifest modeへ手動で渡せる再実行可能なローカル入力にする。

出力:

```text
data/windows_capture/capture-<UTC>-<unique>/
  frame.png
  frame_manifest.csv
  capture_metadata.json
```

`frame_manifest.csv` は1行だけを持ち、`image_path=frame.png` はmanifest directory相対、`timestamp_ms` はWindows processの単調増加ミリ秒値とする。`screen_type=unknown`、`capture_source`、`width`、`height`、`captured_at_utc` は任意列として保持される。UTC wall-clock時刻は監査用metadataであり、time-based confirmationに渡す `timestamp_ms` と混同しない。

Windows Graphics Capture adapterはpicker、D3D11 device、free-threaded frame pool、capture session、PNG encodingを担当する。UIはowner HWNDを渡して結果statusを表示し、writerは画像内容を解釈しない。成功・失敗・cancelの全経路でframe、frame pool、session、D3D device、WinRT streamを解放し、取得ごとにresourceを新規作成する。

writerは `data/` 直下のstaging directoryへ画像、manifest、metadataを書き、3ファイル完成後に一意な最終directoryへrenameする。既存出力は上書きせず、失敗時はstagingを削除する。captureだけでmanifest reader、分類、OCR、confirmed event、正式save input、DB、viewer履歴を呼び出さない。

2026-07-13の実測では、DDR GRAND PRIX windowのWPF captureは画像領域とmanifestの `width,height` がともに1280x720で一致し、window枠、影、余白は画像に含まれなかった。Windows Graphics Captureが返すsurface pixelを入力境界とするため、OSのDPI scaleはROI座標へ再適用しない。この実測入力は既存1280x720 ROI基準へcropなしで渡せた。将来、surface内に枠や余白を含む入力が見つかった場合は、capture原本を書き換えず、manifest補助列でcontent boundsを明示してからROI入力前処理を追加する。

1行manifestは単独で `--sequence-mode manifest` へ渡せる。複数captureをconfirmed-events評価へ使う場合は、各1行を時刻順にまとめたローカル評価manifestを `data/` 配下へ作り、`screen_type`、expected columns、`song_select_view` などの評価補助列を追加する。補助列は `FrameInput.row` に保持され、画像pathは評価manifest directoryまたは明示 `--frame-root` から解決する。期待値がない場合はPoCが出す `ocr_expected_template.csv` と `m3_metadata_expected_template.csv` を埋め、`evaluated` になるまで採用判断へ使わない。

## manifest CSV仕様

最小列:

```csv
image_path,timestamp_ms
frames/frame_001.png,0
frames/frame_002.png,1000
```

`screen_type` 付き:

```csv
image_path,timestamp_ms,screen_type
frames/frame_001.png,0,menu_setup
frames/frame_002.png,1000,result
```

期待値列付き:

```csv
image_path,timestamp_ms,screen_type,expected_score,max_combo,miss,ex_score
frames/result_a.png,0,result,935730,111,1,552
```

## 将来の実キャプチャAPIが満たすこと

- 1フレームごとに画像参照または保存済み画像パスを渡す。
- 各フレームへ単調増加する `timestamp_ms` を付ける。
- 入力順は時系列順にする。
- dry-run manifest互換出力をしばらく維持する。
- `FrameInput.row` 相当の任意列を保持できる余地を残す。

## 守るべき互換性

- manifest mode は `image_path,timestamp_ms` の最小CSVを読める。
- timestamped が出す manifest は expected columns を保持する。
- dry-run capture provider が出す manifest は manifest mode で読める。
- WPF single-frame capture が出す1行manifestはmanifest directory相対で読め、capture補助列を `FrameInput.row` に保持する。
- timestamp 付き入力は `confirmation_mode=time` になる。
- `--ocr-target confirmed-events` は `confirmed_result=true` かつ `duplicate=false` のみを対象にする。
