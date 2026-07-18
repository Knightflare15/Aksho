# Unity Production Setup

This checklist covers the authored assets that code cannot manufacture at production quality.

## Build And Services

1. Enable only `MainMenu`, `Town`, `Route`, `Gym`, `Battle`, and `Shop` in Build Settings, with `MainMenu` first.
2. Run `The Script > Validate Strict Release Readiness` and resolve every error and warning. Signed Android releases enforce this automatically.
3. Configure the Firebase project ID and HTTPS Functions base URL on `MainMenuController`.
4. Revoke the Gemini key previously exposed in chat. Create a replacement and run `firebase functions:secrets:set GEMINI_API_KEY` from `TeacherPortal`.
5. Build and deploy Functions, Firestore rules, and the teacher portal.
6. Configure Android `RECORD_AUDIO` permission and iOS microphone and speech-recognition usage descriptions.

## Player Prefab

Required components:

- `CharacterController`
- `PlayerController`
- `PlayerHealth`
- `PlayerHurtbox`
- `GrammarVoiceCombatController` (runtime fallback is added automatically)
- `CreatureCombatController`
- `CreatureCombatRegistry` with `CreatureCombatCatalog_Main`
- `VoiceUnlockRecognizer`
- child `Animator`
- camera reference and summon origin

Player Animator parameters:

| Type | Parameter | Purpose |
| --- | --- | --- |
| Float | `MoveSpeed` | locomotion blend |
| Float | `MoveX` | lateral locomotion |
| Float | `MoveY` | forward locomotion |
| Bool | `Grounded` | grounded state |
| Trigger | `Jump` | jump takeoff |
| Trigger | `Land` | landing |
| Trigger | `attack` | optional command gesture |

Bind keyboard, controller, and mobile pointer-down/pointer-up to the same Attack action. The action must fire `started` while held and `canceled` on release.

## NPC Prefabs

Each NPC needs a visible model, collider, interaction trigger, `GrammarNpc`, dialogue interaction point, audio source, and Animator. Keep the interaction trigger separate from the solid body collider.

NPC Animator parameters:

| Type | Parameter | Purpose |
| --- | --- | --- |
| Float | `MoveSpeed` | idle/walk blend |
| Bool | `Talking` | looping talk state |
| Trigger | `Listen` | learner response pose |
| Trigger | `Success` | accepted response |
| Trigger | `Encourage` | retry feedback |

Assign at least one NPC prefab to every `ProceduralGrammarSceneGenerator.prefabSet.npcPrefabs` array.

## Creature And Enemy Prefabs

Summoned creatures need `SummonedCreatureActor`, model, Animator, collider/hurtbox, movement/attack implementation, and an AudioSource. Assign noun-specific prefabs in `CreatureCombatCatalog_Main`; the generated capsule is only a development fallback.

Enemies need `SpellTarget`/`EnemyTarget`, `CheeseEnemyAgent` or the intended brain, `NavMeshAgent`, body collider, combat hurtbox, attack hitbox, Animator, and AudioSource. Assign every enemy prefab in `EnemyCatalog_Main`.

Common combat Animator parameters:

| Type | Parameter | Purpose |
| --- | --- | --- |
| Float | `MoveSpeed` | locomotion |
| Bool | `Attacking` | active attack state |
| Trigger | `Move` | begin movement |
| Trigger | `Summon` | creature arrival |
| Trigger | `Command` | command acknowledgement |
| Trigger | `Attack` | standard attack |
| Trigger | `Defend` | defense action |
| Trigger | `Dodge` | dodge action |
| Trigger | `Hit` | damage reaction |
| Trigger | `Defeat` | knockout/death |
| Trigger | `Victory` | encounter victory |

Every `EnemyAttackDefinition.animationTrigger` must exactly match a trigger in that enemy Animator Controller.

## World Prefabs

Assign authored template, building, road, NPC, treasure, arena-prop, and wild-encounter prefabs to each grammar scene generator. Add portal trigger colliders, checkpoint transforms, encounter spawn points, NavMesh surfaces, and interaction anchors. Bake navigation separately in Town, Route, and Gym and verify every spawn point lands on the NavMesh.

Forward portals must require current-area completion. Backtracking portals must remain open. Town requires its dialogue tasks; Route and Gym require both dialogue tasks and their configured encounter.

## Device Acceptance Pass

1. Fresh install starts at Welcome Village without login.
2. Guest progress survives restart and no network records are created.
3. Login loads a goal without moving the player.
4. Town shows full Buddy support, Route gives clues only, and Gym exposes no Buddy action.
5. Hold-to-speak displays the transcript and executes valid creature commands.
6. Losing a battle resets only the encounter.
7. Clearing an assigned gym awards the configured coins once.
8. Reopening the scene, reconnecting, and pressing through duplicate callbacks never pays twice.
9. A late clear appears in the teacher portal with no bonus.
