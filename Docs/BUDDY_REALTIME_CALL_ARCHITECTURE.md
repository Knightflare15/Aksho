# Buddy realtime call architecture

Buddy has one conversational execution path: a LiveKit room with one learner and one dispatched
Buddy agent. The older `requestBuddyHelp` callable is no longer exported.

## Call lifecycle

1. Unity shows `Ringing Buddy` and calls `createBuddyVoiceSession`.
2. Firebase verifies learner identity, consent, App Check, capacity, and service tier.
3. During the ring, Firebase loads the trusted dialogue task, learner profile, compact learner
   summary, and allow-listed relationship tags.
4. Firebase signs that bounded context into the agent dispatch and returns a short-lived,
   room-scoped learner token.
5. Unity joins the room, publishes one open microphone track, and subscribes to Buddy audio.
6. The worker performs VAD, streaming STT, streaming LLM generation, and streaming TTS in one
   persistent agent session. The learner can interrupt Buddy naturally.
7. Wrong-answer, word-meaning, and task-completed UI events use the authenticated room data
   channel and the existing agent history. They never call a second model endpoint.
8. Hang-up disconnects media and calls `closeBuddyVoiceSession`; a verified LiveKit webhook also
   releases abandoned rooms.

## Context boundary

The dispatch includes the current task, language and explanation preferences, support band,
bounded strengths and needs, recurring error tags, and allow-listed harmless relationship tags.
It does not include raw attempt history, raw conversation history, names, contact details, or
LiveKit/provider secrets.

## Client policy

- Buddy UI code must use `BuddyRealtimeCallClient`.
- Firebase does not export a separate turn-based Buddy LLM callable.
- Gym checks never start Buddy.
- If realtime admission fails, the call ends visibly; the client does not silently switch to a
  second model conversation.
