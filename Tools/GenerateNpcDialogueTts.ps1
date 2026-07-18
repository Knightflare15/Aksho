param(
    [string]$Root = (Resolve-Path ".").Path,
    [string]$CoreOutputDir = "Assets/Resources/Audio/NpcDialogue",
    [string]$GeneratedOutputDir = "ContentSource/NpcDialogueAudio/Generated",
    [switch]$Force
)

$ErrorActionPreference = "Stop"

function Sanitize-ResourceName([string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) { return "" }
    return (($Value.Trim().ToCharArray() | ForEach-Object {
        if ([char]::IsLetterOrDigit($_) -or $_ -eq '-' -or $_ -eq '_') { $_ } else { '_' }
    }) -join "")
}

function Unescape-CSharpString([string]$Value) {
    if ($null -eq $Value) { return "" }
    return $Value.Replace('\"', '"').Replace('\n', "`n").Replace('\r', "`r").Replace('\t', "`t").Replace('\\', '\')
}

function Add-Dialogue([hashtable]$Map, [string]$Id, [string]$Text) {
    if ([string]::IsNullOrWhiteSpace($Id) -or [string]::IsNullOrWhiteSpace($Text)) { return }
    if (-not $Map.ContainsKey($Id)) {
        $Map[$Id] = $Text.Trim()
    }
}

$dialogues = @{}

$naturalPath = Join-Path $Root "Assets/Scripts/World/NaturalGrammarProgression.cs"
if (Test-Path $naturalPath) {
    $source = Get-Content $naturalPath -Raw
    $matches = [regex]::Matches(
        $source,
        'Task\("(?<id>[^"]+)",\s*GrammarConceptId\.[^,]+,\s*"(?<text>(?:\\.|[^"\\])*)"',
        [System.Text.RegularExpressions.RegexOptions]::Singleline)
    foreach ($match in $matches) {
        Add-Dialogue $dialogues $match.Groups["id"].Value (Unescape-CSharpString $match.Groups["text"].Value)
    }
}

$seedPath = Join-Path $Root "TeacherPortal/functions/src/seedDeterministicGameContent.ts"
if (Test-Path $seedPath) {
    $source = Get-Content $seedPath -Raw
    $matches = [regex]::Matches(
        $source,
        'task\("(?<id>[^"]+)",\s*"[^"]+",\s*"(?<text>(?:\\.|[^"\\])*)"',
        [System.Text.RegularExpressions.RegexOptions]::Singleline)
    foreach ($match in $matches) {
        Add-Dialogue $dialogues $match.Groups["id"].Value (Unescape-CSharpString $match.Groups["text"].Value)
    }
}

$generatedPath = Join-Path $Root "Assets/Resources/Grammar/generated-dialogue-tasks.json"
if (Test-Path $generatedPath) {
    $generated = Get-Content $generatedPath -Raw | ConvertFrom-Json
    foreach ($task in $generated.tasks) {
        Add-Dialogue $dialogues $task.id $task.npcLine
    }
}

Add-Dialogue $dialogues "topic-guide-hello" "Welcome. Listen to the noun, then use a verb to act."

$absoluteCoreOutput = Join-Path $Root $CoreOutputDir
$absoluteGeneratedOutput = Join-Path $Root $GeneratedOutputDir
New-Item -ItemType Directory -Force -Path $absoluteCoreOutput,$absoluteGeneratedOutput | Out-Null

Add-Type -AssemblyName System.Speech
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
$synth.Rate = -1
$synth.Volume = 100

$created = 0
$skipped = 0
foreach ($entry in $dialogues.GetEnumerator() | Sort-Object Name) {
    $fileName = "$(Sanitize-ResourceName $entry.Key).wav"
    # Generated exercise voices are optional source content. Keeping them out
    # of Resources prevents hundreds of megabytes being copied into every app
    # install; runtime device TTS is the fallback until downloadable packs ship.
    $output = if ($entry.Key.StartsWith("gen-", [System.StringComparison]::OrdinalIgnoreCase)) {
        $absoluteGeneratedOutput
    } else {
        $absoluteCoreOutput
    }
    $path = Join-Path $output $fileName
    if ((Test-Path $path) -and -not $Force) {
        $skipped++
        continue
    }

    $synth.SetOutputToWaveFile($path)
    $synth.Speak($entry.Value)
    $synth.SetOutputToNull()
    $created++
}

$synth.Dispose()

Write-Host "NPC dialogue TTS complete. Created/updated $created WAV file(s), skipped $skipped existing file(s), total dialogue ids $($dialogues.Count)."
