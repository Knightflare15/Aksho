# Speech Pipeline Tester

Console wrapper for the game-like flow:

1. Vosk listens to one attempt using the game vocabulary.
2. The captured WAV from that same attempt is saved.
3. The WAV is passed to the current ZIPA_CR pronunciation tester.

Run:

```powershell
dotnet run --project Tools\SpeechPipelineTester\SpeechPipelineTester.csproj -c Release
```

Published executable:

```powershell
Tools\SpeechPipelineTester\publish\SpeechPipelineTester.exe
```

Useful switches:

```powershell
SpeechPipelineTester.exe --word CAT
SpeechPipelineTester.exe --word CAT --wav Assets\Audio\Pronunciations\Spells\CAT.wav
SpeechPipelineTester.exe --word CAT --trim-game-style
SpeechPipelineTester.exe --word CAT --phoneme-backend zipa
SpeechPipelineTester.exe --word CAT --phoneme-backend allosaurus --emit 0.9
SpeechPipelineTester.exe --word CAT --phoneme-backend wavlm
SpeechPipelineTester.exe --word CAT --phoneme-backend wavlm-base-plus-fr-it
SpeechPipelineTester.exe --word CAT --phoneme-backend wav2vec2
SpeechPipelineTester.exe --word CAT --phoneme-backend hubert
```

Default pronunciation handoff is full captured audio, no trim. Use `--trim-game-style` to send the same trimmed audio shape Unity currently attaches to pronunciation insight.

Default backend is `zipa`, using `anyspeech/zipa-large-crctc-ns-800k` with the int8 ONNX file. `allosaurus` is still available for comparison. `wavlm` uses `speech31/wavlm-large-english-phoneme`; `wavlm-base-plus-fr-it` uses `hugofara/wavlm-base-plus-phonemizer-fr-it`; `wav2vec2` uses `mostafaashahin/wav2vec2-base-timit-phoneme-arpa-39-v2`; `hubert` uses `addy88/hubert-base-timit-demo-colab`. The first ZIPA/HF backend run may download model files into the Hugging Face cache.
