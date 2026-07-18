# The Script

The Script is a Unity 6 grammar-learning RPG. There is one continuous game world; weekly teacher goals are optional pacing guidelines layered over ordinary play.

## Player Flow

1. Start or continue in the saved town, route, or gym.
2. Complete every required town conversation with full Buddy support.
3. Complete route dialogue and encounter practice with clue-only Buddy support.
4. Complete gym dialogue and combat assessment without Buddy support.
5. Unlock the next grammar region.

The canonical curriculum order is Welcome, Alphabet, Vowels, Sentences, Nouns, Verbs, Articles, Pronouns, Plurals, Adjectives, and Prepositions.

Guests can play the complete world with local saves and on-device recognition. Login enables Gemini Buddy coaching, cloud pronunciation assessment, teacher-visible evidence, synced weekly goals, and verified goal rewards.

## Voice Combat

Hold the combat microphone action, say a grammar command, and release. The recognized transcript is routed to creature combat:

- `RAT` summons a rat.
- `A RAT` and `THE RAT` apply articles when unlocked.
- `BIG RAT` applies an adjective.
- `RAT BITES` selects or summons the rat and attacks.
- `BITE` commands the active creature.

Later regions unlock pronouns, adverbs, tense, prepositions, and conjunctions. AI never decides correctness or battle outcomes.

## Weekly Goals

A teacher may assign a checkpoint such as clearing Article Arcade Gym by a due date. The goal does not teleport, gate, or start a separate mode. Clearing the target gym triggers the authenticated `claimWorldGoalReward` Firebase function. Claims are idempotent and update the server wallet transactionally. Late completions are recorded without the coin bonus.

## Production Scenes

- `MainMenu`
- `Town`
- `Route`
- `Gym`
- `Shop`

Run `The Script > Validate Production Content` before a build. See [UNITY_PRODUCTION_SETUP.md](UNITY_PRODUCTION_SETUP.md) for prefab, animation, input, Firebase, and device requirements.

## Security

Gemini is called only by the authenticated Firebase `requestBuddyHelp` function. Set `GEMINI_API_KEY` in Firebase Functions Secret Manager. Never put provider secrets in Unity, the web frontend, `PlayerPrefs`, Android resources, or tracked environment files.
