# M0 dry-run sequence scenario 指示テンプレート

次フェーズとして「dry-run capture sequence scenario PoC / 複数screen_type混在manifest / 保存境界の回帰確認」を依頼するときの指示テンプレートです。

```text
C:\work\ddrgp_scorelog で作業してください。AGENTS.md のプロジェクトルールに従ってください。

作業ブランチ codex/vision-poc-ocr-tuning の最新を使ってください。

次フェーズとして、
「dry-run capture sequence scenario PoC / 複数screen_type混在manifest / 保存境界の回帰確認」
を進めてください。

推奨Codex推論レベル:
- High 推奨。
- 理由: 今回は本番キャプチャAPI、実デバイス、常駐監視、非同期処理、DB保存にはまだ踏み込まず、既存dry-run capture providerとmanifest modeを使って、実キャプチャ前に近い時系列シナリオを検証する作業です。`timestamp_ms` の単調増加、`confirmation_mode=time`、`confirmed_result=true` かつ `duplicate=false` の保存境界、`transition_countup_*` の除外、expected columns の保持を壊さない必要があります。一方で本番キャプチャ設計やOCR刷新はしないため xhigh は不要です。

スコープ外:
- 本番キャプチャAPIの実装
- 実キャプチャデバイス依存コード
- 常駐監視ループ
- 非同期処理
- DB保存
- OCR方式の刷新
- ROI座標定義の大変更
- duplicate key の本格実装差し替え

やってほしいこと:

1. 現状確認
   - git status とブランチを確認する。
   - 最新を取得する。
   - 既存テストを実行する。
     python -X utf8 -m pytest tests

2. dry-run capture sequence scenario の小さな受け口を検討・追加
   - 既存 `--capture-dry-run` は単一ディレクトリをファイル名昇順で読む入口として維持する。
   - 必要なら、複数screen_typeを混ぜたシナリオmanifestを作る補助関数または軽いCLI入口を追加する。
   - 本番API名を先取りしすぎない。
   - 出力先は `data/` 配下に限定する。
   - 生成manifestは `--sequence-mode manifest` でそのまま読めること。
   - `screen_type`、OCR期待値列、任意列を保持できる既存manifest契約を壊さない。

3. シナリオ内容
   - 少なくとも以下を含む時系列manifestを生成またはテストで再現する。
     - non-result frames
     - result_candidate が短すぎて未確定の result frames
     - time-based confirmation を満たす result frames
     - duplicate window 内の duplicate result frames
     - `transition_countup_*` 相当の shape candidate frames
   - `transition_countup_*` は `result_shape_candidate=true` でも保存対象外であることを確認する。
   - confirmed-events の保存境界は `confirmed_result=true` かつ `duplicate=false` のまま維持する。

4. テスト追加
   - 複数screen_type混在manifestが `read_frame_manifest()` で読めること。
   - manifest mode で `confirmation_mode=time` が維持されること。
   - short result sequence が保存確定しないこと。
   - sustained result sequence が保存確定すること。
   - duplicate result が `duplicate=true` になること。
   - `transition_countup_*` が `rejected_transition` になること。
   - confirmed-events OCR対象が `confirmed_result=true` かつ `duplicate=false` のみに絞られること。
   - expected columns / no_expected_values / partially_evaluated の扱いを壊していないことを既存テストまたは追加テストで確認する。

5. README/docs 更新
   - dry-run capture provider の次段として、複数screen_type混在シナリオで保存境界を確認する方針を書く。
   - 実キャプチャAPI導入時も、しばらく manifest互換 dry-run 出力を維持して、実キャプチャ入力と分類/OCR/イベント確定を切り分ける方針を書く。
   - confirmed-events の保存境界は `confirmed_result=true` かつ `duplicate=false` のまま維持することを書く。
   - `transition_countup_*` は `result_shape_candidate=true` でも保存対象外であることを再確認する。
   - DB保存、常駐監視、非同期処理、OCR刷新はまだやらないことを書く。

6. 検証
   - python -X utf8 -m ruff check tools\vision_poc pyproject.toml
   - python -X utf8 -m ruff check tests
   - python -X utf8 -m compileall tools\vision_poc
   - python -X utf8 -m pytest tests
   - python -X utf8 -m tools.vision_poc --sequence-mode timestamped --ocr-target confirmed-events --ocr-rois all --ocr-profile all
   - python -X utf8 -m tools.vision_poc --sequence-mode manifest --frame-manifest data\vision_poc_timestamped\frame_manifest.csv --frame-root samples\screenshots --ocr-target confirmed-events --ocr-rois all --ocr-profile all
   - 追加した dry-run sequence 入口がある場合は、その出力manifestを `--sequence-mode manifest` で再読込するコマンドも実行する。

コミット/Push:
- metadata.csv、data/、logs/、ローカル素材、ローカルDBはコミットしない。
- コード、README、docs、テストに変更がある場合のみ、今回作業分だけをステージしてコミットする。
- 完了後、作業ブランチをpushする。

完了条件:
- 複数screen_type混在のdry-run/manifestシナリオで保存境界を確認できる。
- 生成manifestが既存manifest modeで読める。
- timestamp_ms の単調増加と `confirmation_mode=time` が維持されている。
- confirmed-events の保存境界が維持されている。
- `transition_countup_*` が保存対象外として維持されている。
- expected columns / no_expected_values / partially_evaluated の扱いが壊れていない。
- 本番キャプチャAPI、常駐監視、非同期処理、DB保存には踏み込んでいない。
- 検証コマンドが通っている。
- 必要な変更があればコミットされ、pushされている。
```
