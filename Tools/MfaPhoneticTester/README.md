# MFA Phonetic Tester

Batch comparison harness for Montreal Forced Aligner using the `english_us_arpa` acoustic model and dictionary.

MFA is a forced aligner, not a free phoneme recognizer. It knows the reference word transcript and aligns the expected phones to the audio. That makes it the right primitive for known-word pronunciation feedback, but a simple coverage score is not a fair independent-recognition score. Pronunciation grading should use alignment quality, likelihood, phone durations, and Vosk's word gate.

## Setup Used

```cmd
scoop bucket add extras
scoop install miniconda3
C:\Users\Aryan\scoop\apps\miniconda3\current\Scripts\conda.exe create -y -n mfa-phonetic --override-channels -c conda-forge montreal-forced-aligner
C:\Users\Aryan\scoop\apps\miniconda3\current\Scripts\conda.exe install -y -n mfa-phonetic --override-channels -c conda-forge "kalpy<0.10"
C:\Users\Aryan\scoop\apps\miniconda3\current\Scripts\conda.exe run -n mfa-phonetic mfa model download acoustic english_us_arpa
C:\Users\Aryan\scoop\apps\miniconda3\current\Scripts\conda.exe run -n mfa-phonetic mfa model download dictionary english_us_arpa
```

The `kalpy<0.10` pin is needed because the current conda solve selected `kalpy 0.10.0`, which removed an API symbol that MFA `3.3.9` imports.

## Run

```cmd
C:\Users\Aryan\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe Tools\MfaPhoneticTester\evaluate_mfa_assets.py --quiet
```

Current labelled spell WAV result:

```text
Files: 86
Usable: 86
Exact: 86
Partial: 0
Zero: 0
Average coverage: 100.0%
```

CSV:

```text
Tools\MfaPhoneticTester\asset-eval-mfa-english-us-arpa-latest.csv
```
