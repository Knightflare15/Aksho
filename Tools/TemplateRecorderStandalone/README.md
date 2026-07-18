# Template Recorder Standalone

This is a Windows-only helper app for recording handwriting templates without Unity.

## What it does

- `Add Template`: pick a letter and save one or more handwritten samples
- `Test Recognition`: draw any shape and see what the recognizer thinks it is
- `View / Edit`: browse saved templates, mark a prime template, or delete bad samples

## Data file

- The app reads and writes `templates.json`
- That file lives next to the EXE when you publish the app
- The JSON format matches the game's current template database shape
- New entries contain a `handwriting_sample_v1` record with timing, normalization, writer/session provenance, canvas/device metadata, and future review-label fields
- Raw WinForms coordinates are identified as top-left/Y-down; normalized coordinates are emitted in canonical bottom-left/Y-up form matching Unity
- Legacy `x`, `y`, and `strokeId` points remain present and are still the only fields passed to recognition
- Older `templates.json` files without rich records continue to load

## Designer handoff

1. Publish the app.
2. Zip the publish folder.
3. If you want your designer to start from your current library, include your current `templates.json` in that folder before zipping.
4. They run the EXE, record templates, then send the returned `templates.json` back.
5. Replace the game's existing template file with the returned one.

## Controls

- `Left click`: draw
- `Right click`: lift pen / start a new stroke
- Set a pseudonymous writer ID, session ID, coarse age band, and handedness before saving. Do not enter a person's name or birth date.

## Compatibility self-test

```powershell
dotnet run -- --self-test
```

This verifies rich JSON save/reload and confirms that the same entry still produces the legacy recognizer point cloud.

## Publish

Run:

```powershell
.\publish.ps1
```

That creates a Windows publish folder under `publish\win-x64\` and a zip next to it.
