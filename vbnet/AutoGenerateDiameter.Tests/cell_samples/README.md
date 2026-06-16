# Cell OCR sample dataset

Labelled, pre-cropped **single-cell** images used by `CellOcrAccuracyTests` to measure how
well the cell-level OCR pipeline reads handwritten diameter numbers.

## How to produce a sample

1. Run the app, **Compare Manual**, and perspective-align/crop a real sheet photo.
2. The aligned image is the crop source. Each expected R/L value becomes one cell.
   (You can save cells during a debugging session, or crop them by hand from the aligned image.)
3. Save each cell as its own image in this folder.

## Naming convention

```
<expected>[_r<row>]_<anything>.<ext>
```

- `<expected>` — the correct number written in the cell, e.g. `65`.
- `_r<row>`   — optional row number 1–6 (only affects the 3-digit disambiguation rule; defaults to 1).
- `<anything>` — free text to keep filenames unique.

Examples:

```
65_r1_t1R_001.png
60_r3_t14L_002.png
75_r2_sample.jpg
```

Supported extensions: `.png .jpg .jpeg .bmp .tif .tiff`

## How the gate works

- < 10 samples → test reports accuracy but stays **Inconclusive**.
- ≥ 10 samples → test **fails** if accuracy drops below **70%**.
- Tesseract not installed → **Inconclusive**.

You can point the test at a different folder with the `CELL_SAMPLES_DIR` environment variable.