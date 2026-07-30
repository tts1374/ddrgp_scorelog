These are the app-owned M7a digit-recognition runtime assets.

The `.pbm` files are normalized bitmap masks, not screenshots. The runtime
loads all ten labels (`0` through `9`) for each required template set and uses
the following search order:

* ROI-specific directory (`score_digits`, `max_combo`, `marvelous`,
  `perfect`, or `miss`)
* shared `judgment_counts` for the judgment-count fields
* shared `combo_ex_score` for `max_combo` and `ex_score`
* `max_combo` as the existing `ex_score` fallback

The directory is resolved from the packaged application first, or from the
explicit `DDRGP_SCORE_VIEWER_RUNTIME_DATA` directory when configured. Runtime
code must not reach into repository `samples` or `tools/vision_poc`, and must
not invoke Python or Tesseract.
