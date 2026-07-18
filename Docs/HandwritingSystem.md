# Handwriting system

## Runtime flow

The handwriting loop is deliberately hybrid and offline-first:

1. `DrawController` captures the untouched pointer path and a separate assisted display path.
2. The safe-zone/formation coach may gently pull visible ink toward the guide, but recognition, diagnostics, and evidence always use the untouched path.
3. `RecognizerHost` runs an unconstrained A-Z P$ pass and an expected-letter P$ pass.
4. Expected-letter thresholds are calibrated per letter at startup using both leave-one-template-out positives and other-letter impostors. A weakly separated calibration can request a retry but cannot cause a score-only hard rejection. The authored Inspector threshold remains a hard ceiling.
5. The EMNIST LeNet CNN supplies an independent image opinion. Its disagreement requests review; it cannot independently mark a child wrong.
6. Deterministic diagnostics assess baseline, size, spacing, mirror, stroke count, direction, overdraw and wobble. Every finding carries a source, confidence and `actionable` flag; canonical stroke-order guesses remain advisory.
7. `HandwritingAcceptancePolicy` produces `Accept`, `Retry`, or `Reject`.

## Decision policy

- Tiny taps, extremely short paths, and drawings far beyond the calibrated expected shape are rejected.
- A near expected match, confident CNN disagreement, strong unconstrained-P$ contradiction, ambiguity, mirror evidence, or mostly out-of-zone writing requests a neutral retry.
- An excellent geometric match is not vetoed by the out-of-domain CNN.
- Formation style does not gate letter identity. A recognizable letter can be accepted while receiving a formation suggestion.
- Safe-zone assistance records the affected-point fraction and mean/maximum displacement. Only material assistance counts as support; a tiny isolated visual correction does not erase independent mastery.
- One letter is bounded to 2,048 captured points and 32 pen lifts to prevent accidental scribbles from allocating unbounded visual objects or diagnostic work.

## Learner feedback

Release builds show short actionable language and never expose raw model scores or diagnostic tag names. Technical details remain in development builds, structured attempt records, and teacher evidence.

The first unassisted formation attempt is quiet: the canonical guide is one valid formation, not the only valid stroke order. Direction/order coaching becomes more explicit after a retry or when the learner asks for help.

## Evidence and privacy

- Every `LetterAttemptRecord` has an immutable `attemptId`, assessment outcome/reason, P$/CNN/broad evidence, calibration separation, and measured assistance status.
- Accepted samples are bounded to 512 points and uploaded only when `collectAcceptedHandwritingEvidence` is enabled after school consent and retention configuration.
- Rejected/uncertain raw strokes are bounded to 256 points and uploaded only when `collectRejectedHandwritingEvidence` is enabled after school consent and retention configuration.
- Downsampling preserves at least the endpoints of every non-empty stroke when capacity permits, so dots and crossbars are not erased by a long main stroke. Retained points include raw coordinates plus attempt-bounds-normalized `nx`/`ny` values.
- The server assigns `rawStrokeExpiresAtUtc`; the daily retention job removes raw points while preserving aggregate diagnostics.
- `HANDWRITING_RAW_STROKE_RETENTION_DAYS` defaults to 180 and is bounded to 7–730 days.

## Recording contract

Every newly saved Unity template, standalone-recorder template, and consented gameplay stroke capture uses `handwriting_sample_v1`. Existing template files remain valid: recognizers continue to consume the legacy `x`, `y`, and `strokeId` fields, while the rich record is stored alongside them.

Each rich sample records:

- immutable sample, pseudonymous writer, collection-session and source IDs;
- expected letter, target-word/letter position when available, attempt number, outcome and review placeholders;
- coarse optional age band, handedness, cohort and consent-reference fields—never names or birth dates;
- UTC start/completion, total duration, per-point elapsed/delta time and stable point order;
- raw coordinates, glyph-bounds-normalized coordinates, canvas-normalized coordinates and stroke ID;
- mouse/touch/stylus type, pointer ID, normalized pressure when exposed, and stylus altitude/azimuth when available;
- platform, OS, device model/type, screen/canvas dimensions, DPI, app version and pressure/tilt availability;
- guide/tracing/assistance state and measured assistance displacement;
- human-review status, reviewed letter and future multi-label quality-tag slots.

Unity model: `Assets/Scripts/Drawing/HandwritingSampleModels.cs`  
Standalone parity model: `Tools/TemplateRecorderStandalone/HandwritingSampleModels.cs`

Recognition compatibility is deliberate. `TemplateLibrary.ToRecognizerTemplates()` and standalone `TemplateStore.ToRecognizerTemplates()` read only legacy raw coordinates. Adding model-ready fields therefore cannot alter P$ scores or CNN rendering. Old records load with a null rich sample; all newly recorded samples include one.

Raw coordinate systems are explicit because Unity records center-origin/Y-up while WinForms records top-left/Y-down. Both tools emit `nx`/`ny` and `canvasX`/`canvasY` in the same canonical `unit_square_bottom_left_y_up` system for training.

Gameplay constructs the rich capture in memory for recognition regardless of analytics, but raw points leave the device only when the relevant consent-controlled collection flag is enabled. Expiry removes raw point arrays while retaining bounded diagnostics and provenance.

## Validation

`HandwritingAcceptancePolicyTests` covers tap rejection, expected acceptance, CNN disagreement, excellent-fit protection, unconstrained-P$ contradiction, safe-zone overflow, weak-calibration protection, assistance materiality, structured diagnostic evidence, and deterministic touchscreen perturbations of all 26 authored letters.

This suite validates engineering invariants, not population accuracy. Production thresholds still require held-out child handwriting collected across learners and devices.

## Future learned assessor contract

The deterministic system should remain the offline fallback. A future model can replace or augment recognition only behind a versioned provider interface.

### Model input

```json
{
  "expectedLetter": "B",
  "strokes": [
    [{"x": 0.31, "y": 0.84, "tMs": 0}, {"x": 0.32, "y": 0.42, "tMs": 90}],
    [{"x": 0.32, "y": 0.82, "tMs": 220}, {"x": 0.64, "y": 0.67, "tMs": 310}]
  ],
  "canvas": {"width": 1080, "height": 720, "input": "touch", "strokeWidth": 18},
  "optionalRaster": "1x64x64 grayscale tensor"
}
```

Coordinates sent to a model should be normalized to the writing slot. Timing, pressure and tilt are optional and must have explicit missing-value masks because many devices do not supply them.

### Model output

```json
{
  "modelVersion": "child-handwriting-1.0.0",
  "letterProbabilities": {"B": 0.86, "D": 0.08, "P": 0.03},
  "expectedLetterProbability": 0.86,
  "outOfDistributionProbability": 0.04,
  "quality": {
    "mirror": 0.02,
    "openLoop": 0.71,
    "extraStroke": 0.08,
    "baselineIssue": 0.14,
    "wobble": 0.22
  },
  "calibratedConfidence": 0.81,
  "recommendedOutcome": "Retry"
}
```

The client policy, not the model, owns the final outcome. Missing models, unsupported devices, high out-of-distribution probability and low calibrated confidence must fall back to the deterministic policy.

### Models worth developing

1. **Letter identity:** start with a small raster classifier such as MobileNetV3-small or a compact CNN, then compare it with a point-sequence TCN/Transformer. A late-fusion model can combine raster shape and stroke dynamics once both have enough data.
2. **Formation diagnostics:** use a multi-label sequence model for mirror, loop, extra/missing stroke and direction labels. Keep baseline, size and safe-zone geometry deterministic because ML adds little value there.
3. **Out-of-distribution detector:** use energy/entropy or an explicit reject head trained on scribbles, partial letters, digits and non-letter shapes. This is essential before allowing learned confidence to affect progression.
4. **Probability calibration:** fit temperature scaling or isotonic calibration on a child-disjoint validation set. Raw softmax values must never be presented as confidence.

### Data that still has to be collected externally

- Consented raw stroke sequences and rendered images from many children, devices and ages.
- Correct letters plus incomplete, mirrored, overwritten, look-alike and deliberately wrong examples.
- At least two independent human labels for subjective formation tags, with adjudication for disagreements.
- Valid alternative stroke orders so the model does not learn one teacher's template as the only correct style.
- Device, input modality and coarse age band metadata; avoid names, free text and unnecessary identifiers.

A reasonable first fine-tuning pilot is tens of thousands of letters across at least 100 independent writers. Diagnostic heads need substantial positive examples for every tag; rare tags should be actively sampled rather than inferred from class imbalance. These are planning ranges, not guarantees.

### Required evaluation gates

- Split by child, never by individual attempt. Prefer an additional held-out school/device set.
- Report per-letter false acceptance and false rejection, macro-F1, confusion pairs, expected calibration error and out-of-distribution AUROC.
- Slice results by age band, device/input type, handedness when consented, and assistance state.
- Set explicit maximum regression limits for `B/D/P/R`, `C/G`, `I/L/T`, mirror-sensitive letters and incomplete strokes.
- Measure on-device cold start, p95 latency, memory and battery use on the lowest supported Android device.
- Shadow-run new models first, compare them with human labels, then canary by model version with an immediate rollback switch.

Dataset consent, collection, expert labeling and real-child validation cannot be completed from the repository alone. Until those exist, ML output must remain advisory and the deterministic system remains authoritative.
