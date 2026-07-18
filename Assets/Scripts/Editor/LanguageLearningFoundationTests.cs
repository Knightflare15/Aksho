#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public class LanguageLearningFoundationTests
{
    [Test]
    public void AnswerChecker_AcceptsExactAndAllowedVariants()
    {
        var checker = new AnswerChecker();
        DialogueLine line = BuildColorLine();

        AnswerCheckResult exact = checker.CheckAnswer(new AnswerCheckRequest
        {
            line = line,
            taskType = TaskType.WrittenAnswer,
            supportLevel = SupportLevel.LowSupport,
            learnerAnswer = "My dress is red.",
        });
        AnswerCheckResult allowed = checker.CheckAnswer(new AnswerCheckRequest
        {
            line = line,
            taskType = TaskType.SpokenAnswer,
            supportLevel = SupportLevel.MediumSupport,
            learnerAnswer = "it is red",
        });

        Assert.IsTrue(exact.isCorrect);
        Assert.AreEqual(1f, exact.score);
        Assert.IsTrue(allowed.isCorrect);
        Assert.AreEqual(1f, allowed.score);
    }

    [Test]
    public void AnswerChecker_AcceptsPartialAnswerOnlyWhenSupportAllowsIt()
    {
        var checker = new AnswerChecker();
        DialogueLine line = BuildColorLine();

        AnswerCheckResult easy = checker.CheckAnswer(new AnswerCheckRequest
        {
            line = line,
            taskType = TaskType.SpokenAnswer,
            supportLevel = SupportLevel.HighSupport,
            learnerAnswer = "red",
        });
        AnswerCheckResult hard = checker.CheckAnswer(new AnswerCheckRequest
        {
            line = line,
            taskType = TaskType.SpokenAnswer,
            supportLevel = SupportLevel.AudioOnly,
            learnerAnswer = "red",
        });

        Assert.IsTrue(easy.isCorrect);
        Assert.Less(easy.score, 1f);
        Assert.IsFalse(hard.isCorrect);
    }

    [Test]
    public void AnswerChecker_FillBlankAndJumbleAreDeterministic()
    {
        var checker = new AnswerChecker();
        DialogueLine line = BuildTownLine();

        AnswerCheckResult fillBlank = checker.CheckAnswer(new AnswerCheckRequest
        {
            line = line,
            taskType = TaskType.FillBlank,
            supportLevel = SupportLevel.HighSupport,
            learnerAnswer = "my",
        });
        AnswerCheckResult wrongOrder = checker.CheckAnswer(new AnswerCheckRequest
        {
            line = line,
            taskType = TaskType.SentenceJumble,
            supportLevel = SupportLevel.MediumSupport,
            learnerAnswer = "town is this my",
        });

        Assert.IsTrue(fillBlank.isCorrect);
        Assert.IsFalse(wrongOrder.isCorrect);
        Assert.AreEqual(AnswerErrorType.WrongWordOrder, wrongOrder.detectedErrorType);
    }

    [Test]
    public void SupportLevelManager_TransitionsFromNewToStrongAndBackToHighOnMistakes()
    {
        var manager = new SupportLevelManager();
        var progress = new ConceptProgress { conceptId = "possessive_my" };

        Assert.AreEqual(SupportLevel.HighSupport, manager.DetermineSupportLevel(progress));

        progress.attempts = 2;
        progress.correctAttempts = 1;
        progress.masteryScore = 0.25f;
        Assert.AreEqual(SupportLevel.MediumSupport, manager.DetermineSupportLevel(progress));

        progress.correctAttempts = 4;
        progress.masteryScore = 0.7f;
        Assert.AreEqual(SupportLevel.LowSupport, manager.DetermineSupportLevel(progress));

        progress.correctAttempts = 6;
        progress.masteryScore = 0.9f;
        Assert.AreEqual(SupportLevel.AudioOnly, manager.DetermineSupportLevel(progress));

        progress.consecutiveWrongAnswers = 2;
        Assert.AreEqual(SupportLevel.HighSupport, manager.DetermineSupportLevel(progress));
    }

    [Test]
    public void HintManager_ReturnsLayeredHintsAndLocalLanguageLast()
    {
        var manager = new HintManager();
        DialogueLine line = BuildTownLine();

        HintData first = manager.GetNextHint(line, 0);
        HintData second = manager.GetNextHint(line, 1);
        HintData third = manager.GetNextHint(line, 2);
        HintData local = manager.GetNextHint(line, 3);

        Assert.AreEqual("Listen to the word before town.", first.text);
        Assert.AreEqual("The missing word shows ownership.", second.text);
        Assert.AreEqual("Use 'my' when something belongs to you.", third.text);
        Assert.IsTrue(local.isLocalLanguageHint);
    }

    [Test]
    public void ProgressTracker_UpdatesMasteryAndWeakFlags()
    {
        var tracker = new ProgressTracker();
        DialogueLine line = BuildTownLine();

        ConceptProgress wrong = tracker.RecordAttempt(
            line,
            TaskType.FillBlank,
            SupportLevel.HighSupport,
            new AnswerCheckResult { isCorrect = false, feedbackMessage = "Wrong" },
            usedHint: false);
        tracker.RecordHintUsed(line.conceptId);
        ConceptProgress correct = tracker.RecordAttempt(
            line,
            TaskType.FillBlank,
            SupportLevel.HighSupport,
            new AnswerCheckResult { isCorrect = true, score = 1f },
            usedHint: true);

        Assert.AreEqual(2, correct.attempts);
        Assert.AreEqual(1, correct.correctAttempts);
        Assert.AreEqual(1, correct.wrongAttempts);
        Assert.Greater(correct.masteryScore, 0f);
        Assert.AreEqual(1, correct.hintUsageCount);
        Assert.IsFalse(wrong.isStrong);
    }

    [Test]
    public void DialogueManager_DemoFlowMovesSupportForwardAfterRecovery()
    {
        var go = new GameObject("LanguageLearningDialogueManagerTest");
        try
        {
            var manager = go.AddComponent<DialogueManager>();
            manager.ConfigureForTests(ContentDatabase.CreateRuntimeDefault());

            DialogueSessionState session = manager.StartInteraction("town_guard");
            Assert.NotNull(session);
            Assert.AreEqual(SupportLevel.HighSupport, session.presentation.supportLevel);
            Assert.AreEqual(TaskType.FullSentenceListen, session.presentation.taskType);

            DialogueTurnResult wrong = manager.SubmitAnswer("This is your town");
            Assert.IsFalse(wrong.answerResult.isCorrect);
            Assert.NotNull(wrong.hint);
            Assert.AreEqual("Listen to the word before town.", wrong.hint.text);

            session = manager.StartInteraction("town_guard");
            DialogueTurnResult correct = manager.SubmitAnswer("This is my town");
            Assert.IsTrue(correct.answerResult.isCorrect);
            Assert.AreEqual(SupportLevel.MediumSupport, correct.nextSupportLevel);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void DialogueManager_CanUseSpeechStubAndPersistenceStub()
    {
        var go = new GameObject("LanguageLearningServiceStubTest");
        try
        {
            var manager = go.AddComponent<DialogueManager>();
            var speech = go.AddComponent<ManualSpeechRecognitionServiceStub>();
            var persistence = go.AddComponent<PlayerPrefsLearnerProgressPersistence>();
            speech.nextTranscript = "This is my town";
            manager.speechRecognitionComponent = speech;
            manager.persistenceComponent = persistence;
            manager.ConfigureForTests(ContentDatabase.CreateRuntimeDefault());

            manager.StartInteraction("town_guard", "stub-learner");
            DialogueTurnResult result = manager.SubmitCapturedSpeechAnswer();
            manager.SaveProgress("stub-learner");

            Assert.IsTrue(result.answerResult.isCorrect);
            LearnerProgressSummary saved = persistence.Load("stub-learner");
            Assert.IsNotNull(saved);
            Assert.IsTrue(saved.concepts.Count > 0);
        }
        finally
        {
            PlayerPrefs.DeleteKey("language_learning_progress_stub-learner");
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void GrammarNpcLearningBridge_StartsAndCompletesLocalizedInteraction()
    {
        var go = new GameObject("GrammarNpcLearningBridgeTest");
        try
        {
            var npc = go.AddComponent<GrammarNpc>();
            npc.npcId = "bridge-npc";
            npc.displayName = "Bridge NPC";
            npc.dialogueLines = new System.Collections.Generic.List<LocalizedDialogueLine>
            {
                new LocalizedDialogueLine
                {
                    lineId = "bridge-line",
                    dialogueTaskId = "bridge-line",
                    npcLine = "This is my town.",
                    expectedEnglishResponse = "This is my town",
                    conceptId = GrammarConceptId.Articles,
                    localLanguageHint = "local hint",
                    inputMode = GrammarDialogueInputMode.WriteOnly,
                    malfunctionType = GrammarDialogueMalfunctionType.None,
                },
            };

            var interaction = go.AddComponent<NPCInteractionManager>();
            interaction.dialogueManager = go.AddComponent<DialogueManager>();
            interaction.dialogueManager.ConfigureForTests(ContentDatabase.CreateRuntimeDefault());

            var bridge = go.AddComponent<GrammarNpcLearningBridge>();
            bridge.grammarNpc = npc;
            bridge.interactionManager = interaction;
            bridge.BeginLearningInteraction();
            DialogueTurnResult result = bridge.SubmitAnswer("This is my town");

            Assert.IsTrue(result.answerResult.isCorrect);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void NPCInteractionManager_StartLocalizedDialogueInitializesRuntimeDatabase()
    {
        var go = new GameObject("NPCInteractionRuntimeDatabaseTest");
        try
        {
            var interaction = go.AddComponent<NPCInteractionManager>();
            interaction.dialogueManager = go.AddComponent<DialogueManager>();
            var line = new LocalizedDialogueLine
            {
                lineId = "runtime-localized-line",
                npcLine = "Welcome to town.",
                expectedEnglishResponse = "I understand",
                inputMode = GrammarDialogueInputMode.WriteOnly,
            };

            DialogueSessionState session = interaction.StartLocalizedDialogue("runtime-npc", "Runtime NPC", line);

            Assert.NotNull(session);
            Assert.AreEqual("runtime-npc", session.npcId);
            Assert.AreEqual("Welcome to town.", session.line.text);
            Assert.IsTrue(session.line.expectedAnswers.Contains("I understand"));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void NPCInteractionManager_MapsSpeakAndWriteToCombinedTaskType()
    {
        var go = new GameObject("NPCInteractionSpeakAndWriteTest");
        try
        {
            var interaction = go.AddComponent<NPCInteractionManager>();
            interaction.dialogueManager = go.AddComponent<DialogueManager>();
            var line = new LocalizedDialogueLine
            {
                lineId = "speak-write-line",
                npcLine = "A wild RAT appears. Say and write R.",
                expectedEnglishResponse = "R",
                inputMode = GrammarDialogueInputMode.SpeakAndWrite,
            };

            DialogueSessionState session = interaction.StartLocalizedDialogue("wild-letter", "Wild Letter Encounter", line);

            Assert.NotNull(session);
            CollectionAssert.Contains(session.line.taskTypes, TaskType.SpokenAndWrittenAnswer);
            AnswerCheckResult result = new AnswerChecker().CheckAnswer(new AnswerCheckRequest
            {
                line = session.line,
                taskType = TaskType.SpokenAndWrittenAnswer,
                supportLevel = SupportLevel.LowSupport,
                learnerAnswer = "R",
            });
            Assert.IsTrue(result.isCorrect);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void ContentDatabase_ReadinessFlagsUnanswerableDialogue()
    {
        ContentDatabase database = ScriptableObject.CreateInstance<ContentDatabase>();
        try
        {
            database.grammarConcepts.Add(new GrammarConcept { conceptId = "concept" });
            database.dialogueLines.Add(new DialogueLine
            {
                dialogueId = "bad-line",
                npcId = "npc",
                text = "Hello.",
                conceptId = "concept",
                taskTypes = new System.Collections.Generic.List<TaskType> { TaskType.WrittenAnswer },
            });
            var issues = new System.Collections.Generic.List<ContentValidationIssue>();

            database.ValidateReadiness(issues, productionStrict: true);

            Assert.IsTrue(issues.Exists(issue => issue.message.Contains("no accepted answer")));
        }
        finally
        {
            Object.DestroyImmediate(database);
        }
    }

    [Test]
    public void TacticalGrammarBattler_SummonAppliesAdjectiveStats()
    {
        var go = new GameObject("TacticalGrammarSummonTest");
        try
        {
            var registry = go.AddComponent<CreatureCombatRegistry>();
            registry.catalog = CreatureCombatCatalog.CreateRuntimeDefault();

            var battler = new TacticalGrammarBattler(registry);
            TacticalBattleCommandResult baseSummon = battler.SummonPlayer("rat", new TacticalBattlePosition(0, 2));
            TacticalBattleStats baseStats = battler.State.playerUnit.stats;

            TacticalBattleCommandResult bigSummon = battler.SummonPlayer("a big rat", new TacticalBattlePosition(0, 2));
            TacticalBattleStats bigStats = battler.State.playerUnit.stats;

            Assert.IsTrue(baseSummon.success);
            Assert.IsTrue(bigSummon.success);
            Assert.Greater(bigStats.maxHp, baseStats.maxHp);
            Assert.Greater(bigStats.attack, baseStats.attack);
            Assert.Less(bigStats.speed, baseStats.speed);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void TacticalGrammarBattler_RejectsInvalidSummon()
    {
        var go = new GameObject("TacticalGrammarInvalidSummonTest");
        try
        {
            var registry = go.AddComponent<CreatureCombatRegistry>();
            registry.catalog = CreatureCombatCatalog.CreateRuntimeDefault();

            var battler = new TacticalGrammarBattler(registry);
            TacticalBattleCommandResult result = battler.SummonPlayer("the runs", new TacticalBattlePosition(0, 0));

            Assert.IsFalse(result.success);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void TacticalGrammarBattler_CanMoveAroundHazard()
    {
        var go = new GameObject("TacticalGrammarMoveAroundTest");
        try
        {
            var registry = go.AddComponent<CreatureCombatRegistry>();
            registry.catalog = CreatureCombatCatalog.CreateRuntimeDefault();

            var battler = new TacticalGrammarBattler(registry);
            battler.SetTerrain(new TacticalBattlePosition(2, 2), TacticalBattleCellType.Spikes);
            battler.SetEnemyUnit("CAT", new TacticalBattlePosition(4, 2));
            battler.SummonPlayer("rat", new TacticalBattlePosition(0, 2));

            TacticalBattleCommandResult result = battler.ExecutePlayerCommand("the rat runs around the spikes");

            Assert.IsTrue(result.success);
            Assert.AreNotEqual(0, result.finalPosition.x);
            Assert.AreNotEqual(TacticalBattleCellType.Spikes, battler.State.GetCell(result.finalPosition));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void TacticalGrammarBattler_MovementUsesHexNeighborGrid()
    {
        var go = new GameObject("TacticalGrammarHexMovementTest");
        try
        {
            var registry = go.AddComponent<CreatureCombatRegistry>();
            registry.catalog = CreatureCombatCatalog.CreateRuntimeDefault();

            var start = new TacticalBattlePosition(0, 0);
            var enemy = new TacticalBattlePosition(4, 4);
            var walkBattler = new TacticalGrammarBattler(registry);
            walkBattler.SetEnemyUnit("CAT", enemy);
            walkBattler.SummonPlayer("rat", start);

            TacticalBattleCommandResult walk = walkBattler.ExecutePlayerCommand("the rat walks");

            Assert.IsTrue(walk.success);
            Assert.AreEqual(1, TestHexDistance(start, walk.finalPosition));
            Assert.Less(TestHexDistance(walk.finalPosition, enemy), TestHexDistance(start, enemy));
            Assert.AreEqual(1, walk.actionProfile.movementCells);
            Assert.AreEqual(CreatureVerbCategory.Movement, walk.actionProfile.category);

            var runBattler = new TacticalGrammarBattler(registry);
            runBattler.SetEnemyUnit("CAT", enemy);
            runBattler.SummonPlayer("rat", start);

            TacticalBattleCommandResult run = runBattler.ExecutePlayerCommand("the rat runs");

            Assert.IsTrue(run.success);
            Assert.GreaterOrEqual(TestHexDistance(start, run.finalPosition), 1);
            Assert.LessOrEqual(TestHexDistance(start, run.finalPosition), 2);
            Assert.Less(TestHexDistance(run.finalPosition, enemy), TestHexDistance(walk.finalPosition, enemy));
            Assert.AreEqual(2, run.actionProfile.movementCells);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void TacticalGrammarBattler_MovementDirectionsAreRelativeToFacing()
    {
        var go = new GameObject("TacticalGrammarRelativeMovementTest");
        try
        {
            var registry = go.AddComponent<CreatureCombatRegistry>();
            registry.catalog = CreatureCombatCatalog.CreateRuntimeDefault();
            var battler = new TacticalGrammarBattler(registry);
            var origin = new TacticalBattlePosition(2, 2);
            battler.SetEnemyUnit("CAT", new TacticalBattlePosition(4, 4));
            battler.SummonPlayer("rat", origin);
            battler.FacePlayerToward(new TacticalBattlePosition(4, 2));

            TacticalBattleCommandResult left = battler.ExecutePlayerCommand("the rat walks left");
            Assert.IsTrue(left.success, left.message);
            Assert.AreEqual(2, left.finalPosition.x);
            Assert.AreEqual(3, left.finalPosition.y);

            battler.State.playerUnit.position = origin;
            battler.State.playerUnit.currentPp = battler.State.playerUnit.stats.maxPp;
            TacticalBattleCommandResult backward = battler.ExecutePlayerCommand("the rat runs backward");
            Assert.IsTrue(backward.success, backward.message);
            Assert.AreEqual(0, backward.finalPosition.x);
            Assert.AreEqual(2, backward.finalPosition.y);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void TacticalGrammarBattler_ClickedHeadingPreviewsLinearMovementAndJumpClearsObstacle()
    {
        var go = new GameObject("TacticalGrammarMovementPreviewTest");
        try
        {
            var registry = go.AddComponent<CreatureCombatRegistry>();
            registry.catalog = CreatureCombatCatalog.CreateRuntimeDefault();
            var battler = new TacticalGrammarBattler(registry);
            battler.SetEnemyUnit("CAT", new TacticalBattlePosition(4, 4));
            battler.SummonPlayer("rat", new TacticalBattlePosition(0, 2));
            battler.SetTerrain(new TacticalBattlePosition(1, 2), TacticalBattleCellType.Rock);
            Assert.IsTrue(battler.TrySelectTacticalCell(new TacticalBattlePosition(4, 2), out string selectionError), selectionError);

            List<TacticalBattlePosition> runPreview = battler.GetPlayerMovementPreviewCells(2, "RUN");
            Assert.IsEmpty(runPreview, "A normal run must stop when the first cell is blocked.");

            List<TacticalBattlePosition> jumpPreview = battler.GetPlayerMovementPreviewCells(2, "JUMP");
            Assert.AreEqual(2, jumpPreview.Count);
            Assert.AreEqual(TacticalBattleCellType.Rock, battler.State.GetCell(jumpPreview[0]));
            Assert.AreEqual(2, jumpPreview[1].x);
            Assert.AreEqual(2, jumpPreview[1].y);

            TacticalBattleCommandResult jump = battler.ExecutePlayerCommand("the rat jumps over the rock");
            Assert.IsTrue(jump.success, jump.message);
            Assert.AreEqual(2, jump.finalPosition.x);
            Assert.AreEqual(2, jump.finalPosition.y);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void TacticalGrammarBattler_DefaultAttacksRequireAdjacentHexCell()
    {
        var go = new GameObject("TacticalGrammarAttackLineTest");
        try
        {
            var registry = go.AddComponent<CreatureCombatRegistry>();
            registry.catalog = CreatureCombatCatalog.CreateRuntimeDefault();

            var battler = new TacticalGrammarBattler(registry);
            battler.SetEnemyUnit("CAT", new TacticalBattlePosition(2, 0));
            battler.SummonPlayer("rat", new TacticalBattlePosition(2, 2));

            TacticalBattleCommandResult tooFar = battler.ExecutePlayerCommand("the rat attacks");
            Assert.IsTrue(tooFar.success);
            Assert.IsFalse(battler.CanPlayerAttackTarget(tooFar.actionProfile, out string rangeReason));
            StringAssert.Contains("reaches 1", rangeReason);

            battler.State.enemyUnit.position = new TacticalBattlePosition(2, 1);
            TacticalBattleCommandResult side = battler.ExecutePlayerCommand("the rat attacks");
            Assert.IsTrue(side.success);
            Assert.IsFalse(battler.CanPlayerAttackTarget(side.actionProfile, out string arcReason));
            StringAssert.Contains("arc", arcReason);

            battler.FacePlayerToward(battler.State.enemyUnit.position);
            TacticalBattleCommandResult adjacent = battler.ExecutePlayerCommand("the rat attacks");
            Assert.IsTrue(adjacent.success);
            Assert.IsTrue(battler.CanPlayerAttackTarget(adjacent.actionProfile, out string adjacentReason), adjacentReason);
            Assert.Greater(battler.ResolvePlayerAttackDamage(adjacent.actionProfile), 0);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void TacticalGrammarBattler_RangedVerbsReachFartherButHitSofter()
    {
        var go = new GameObject("TacticalGrammarRangedVerbTest");
        try
        {
            var registry = go.AddComponent<CreatureCombatRegistry>();
            registry.catalog = CreatureCombatCatalog.CreateRuntimeDefault();

            var battler = new TacticalGrammarBattler(registry);
            battler.SetEnemyUnit("CAT", new TacticalBattlePosition(0, 2));
            battler.SummonPlayer("fish", new TacticalBattlePosition(0, 0));

            TacticalBattleCommandResult splash = battler.ExecutePlayerCommand("the fish splashes");
            Assert.IsTrue(splash.success);
            Assert.AreEqual(CreatureVerbCategory.Attack, splash.actionProfile.category);
            Assert.AreEqual(2, splash.actionProfile.rangeCells);
            Assert.Less(splash.actionProfile.damageMultiplier, 1f);
            Assert.IsTrue(battler.CanPlayerAttackTarget(splash.actionProfile, out string clearReason), clearReason);

            battler.SetTerrain(new TacticalBattlePosition(0, 1), TacticalBattleCellType.Spikes);
            TacticalBattleCommandResult blocked = battler.ExecutePlayerCommand("the fish splashes");
            Assert.IsTrue(blocked.success);
            Assert.IsFalse(battler.CanPlayerAttackTarget(blocked.actionProfile, out string blockedReason));
            Assert.IsTrue(blockedReason.Contains("Spikes"));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void TacticalGrammarBattler_EnemyOnlyAttacksAdjacentAndRetreatsAfterContact()
    {
        var go = new GameObject("TacticalGrammarEnemyAdjacencyTest");
        try
        {
            var registry = go.AddComponent<CreatureCombatRegistry>();
            registry.catalog = CreatureCombatCatalog.CreateRuntimeDefault();

            var battler = new TacticalGrammarBattler(registry);
            battler.SetEnemyUnit("CAT", new TacticalBattlePosition(2, 2));
            battler.SummonPlayer("rat", new TacticalBattlePosition(0, 2));

            Assert.IsFalse(battler.CanEnemyAttackPlayer());
            Assert.IsTrue(battler.TryAdvanceEnemyTowardPlayer(1, out int advancedCells));
            Assert.AreEqual(1, advancedCells);
            Assert.IsTrue(battler.CanEnemyAttackPlayer());

            Assert.IsTrue(battler.TryRetreatEnemyFromPlayer(out int retreatedCells));
            Assert.GreaterOrEqual(retreatedCells, 1);
            Assert.LessOrEqual(retreatedCells, TacticalGrammarBattler.EnemyRetreatCells);
            Assert.IsFalse(battler.CanEnemyAttackPlayer());
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void TacticalGrammarBattler_UtilityVerbBoostsStatsInsteadOfDamage()
    {
        var go = new GameObject("TacticalGrammarUtilityVerbTest");
        try
        {
            var registry = go.AddComponent<CreatureCombatRegistry>();
            registry.catalog = CreatureCombatCatalog.CreateRuntimeDefault();

            var battler = new TacticalGrammarBattler(registry);
            battler.SetEnemyUnit("CAT", new TacticalBattlePosition(2, 0));
            battler.SummonPlayer("rat", new TacticalBattlePosition(0, 0));
            int baseSpeed = battler.State.playerUnit.stats.speed;

            TacticalBattleCommandResult result = battler.ExecutePlayerCommand("the rat looks");

            Assert.IsTrue(result.success);
            Assert.AreEqual(CreatureVerbCategory.Utility, result.actionProfile.category);
            Assert.Greater(battler.State.playerUnit.stats.speed, baseSpeed);
            Assert.IsFalse(battler.CanPlayerAttackTarget(result.actionProfile, out _));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void TacticalGrammarBattler_DefenseVerbsCreateShieldAndDuration()
    {
        var go = new GameObject("TacticalGrammarDefenseShieldTest");
        try
        {
            var registry = go.AddComponent<CreatureCombatRegistry>();
            registry.catalog = CreatureCombatCatalog.CreateRuntimeDefault();

            var battler = new TacticalGrammarBattler(registry);
            battler.SetEnemyUnit("CAT", new TacticalBattlePosition(0, 1));
            battler.SummonPlayer("rat", new TacticalBattlePosition(0, 0));

            TacticalBattleCommandResult block = battler.ExecutePlayerCommand("the rat blocks");
            battler.State.playerUnit.currentPp = battler.State.playerUnit.stats.maxPp;
            TacticalBattleCommandResult slowBlock = battler.ExecutePlayerCommand("the rat blocks slowly");

            Assert.IsTrue(block.success);
            Assert.IsTrue(slowBlock.success);
            Assert.AreEqual(CreatureVerbCategory.Defense, block.actionProfile.category);
            Assert.Greater(block.actionProfile.shieldAmount, 0);
            Assert.AreEqual(TacticalGrammarBattler.BaseShieldDurationSeconds, block.actionProfile.shieldDurationSeconds);
            Assert.GreaterOrEqual(slowBlock.actionProfile.shieldAmount, block.actionProfile.shieldAmount);
            Assert.Greater(slowBlock.actionProfile.shieldDurationSeconds, block.actionProfile.shieldDurationSeconds);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void TacticalGrammarBattler_DodgeUsesMovementRangeAndAdverbSpeed()
    {
        var go = new GameObject("TacticalGrammarDodgeSpeedTest");
        try
        {
            var registry = go.AddComponent<CreatureCombatRegistry>();
            registry.catalog = CreatureCombatCatalog.CreateRuntimeDefault();

            var battler = new TacticalGrammarBattler(registry);
            battler.SetEnemyUnit("CAT", new TacticalBattlePosition(3, 2));
            battler.SummonPlayer("rat", new TacticalBattlePosition(2, 2));

            TacticalBattleCommandResult dodge = battler.ExecutePlayerCommand("the rat dodges");
            battler.State.playerUnit.position = new TacticalBattlePosition(2, 2);
            battler.State.playerUnit.currentPp = battler.State.playerUnit.stats.maxPp;
            TacticalBattleCommandResult fastDodge = battler.ExecutePlayerCommand("the rat dodges fast");

            Assert.IsTrue(dodge.success);
            Assert.IsTrue(fastDodge.success);
            Assert.AreEqual(1, dodge.actionProfile.movementCells);
            Assert.Greater(fastDodge.actionProfile.actionSpeed, dodge.actionProfile.actionSpeed);
            Assert.Greater(fastDodge.actionProfile.ppCost, dodge.actionProfile.ppCost);
            Assert.Greater(TestHexDistance(dodge.finalPosition, battler.State.enemyUnit.position), TestHexDistance(new TacticalBattlePosition(2, 2), battler.State.enemyUnit.position));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void TacticalGrammarBattler_AdverbModifiesActionProfile()
    {
        var go = new GameObject("TacticalGrammarActionProfileTest");
        try
        {
            var registry = go.AddComponent<CreatureCombatRegistry>();
            registry.catalog = CreatureCombatCatalog.CreateRuntimeDefault();

            var battler = new TacticalGrammarBattler(registry);
            battler.SummonPlayer("rat", new TacticalBattlePosition(0, 2));

            TacticalBattleCommandResult normal = battler.ExecutePlayerCommand("the rat runs");
            battler.State.playerUnit.currentPp = battler.State.playerUnit.stats.maxPp;
            TacticalBattleCommandResult fast = battler.ExecutePlayerCommand("the rat runs fast");

            Assert.IsTrue(normal.success);
            Assert.IsTrue(fast.success);
            Assert.Greater(fast.actionProfile.speedScore, normal.actionProfile.speedScore);
            Assert.Greater(fast.actionProfile.ppCost, normal.actionProfile.ppCost);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void TacticalGrammarBattler_CooldownPreventsVerbSpamWhenTimed()
    {
        var go = new GameObject("TacticalGrammarCooldownTest");
        try
        {
            var registry = go.AddComponent<CreatureCombatRegistry>();
            registry.catalog = CreatureCombatCatalog.CreateRuntimeDefault();

            var battler = new TacticalGrammarBattler(registry);
            battler.SetEnemyUnit("CAT", new TacticalBattlePosition(0, 1));
            battler.SummonPlayer("rat", new TacticalBattlePosition(0, 0));

            TacticalBattleCommandResult first = battler.ExecutePlayerCommand("the rat attacks", 10f);
            TacticalBattleCommandResult spam = battler.ExecutePlayerCommand("the rat attacks", 10.1f);
            battler.State.playerUnit.currentPp = battler.State.playerUnit.stats.maxPp;
            TacticalBattleCommandResult recovered = battler.ExecutePlayerCommand("the rat attacks", 10.1f + first.actionProfile.cooldownSeconds);

            Assert.IsTrue(first.success);
            Assert.Greater(first.actionProfile.cooldownSeconds, 0f);
            Assert.IsFalse(spam.success);
            StringAssert.Contains("recovering", spam.message);
            Assert.IsTrue(recovered.success);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void TacticalGrammarBattler_PrepositionsRequireSpatialSemantics()
    {
        var go = new GameObject("TacticalGrammarPrepositionSemanticsTest");
        try
        {
            var registry = go.AddComponent<CreatureCombatRegistry>();
            registry.catalog = CreatureCombatCatalog.CreateRuntimeDefault();

            var battler = new TacticalGrammarBattler(registry);
            battler.SetTerrain(new TacticalBattlePosition(2, 2), TacticalBattleCellType.Rock);
            battler.SetTerrain(new TacticalBattlePosition(2, 0), TacticalBattleCellType.Wall);
            battler.SetEnemyUnit("CAT", new TacticalBattlePosition(4, 4));
            battler.SummonPlayer("rat", new TacticalBattlePosition(0, 0));

            TacticalBattleCommandResult beside = battler.ExecutePlayerCommand("the rat runs beside the rock");
            Assert.IsTrue(beside.success);
            Assert.AreEqual(1, TestHexDistance(beside.finalPosition, new TacticalBattlePosition(2, 2)));

            battler.State.playerUnit.currentPp = battler.State.playerUnit.stats.maxPp;
            TacticalBattleCommandResult near = battler.ExecutePlayerCommand("the rat runs near the rock");
            int nearDistance = TestHexDistance(near.finalPosition, new TacticalBattlePosition(2, 2));
            Assert.IsTrue(near.success);
            Assert.GreaterOrEqual(nearDistance, 1);
            Assert.LessOrEqual(nearDistance, 2);

            battler.State.playerUnit.position = new TacticalBattlePosition(0, 0);
            battler.State.playerUnit.currentPp = battler.State.playerUnit.stats.maxPp;
            TacticalBattleCommandResult impossibleOver = battler.ExecutePlayerCommand("the rat runs over the wall");
            Assert.IsFalse(impossibleOver.success);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void TacticalGrammarBattleDemoRunner_ExecutesWithoutThrowing()
    {
        var go = new GameObject("TacticalGrammarBattleDemoRunnerTest");
        try
        {
            var registry = go.AddComponent<CreatureCombatRegistry>();
            registry.catalog = CreatureCombatCatalog.CreateRuntimeDefault();
            var runner = go.AddComponent<TacticalGrammarBattleDemoRunner>();
            runner.creatureCombatRegistry = registry;

            Assert.DoesNotThrow(() => runner.RunDemo());
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    static DialogueLine BuildTownLine()
    {
        return ContentDatabase.CreateRuntimeDefault().GetDialoguesForNpc("town_guard")[0];
    }

    static DialogueLine BuildColorLine()
    {
        return ContentDatabase.CreateRuntimeDefault().GetDialoguesForNpc("tailor")[0];
    }

    static int TestHexDistance(TacticalBattlePosition a, TacticalBattlePosition b)
    {
        TestHexCube ca = ToTestHexCube(a);
        TestHexCube cb = ToTestHexCube(b);
        return Mathf.Max(Mathf.Abs(ca.x - cb.x), Mathf.Abs(ca.y - cb.y), Mathf.Abs(ca.z - cb.z));
    }

    static TestHexCube ToTestHexCube(TacticalBattlePosition position)
    {
        int x = position.x - (position.y - (position.y & 1)) / 2;
        int z = position.y;
        int y = -x - z;
        return new TestHexCube(x, y, z);
    }

    readonly struct TestHexCube
    {
        public readonly int x;
        public readonly int y;
        public readonly int z;

        public TestHexCube(int x, int y, int z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }
    }
}
#endif
