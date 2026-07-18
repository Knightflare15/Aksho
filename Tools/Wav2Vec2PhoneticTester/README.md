# Wav2Vec2 Phonetic Tester

Standalone probe for phonetic experiments. The published executable currently launches Allosaurus' English-only recognizer by default:

```text
Allosaurus eng2102, lang=eng, emit=0.75, no voice trimming
```

The original Charsiu wav2vec2 tester is still available as `phonetic_tester.py`. By default it uses the fuller checked-in Charsiu model when present:

```text
Assets/MLModels/charsiu-en-w2v2-fc-10ms
```

If that folder is missing, it falls back to `Assets/MLModels/charsiu-en-w2v2-tiny-fc-10ms`.

Use this to compare the current Vosk word gate with real frame-level phoneme output. Vosk answers "did the expected word get accepted?" These tools answer "what phones did the model appear to hear, and when?"

## Accuracy

The current Unity pronunciation layer is lightweight and heuristic: it uses Vosk text plus expected spelling segments, so it is useful for gentle coaching but not true phoneme recognition.

`allosaurus_tester.py` runs Allosaurus as a free IPA phone decoder, so expect extra/noisy phones. `phonetic_tester.py` runs the actual Charsiu wav2vec2 frame classifier. These are better evidence for phonetic timing than spelling heuristics, but still not diagnosis or hard grading systems.

For known game words, the Charsiu tester's default mode is forced alignment against the expected phone sequence. The current Charsiu confidence gate is `0.15`, chosen from the labelled spell asset sweep because it gave the best coverage with the fuller model.

## Setup

Install the Python packages into whichever Python you want to use:

```powershell
python -m pip install -r Tools\Wav2Vec2PhoneticTester\requirements.txt
```

The launchers use `$env:PYTHON` when set, otherwise they try the Codex bundled Python, then `python`.

## Publish An Exe

```powershell
.\Tools\Wav2Vec2PhoneticTester\publish.ps1
```

The exe is written to:

```text
Tools\Wav2Vec2PhoneticTester\publish\Wav2Vec2PhoneticTester.exe
```

The exe is a framework-dependent launcher for the local Python phoneme model probe, so .NET plus Python with `allosaurus`, `sounddevice`, `torch`, and `transformers` are still needed. Set `$env:PYTHON` if you want it to use a specific virtual environment.

## Run From Microphone

```powershell
.\Tools\Wav2Vec2PhoneticTester\run.ps1 --word CAT --seconds 3
```

Published exe:

```cmd
Tools\Wav2Vec2PhoneticTester\publish\Wav2Vec2PhoneticTester.exe --word CAT --seconds 3
```

Double-click/no-args mode now uses live utterance detection instead of fixed seconds: enter the target word, speak when the listener is active, and it analyzes after a short silence. It uses a temporary WAV for Allosaurus and deletes it after analysis unless you pass `--save-trimmed-dir`.

If PowerShell script execution is disabled, use the Command Prompt launcher:

```cmd
Tools\Wav2Vec2PhoneticTester\run.cmd --word CAT --seconds 3
```

## Run From A WAV File

```powershell
.\Tools\Wav2Vec2PhoneticTester\run.ps1 --word SHIP --wav C:\path\to\ship.wav
```

To save a trimmed WAV from any command-line run:

```cmd
Tools\Wav2Vec2PhoneticTester\publish\Wav2Vec2PhoneticTester.exe --word CAT --wav Assets\Audio\Pronunciations\Spells\CAT.wav --save-trimmed-dir Tools\Wav2Vec2PhoneticTester\attempts
```

To trim all labelled spell pronunciation assets:

```cmd
Tools\Wav2Vec2PhoneticTester\.venv\Scripts\python.exe Tools\Wav2Vec2PhoneticTester\evaluate_assets.py --dir Assets\Audio\Pronunciations\Spells --trimmed-dir Tools\Wav2Vec2PhoneticTester\trimmed-spell-assets
```

## Compare Allosaurus

Allosaurus can be tested as a second online-downloaded phone recognizer:

```cmd
Tools\Wav2Vec2PhoneticTester\.venv\Scripts\python.exe Tools\Wav2Vec2PhoneticTester\evaluate_allosaurus_assets.py --dir Assets\Audio\Pronunciations\Spells --lang stan1293 --emit 2.5 --csv Tools\Wav2Vec2PhoneticTester\asset-eval-allosaurus-stan1293-emit25-no-trim.csv --json Tools\Wav2Vec2PhoneticTester\asset-eval-allosaurus-stan1293-emit25-no-trim.json
```

The first run downloads Allosaurus' `latest` pretrained universal model. That universal model was noisy even with English inventory: its best useful no-trim sweep was `stan1293` English inventory with `emit=2.5`: 19/86 exact, 61.9% average coverage.

Allosaurus also ships an English-only model that is not downloaded automatically:

```cmd
Tools\Wav2Vec2PhoneticTester\.venv\Scripts\python.exe -m allosaurus.bin.download_model -m eng2102
Tools\Wav2Vec2PhoneticTester\.venv\Scripts\python.exe Tools\Wav2Vec2PhoneticTester\evaluate_allosaurus_assets.py --dir Assets\Audio\Pronunciations\Spells --model eng2102 --lang eng --emit 3.0 --csv Tools\Wav2Vec2PhoneticTester\asset-eval-allosaurus-eng2102-emit30-no-trim.csv --json Tools\Wav2Vec2PhoneticTester\asset-eval-allosaurus-eng2102-emit30-no-trim.json
```

That refined Allosaurus run reached 38/86 exact and 76.9% average coverage. It is much better than the universal model, but still below the Charsiu full-model no-trim result. Higher `emit` settings improve target coverage by emitting more phones, but the output gets noisy quickly.

## Compare WavLM / HuBERT CTC

`hf_ctc_tester.py` runs Hugging Face CTC phone models. These are closer to true free phoneme recognition than Allosaurus for the current English spell assets.

```cmd
Tools\Wav2Vec2PhoneticTester\.venv\Scripts\python.exe Tools\Wav2Vec2PhoneticTester\hf_ctc_tester.py --backend wavlm --word CAT --wav Assets\Audio\Pronunciations\Spells\CAT.wav
```

Default WavLM model:

```text
speech31/wavlm-large-english-phoneme
```

WavLM base+ phonemizer candidate found later:

```cmd
Tools\Wav2Vec2PhoneticTester\.venv\Scripts\python.exe Tools\Wav2Vec2PhoneticTester\hf_ctc_tester.py --backend wavlm-base-plus-fr-it --word CAT --wav Assets\Audio\Pronunciations\Spells\CAT.wav
```

Default WavLM base+ phonemizer model:

```text
hugofara/wavlm-base-plus-phonemizer-fr-it
```

This one is a real phonemizer head on top of `microsoft/wavlm-base-plus`, so it fixes the "base+ is encoder-only" problem. It is trained for French/Italian, so English spell accuracy must be measured before using it in-game.

On the 86 labelled English spell WAVs, no trim, with the custom model class loaded correctly, this model produced:

```text
Exact: 10  partial: 68  zero: 8  average coverage: 54.9%
```

It is efficient, but not accurate enough for the current English spell set.

On the 86 labelled spell WAVs, no trim, the WavLM CTC sweep produced:

```text
Exact: 45  partial: 41  zero: 0  average coverage: 83.6%
```

CSV:

```text
Tools\Wav2Vec2PhoneticTester\asset-eval-hf-wavlm-no-trim.csv
```

The efficient wav2vec2 baseline is also wired in:

```cmd
Tools\Wav2Vec2PhoneticTester\.venv\Scripts\python.exe Tools\Wav2Vec2PhoneticTester\hf_ctc_tester.py --backend wav2vec2 --word CAT --wav Assets\Audio\Pronunciations\Spells\CAT.wav
```

Default wav2vec2 model:

```text
mostafaashahin/wav2vec2-base-timit-phoneme-arpa-39-v2
```

On the 86 labelled spell WAVs, no trim, this smaller wav2vec2 model produced:

```text
Exact: 31  partial: 50  zero: 5  average coverage: 69.7%
```

HuBERT is wired as `--backend hubert` with `addy88/hubert-base-timit-demo-colab`, but the first download/load did not complete quickly on this machine during testing. It remains selectable for another attempt or for a different `--model` checkpoint.

The benchmark candidate list also includes the official HF `phoneme-recognition`-filtered Wav2Vec2Phoneme checkpoints:

```text
facebook/wav2vec2-lv-60-espeak-cv-ft
facebook/wav2vec2-xlsr-53-espeak-cv-ft
bookbot/wav2vec2-ljspeech-gruut
```

Measured no-trim results after installing the Python `phonemizer` dependency and bypassing text phonemization for audio-only decoding:

```text
facebook/wav2vec2-lv-60-espeak-cv-ft:   26/86 exact, 68.6% average, 32.4s eval
facebook/wav2vec2-xlsr-53-espeak-cv-ft: 33/86 exact, 73.6% average, 32.9s eval
bookbot/wav2vec2-ljspeech-gruut:        23/86 exact, 60.3% average, 11.8s eval
```

and newer tagged candidates:

```text
slplab/wav2vec2-large-robust-L2-english-phoneme-recognition
Peacockery/hubert-base-phoneme-en
ct-vikramanantha/phoneme-scorer-v2-wav2vec2
```

`microsoft/wavlm-base-plus` is not directly runnable as a phone recognizer because it is an encoder-only checkpoint with no phoneme CTC/transducer head.

## Compare ZIPA ONNX

ZIPA uses a separate ONNX + Lhotse fbank inference path. The default wired checkpoint is the int8 ONNX build:

```text
anyspeech/zipa-large-crctc-ns-800k
```

Run one WAV:

```cmd
Tools\Wav2Vec2PhoneticTester\.venv\Scripts\python.exe Tools\Wav2Vec2PhoneticTester\zipa_onnx_tester.py --word CAT --wav Assets\Audio\Pronunciations\Spells\CAT.wav --precision int8
```

Published exe:

```cmd
Tools\Wav2Vec2PhoneticTester\publish-zipa\Wav2Vec2PhoneticTester.exe --backend zipa --word CAT --wav Assets\Audio\Pronunciations\Spells\CAT.wav
```

In the Vosk + pronunciation wrapper:

```cmd
Tools\SpeechPipelineTester\publish-wavlm\SpeechPipelineTester.exe --phoneme-backend zipa --word CAT
```

Measured on the 86 labelled English spell WAVs, no trim:

```text
Exact: 38  partial: 47  zero: 1  average coverage: 76.9%  eval: 10.7s
```

It is much faster than WavLM large and roughly ties the best Allosaurus coverage, but WavLM large is still more accurate on this labelled set.

## Final Measured Tradeoff Table

These are real local benchmark results from the labelled spell WAV set. The run used 86 English spell audio files from `Assets\Audio\Pronunciations\Spells`, with no trimming.

| Model / backend | Checkpoint | Files | Matched | Zero | Avg coverage | Eval time | Avg/file | Approx size | Notes |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| WavLM large CTC | `speech31/wavlm-large-english-phoneme` | 86 | 86 | 0 | 83.6% | 48.4s | 0.563s | 3579 MB | Best measured accuracy, but heavy. |
| ZIPA-CT / ZIPA CR-CTC int8 | `anyspeech/zipa-large-crctc-ns-800k` | 86 | 85 | 1 | 76.9% | 9.6s | 0.112s | 310 MB | Best current game tradeoff; fast ONNX path and already integrated. |
| Allosaurus English `eng2102`, emit 3.0 | `eng2102` | 86 | 83 | 3 | 76.9% | n/a | n/a | n/a | Similar target coverage to ZIPA, but noisier free-phone output. |
| Facebook Wav2Vec2Phoneme XLSR-53 | `facebook/wav2vec2-xlsr-53-espeak-cv-ft` | 86 | 83 | 3 | 73.6% | 32.9s | 0.382s | 1200 MB | Good coverage, larger and slower than ZIPA. |
| wav2vec2 base TIMIT ARPA39 | `mostafaashahin/wav2vec2-base-timit-phoneme-arpa-39-v2` | 86 | 81 | 5 | 69.7% | 14.3s | 0.167s | 360 MB | Small efficient baseline, weaker than ZIPA. |
| Facebook Wav2Vec2Phoneme LV-60 | `facebook/wav2vec2-lv-60-espeak-cv-ft` | 86 | 82 | 4 | 68.6% | 32.4s | 0.377s | 1200 MB | Usable, but not the best accuracy/efficiency tradeoff. |
| Allosaurus universal `stan1293`, emit 2.5 | `latest` + `stan1293` inventory | 86 | 82 | 4 | 61.9% | n/a | n/a | n/a | Useful reference, but noisy on these English assets. |
| Bookbot Wav2Vec2 gruut | `bookbot/wav2vec2-ljspeech-gruut` | 86 | 77 | 9 | 60.3% | 11.8s | 0.137s | 360 MB | Fast, but weaker coverage. |
| WavLM base+ FR/IT phonemizer | `hugofara/wavlm-base-plus-phonemizer-fr-it` | 86 | 78 | 8 | 54.9% | 16.0s | 0.186s | 361 MB | Real phonemizer head, but trained for French/Italian, weak on English spells. |
| WavLM base+ encoder only | `microsoft/wavlm-base-plus` | 0 | 0 | 0 | n/a | n/a | n/a | 360 MB | Not directly runnable as phone recognition without a phoneme head. |

Projected to a hypothetical 400-file spell set using the same matched/zero rates:

| Model / backend | Projected files | Projected matched | Projected zero | Avg coverage from measured run | Projected eval time | Avg response/file | Approx size |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| WavLM large CTC | 400 | 400 | 0 | 83.6% | 225.2s | 0.563s | 3579 MB |
| ZIPA-CT / ZIPA CR-CTC int8 | 400 | 395 | 5 | 76.9% | 44.8s | 0.112s | 310 MB |
| Allosaurus English `eng2102`, emit 3.0 | 400 | 386 | 14 | 76.9% | n/a | n/a | n/a |
| Facebook Wav2Vec2Phoneme XLSR-53 | 400 | 386 | 14 | 73.6% | 152.8s | 0.382s | 1200 MB |
| wav2vec2 base TIMIT ARPA39 | 400 | 377 | 23 | 69.7% | 66.8s | 0.167s | 360 MB |
| Facebook Wav2Vec2Phoneme LV-60 | 400 | 381 | 19 | 68.6% | 150.8s | 0.377s | 1200 MB |
| Allosaurus universal `stan1293`, emit 2.5 | 400 | 381 | 19 | 61.9% | n/a | n/a | n/a |
| Bookbot Wav2Vec2 gruut | 400 | 358 | 42 | 60.3% | 54.8s | 0.137s | 360 MB |
| WavLM base+ FR/IT phonemizer | 400 | 363 | 37 | 54.9% | 74.4s | 0.186s | 361 MB |

Current-folder ZIPA sanity check: the comparison table above uses the older shared 86-file benchmark so the models stay comparable. A later ZIPA-only rerun against the current `Assets\Audio\Pronunciations\Spells` folder found 114 WAV files and measured:

```text
ZIPA-CT / ZIPA CR-CTC int8: 114 files, 39 exact, 74 partial, 1 zero, 73.8% average coverage, 14.875s eval, 0.130s/file
```

So `0.112s/file` is the old 86-file benchmark average. The current folder's ZIPA-only response estimate is closer to `0.130s/file`, excluding microphone recording time and model startup.

The benchmark harness for the final tradeoff table is:

```cmd
Tools\Wav2Vec2PhoneticTester\.venv\Scripts\python.exe Tools\Wav2Vec2PhoneticTester\benchmark_phone_models.py --csv Tools\Wav2Vec2PhoneticTester\phone-model-benchmark.csv --json Tools\Wav2Vec2PhoneticTester\phone-model-benchmark.json
```

## Save JSON

```powershell
.\Tools\Wav2Vec2PhoneticTester\run.ps1 --word THIN --seconds 3 --json Tools\Wav2Vec2PhoneticTester\last-report.json
```

The report prints collapsed phone spans such as `K`, `AE`, `T`, then a rough target coverage table for the expected game word.
