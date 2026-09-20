# getEQd measurement profiles - provenance

Five measured profiles for the native getEQd measured lab (`windows\profiles\*.json`).

Every curve here is a real third-party measurement of one specific headphone model. Nothing was
invented, smoothed, interpolated, extrapolated, or averaged by getEQd. Each file carries the raw
measurement in `gainDb` and the reference target in `targetDb`, so the app computes
`correction = target - raw` itself.

**Measured lab invariant:** the drawn correction curve is derived from these numbers only. No cosmetic
substitution was made at any point.

## Source chain

1. **Measurement:** oratory1990 measures each headphone on a GRAS ear-and-cheek simulator and publishes a
   one-page PDF preset document per model. That PDF states the rig, the target, the date, and the filter set.
2. **Numeric export:** the AutoEq project (`jaakkopasanen/AutoEq`) converts those measurements into CSV and
   redistributes them under its own repository licence.
3. **getEQd export:** these files are AutoEq's CSV values, copied verbatim onto the profile schema. The only
   change is JSON packaging.

AutoEq data files used (all on `master`, retrieved 2026-09-20):

- `measurements/oratory1990/data/over-ear/<model>.csv` (the raw curve)
- `targets/Harman over-ear 2018.csv` (the target curve)

## Profile table

| Model | Source | URL | Rig | Date | Licence observed | Range exported | Points |
|---|---|---|---|---|---|---|---|
| Sennheiser HD 600 | oratory1990, PDF `Sennheiser HD600.pdf`; numeric CSV via AutoEq | PDF: https://www.dropbox.com/s/pn3heg2rkwzeh34/Sennheiser%20HD600.pdf <br> CSV: https://github.com/jaakkopasanen/AutoEq/blob/master/measurements/oratory1990/data/over-ear/Sennheiser%20HD%20600.csv | GRAS 45BC-10 KEMAR, KB5000/5001 pinnae, APx515/APx526 analyzer, APx1701 amplifier | 2022-09-10 | MIT (AutoEq repo `LICENSE`, which covers the redistributed data file); oratory1990's own terms are not stated on the document | 20.00 - 19955.54 Hz | 695 |
| Sony WH-1000XM5 | oratory1990, PDF `Sony WH1000XM5.pdf` (wireless, ANC on); numeric CSV via AutoEq | PDF: https://www.dropbox.com/s/ozv8rsyvq73zavf/Sony%20WH1000XM5.pdf <br> CSV: https://github.com/jaakkopasanen/AutoEq/blob/master/measurements/oratory1990/data/over-ear/Sony%20WH-1000XM5.csv | GRAS 45BC-10 KEMAR, KB5000/5001 pinnae, APx515/APx526 analyzer, APx1701 amplifier | 2022-11-30 | MIT (AutoEq repo `LICENSE`); upstream terms unstated | 20.00 - 19955.54 Hz | 695 |
| Bose QuietComfort 45 | oratory1990, PDF `Bose QC45.pdf` (ANC on); numeric CSV via AutoEq | PDF: https://www.dropbox.com/s/cudwewakteknimi/Bose%20QC45.pdf <br> CSV: https://github.com/jaakkopasanen/AutoEq/blob/master/measurements/oratory1990/data/over-ear/Bose%20QuietComfort%2045.csv | GRAS 45BC-10 KEMAR, KB5000/5001 pinnae, APx515/APx526 analyzer, APx1701 amplifier | 2023-07-18 | MIT (AutoEq repo `LICENSE`); upstream terms unstated | 20.00 - 19955.54 Hz | 695 |
| Apple AirPods Max | oratory1990, PDF `Apple AirPods Max.pdf` (ANC on or Transparency); numeric CSV via AutoEq | PDF: https://www.dropbox.com/s/35z2n6hl2g8sp1i/Apple%20AirPods%20Max.pdf <br> CSV: https://github.com/jaakkopasanen/AutoEq/blob/master/measurements/oratory1990/data/over-ear/Apple%20AirPods%20Max.csv | GRAS 45BC-10 KEMAR, KB5000/5001 pinnae, APx515/APx526 analyzer, APx1701 amplifier | 2022-02-09 | MIT (AutoEq repo `LICENSE`); upstream terms unstated | 20.00 - 19955.54 Hz | 695 |
| Beyerdynamic DT 770 Pro | oratory1990, PDF `Beyerdynamic DT770 (new earpads).pdf`; numeric CSV via AutoEq | PDF: https://www.dropbox.com/s/npqrz9dqdda292x/Beyerdynamic%20DT770%20%28new%20earpads%29.pdf <br> CSV: https://github.com/jaakkopasanen/AutoEq/blob/master/measurements/oratory1990/data/over-ear/Beyerdynamic%20DT%20770%20Pro.csv | GRAS 45BC KEMAR, KB5000/5001 pinnae, APx515/APx526 analyzer, APx1701 amplifier | 2021-04-14 | MIT (AutoEq repo `LICENSE`); upstream terms unstated | 20.00 - 19955.54 Hz | 695 |

Target for all five: **Harman AE/OE 2018** (over-ear), file
`targets/Harman over-ear 2018.csv`, labelled on every oratory1990 PDF as "Harman AE/OE 2018 target".
The target is a population-average research curve, which is its correct role here: it is the *reference*,
never a stand-in for a model measurement.

## Data semantics

- **Grid:** AutoEq's standard logarithmic grid, ratio 1.01, 695 points, 20.00 Hz to 19955.54 Hz. The
  measurement and target files share this grid point-for-point (verified numerically, tolerance 1e-9 Hz),
  so `targetDb` is aligned to `gainDb` by construction. No resampling was required.
- **Normalisation:** AutoEq centres each raw curve to 0 dB at 1 kHz, and the Harman target is likewise
  referenced to 0 dB at 1 kHz. Because both share that reference, `target - raw` is the true error curve.
- **Beyerdynamic DT 770 Pro:** the AutoEq file named `Beyerdynamic DT 770 Pro.csv` corresponds to
  oratory1990's "fresh earpads" document, confirmed by AutoEq's
  `measurements/oratory1990/name_index.tsv` mapping. oratory1990 states the preset only applies while the
  earpads are under one year old; that caveat is recorded in the profile's `notes` and `rig` fields.
- **Wireless models:** the XM5, QC45, and AirPods Max entries are measurements of those headphones in
  active mode (ANC on, or ANC/Transparency for the AirPods Max). They are not passive/wired measurements.

## Verification performed

1. **Schema/validation:** all five files were checked against the exact rules in
   `windows\src\GetEQd.App\Audio\Profiles.cs` (`MeasurementProfileIO.Parse`): schema string, non-empty
   model, matching arrays of at least two points, finite frequencies above zero, finite gains, `responseType`
   of `raw`, and `targetDb` length equal to `frequenciesHz`. All five pass.
2. **Grid alignment:** measurement and target frequencies compared point-by-point; identical.
3. **Independent cross-check:** oratory1990's own 10-band parametric filter table (read from each source PDF)
   was rebuilt as RBJ biquads and compared against `target - raw` at 20 Hz through 14 kHz. Over 20 Hz - 4 kHz
   the mean absolute difference is 0.63 dB (QC45), 1.18 dB (AirPods Max), 1.25 dB (XM5), 1.30 dB (HD 600)
   and 1.55 dB (DT 770 Pro), with **no constant offset**. That is the agreement expected between a smoothed
   parametric fit and the exact error curve, and it confirms the raw and target curves share one reference.
   Differences grow above 8 kHz (up to about 5 dB) because oratory1990 smooths and deliberately limits treble
   correction; AutoEq documents the same above-10 kHz behaviour. This is expected, not an error.

## Rejected, and why

- **Harman AE/OE 2018 target curve** - a population-average target, not a measurement of any one headphone.
  Used only as `targetDb`; never presented as a model measurement.
- **Rtings / Innerfidelity / Headphone.com Legacy average curves** (present in AutoEq `targets/`) - category
  averages, not model-specific data. Excluded as measurements.
- **Crinacle measurement sets** - AutoEq's `measurements/crinacle/name_index.tsv` points at `file://` paths
  into a `raw_data` folder, and `measurements/crinacle/raw_data` does not exist in the repository (HTTP 404).
  No per-model curve with a verifiable rig and date could be pulled from that mirror, so nothing was used.
- **Bose QuietComfort Ultra Headphones** - no measurement in oratory1990's index. That source covers Bose
  QC20, QC25, QC45 and Noise Cancelling Headphones 700 only. Not substituted with the QC45 curve.
- **Sony WH-1000XM6** - no measurement in oratory1990's index; the Sony set tops out at WH-1000XM5. Not
  substituted with the XM5 curve.
- **Apple AirPods Pro (2nd generation)** - oratory1990's index contains only the 1st-generation AirPods Pro,
  measured on a GRAS 43AC in-ear rig. Different model, different form factor and different rig, so out of
  scope for this over-ear set.

## Licensing - what was observed, and the open question

- **Observed:** the AutoEq repository is MIT-licensed (`LICENSE` at repo root, "Copyright (c) 2018-2022
  Jaakko Pasanen"). The measurement CSVs are files inside that repository.
- **Observed:** every oratory1990 PDF carries attribution ("by oratory1990") and a version marker, but **no
  licence text**.
- **Observed:** a third-party repository (`goeddea/oratory1990_equalizerAPO`) redistributes oratory1990's EQ
  settings under CC0 1.0. That is a redistributor's choice and is not oratory1990's own grant.
- **Not verified:** any explicit licence statement from oratory1990 covering the measurement data itself. His
  Reddit wiki/FAQ, the Wayback Machine and Patreon all refused automated access (HTTP 403 / bot challenge), so
  no first-party terms could be retrieved.

**Interpretation:** redistributing these curves is what the AutoEq project already does under MIT, so
shipping them in getEQd carries no *additional* exposure beyond that project's own position. **Uncertainty:**
the upstream grant is unstated, so whether getEQd may redistribute the measurement data in a commercial
release is not something this pass can confirm. Confirm oratory1990's terms with a human before shipping
these profiles publicly. This is flagged, not resolved.

## Reproducing

Fetch `measurements/oratory1990/data/over-ear/<model>.csv` and `targets/Harman over-ear 2018.csv` from
`https://raw.githubusercontent.com/jaakkopasanen/AutoEq/master/`, drop the header row, and emit
`frequenciesHz`, `gainDb` and `targetDb` unchanged. The per-model date was read from the bottom-right corner
of the source PDF and matches that PDF's creation timestamp exactly in all five cases.
