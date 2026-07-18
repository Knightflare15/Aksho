# Grammar RPG Code Map

This project has one player-facing game loop:

`MainMenu -> Town -> Route -> Gym -> next Town`

Teacher goals are optional overlays on that loop. They never choose the scene,
teleport the student, or lock normal progression.

## Start Here

| Change you want | Primary file | What it owns |
| --- | --- | --- |
| Menu buttons, login entry, new-game confirmation | `Assets/Scripts/UI/MainMenuController.cs` | Play, New Game, Login, Shop, Settings |
| Start, continue, retry, shop, menu return | `Assets/Scripts/World/WorldSessionManager.cs` | The persistent world session |
| Area completion, unlocks, checkpoints, save migration | `Assets/Scripts/World/GrammarWorldProgressService.cs` | Player world progress |
| Canonical town/route/gym order and generated learning content | `Assets/Scripts/World/NaturalGrammarProgression.cs` | Curriculum contract |
| Runtime template configuration and portals | `Assets/Scripts/World/GrammarWorldRuntimeBootstrap.cs` | One Town, Route, and Gym template scene |
| NPC authored data, dialogue, trainer encounter start | `Assets/Scripts/World/GrammarSceneController.cs` | Scene-local content |
| Grammar-aware fill-in-the-blank prompts | `Assets/Scripts/LanguageLearning/DialogueFillInBlankScaffold.cs` | Chooses the authored or concept-specific word to hide |
| Sentence jumbles and position feedback | `Assets/Scripts/LanguageLearning/DialogueSentenceJumble.cs` | Word banks, distractors, duplicate-safe evaluation |
| Transcript pronunciation and meanings | `Assets/Scripts/LanguageLearning/DialogueTranscriptWordInteractions.cs` | Tappable words and the swappable interaction provider |
| Hold/release speech combat | `Assets/Scripts/Combat/GrammarVoiceCombatController.cs` | Combat microphone state only |
| Grammar phrase interpretation and creature actions | `Assets/Scripts/Creatures/CreatureCombatController.cs` | What a valid phrase does |
| Combat word suggestions and use cases | `Assets/Scripts/Combat/CombatLanguageGuide.cs` | Valid verbs, categories, adjectives, and adverbs shown by the HUD |
| Repetition/diminishing returns | `Assets/Scripts/Combat/CombatWordVarietyTracker.cs` | Bounded per-encounter adjective, verb, and adverb history |
| Optional goal state and reward request | `Assets/Scripts/Curriculum/WorldGoalTracker.cs` | HUD-facing goal lifecycle |
| Coins and shop balance | `Assets/Scripts/Progression/WorldEconomyService.cs` | Persistent wallet |
| Shared runtime UI styling | `Assets/Scripts/UI/GameUiTheme.cs` | Colors, typography, panels, buttons, and canvas scaling |

## Normal Flow

1. `MainMenuController` calls `WorldSessionManager.Play()`.
2. `WorldSessionManager` either restores the saved area or starts Welcome
   Village.
3. `GrammarWorldRuntimeBootstrap` turns the relevant Town, Route, or Gym
   template into the requested curriculum area.
4. `GrammarSceneController` configures the scene's NPCs, portals, vocabulary,
   Buddy mode, and encounter context.
5. `GrammarWorldProgressService` records dialogue tasks and encounter outcomes.
   It unlocks the forward portal only when the current area's requirements are
   complete.
6. Clearing a Gym unlocks its grammar/vocabulary rewards and reveals the next
   Town. If that Gym matches the optional teacher goal, `WorldGoalTracker`
   requests the server-owned reward claim.

## Content Rules

- A Town completes after every required town dialogue task.
- A Route completes after its required dialogue tasks and encounter.
- A Gym completes after its checks and required encounter.
- Only Gym completion grants the region's persistent vocabulary and grammar
  pattern rewards.
- NPC lines with no `dialogueTaskId` are flavour or teaching. They cannot bypass
  required progress.
- Town has full Buddy help, Route has clue-only help, Gym and combat have none.
- Generated Town and Route lines carry a place cue, grammar topic, focus words,
  and optional jumble distractors. Authored focus words always win over a
  generic fill-in-the-blank fallback.
- Transcript and reply-choice words can be heard or inspected individually.
- Sentence jumbles use green for the correct position, blue for a real word in
  the wrong position, and grey for a distractor.
- Combat exposes the currently valid grammar vocabulary. Reusing the same
  adjective, verb, or adverb reduces effectiveness and raises resource costs
  until the encounter ends.

## What You Author In Unity

Use `Town`, `Route`, and `Gym` as reusable template scenes. Place the terrain,
roads, buildings, landmarks, NavMesh, ambience, spawn anchors, and visual
prefabs there. The runtime code supplies the curriculum-specific NPCs, portals,
and fallback objects when an authored prefab is missing.

For final production content, assign your prefabs in the scene generator and
catalogs rather than relying on cubes, capsules, generated labels, or generated
materials. The exact prefab, Animator, collider, NavMesh, microphone, and build
settings checklist is in `UNITY_PRODUCTION_SETUP.md`.

## Cloud Boundary

Unity never contains the Sarvam, Gemini, or Azure Speech secret. The client calls Firebase
Functions only after a student login. The callable implementation is in
`TeacherPortal/functions/src/index.ts`:

- `requestBuddyHelp`: optional Town/Route coaching.
- `transcribeBuddySpeech`: optional short-turn Buddy STT.
- `dispatchAnalysisJob`: server-side Azure pronunciation processing.
- `claimWorldGoalReward`: atomic, idempotent teacher-goal payout.

The teacher portal authors and displays goals in `TeacherPortal/src/App.tsx` and
persists them through `TeacherPortal/src/portalData.ts`. Public marketing,
pricing, login, and individual/educator/school subscription entry live in
`TeacherPortal/src/PublicWebsite.tsx`. Authentication and checkout policy are
isolated under `TeacherPortal/src/services/`; a real purchase is never claimed
unless a configured provider returns a validated destination.

## Service And Model Swaps

- `Assets/Scripts/Infrastructure/CurriculumProviderFactory.cs` selects the
  curriculum provider behind `ICurriculumProviderFactory`.
- `Assets/Scripts/Infrastructure/PronunciationAnalysisEndpointResolver.cs`
  owns pronunciation endpoint selection.
- `Assets/Scripts/Infrastructure/LocalSpeechModelPaths.cs` resolves local
  Vosk, Charsiu, and ZIPA paths from
  `Assets/Resources/LocalSpeechModelConfiguration.json`. A deployment can
  override the root with `THE_SCRIPT_MODEL_ROOT`; model binaries do not belong
  in compiled scripts.
- `DialogueTranscriptWordInteractions.cs` owns the replaceable word speech and
  meaning provider used by NPC transcripts and reply choices.
- The WavLM service entry point is `Server/WavLMWorker/app/main.py`; settings,
  request schemas, authentication, and local-file policy are split into
  `config.py`, `schemas.py`, `auth.py`, and `local_path_policy.py`.

## Teacher Website

`TeacherPortal/src/PublicWebsite.tsx` is the public route shell. Focused pieces
for checkout notices and the signed-in workspace live in `src/components/`.
Plan definitions, checkout validation, and provider adapters live in
`src/services/`. Configure hosted checkout destinations with the documented
`VITE_*` variables; webhook-based entitlement provisioning remains a server
deployment responsibility.

## Retired Systems

The normal menu and mobile controls deliberately do not expose sandbox runs,
spell slots, forging, projectiles, arena cycles, or run timers. Some legacy code
is kept only so old non-production assets can still open while content is being
migrated. Do not add new world features to those paths; add them to the world
services above.
