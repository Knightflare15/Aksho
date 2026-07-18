# Vosk Voice Tester

Standalone Windows tester for the game's offline Vosk speech model.

It loads:

- `Assets/StreamingAssets/VoskModel`
- `Assets/Plugins/Vosk/lib/netstandard2.0/Vosk.dll`
- `Assets/Plugins/Vosk/libvosk.dll` and the native Windows dependency DLLs

Use it to test whether the current model can robustly recognize the game's spell
words, letters, and letter aliases without entering Unity play mode.

## Run from source

```powershell
dotnet run --project Tools\VoskVoiceTester\VoskVoiceTester.csproj
```

## Publish an exe

```powershell
.\Tools\VoskVoiceTester\publish.ps1
```

The exe is written to:

```text
Tools\VoskVoiceTester\publish\VoskVoiceTester.exe
```
