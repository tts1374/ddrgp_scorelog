These are the app-owned M7a digit-recognition runtime assets.

The `.pbm` files are normalized bitmap masks, not screenshots. The runtime
loads all ten labels (`0` through `9`) for each required template set and uses
the following search order:

* ROI-specific directory (`score_digits`)
* shared `judgment_counts` for all judgment-count fields, including `miss`
* shared `combo_ex_score` for `max_combo` and `ex_score`
* optional `max_combo` as the existing `ex_score` fallback when supplied by
  an explicit runtime data path

The packaged template sets are `score_digits`, `judgment_counts`, and
`combo_ex_score`. The runtime still accepts additional ROI-specific or
fallback directories from an explicit runtime data path.

The directory is resolved from the packaged application first, or from the
explicit `DDRGP_SCORE_VIEWER_RUNTIME_DATA` directory when configured. Runtime
code must not reach into repository `samples` or `tools/vision_poc`, and must
not invoke Python or Tesseract.
