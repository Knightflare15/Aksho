#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class FeedbackAndSpeechTests
{
    [Test]
    public void NormalizeKeyword_RemovesPunctuationAndNormalizesCase()
    {
        Assert.AreEqual("STONE", VoiceUnlockRecognizer.NormalizeKeyword("  stone! "));
        Assert.AreEqual("RED FOX", VoiceUnlockRecognizer.NormalizeKeyword("red---fox"));
    }

    [Test]
    public void FakeSpeechProvider_EmitsAlternativesOnce()
    {
        var provider = new FakeSpeechRecognitionProvider();
        SpeechRecognitionResult received = default;
        provider.ResultReceived += value => received = value;
        provider.Start(new SpeechRecognitionRequest(new[] { "RAT" }, "en-US"));
        provider.Submit("cat", "rat");

        Assert.IsFalse(provider.IsListening);
        CollectionAssert.AreEqual(new[] { "cat", "rat" }, received.Alternatives);
    }

    [Test]
    public void PronunciationProfile_GroupsDigraphsIntoSingleSounds()
    {
        IReadOnlyList<PhoneticSoundSegment> segments = PronunciationProfileBuilder.BuildSegments("SHIP");

        Assert.AreEqual("SH", segments[0].Spelling);
        Assert.AreEqual("sh", segments[0].FriendlySound);
        Assert.AreEqual("I", segments[1].Spelling);
        Assert.AreEqual("P", segments[2].Spelling);
    }

    [Test]
    public void PhoneticDisplayState_FormatsLastSuccessfulCast()
    {
        var go = new GameObject("PhoneticDisplayStateTest");
        try
        {
            var state = go.AddComponent<PhoneticDisplayState>();
            state.RecordSuccessfulCast("ship", false);

            Assert.AreEqual(PhoneticDisplaySource.SpellCast, state.Source);
            Assert.AreEqual("SHIP", state.TargetText);
            Assert.AreEqual("SH", state.Segments[0].Spelling);
            StringAssert.Contains("Last cast: SHIP", state.BuildHudText());
            StringAssert.Contains("Expected: sh / i / p", state.BuildHudText());
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void PhoneticDisplayState_DoesNotGradeUnavailablePronunciationInsight()
    {
        var go = new GameObject("PhoneticDisplayStateUnavailableInsightTest");
        try
        {
            var state = go.AddComponent<PhoneticDisplayState>();
            IReadOnlyList<PhoneticSoundSegment> segments = PronunciationProfileBuilder.BuildSegments("CAT");
            var insight = new PronunciationInsightResult(
                "Server pronunciation review",
                "CAT",
                "CAT",
                "cat",
                true,
                false,
                0f,
                PronunciationHintKey.TryAgain,
                segments[0],
                segments,
                PronunciationProfileBuilder.BuildSyllableBeats("CAT", segments),
                "No captured Vosk attempt audio was available for phoneme recognition.");

            state.RecordSuccessfulCast("cat", false, insight);
            string hudText = state.BuildHudText();

            StringAssert.Contains("Successful cast", hudText);
            StringAssert.Contains("Heard: cat", hudText);
            Assert.IsFalse(hudText.Contains("Word accepted"));
            Assert.IsFalse(hudText.Contains("Pronunciation 0%"));
            Assert.IsFalse(hudText.Contains("Practice:"));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void PhoneticDisplayState_FeedbackVisibilityExpiresAfterTwoSeconds()
    {
        var go = new GameObject("PhoneticDisplayStateVisibilityTest");
        try
        {
            var state = go.AddComponent<PhoneticDisplayState>();
            state.RecordSuccessfulCast("ship", false);

            Assert.IsTrue(state.IsFeedbackVisible(state.LastUpdatedAt + 1.9f));
            Assert.IsFalse(state.IsFeedbackVisible(state.LastUpdatedAt + 2.01f));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void PronunciationInsight_FindsMissingFirstSound()
    {
        var provider = new LightweightWav2Vec2PronunciationInsightProvider();
        PronunciationInsightResult insight = provider.Analyze(new PronunciationInsightRequest(
            "CAT",
            "",
            "at",
            false,
            new byte[16000],
            16000));

        Assert.AreEqual(PronunciationHintKey.TryFirstSound, insight.HintKey);
        Assert.AreEqual("C", insight.FocusSegment.Spelling);
        Assert.AreEqual(PhoneticSegmentStatus.Missing, insight.Segments[0].Status);
    }

    [Test]
    public void PronunciationInsight_AcceptedAliasStillFlagsInitialSound()
    {
        var provider = new LightweightWav2Vec2PronunciationInsightProvider();
        PronunciationInsightResult insight = provider.Analyze(new PronunciationInsightRequest(
            "RABBIT",
            "RABBIT",
            "wabbit",
            true,
            new byte[32000],
            16000));

        Assert.IsTrue(insight.VoskConfirmedWord);
        Assert.AreEqual(PronunciationHintKey.TryFirstSound, insight.HintKey);
        Assert.AreEqual("R", insight.FocusSegment.Spelling);
        Assert.AreEqual("wa", insight.FocusSegment.HeardSound);
        Assert.AreEqual(PhoneticSegmentStatus.Missing, insight.Segments[0].Status);
    }

    [Test]
    public void PronunciationInsightFactory_DefaultsToVoskOnlyProvider()
    {
        IPronunciationInsightProvider provider = PronunciationInsightProviderFactory.Create();

        Assert.IsInstanceOf<VoskOnlyPronunciationInsightProvider>(provider);
        Assert.AreEqual("Vosk-only pronunciation gate", provider.Name);
        Assert.IsFalse(provider.IsAvailable);
    }

    [Test]
    public void MobileZipaProvider_FallsBackWhenNativeRunnerUnavailable()
    {
        var provider = new MobileZipaOnnxPronunciationInsightProvider();
        PronunciationInsightResult insight = provider.Analyze(new PronunciationInsightRequest(
            "CAT",
            "",
            "at",
            false,
            new byte[16000],
            16000));

        Assert.AreEqual("CAT", insight.TargetWord);
        Assert.AreEqual(PronunciationHintKey.TryFirstSound, insight.HintKey);
        StringAssert.Contains("lightweight pronunciation fallback", insight.Message);
    }

    [Test]
    public void FeedbackPriority_RejectsGuidanceDuringSuccess()
    {
        var go = new GameObject("FeedbackTest");
        try
        {
            var manager = go.AddComponent<FeedbackManager>();
            MethodInfo beginCue = typeof(FeedbackManager).GetMethod("BeginCue", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(beginCue);
            Assert.IsTrue((bool)beginCue.Invoke(manager, new object[] { new FeedbackRequest(FeedbackCue.Success), 1f }));
            Assert.IsFalse((bool)beginCue.Invoke(manager, new object[] { new FeedbackRequest(FeedbackCue.Guidance), 0.2f }));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void AccessibilitySettings_ClampShakeIntensity()
    {
        float original = AccessibilitySettings.ShakeIntensity;
        try
        {
            AccessibilitySettings.ShakeIntensity = 2f;
            Assert.AreEqual(1f, AccessibilitySettings.ShakeIntensity);
            AccessibilitySettings.ShakeIntensity = -1f;
            Assert.AreEqual(0f, AccessibilitySettings.ShakeIntensity);
        }
        finally
        {
            AccessibilitySettings.ShakeIntensity = original;
        }
    }

    [Test]
    public void SpellbookSlot_LetterAndSpecialPagesKeepDistinctContracts()
    {
        var slot = new SpellbookSlot();
        slot.FillLetter('c', 5);
        Assert.AreEqual(SpellbookPageType.Letter, slot.pageType);
        Assert.AreEqual("C", slot.pageLetter);
        Assert.AreEqual(5, slot.currentAmmo);
        Assert.IsNull(slot.spellDefinition);

        var definition = new SpellDefinition { word = "CAT" };
        slot.Clear();
        slot.FillSpecial(definition, 2);
        Assert.AreEqual(SpellbookPageType.SpecialWord, slot.pageType);
        Assert.AreEqual("CAT", slot.spellWord);
        Assert.AreEqual(2, slot.currentAmmo);
    }

    [Test]
    public void WordActionHandler_RoutesCreaturePhrasesOnlyWhenSpellbookSlotIsEmpty()
    {
        GrammarSceneController[] ambientSceneControllers = Object.FindObjectsByType<GrammarSceneController>(
            FindObjectsInactive.Exclude);
        var ambientControllerStates = new bool[ambientSceneControllers.Length];
        for (int i = 0; i < ambientSceneControllers.Length; i++)
        {
            ambientControllerStates[i] = ambientSceneControllers[i] != null && ambientSceneControllers[i].enabled;
            if (ambientSceneControllers[i] != null)
                ambientSceneControllers[i].enabled = false;
        }

        var go = new GameObject("WordActionCreatureRoutingTest");
        try
        {
            var registry = go.AddComponent<CreatureCombatRegistry>();
            registry.catalog = CreatureCombatCatalog.CreateRuntimeDefault();
            var handler = go.AddComponent<WordActionHandler>();
            var combat = go.GetComponent<CreatureCombatController>();
            Assert.IsNotNull(combat);
            Assert.AreSame(registry, combat.registry);

            InvokeCastWordRecognized(handler, "rat");
            Assert.IsNotNull(combat.ActiveCreature);

            combat.ClearActiveCreature();
            handler.SelectedSlot.FillLetter('r', 1);
            InvokeCastWordRecognized(handler, "rat");
            Assert.IsNull(combat.ActiveCreature);
        }
        finally
        {
            Object.DestroyImmediate(go);
            for (int i = 0; i < ambientSceneControllers.Length; i++)
            {
                if (ambientSceneControllers[i] != null)
                    ambientSceneControllers[i].enabled = ambientControllerStates[i];
            }
        }
    }

    [Test]
    public void LetterProgression_DefaultsUseMotorPatternOrderAndSpeechAliases()
    {
        List<LetterProgressionCatalog.LetterEntry> entries = LetterProgressionCatalog.BuildDefaults();
        Assert.AreEqual("L", entries[0].letter);
        Assert.AreEqual("C", entries[7].letter);
        CollectionAssert.Contains(entries[7].speechAliases, "SEE");
        CollectionAssert.Contains(entries[7].speechAliases, "SEA");
    }

    [Test]
    public void LetterFormation_LargerLIsStillOnTrack()
    {
        var result = LetterFormationCoach.AnalyzeForTests('L', Strokes(
            new Vector2(-180f, 180f),
            new Vector2(-180f, -180f),
            new Vector2(170f, -180f)));

        Assert.AreEqual(LetterFormationCoach.FormationState.OnTrack, result.state);
    }

    [Test]
    public void LetterFormation_RotatedLIsStillOnTrack()
    {
        var result = LetterFormationCoach.AnalyzeForTests('L', Strokes(
            Rotate(new Vector2(0f, 160f), 18f),
            Rotate(new Vector2(0f, 0f), 18f),
            Rotate(new Vector2(140f, 0f), 18f)));

        Assert.AreEqual(LetterFormationCoach.FormationState.OnTrack, result.state);
    }

    [Test]
    public void LetterFormation_LMissingFootProducesDiagnostic()
    {
        var result = LetterFormationCoach.AnalyzeForTests('L', Strokes(
            new Vector2(0f, 180f),
            new Vector2(0f, 80f),
            new Vector2(0f, -80f)));

        Assert.AreEqual(LetterFormationCoach.DiagnosticTag.MissingFoot, result.diagnostic);
    }

    [Test]
    public void LetterFormation_LWarnsForSidewaysFirstMovement()
    {
        var result = LetterFormationCoach.AnalyzeForTests('L', Strokes(
            new Vector2(-120f, 120f),
            new Vector2(-40f, 116f),
            new Vector2(60f, 112f)));

        Assert.AreEqual(LetterFormationCoach.FormationState.NeedsHelp, result.state);
        Assert.AreEqual(LetterFormationCoach.DiagnosticTag.WrongStart, result.diagnostic);
    }

    [Test]
    public void LetterFormation_OpenCIsOnTrack()
    {
        var result = LetterFormationCoach.AnalyzeForTests('C', Strokes(
            new Vector2(90f, 70f),
            new Vector2(20f, 110f),
            new Vector2(-80f, 70f),
            new Vector2(-105f, 0f),
            new Vector2(-78f, -72f),
            new Vector2(24f, -106f),
            new Vector2(88f, -68f)));

        Assert.AreEqual(LetterFormationCoach.FormationState.OnTrack, result.state);
    }

    [Test]
    public void LetterFormation_CReversedCurveUsesSoftParity()
    {
        var result = LetterFormationCoach.AnalyzeForTests('C', Strokes(
            new Vector2(-82f, -70f),
            new Vector2(-104f, 0f),
            new Vector2(-76f, 72f),
            new Vector2(24f, 106f),
            new Vector2(88f, 70f)));

        Assert.AreNotEqual(LetterFormationCoach.FormationState.NeedsHelp, result.state);
        Assert.AreNotEqual(LetterFormationCoach.DiagnosticTag.WrongStart, result.diagnostic);
    }

    [Test]
    public void LetterFormation_CSnakeBackWarnsInsteadOfPraise()
    {
        var result = LetterFormationCoach.AnalyzeForTests('C', Strokes(
            new Vector2(92f, 76f),
            new Vector2(30f, 110f),
            new Vector2(-68f, 76f),
            new Vector2(-102f, 8f),
            new Vector2(-32f, -38f),
            new Vector2(74f, -54f),
            new Vector2(96f, -92f),
            new Vector2(12f, -126f),
            new Vector2(-74f, -92f)));

        Assert.AreEqual(LetterFormationCoach.FormationState.NeedsHelp, result.state);
        Assert.AreEqual(LetterFormationCoach.DiagnosticTag.WrongDirection, result.diagnostic);
    }

    [Test]
    public void LetterFormation_ClosedCWarnsAgainstO()
    {
        var result = LetterFormationCoach.AnalyzeForTests('C', Strokes(
            new Vector2(0f, 100f),
            new Vector2(-80f, 70f),
            new Vector2(-105f, 0f),
            new Vector2(-80f, -70f),
            new Vector2(0f, -100f),
            new Vector2(80f, -70f),
            new Vector2(105f, 0f),
            new Vector2(80f, 70f),
            new Vector2(0f, 100f)));

        Assert.AreEqual(LetterFormationCoach.DiagnosticTag.ClosingTooSoon, result.diagnostic);
    }

    [Test]
    public void LetterFormation_ScribbledCNeedsHelpInsteadOfPraise()
    {
        var result = LetterFormationCoach.AnalyzeForTests('C', Strokes(
            new Vector2(-90f, 55f),
            new Vector2(-20f, 110f),
            new Vector2(65f, 62f),
            new Vector2(18f, -28f),
            new Vector2(-74f, -84f),
            new Vector2(-105f, 12f),
            new Vector2(-12f, 92f),
            new Vector2(95f, 36f),
            new Vector2(70f, -72f),
            new Vector2(-35f, -112f),
            new Vector2(-92f, -18f),
            new Vector2(0f, 75f),
            new Vector2(88f, 8f),
            new Vector2(18f, -92f)));

        Assert.AreEqual(LetterFormationCoach.FormationState.NeedsHelp, result.state);
        Assert.AreEqual(LetterFormationCoach.DiagnosticTag.Overdrawn, result.diagnostic);
    }

    [Test]
    public void LetterFormation_UnfinishedOAsksForClosure()
    {
        var result = LetterFormationCoach.AnalyzeForTests('O', Strokes(
            new Vector2(0f, 100f),
            new Vector2(-80f, 70f),
            new Vector2(-105f, 0f),
            new Vector2(-80f, -70f),
            new Vector2(0f, -100f),
            new Vector2(75f, -70f)));

        Assert.AreEqual(LetterFormationCoach.DiagnosticTag.OpenLoop, result.diagnostic);
    }

    [Test]
    public void LetterFormation_ScribbledONeedsHelpInsteadOfPraise()
    {
        var result = LetterFormationCoach.AnalyzeForTests('O', Strokes(
            new Vector2(0f, 100f),
            new Vector2(-84f, 72f),
            new Vector2(-104f, -8f),
            new Vector2(-38f, -94f),
            new Vector2(64f, -72f),
            new Vector2(104f, 16f),
            new Vector2(38f, 98f),
            new Vector2(-68f, 68f),
            new Vector2(-96f, -28f),
            new Vector2(-16f, -108f),
            new Vector2(88f, -42f),
            new Vector2(72f, 64f),
            new Vector2(-18f, 92f)));

        Assert.AreEqual(LetterFormationCoach.FormationState.NeedsHelp, result.state);
        Assert.AreEqual(LetterFormationCoach.DiagnosticTag.Overdrawn, result.diagnostic);
    }

    [Test]
    public void LetterFormation_DoubleLoopONeedsHelp()
    {
        var result = LetterFormationCoach.AnalyzeForTests('O', Strokes(
            new Vector2(0f, 100f),
            new Vector2(-80f, 70f),
            new Vector2(-105f, 0f),
            new Vector2(-80f, -70f),
            new Vector2(0f, -100f),
            new Vector2(80f, -70f),
            new Vector2(105f, 0f),
            new Vector2(80f, 70f),
            new Vector2(0f, 100f),
            new Vector2(-80f, 70f),
            new Vector2(-105f, 0f),
            new Vector2(-80f, -70f),
            new Vector2(0f, -100f),
            new Vector2(80f, -70f),
            new Vector2(105f, 0f),
            new Vector2(80f, 70f),
            new Vector2(0f, 100f)));

        Assert.AreEqual(LetterFormationCoach.FormationState.NeedsHelp, result.state);
        Assert.AreEqual(LetterFormationCoach.DiagnosticTag.Overdrawn, result.diagnostic);
    }

    [Test]
    public void TemplateFormationCompiler_LBuildsVerticalAndHorizontalLines()
    {
        TemplateFormationSpec spec = TemplateFormationCompiler.Compile('L', null, new Vector2(220f, 220f));

        Assert.GreaterOrEqual(spec.primitives.Count, 2);
        Assert.AreEqual(FormationPrimitiveKind.Line, spec.primitives[0].kind);
        Assert.AreEqual(FormationAxis.Vertical, spec.primitives[0].axis);
        Assert.AreEqual(FormationPrimitiveKind.Line, spec.primitives[1].kind);
        Assert.AreEqual(FormationAxis.Horizontal, spec.primitives[1].axis);
    }

    [Test]
    public void TemplateFormationCompiler_CBuildsOneOpenCurve()
    {
        TemplateFormationSpec spec = TemplateFormationCompiler.Compile('C', null, new Vector2(220f, 220f));

        Assert.AreEqual(1, spec.primitives.Count);
        Assert.AreEqual(FormationPrimitiveKind.Curve, spec.primitives[0].kind);
        Assert.IsFalse(spec.primitives[0].closed);
    }

    [Test]
    public void TemplateFormationCompiler_OBuildsOneClosedLoop()
    {
        TemplateFormationSpec spec = TemplateFormationCompiler.Compile('O', null, new Vector2(220f, 220f));

        Assert.AreEqual(1, spec.primitives.Count);
        Assert.AreEqual(FormationPrimitiveKind.Loop, spec.primitives[0].kind);
        Assert.IsTrue(spec.primitives[0].closed);
    }

    [Test]
    public void TemplateFormationCompiler_MultiStrokeLettersKeepUsefulPrimitiveOrder()
    {
        TemplateFormationSpec a = TemplateFormationCompiler.Compile('A', null, new Vector2(220f, 220f));
        TemplateFormationSpec t = TemplateFormationCompiler.Compile('T', null, new Vector2(220f, 220f));
        TemplateFormationSpec h = TemplateFormationCompiler.Compile('H', null, new Vector2(220f, 220f));
        TemplateFormationSpec x = TemplateFormationCompiler.Compile('X', null, new Vector2(220f, 220f));

        Assert.GreaterOrEqual(a.primitives.Count, 3);
        Assert.GreaterOrEqual(t.primitives.Count, 2);
        Assert.GreaterOrEqual(h.primitives.Count, 3);
        Assert.GreaterOrEqual(x.primitives.Count, 2);
    }

    [Test]
    public void HandwritingDiagnostics_SmoothCurveDoesNotTriggerWobble()
    {
        HandwritingDiagnosticSummary summary = AnalyzeDiagnostic('C', PageTemplateStrokes('C'));

        Assert.IsFalse(summary.HasTag(HandwritingDiagnosticTag.Wobbly));
        Assert.Less(summary.localKinkScore, 0.2f);
        Assert.AreEqual(0, summary.localKinkCount);
    }

    [Test]
    public void HandwritingDiagnostics_JaggedCurveTriggersTemplateRelativeWobble()
    {
        HandwritingDiagnosticSummary summary = AnalyzeDiagnostic('C', JitterStrokes(PageTemplateStrokes('C'), 22f));

        Assert.IsTrue(summary.HasTag(HandwritingDiagnosticTag.Wobbly));
        Assert.GreaterOrEqual(summary.localKinkCount, 2);
        Assert.Greater(summary.wobbleScore, summary.wobbleThresholdUsed);
    }

    [Test]
    public void HandwritingDiagnostics_SmoothCorrectionStrokeDoesNotTriggerWobble()
    {
        List<List<Vector2>> strokes = PageTemplateStrokes('C');
        strokes.AddRange(TranslateStrokes(PageTemplateStrokes('C'), new Vector2(6f, 0f)));

        HandwritingDiagnosticSummary summary = AnalyzeDiagnostic('C', strokes);

        Assert.IsTrue(summary.HasTag(HandwritingDiagnosticTag.RepeatedCorrection));
        Assert.IsFalse(summary.HasTag(HandwritingDiagnosticTag.Wobbly));
    }

    [Test]
    public void LetterFormation_VerticalLineDirectionUsesSoftParity()
    {
        var result = LetterFormationCoach.AnalyzeForTests('L', Strokes(
            new Vector2(0f, -120f),
            new Vector2(0f, 120f)));

        Assert.AreNotEqual(LetterFormationCoach.FormationState.NeedsHelp, result.state);
        Assert.AreNotEqual(LetterFormationCoach.DiagnosticTag.WrongDirection, result.diagnostic);
    }

    [Test]
    public void LetterFormation_HorizontalLineDirectionUsesSoftParity()
    {
        var result = LetterFormationCoach.AnalyzeForTests('T', Strokes(
            new Vector2(120f, 90f),
            new Vector2(-120f, 90f)));

        Assert.AreNotEqual(LetterFormationCoach.FormationState.NeedsHelp, result.state);
        Assert.AreNotEqual(LetterFormationCoach.DiagnosticTag.WrongDirection, result.diagnostic);
    }

    [Test]
    public void LetterFormation_PreviewCanShowAndVanish()
    {
        var go = new GameObject("FormationCoachPreviewTest", typeof(RectTransform));
        try
        {
            var panel = go.GetComponent<RectTransform>();
            panel.sizeDelta = new Vector2(400f, 400f);
            var coach = new LetterFormationCoach(panel, null, new Vector2(220f, 220f));
            coach.BeginLetter('C');

            Assert.IsFalse(coach.IsOverlayVisible);
            coach.ShowAnimatedDemo(0.5f);
            Assert.IsTrue(coach.IsOverlayVisible);
            coach.HideVisual();
            Assert.IsFalse(coach.IsOverlayVisible);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void LetterFormation_HelpEscalatesFromDemoToTrace()
    {
        var go = new GameObject("FormationCoachHelpTest", typeof(RectTransform));
        try
        {
            var panel = go.GetComponent<RectTransform>();
            panel.sizeDelta = new Vector2(400f, 400f);
            var coach = new LetterFormationCoach(panel, null, new Vector2(220f, 220f));
            coach.BeginLetter('L');

            LetterFormationCoach.FormationResult first = coach.ShowHelp(1);
            Assert.AreEqual(LetterFormationCoach.FormationState.NeedsNudge, first.state);

            LetterFormationCoach.FormationResult second = coach.ShowHelp(2);
            Assert.AreEqual(LetterFormationCoach.FormationState.NeedsHelp, second.state);
            Assert.IsTrue(coach.IsOverlayVisible);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void SpellPillar_DefenseStateOnlyCompletesExplicitly()
    {
        var go = new GameObject("PillarStateTest");
        try
        {
            var pillar = go.AddComponent<SpellPillarObjective>();
            Assert.AreEqual(SpellPillarObjective.PillarState.Dormant, pillar.State);
            Assert.IsTrue(pillar.BeginDefense());
            Assert.AreEqual(SpellPillarObjective.PillarState.Defending, pillar.State);
            Assert.IsFalse(pillar.IsActivated);

            pillar.CompleteDefense();
            Assert.AreEqual(SpellPillarObjective.PillarState.Completed, pillar.State);
            Assert.IsTrue(pillar.IsActivated);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void PillarDefense_NoSpawnedEnemiesCompletesInsteadOfResetting()
    {
        MethodInfo method = typeof(EnemyWaveDirector).GetMethod(
            "ShouldCompleteEmptyTrackedEncounter",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        Assert.IsTrue((bool)method.Invoke(null, new object[] { EncounterType.PillarDefense, 0, 0 }));
        Assert.IsFalse((bool)method.Invoke(null, new object[] { EncounterType.PillarDefense, 1, 0 }));
        Assert.IsFalse((bool)method.Invoke(null, new object[] { EncounterType.Pressure, 0, 0 }));
        Assert.IsTrue((bool)method.Invoke(null, new object[] { EncounterType.PillarDefense, 1, 1 }));
    }

    [Test]
    public void EnemyAttackCoordinator_AllowsOneAttackerByDefault()
    {
        var go = new GameObject("EnemyAttackCoordinatorTest");
        var agents = new List<CheeseEnemyAgent>();
        try
        {
            var coordinator = go.AddComponent<EnemyAttackCoordinator>();
            coordinator.attackGapSeconds = 0f;
            CheeseEnemyAgent first = CreateTestEnemyAgent("FirstCoordinatorEnemy", agents);
            CheeseEnemyAgent second = CreateTestEnemyAgent("SecondCoordinatorEnemy", agents);
            coordinator.RegisterAgent(first);
            coordinator.RegisterAgent(second);

            Assert.IsTrue(coordinator.TryRequestAttack(first));
            Assert.IsFalse(coordinator.TryRequestAttack(second));
            Assert.AreEqual(1, coordinator.ActiveAttackersCount);
        }
        finally
        {
            DestroyTestEnemies(agents);
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void EnemyAttackCoordinator_ReleaseLetsAnotherEnemyAttack()
    {
        var go = new GameObject("EnemyAttackCoordinatorReleaseTest");
        var agents = new List<CheeseEnemyAgent>();
        try
        {
            var coordinator = go.AddComponent<EnemyAttackCoordinator>();
            coordinator.attackGapSeconds = 0f;
            CheeseEnemyAgent first = CreateTestEnemyAgent("FirstReleaseEnemy", agents);
            CheeseEnemyAgent second = CreateTestEnemyAgent("SecondReleaseEnemy", agents);
            coordinator.RegisterAgent(first);
            coordinator.RegisterAgent(second);

            Assert.IsTrue(coordinator.TryRequestAttack(first));
            coordinator.ReleaseAttack(first);

            Assert.IsTrue(coordinator.TryRequestAttack(second));
            Assert.AreEqual(1, coordinator.ActiveAttackersCount);
        }
        finally
        {
            DestroyTestEnemies(agents);
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void EnemyAttackCoordinator_LargeGroupsAllowCrowdPressure()
    {
        var go = new GameObject("EnemyAttackCoordinatorCrowdTest");
        var agents = new List<CheeseEnemyAgent>();
        try
        {
            var coordinator = go.AddComponent<EnemyAttackCoordinator>();
            coordinator.attackGapSeconds = 0f;
            for (int i = 0; i < 5; i++)
            {
                CheeseEnemyAgent agent = CreateTestEnemyAgent($"CrowdEnemy{i}", agents);
                coordinator.RegisterAgent(agent);
            }

            Assert.IsTrue(coordinator.TryRequestAttack(agents[0]));
            Assert.IsTrue(coordinator.TryRequestAttack(agents[1]));
            Assert.IsFalse(coordinator.TryRequestAttack(agents[2]));
            Assert.AreEqual(2, coordinator.ActiveAttackersCount);
        }
        finally
        {
            DestroyTestEnemies(agents);
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void EnemyAttackCoordinator_UnregisteringAttackerReleasesTicket()
    {
        var go = new GameObject("EnemyAttackCoordinatorUnregisterTest");
        var agents = new List<CheeseEnemyAgent>();
        try
        {
            var coordinator = go.AddComponent<EnemyAttackCoordinator>();
            coordinator.attackGapSeconds = 0f;
            CheeseEnemyAgent first = CreateTestEnemyAgent("FirstUnregisterEnemy", agents);
            CheeseEnemyAgent second = CreateTestEnemyAgent("SecondUnregisterEnemy", agents);
            coordinator.RegisterAgent(first);
            coordinator.RegisterAgent(second);

            Assert.IsTrue(coordinator.TryRequestAttack(first));
            coordinator.UnregisterAgent(first);

            Assert.IsTrue(coordinator.TryRequestAttack(second));
            Assert.AreEqual(1, coordinator.ActiveAttackersCount);
        }
        finally
        {
            DestroyTestEnemies(agents);
            Object.DestroyImmediate(go);
        }
    }

    static List<List<Vector2>> Strokes(params Vector2[] points)
    {
        return new List<List<Vector2>> { new List<Vector2>(points) };
    }

    static CheeseEnemyAgent CreateTestEnemyAgent(string name, List<CheeseEnemyAgent> agents)
    {
        var go = new GameObject(name);
        var agent = go.AddComponent<CheeseEnemyAgent>();
        agents.Add(agent);
        return agent;
    }

    static void DestroyTestEnemies(List<CheeseEnemyAgent> agents)
    {
        foreach (CheeseEnemyAgent agent in agents)
        {
            if (agent != null)
                Object.DestroyImmediate(agent.gameObject);
        }
    }

    static HandwritingDiagnosticSummary AnalyzeDiagnostic(char letter, List<List<Vector2>> strokes)
    {
        var go = new GameObject("HandwritingDiagnosticPanel", typeof(RectTransform));
        try
        {
            RectTransform panel = go.GetComponent<RectTransform>();
            panel.sizeDelta = new Vector2(600f, 400f);
            var formation = new LetterFormationCoach.FormationResult
            {
                state = LetterFormationCoach.FormationState.OnTrack,
                diagnostic = LetterFormationCoach.DiagnosticTag.None,
                confidence = 1f
            };

            return HandwritingDiagnosticAnalyzer.Analyze(
                letter,
                letter.ToString(),
                0,
                strokes,
                panel,
                null,
                formation,
                true);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    static List<List<Vector2>> PageTemplateStrokes(char letter)
    {
        var panelRect = new Rect(-300f, -200f, 600f, 400f);
        Rect frame = NotebookWritingGuide.CalculateTemplateFrame(panelRect, letter.ToString(), 0);
        TemplateFormationSpec spec = TemplateFormationCompiler.Compile(letter, null, frame.size);
        var result = new List<List<Vector2>>();
        foreach (List<Vector2> stroke in spec.guideStrokes)
        {
            var translated = new List<Vector2>();
            foreach (Vector2 point in stroke)
                translated.Add(point + frame.center);
            result.Add(translated);
        }

        return result;
    }

    static List<List<Vector2>> JitterStrokes(List<List<Vector2>> source, float amount)
    {
        var result = new List<List<Vector2>>();
        foreach (List<Vector2> stroke in source)
        {
            var jittered = new List<Vector2>();
            for (int i = 0; i < stroke.Count; i++)
            {
                if (i == 0 || i == stroke.Count - 1)
                {
                    jittered.Add(stroke[i]);
                    continue;
                }

                Vector2 tangent = stroke[Mathf.Min(stroke.Count - 1, i + 1)] - stroke[Mathf.Max(0, i - 1)];
                if (tangent.sqrMagnitude < 0.001f)
                {
                    jittered.Add(stroke[i]);
                    continue;
                }

                tangent.Normalize();
                Vector2 normal = new Vector2(-tangent.y, tangent.x);
                jittered.Add(stroke[i] + normal * (i % 2 == 0 ? amount : -amount));
            }

            result.Add(jittered);
        }

        return result;
    }

    static List<List<Vector2>> TranslateStrokes(List<List<Vector2>> source, Vector2 offset)
    {
        var result = new List<List<Vector2>>();
        foreach (List<Vector2> stroke in source)
        {
            var translated = new List<Vector2>();
            foreach (Vector2 point in stroke)
                translated.Add(point + offset);
            result.Add(translated);
        }

        return result;
    }

    static Vector2 Rotate(Vector2 point, float degrees)
    {
        float radians = degrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(radians);
        float sin = Mathf.Sin(radians);
        return new Vector2(
            point.x * cos - point.y * sin,
            point.x * sin + point.y * cos);
    }

    [Test]
    public void WaveDescriptor_DefaultsToPressureEncounter()
    {
        var descriptor = new WaveDescriptor();
        Assert.AreEqual(EncounterType.Pressure, descriptor.encounterType);
    }

    [Test]
    public void SpellPillar_TintsOnlyNamedStatusMaterialSlot()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Material rock = null;
        Material status = null;
        try
        {
            Renderer renderer = go.GetComponent<Renderer>();
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Assert.NotNull(shader);

            rock = new Material(shader) { name = "Rock" };
            status = new Material(shader) { name = "Material.001" };
            renderer.sharedMaterials = new[] { rock, status };

            var pillar = go.AddComponent<SpellPillarObjective>();
            pillar.visualRenderers = new[] { renderer };
            pillar.statusMaterialName = "material.001";
            Assert.IsTrue(pillar.BeginDefense());

            var rockProperties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(rockProperties, 0);
            Assert.IsTrue(rockProperties.isEmpty);

            var statusProperties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(statusProperties, 1);
            Assert.AreEqual(pillar.defendingColor, statusProperties.GetColor("_BaseColor"));
            Assert.AreEqual(
                pillar.defendingColor * pillar.defendingEmissionIntensity,
                statusProperties.GetColor("_EmissionColor"));
            Assert.AreSame(rock, renderer.sharedMaterials[0]);
            Assert.AreSame(status, renderer.sharedMaterials[1]);
        }
        finally
        {
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(rock);
            Object.DestroyImmediate(status);
        }
    }

    [Test]
    public void GeneratedSpellPillar_TintsFallbackBody()
    {
        SpellPillarObjective pillar = SpellPillarObjective.Create(Vector3.zero, "CAT");
        try
        {
            Renderer renderer = pillar.GetComponentInChildren<Renderer>();
            Assert.NotNull(renderer);
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties, 0);
            Assert.AreEqual(pillar.dormantColor, properties.GetColor("_BaseColor"));
        }
        finally
        {
            Object.DestroyImmediate(pillar.gameObject);
        }
    }

    [Test]
    public void ContentValidation_RejectsDuplicateSpellsAndUnknownWeaknesses()
    {
        var spells = new List<SpellDefinition>
        {
            new SpellDefinition { word = "CAT", projectileSpeed = 10f },
            new SpellDefinition { word = "cat", projectileSpeed = 10f },
        };
        var enemies = new List<EnemyDefinition>
        {
            new EnemyDefinition { enemyId = "unknown", weaknessSpell = "ZZZ", maxHp = 3 },
        };

        List<ContentValidationIssue> issues = ContentValidation.Validate(spells, enemies);

        Assert.IsTrue(issues.Exists(issue =>
            issue.severity == ContentValidationSeverity.Error && issue.message.Contains("duplicated")));
        Assert.IsTrue(issues.Exists(issue =>
            issue.severity == ContentValidationSeverity.Error && issue.message.Contains("not registered")));
    }

    [Test]
    public void ContentValidation_AcceptsCreatureNounFamilyWeaknessWithoutPrefab()
    {
        var spells = new List<SpellDefinition>
        {
            new SpellDefinition { word = "CAT", projectileSpeed = 10f },
        };
        var enemies = new List<EnemyDefinition>
        {
            new EnemyDefinition { enemyId = "rat_family", weaknessSpell = "RAT", creatureFamilyNoun = "RAT", maxHp = 3 },
        };

        List<ContentValidationIssue> issues = ContentValidation.Validate(spells, enemies);

        Assert.IsFalse(issues.Exists(issue =>
            issue.severity == ContentValidationSeverity.Error && issue.message.Contains("not registered")));
        Assert.IsTrue(issues.Exists(issue =>
            issue.severity == ContentValidationSeverity.Warning && issue.message.Contains("placeholder cube")));
    }

    [Test]
    public void SpellTarget_AdditionalWeaknessUsesStrongHitDamage()
    {
        var go = new GameObject("SpellTargetAdditionalWeaknessTest");
        try
        {
            var target = go.AddComponent<SpellTarget>();
            target.requiredSpell = "OWL";
            target.additionalAcceptedSpells = new List<string> { "CAT" };
            target.maxHp = 3;
            target.hitsToDefeat = 1;

            Assert.AreEqual(3, target.GetDamageForSpell("CAT"));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void CreatureCombatRegistry_ParsesNounsVerbsAndModifiers()
    {
        var go = new GameObject("CreatureCombatRegistryParseTest");
        try
        {
            var registry = go.AddComponent<CreatureCombatRegistry>();
            registry.catalog = CreatureCombatCatalog.CreateRuntimeDefault();

            Assert.IsTrue(registry.TryParsePhrase("big rat", out CreaturePhraseParseResult nounPhrase));
            Assert.AreEqual(CreaturePhraseKind.NounSummon, nounPhrase.kind);
            Assert.AreEqual("RAT", nounPhrase.noun.canonicalNoun);
            Assert.AreEqual("BIG", nounPhrase.modifier.modifier);

            Assert.IsTrue(registry.TryParsePhrase("a rat", out CreaturePhraseParseResult articleRat));
            Assert.AreEqual(CreaturePhraseKind.NounSummon, articleRat.kind);
            Assert.AreEqual(GrammarPhrasePattern.DeterminerNoun, articleRat.pattern);
            Assert.AreEqual("A RAT", articleRat.canonicalText);

            Assert.IsTrue(registry.TryParsePhrase("an owl", out CreaturePhraseParseResult articleOwl));
            Assert.AreEqual(GrammarPhrasePattern.DeterminerNoun, articleOwl.pattern);
            Assert.AreEqual("AN OWL", articleOwl.canonicalText);

            Assert.IsTrue(registry.TryParsePhrase("the cat", out CreaturePhraseParseResult articleCat));
            Assert.AreEqual(GrammarPhrasePattern.DeterminerNoun, articleCat.pattern);
            Assert.AreEqual("THE CAT", articleCat.canonicalText);
            Assert.IsFalse(registry.TryParsePhrase("an rat", out _));

            Assert.IsTrue(registry.TryParsePhrase("a big rat", out CreaturePhraseParseResult articleAdjectiveRat));
            Assert.AreEqual(CreaturePhraseKind.NounSummon, articleAdjectiveRat.kind);
            Assert.AreEqual(GrammarPhrasePattern.DeterminerAdjectiveNoun, articleAdjectiveRat.pattern);
            Assert.AreEqual("BIG", articleAdjectiveRat.modifier.modifier);
            Assert.AreEqual("A BIG RAT", articleAdjectiveRat.canonicalText);

            Assert.IsTrue(registry.TryParsePhrase("the small cat", out CreaturePhraseParseResult articleAdjectiveCat));
            Assert.AreEqual(GrammarPhrasePattern.DeterminerAdjectiveNoun, articleAdjectiveCat.pattern);
            Assert.AreEqual("THE SMALL CAT", articleAdjectiveCat.canonicalText);
            Assert.IsFalse(registry.TryParsePhrase("an big owl", out _));

            Assert.IsTrue(registry.TryParsePhrase("run fast", out CreaturePhraseParseResult verbPhrase));
            Assert.AreEqual(CreaturePhraseKind.VerbCommand, verbPhrase.kind);
            Assert.AreEqual("RUN", verbPhrase.verb.verb);
            Assert.AreEqual("FAST", verbPhrase.modifier.modifier);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void CreatureCombatRegistry_ParsesSimplePresentBattleSentences()
    {
        var go = new GameObject("CreatureCombatRegistryPresentParseTest");
        try
        {
            var registry = go.AddComponent<CreatureCombatRegistry>();
            registry.catalog = CreatureCombatCatalog.CreateRuntimeDefault();

            Assert.IsTrue(registry.TryParsePhrase("rat bites", out CreaturePhraseParseResult ratBites));
            Assert.AreEqual(CreaturePhraseKind.VerbCommand, ratBites.kind);
            Assert.AreEqual(GrammarPhrasePattern.NounVerbPresent, ratBites.pattern);
            Assert.AreEqual("RAT", ratBites.noun.canonicalNoun);
            Assert.AreEqual("BITE", ratBites.verb.verb);

            Assert.IsTrue(registry.TryParsePhrase("I bite", out CreaturePhraseParseResult iBite));
            Assert.AreEqual(CreaturePhraseKind.VerbCommand, iBite.kind);
            Assert.AreEqual(GrammarPhrasePattern.PronounVerbPresent, iBite.pattern);
            Assert.IsNull(iBite.noun);
            Assert.AreEqual("BITE", iBite.verb.verb);

            Assert.IsTrue(registry.TryParsePhrase("he bites", out CreaturePhraseParseResult heBites));
            Assert.AreEqual(CreaturePhraseKind.VerbCommand, heBites.kind);
            Assert.AreEqual("BITE", heBites.verb.verb);

            Assert.IsTrue(registry.TryParsePhrase("they bite", out CreaturePhraseParseResult theyBite));
            Assert.AreEqual(CreaturePhraseKind.VerbCommand, theyBite.kind);
            Assert.AreEqual("BITE", theyBite.verb.verb);

            Assert.IsTrue(registry.TryParsePhrase("cats jump", out CreaturePhraseParseResult catsJump));
            Assert.AreEqual(CreaturePhraseKind.VerbCommand, catsJump.kind);
            Assert.AreEqual("CAT", catsJump.noun.canonicalNoun);
            Assert.AreEqual("JUMP", catsJump.verb.verb);

            Assert.IsFalse(registry.TryParsePhrase("he bite", out _));

            Assert.IsTrue(registry.TryParsePhrase("rat bit", out CreaturePhraseParseResult ratBit));
            Assert.AreEqual(GrammarPhrasePattern.PastTense, ratBit.pattern);
            Assert.AreEqual("BITE", ratBit.verb.verb);

            Assert.IsTrue(registry.TryParsePhrase("rat is biting", out CreaturePhraseParseResult ratIsBiting));
            Assert.AreEqual(GrammarPhrasePattern.ProgressiveTense, ratIsBiting.pattern);
            Assert.AreEqual("BITE", ratIsBiting.verb.verb);

            Assert.IsTrue(registry.TryParsePhrase("they are running", out CreaturePhraseParseResult theyAreRunning));
            Assert.AreEqual(GrammarPhrasePattern.ProgressiveTense, theyAreRunning.pattern);
            Assert.AreEqual("RUN", theyAreRunning.verb.verb);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void CreatureCombatRegistry_EnforcesContextualNounVerbPools()
    {
        var go = new GameObject("CreatureCombatRegistryContextualVerbTest");
        try
        {
            var registry = go.AddComponent<CreatureCombatRegistry>();
            registry.catalog = CreatureCombatCatalog.CreateRuntimeDefault();

            Assert.IsTrue(registry.TryParsePhrase("bird flies", out CreaturePhraseParseResult birdFlies));
            Assert.AreEqual("BIRD", birdFlies.noun.canonicalNoun);
            Assert.AreEqual("FLY", birdFlies.verb.verb);
            Assert.AreEqual(BattleActionRole.Dodge, birdFlies.verb.role);

            Assert.IsTrue(registry.TryParsePhrase("bird pecks", out CreaturePhraseParseResult birdPecks));
            Assert.AreEqual("PECK", birdPecks.verb.verb);
            Assert.AreEqual(BattleActionRole.Offense, birdPecks.verb.role);

            Assert.IsTrue(registry.TryParsePhrase("fish swims", out CreaturePhraseParseResult fishSwims));
            Assert.AreEqual("SWIM", fishSwims.verb.verb);
            Assert.AreEqual(BattleActionRole.Defense, fishSwims.verb.role);

            Assert.IsTrue(registry.TryParsePhrase("boat drifts", out CreaturePhraseParseResult boatDrifts));
            Assert.AreEqual("DRIFT", boatDrifts.verb.verb);

            Assert.IsTrue(registry.TryParsePhrase("apple drops", out CreaturePhraseParseResult appleDrops));
            Assert.AreEqual("DROP", appleDrops.verb.verb);

            Assert.IsFalse(registry.TryParsePhrase("rat flies", out _));
            Assert.IsFalse(registry.TryParsePhrase("rat pecks", out _));
            Assert.IsFalse(registry.TryParsePhrase("rat swims", out _));
            Assert.IsFalse(registry.TryParsePhrase("apple flies", out _));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void CreatureCombatCatalog_RuntimeDefaultNounsCoverRequiredVerbCategories()
    {
        CreatureCombatCatalog catalog = CreatureCombatCatalog.CreateRuntimeDefault();

        foreach (NounDefinition noun in catalog.nouns)
        {
            Assert.NotNull(noun, "Catalog contains a null noun definition.");
            bool isContextualTerrain = Array.Exists(
                CreatureCombatCatalog.ContextualTerrainNouns,
                value => string.Equals(value, noun.canonicalNoun, StringComparison.OrdinalIgnoreCase));
            if (isContextualTerrain)
                Assert.IsEmpty(noun.moveSet, $"Context terrain noun {noun.canonicalNoun} should not expose creature moves.");
            else
                Assert.IsNotEmpty(noun.moveSet, $"{noun.canonicalNoun} has no move set.");

            if (!noun.IsCreatureNoun)
                continue;

            Assert.IsTrue(noun.HasRequiredVerbCategory(CreatureVerbCategory.Attack, catalog.verbs), $"{noun.canonicalNoun} lacks an attack verb.");
            Assert.IsTrue(noun.HasRequiredVerbCategory(CreatureVerbCategory.Movement, catalog.verbs), $"{noun.canonicalNoun} lacks a movement verb.");
            Assert.IsTrue(noun.HasRequiredVerbCategory(CreatureVerbCategory.Defense, catalog.verbs), $"{noun.canonicalNoun} lacks a defense verb.");
            Assert.GreaterOrEqual(noun.moveSet.Count, 3, $"{noun.canonicalNoun} needs at least three distinct verb slots.");
        }
    }

    [Test]
    public void CreatureCombatRegistry_FiltersVoiceKeywordsToSliceProgression()
    {
        var go = new GameObject("CreatureCombatRegistryVoiceKeywordFilterTest");
        try
        {
            var registry = go.AddComponent<CreatureCombatRegistry>();
            registry.catalog = CreatureCombatCatalog.CreateRuntimeDefault();

            List<string> keywords = registry.GetVoiceKeywordsForProgression(
                new[] { GrammarPhrasePattern.NounOnly, GrammarPhrasePattern.VerbOnly, GrammarPhrasePattern.NounVerbPresent },
                new[] { "RAT", "BITE", "RUN" });

            CollectionAssert.Contains(keywords, "RAT");
            CollectionAssert.Contains(keywords, "BITE");
            CollectionAssert.Contains(keywords, "RAT BITES");
            CollectionAssert.DoesNotContain(keywords, "HE BITES");
            CollectionAssert.DoesNotContain(keywords, "RUN FAST");
            CollectionAssert.DoesNotContain(keywords, "RAT BIT");
            CollectionAssert.DoesNotContain(keywords, "RAT IS BITING");
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void CreatureCombatController_RejectsLockedGrammarPatternsUntilUnlocked()
    {
        GrammarWorldProgressService service = GrammarWorldProgressService.Instance;
        GrammarWorldProgressData progress = service.Data;
        string originalCurrentAreaId = progress.currentAreaId;
        string originalCurrentSceneName = progress.currentSceneName;
        List<string> originalUnlockedPatterns = progress.unlockedGrammarPatterns != null
            ? new List<string>(progress.unlockedGrammarPatterns)
            : new List<string>();
        List<string> originalUnlockedVocabulary = progress.unlockedVocabulary != null
            ? new List<string>(progress.unlockedVocabulary)
            : new List<string>();
        string areaId = $"TEST:LOCKED_PATTERN:{Guid.NewGuid():N}";

        var controllerGo = new GameObject("CreaturePatternUnlockControllerTest");
        try
        {
            progress.currentAreaId = areaId;
            progress.currentSceneName = "PatternUnlockTestScene";
            progress.unlockedGrammarPatterns = new List<string> { GrammarPhrasePattern.NounOnly.ToString() };
            progress.unlockedVocabulary = new List<string> { "RAT" };
            progress.areas.Add(new GrammarMapAreaState
            {
                areaId = areaId,
                displayName = "Noun Route Test",
                sceneKind = SemanticZoneKind.Route,
                grammarTopic = "Nouns",
                grammarTopicTier = 5,
                visible = true,
                explored = true,
            });

            // ResolveActiveProgressionService intentionally ignores a persistent
            // save when no live scene owns its current area. Give this isolated
            // fixture the same explicit scene context that production combat has.
            var sceneController = controllerGo.AddComponent<GrammarSceneController>();
            sceneController.mapAreaId = areaId;
            sceneController.sceneKind = SemanticZoneKind.Route;
            sceneController.grammarTopic = "Nouns";
            sceneController.grammarTopicTier = 5;

            var registry = controllerGo.AddComponent<CreatureCombatRegistry>();
            registry.catalog = CreatureCombatCatalog.CreateRuntimeDefault();
            var combat = controllerGo.AddComponent<CreatureCombatController>();
            combat.registry = registry;
            var statuses = new List<string>();
            combat.OnStatus += statuses.Add;

            Assert.IsTrue(combat.TryHandlePhrase("rat"));
            Assert.NotNull(combat.ActiveCreature);

            statuses.Clear();
            Assert.IsTrue(combat.TryHandlePhrase("apple"));
            Assert.IsTrue(statuses.Exists(status => status.Contains("still locked") && status.Contains("APPLE")));

            statuses.Clear();
            Assert.IsTrue(combat.TryHandlePhrase("rat bites"));
            Assert.IsTrue(statuses.Exists(status => status.Contains("locked")));

            progress.unlockedGrammarPatterns.Add(GrammarPhrasePattern.NounVerbPresent.ToString());
            statuses.Clear();
            Assert.IsTrue(combat.TryHandlePhrase("rat bites"));
            Assert.IsTrue(statuses.Exists(status => status.Contains("still locked") && status.Contains("BITE")));

            progress.unlockedVocabulary.Add("BITE");
            statuses.Clear();
            Assert.IsTrue(combat.TryHandlePhrase("rat bites"));
            Assert.IsFalse(statuses.Exists(status => status.Contains("locked")));
        }
        finally
        {
            progress.currentAreaId = originalCurrentAreaId;
            progress.currentSceneName = originalCurrentSceneName;
            progress.unlockedGrammarPatterns = originalUnlockedPatterns;
            progress.unlockedVocabulary = originalUnlockedVocabulary;
            progress.areas.RemoveAll(area => area != null && string.Equals(area.areaId, areaId, StringComparison.OrdinalIgnoreCase));
            Object.DestroyImmediate(controllerGo);
        }
    }

    [Test]
    public void CreatureCombatController_RejectsRepeatedSameSummonToPreventPpReset()
    {
        GrammarWorldProgressService service = GrammarWorldProgressService.Instance;
        GrammarWorldProgressData progress = service.Data;
        string originalCurrentAreaId = progress.currentAreaId;
        string originalCurrentSceneName = progress.currentSceneName;
        List<string> originalUnlockedPatterns = progress.unlockedGrammarPatterns != null
            ? new List<string>(progress.unlockedGrammarPatterns)
            : new List<string>();
        List<string> originalUnlockedVocabulary = progress.unlockedVocabulary != null
            ? new List<string>(progress.unlockedVocabulary)
            : new List<string>();
        string areaId = $"TEST:SUMMON_SPAM:{Guid.NewGuid():N}";

        var controllerGo = new GameObject("CreatureSummonAntiSpamTest");
        try
        {
            progress.currentAreaId = areaId;
            progress.currentSceneName = "SummonAntiSpamTestScene";
            progress.unlockedGrammarPatterns = new List<string>
            {
                GrammarPhrasePattern.NounOnly.ToString(),
                GrammarPhrasePattern.AdjectiveNoun.ToString(),
                GrammarPhrasePattern.VerbOnly.ToString(),
            };
            progress.unlockedVocabulary = new List<string> { "RAT", "BIG", "SMALL", "BITE" };
            progress.areas.Add(new GrammarMapAreaState
            {
                areaId = areaId,
                displayName = "Adjective Route Test",
                sceneKind = SemanticZoneKind.Route,
                grammarTopic = "Adjectives",
                grammarTopicTier = 10,
                visible = true,
                explored = true,
            });

            var registry = controllerGo.AddComponent<CreatureCombatRegistry>();
            registry.catalog = CreatureCombatCatalog.CreateRuntimeDefault();
            var combat = controllerGo.AddComponent<CreatureCombatController>();
            combat.registry = registry;
            combat.summonCooldownSeconds = 0f;
            var statuses = new List<string>();
            combat.OnStatus += statuses.Add;

            Assert.IsTrue(combat.TryHandlePhrase("big rat"));
            SummonedCreatureActor firstSummon = combat.ActiveCreature;
            Assert.NotNull(firstSummon);

            Assert.IsTrue(combat.TryHandlePhrase("bite"));
            Assert.Less(firstSummon.CurrentPp, firstSummon.Stats.maxPp);
            int ppAfterBite = firstSummon.CurrentPp;

            statuses.Clear();
            Assert.IsTrue(combat.TryHandlePhrase("big rat"));
            Assert.AreSame(firstSummon, combat.ActiveCreature);
            Assert.AreEqual(ppAfterBite, combat.ActiveCreature.CurrentPp);
            Assert.IsTrue(statuses.Exists(status => status.Contains("Already using BIG RAT")));

            statuses.Clear();
            Assert.IsTrue(combat.TryHandlePhrase("small rat"));
            Assert.AreNotSame(firstSummon, combat.ActiveCreature);
            Assert.AreEqual("SMALL", combat.ActiveCreature.Adjective.modifier);
            Assert.Less(combat.ActiveCreature.CurrentPp, combat.ActiveCreature.Stats.maxPp);
            Assert.IsTrue(statuses.Exists(status => status.Contains("Starting PP reduced")));
        }
        finally
        {
            progress.currentAreaId = originalCurrentAreaId;
            progress.currentSceneName = originalCurrentSceneName;
            progress.unlockedGrammarPatterns = originalUnlockedPatterns;
            progress.unlockedVocabulary = originalUnlockedVocabulary;
            progress.areas.RemoveAll(area => area != null && string.Equals(area.areaId, areaId, StringComparison.OrdinalIgnoreCase));
            Object.DestroyImmediate(controllerGo);
        }
    }

    [Test]
    public void CreatureCombatController_DefensiveCommandsMitigateIncomingPlayerDamage()
    {
        GrammarWorldProgressService service = GrammarWorldProgressService.Instance;
        GrammarWorldProgressData progress = service.Data;
        string originalCurrentAreaId = progress.currentAreaId;
        string originalCurrentSceneName = progress.currentSceneName;
        List<string> originalUnlockedPatterns = progress.unlockedGrammarPatterns != null
            ? new List<string>(progress.unlockedGrammarPatterns)
            : new List<string>();
        List<string> originalUnlockedVocabulary = progress.unlockedVocabulary != null
            ? new List<string>(progress.unlockedVocabulary)
            : new List<string>();
        string areaId = $"TEST:DEFENSE_WINDOW:{Guid.NewGuid():N}";

        var playerGo = new GameObject("CreatureDefenseMitigationPlayer");
        try
        {
            progress.currentAreaId = areaId;
            progress.currentSceneName = "DefenseMitigationTestScene";
            progress.unlockedGrammarPatterns = new List<string>
            {
                GrammarPhrasePattern.NounOnly.ToString(),
                GrammarPhrasePattern.VerbOnly.ToString(),
            };
            progress.unlockedVocabulary = new List<string> { "BIRD", "FISH", "FLY", "SWIM" };
            progress.areas.Add(new GrammarMapAreaState
            {
                areaId = areaId,
                displayName = "Defense Route Test",
                sceneKind = SemanticZoneKind.Route,
                grammarTopic = "Verbs",
                grammarTopicTier = 6,
                visible = true,
                explored = true,
            });

            playerGo.AddComponent<PlayerController>();
            var health = playerGo.AddComponent<PlayerHealth>();
            health.damageInvulnerabilitySeconds = 0f;
            var registry = playerGo.AddComponent<CreatureCombatRegistry>();
            registry.catalog = CreatureCombatCatalog.CreateRuntimeDefault();
            var combat = playerGo.AddComponent<CreatureCombatController>();
            combat.registry = registry;
            combat.summonCooldownSeconds = 0f;

            int startingHp = health.CurrentHp;
            Assert.IsTrue(combat.TryHandlePhrase("bird"));
            Assert.IsTrue(combat.TryHandlePhrase("fly"));
            Assert.IsTrue(health.TakeDamage(2, "Enemy pecks"));
            Assert.AreEqual(startingHp, health.CurrentHp);

            int birdHpAfterDodge = combat.ActiveCreature.CurrentHp;
            Assert.IsTrue(health.TakeDamage(2, "Enemy pecks again"));
            Assert.AreEqual(startingHp, health.CurrentHp);
            Assert.Less(combat.ActiveCreature.CurrentHp, birdHpAfterDodge);

            health.RestoreFull();
            startingHp = health.CurrentHp;
            Assert.IsTrue(combat.TryHandlePhrase("fish"));
            Assert.IsTrue(combat.TryHandlePhrase("swim"));
            int fishHpBeforeHit = combat.ActiveCreature.CurrentHp;
            Assert.IsTrue(health.TakeDamage(3, "Enemy bites"));
            Assert.AreEqual(startingHp, health.CurrentHp);
            Assert.Less(combat.ActiveCreature.CurrentHp, fishHpBeforeHit);
            Assert.Greater(combat.ActiveCreature.CurrentHp, fishHpBeforeHit - 3);
        }
        finally
        {
            progress.currentAreaId = originalCurrentAreaId;
            progress.currentSceneName = originalCurrentSceneName;
            progress.unlockedGrammarPatterns = originalUnlockedPatterns;
            progress.unlockedVocabulary = originalUnlockedVocabulary;
            progress.areas.RemoveAll(area => area != null && string.Equals(area.areaId, areaId, StringComparison.OrdinalIgnoreCase));
            Object.DestroyImmediate(playerGo);
        }
    }

    [Test]
    public void SummonedCreatureActor_DefenseAndDodgeActionsDoNotRequireTarget()
    {
        var go = new GameObject("CreatureDefenseActionTest");
        try
        {
            var actor = go.AddComponent<SummonedCreatureActor>();
            var noun = new NounDefinition
            {
                canonicalNoun = "BIRD",
                allowedVerbs = new List<string> { "FLY" },
                baseStats = CreatureStatBlock.Default,
            };
            var fly = new VerbActionDefinition
            {
                verb = "FLY",
                role = BattleActionRole.Dodge,
                ppCost = 2,
                power = 0,
                range = 5f,
                cooldownSeconds = 0f,
                movementVerb = true,
            };

            actor.Configure(noun, null);

            Assert.IsTrue(actor.TryUseVerb(fly, null, null, out string resultMessage));
            Assert.That(resultMessage, Does.Contain("dodge"));
            Assert.AreEqual(actor.Stats.maxPp - 2, actor.CurrentPp);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void SummonedCreatureActor_CurrentRegionVerbsCostLessPpThanReviewVerbs()
    {
        var go = new GameObject("CreatureFocusPpTest");
        try
        {
            var actor = go.AddComponent<SummonedCreatureActor>();
            var noun = new NounDefinition
            {
                canonicalNoun = "RAT",
                allowedVerbs = new List<string> { "BITE", "SCRATCH" },
                baseStats = new CreatureStatBlock
                {
                    maxHp = 5,
                    attack = 2,
                    defense = 1,
                    speed = 5,
                    maxPp = 12,
                },
            };
            var bite = new VerbActionDefinition
            {
                verb = "BITE",
                thirdPersonSingularForms = new List<string> { "BITES" },
                role = BattleActionRole.Offense,
                ppCost = 3,
                cooldownSeconds = 0f,
            };
            var scratch = new VerbActionDefinition
            {
                verb = "SCRATCH",
                role = BattleActionRole.Offense,
                ppCost = 2,
                cooldownSeconds = 0f,
            };

            actor.Configure(noun, null, new[] { "BITES" });

            Assert.IsTrue(actor.TryUseVerb(bite, null, null, out _));
            Assert.AreEqual(2, actor.LastPpSpent);
            Assert.AreEqual(10, actor.CurrentPp);

            Assert.IsTrue(actor.TryUseVerb(scratch, null, null, out _));
            Assert.AreEqual(3, actor.LastPpSpent);
            Assert.AreEqual(7, actor.CurrentPp);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void CreatureCombatCatalog_DefaultNounsUseSemanticTagsAndOverrides()
    {
        CreatureCombatCatalog catalog = CreatureCombatCatalog.CreateRuntimeDefault();
        NounDefinition boat = catalog.nouns.Find(noun => noun != null && noun.canonicalNoun == "BOAT");
        NounDefinition apple = catalog.nouns.Find(noun => noun != null && noun.canonicalNoun == "APPLE");

        Assert.NotNull(boat);
        Assert.NotNull(apple);
        Assert.IsTrue(boat.HasSemanticTag("VEHICLE"));
        Assert.IsTrue(boat.HasSemanticTag("AQUATIC"));
        Assert.NotNull(boat.ResolveMoveSlot("DRIFT"));
        Assert.NotNull(boat.ResolveMoveSlot("ROCK"));
        Assert.IsNull(boat.ResolveMoveSlot("BITE"));

        Assert.IsTrue(apple.HasSemanticTag("FOOD"));
        Assert.NotNull(apple.ResolveMoveSlot("ROLL"));
        Assert.NotNull(apple.ResolveMoveSlot("DROP"));
        Assert.NotNull(apple.ResolveMoveSlot("BOUNCE"));
        Assert.IsNull(apple.ResolveMoveSlot("FLY"));
    }

    [Test]
    public void CreatureCombatController_RejectsInvalidVerbAdverbPairings()
    {
        GrammarWorldProgressService service = GrammarWorldProgressService.Instance;
        GrammarWorldProgressData progress = service.Data;
        string originalCurrentAreaId = progress.currentAreaId;
        string originalCurrentSceneName = progress.currentSceneName;
        List<string> originalUnlockedPatterns = progress.unlockedGrammarPatterns != null
            ? new List<string>(progress.unlockedGrammarPatterns)
            : new List<string>();
        List<string> originalUnlockedVocabulary = progress.unlockedVocabulary != null
            ? new List<string>(progress.unlockedVocabulary)
            : new List<string>();
        string areaId = $"TEST:INVALID_ADVERB:{Guid.NewGuid():N}";

        var controllerGo = new GameObject("CreatureInvalidAdverbControllerTest");
        try
        {
            progress.currentAreaId = areaId;
            progress.currentSceneName = "InvalidAdverbTestScene";
            progress.unlockedGrammarPatterns = new List<string>
            {
                GrammarPhrasePattern.NounOnly.ToString(),
                GrammarPhrasePattern.VerbOnly.ToString(),
                GrammarPhrasePattern.VerbAdverb.ToString(),
            };
            progress.unlockedVocabulary = new List<string> { "RAT", "BITE", "SLOWLY" };
            progress.areas.Add(new GrammarMapAreaState
            {
                areaId = areaId,
                displayName = "Verb Adverb Test",
                sceneKind = SemanticZoneKind.Route,
                grammarTopic = "Verbs",
                grammarTopicTier = 6,
                visible = true,
                explored = true,
            });

            var registry = controllerGo.AddComponent<CreatureCombatRegistry>();
            registry.catalog = CreatureCombatCatalog.CreateRuntimeDefault();
            var combat = controllerGo.AddComponent<CreatureCombatController>();
            combat.registry = registry;
            combat.summonCooldownSeconds = 0f;
            var statuses = new List<string>();
            combat.OnStatus += statuses.Add;

            Assert.IsTrue(combat.TryHandlePhrase("rat"));
            statuses.Clear();
            Assert.IsTrue(combat.TryHandlePhrase("bite slowly"));
            Assert.IsTrue(statuses.Exists(status => status.Contains("does not fit BITE")));
        }
        finally
        {
            progress.currentAreaId = originalCurrentAreaId;
            progress.currentSceneName = originalCurrentSceneName;
            progress.unlockedGrammarPatterns = originalUnlockedPatterns;
            progress.unlockedVocabulary = originalUnlockedVocabulary;
            progress.areas.RemoveAll(area => area != null && string.Equals(area.areaId, areaId, StringComparison.OrdinalIgnoreCase));
            Object.DestroyImmediate(controllerGo);
        }
    }

    [Test]
    public void SummonedCreatureActor_WeakBattleVerbsGainMoreMaxPpThanMasteredOnes()
    {
        var go = new GameObject("CreatureAdaptivePpActor");
        try
        {
            var profile = go.AddComponent<PlayerLearningProfile>();
            profile.RecordBattleCommand("RAT", "BITE", "", "", CreatureCommandTense.Present, "", false, true);
            profile.RecordBattleCommand("RAT", "BITE", "", "", CreatureCommandTense.Present, "", false, true);
            profile.RecordBattleCommand("RAT", "BITE", "", "", CreatureCommandTense.Present, "", false, true);

            profile.RecordBattleCommand("RAT", "SCRATCH", "", "", CreatureCommandTense.Present, "", true);
            profile.RecordBattleCommand("RAT", "SCRATCH", "", "", CreatureCommandTense.Present, "", true);
            profile.RecordBattleCommand("RAT", "SCRATCH", "", "", CreatureCommandTense.Present, "", true);
            profile.RecordBattleCommand("RAT", "SCRATCH", "", "", CreatureCommandTense.Present, "", true);
            profile.RecordBattleCommand("RAT", "SCRATCH", "", "", CreatureCommandTense.Present, "", true);
            profile.RecordBattleCommand("RAT", "SCRATCH", "", "", CreatureCommandTense.Present, "", true);

            var actor = go.AddComponent<SummonedCreatureActor>();
            var noun = new NounDefinition
            {
                canonicalNoun = "RAT",
                baseStats = CreatureStatBlock.Default,
                moveSet = new List<NounMoveSlot>
                {
                    new NounMoveSlot { verbId = "BITE", baseMaxPp = 4, minMaxPp = 2, masteryBias = 0.2f, mistakeBias = 0.5f },
                    new NounMoveSlot { verbId = "SCRATCH", baseMaxPp = 4, minMaxPp = 2, masteryBias = 0.4f, mistakeBias = 0.2f },
                },
            };

            actor.Configure(noun, null, profile);

            Assert.IsTrue(actor.TryGetMovePp("BITE", out int biteCurrent, out int biteMax));
            Assert.IsTrue(actor.TryGetMovePp("SCRATCH", out int scratchCurrent, out int scratchMax));
            Assert.AreEqual(biteCurrent, biteMax);
            Assert.AreEqual(scratchCurrent, scratchMax);
            Assert.Greater(biteMax, scratchMax);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void NaturalGrammarProgression_FirstSliceHasValidRpgMetadata()
    {
        var go = new GameObject("GrammarRpgValidationRegistry");
        try
        {
            var registry = go.AddComponent<CreatureCombatRegistry>();
            registry.catalog = CreatureCombatCatalog.CreateRuntimeDefault();

            List<string> issues = GrammarRpgContentValidation.ValidateRegions(
                NaturalGrammarProgression.RegionDefinitions,
                registry,
                NaturalGrammarProgression.DialogueTasks);

            Assert.IsEmpty(issues,
                string.Join("\n", issues));

            foreach (GrammarRegionDefinition region in NaturalGrammarProgression.RegionDefinitions)
            {
                AssertDialogueTasksExist(region.npcLessonIds);
                AssertDialogueTasksExist(region.routePracticeIds);
                AssertDialogueTasksExist(region.gymCheckIds);
                Assert.GreaterOrEqual(region.npcLessonIds.Length, 4, $"{region.id} should ship with at least four town lessons.");
                Assert.GreaterOrEqual(region.routePracticeIds.Length, 4, $"{region.id} should ship with at least four route practice tasks.");
                Assert.GreaterOrEqual(region.gymCheckIds.Length, 3, $"{region.id} should ship with at least three gym checks.");
                Assert.GreaterOrEqual(region.townNpcNames.Length, region.npcLessonIds.Length, $"{region.id} needs authored town NPC names.");
                Assert.GreaterOrEqual(region.routeNpcNames.Length, region.routePracticeIds.Length, $"{region.id} needs authored route NPC names.");
                Assert.IsFalse(string.IsNullOrWhiteSpace(region.gymLeaderName), $"{region.id} needs an authored gym leader name.");
                Assert.IsNotNull(region.masteryTags);
                Assert.IsNotEmpty(region.masteryTags, $"{region.id} should expose teacher-visible mastery tags.");
                foreach (string taskId in region.npcLessonIds)
                    Assert.AreEqual(TranslatorAssistMode.Full, NaturalGrammarProgression.DialogueTasks[taskId].assistMode, $"{region.id} town lessons should use full Buddy help.");
                foreach (string taskId in region.routePracticeIds)
                    Assert.AreEqual(TranslatorAssistMode.Partial, NaturalGrammarProgression.DialogueTasks[taskId].assistMode, $"{region.id} route practice should use partial Buddy help.");
                foreach (string taskId in region.gymCheckIds)
                    Assert.AreEqual(TranslatorAssistMode.Off, NaturalGrammarProgression.DialogueTasks[taskId].assistMode, $"{region.id} gym checks should hold the line without Buddy help.");

                var routeMalfunctions = new HashSet<GrammarDialogueMalfunctionType>();
                foreach (string taskId in region.routePracticeIds)
                {
                    GrammarDialogueTaskDefinition task = NaturalGrammarProgression.DialogueTasks[taskId];
                    if (task.malfunctionType != GrammarDialogueMalfunctionType.None)
                        routeMalfunctions.Add(task.malfunctionType);
                }
                Assert.GreaterOrEqual(routeMalfunctions.Count, 3, $"{region.id} route practice should cover multiple remediation styles.");

                if (region.encounterMode != GrammarEncounterMode.None)
                {
                    if (region.encounterMode == GrammarEncounterMode.TacticalCommand)
                    {
                        Assert.IsTrue(region.combatUnlocked, $"{region.id} should only set combatUnlocked for tactical command regions.");
                        Assert.GreaterOrEqual(region.wildEncounterPools.Length, 2, $"{region.id} needs authored wild encounter pools.");
                        Assert.GreaterOrEqual(region.trainerBattlePools.Length, 2, $"{region.id} needs authored trainer battle pools.");
                    }
                    else
                    {
                        Assert.IsFalse(region.combatUnlocked, $"{region.id} should use recognition encounters without tactical combat.");
                    }

                    bool hasLiveAnswerTask = false;
                    foreach (string taskId in region.routePracticeIds)
                    {
                        GrammarDialogueInputMode inputMode = NaturalGrammarProgression.DialogueTasks[taskId].inputMode;
                        if (inputMode == GrammarDialogueInputMode.SpeakOrWrite || inputMode == GrammarDialogueInputMode.SpeakAndWrite)
                        {
                            hasLiveAnswerTask = true;
                            break;
                        }
                    }
                    if (!hasLiveAnswerTask)
                    {
                        foreach (string taskId in region.gymCheckIds)
                        {
                            GrammarDialogueInputMode inputMode = NaturalGrammarProgression.DialogueTasks[taskId].inputMode;
                            if (inputMode == GrammarDialogueInputMode.SpeakOrWrite || inputMode == GrammarDialogueInputMode.SpeakAndWrite)
                            {
                                hasLiveAnswerTask = true;
                                break;
                            }
                        }
                    }
                    Assert.IsTrue(hasLiveAnswerTask, $"{region.id} should include a live answer or command task in route or gym practice.");
                }

                foreach (string taskId in region.routePracticeIds)
                {
                    GrammarDialogueTaskDefinition task = NaturalGrammarProgression.DialogueTasks[taskId];
                    if (region.tier < 4)
                        Assert.AreNotEqual(GrammarDialogueMalfunctionType.ScrambledSentence, task.malfunctionType, $"{region.id} should not use jumbled commands before sentence work.");
                    if (task.malfunctionType == GrammarDialogueMalfunctionType.ScrambledSentence)
                    {
                        Assert.AreNotEqual(GrammarPhrasePattern.LetterOnly, task.grammarPattern, $"{task.taskId} should not scramble letters.");
                        Assert.Greater(AnswerChecker.Tokenize(task.expectedResponse).Count, 1, $"{task.taskId} should jumble whole words, not letters.");
                    }
                }
            }

            Assert.AreEqual(GrammarEncounterMode.None, NaturalGrammarProgression.Resolve("Greetings and Survival English", 1).encounterMode);
            Assert.AreEqual(GrammarEncounterMode.LetterRecognition, NaturalGrammarProgression.Resolve("Alphabet", 2).encounterMode);
            Assert.AreEqual(GrammarEncounterMode.LetterRecognition, NaturalGrammarProgression.Resolve("Vowels and Consonants", 3).encounterMode);
            Assert.AreEqual(GrammarEncounterMode.None, NaturalGrammarProgression.Resolve("Sentence Start and Full Stop", 4).encounterMode);
            Assert.AreEqual(GrammarEncounterMode.NounRecognition, NaturalGrammarProgression.Resolve("Nouns", 5).encounterMode);
            Assert.AreEqual(GrammarEncounterMode.TacticalCommand, NaturalGrammarProgression.Resolve("Verbs", 6).encounterMode);
            Assert.AreEqual(GrammarEncounterMode.TacticalCommand, NaturalGrammarProgression.Resolve("Basic Prepositions", 7).encounterMode);
            Assert.AreEqual(GrammarEncounterMode.TacticalCommand, NaturalGrammarProgression.Resolve("Articles", 8).encounterMode);
            Assert.AreEqual(GrammarEncounterMode.TacticalCommand, NaturalGrammarProgression.Resolve("Adjectives", 9).encounterMode);
            Assert.AreEqual(GrammarEncounterMode.TacticalCommand, NaturalGrammarProgression.Resolve("Pronouns", 10).encounterMode);
            Assert.AreEqual(GrammarEncounterMode.None, NaturalGrammarProgression.Resolve("Plurals", 11).encounterMode);
            CollectionAssert.AreEqual(
                new[] { "Welcome Village", "Alphabet Acres", "Vowel Valley", "Sentence Square", "Nounfield Town", "Verb Village", "Article Arcade", "Pronoun Port", "Plural Plains", "Adjective Grove", "Preposition Park" },
                NaturalGrammarProgression.Regions.Select(region => region.displayName).ToArray());

            GrammarEncounterPoolDefinition nounWildPool = NaturalGrammarProgression.ResolveWildEncounterPool("Nouns", 5, 1);
            Assert.AreEqual("nounfield-wild-wing-water", nounWildPool.poolId);
            CollectionAssert.Contains(nounWildPool.nounFamilies, "BIRD");
            Assert.AreEqual(2, NaturalGrammarProgression.ResolveWildEncounterEnemyCount("Nouns", 5, 1));

            GrammarEncounterPoolDefinition pronounTrainerPool = NaturalGrammarProgression.ResolveTrainerBattlePool("Pronouns", 10, 1);
            Assert.AreEqual("pronoun-trainer-cycle-b", pronounTrainerPool.poolId);
            CollectionAssert.Contains(pronounTrainerPool.practicePatterns, GrammarPhrasePattern.PronounVerbPresent);
            CollectionAssert.Contains(pronounTrainerPool.masteryTags, "curse");

            GrammarEncounterPoolDefinition articleTrainerPool = NaturalGrammarProgression.ResolveTrainerBattlePool("Articles", 8, 1);
            Assert.AreEqual("article-trainer-specific", articleTrainerPool.poolId);
            CollectionAssert.Contains(articleTrainerPool.practicePatterns, GrammarPhrasePattern.DeterminerNoun);
            CollectionAssert.Contains(articleTrainerPool.masteryTags, "specific");

            NaturalGrammarRegion nounfield = NaturalGrammarProgression.Resolve("Nouns", 5);
            Assert.IsFalse(nounfield.combatUnlocked);
            CollectionAssert.Contains(nounfield.unlockedPhrasePatterns, GrammarPhrasePattern.NounOnly);
            CollectionAssert.Contains(nounfield.npcLessonIds, "noun-creature-summon");
            CollectionAssert.Contains(nounfield.routePracticeIds, "noun-wild-summon");
            CollectionAssert.Contains(nounfield.gymCheckIds, "noun-mixed-family-battle");

            NaturalGrammarRegion articleArcade = NaturalGrammarProgression.Resolve("Articles", 8);
            CollectionAssert.Contains(articleArcade.unlockedPhrasePatterns, GrammarPhrasePattern.DeterminerNoun);
            CollectionAssert.Contains(articleArcade.npcLessonIds, "article-an");
            CollectionAssert.Contains(articleArcade.routePracticeIds, "article-road-heardwrong");
            CollectionAssert.Contains(articleArcade.gymCheckIds, "article-gym-correct");

            NaturalGrammarRegion verbVillage = NaturalGrammarProgression.Resolve("Verbs", 6);
            Assert.IsTrue(verbVillage.combatUnlocked);
            CollectionAssert.Contains(verbVillage.unlockedPhrasePatterns, GrammarPhrasePattern.NounVerbPresent);
            Assert.AreEqual(GrammarPhrasePattern.NounVerbPresent, NaturalGrammarProgression.DialogueTasks["verb-after-noun"].grammarPattern);
            Assert.AreEqual(GrammarPhrasePattern.NounVerbPresent, NaturalGrammarProgression.DialogueTasks["verb-wild-action"].grammarPattern);
            Assert.AreEqual(GrammarPhrasePattern.NounVerbPresent, NaturalGrammarProgression.DialogueTasks["verb-action-battle"].grammarPattern);
            foreach (GrammarEncounterPoolDefinition pool in verbVillage.trainerBattlePools)
            {
                Assert.IsFalse(pool.displayName.Contains("Offense"), $"{pool.poolId} should not expose combat taxonomy.");
                Assert.IsFalse(pool.displayName.Contains("Movement"), $"{pool.poolId} should not expose combat taxonomy.");
            }

            NaturalGrammarRegion adjectiveGrove = NaturalGrammarProgression.Resolve("Adjectives", 10);
            CollectionAssert.Contains(adjectiveGrove.unlockedPhrasePatterns, GrammarPhrasePattern.DeterminerAdjectiveNoun);
            CollectionAssert.Contains(adjectiveGrove.npcLessonIds, "adjective-article-summon");
            CollectionAssert.Contains(adjectiveGrove.routePracticeIds, "adjective-article-route");
            CollectionAssert.Contains(adjectiveGrove.gymCheckIds, "adjective-article-boss");

            LocalizedDialogueLine routeLine = NaturalGrammarProgression.BuildGeneratedDialogue(
                SemanticZoneKind.Route,
                "Pronouns",
                8,
                0,
                false);
            Assert.AreEqual(TranslatorAssistMode.Partial, routeLine.assistMode);
            Assert.AreEqual(GrammarDialogueMalfunctionType.MissingWord, routeLine.malfunctionType);
            Assert.AreEqual(GrammarDialogueInputMode.WriteOnly, routeLine.inputMode);

            LocalizedDialogueLine gymLine = NaturalGrammarProgression.BuildGeneratedDialogue(
                SemanticZoneKind.Gym,
                "Pronouns",
                8,
                0,
                false);
            Assert.AreEqual(TranslatorAssistMode.Off, gymLine.assistMode);
            CollectionAssert.Contains(pronounTrainerPool.practicePatterns, GrammarPhrasePattern.PronounVerbPresent);
            Assert.AreEqual(GrammarPhrasePattern.PronounVerbPresent, gymLine.grammarPattern);
            CollectionAssert.Contains(gymLine.acceptedEnglishResponses, "He bites");

            List<LocalizedDialogueLine> alphabetGymLines = NaturalGrammarProgression.BuildGeneratedDialogueSet(
                SemanticZoneKind.Gym,
                "Alphabet",
                2,
                false);
            Assert.AreEqual(3, alphabetGymLines.Count);
            Assert.AreEqual("alphabet-gym-capital", alphabetGymLines[0].dialogueTaskId);
            Assert.AreEqual("alphabet-gym-next-letter", alphabetGymLines[1].dialogueTaskId);
            Assert.AreEqual("alphabet-gym-small", alphabetGymLines[2].dialogueTaskId);

            LocalizedDialogueLine adjectiveGymLine = NaturalGrammarProgression.BuildGeneratedDialogue(
                SemanticZoneKind.Gym,
                "Adjectives",
                10,
                0,
                false);
            Assert.AreEqual(GrammarPhrasePattern.AdjectiveNoun, adjectiveGymLine.grammarPattern);
            CollectionAssert.Contains(adjectiveGymLine.acceptedEnglishResponses, "Small cat");

            List<EnemyAttackDefinition> pronounRouteAttacks = NaturalGrammarProgression.BuildEnemyAttackSet(
                SemanticZoneKind.Route,
                "Pronouns",
                8);
            Assert.IsTrue(pronounRouteAttacks.Exists(attack => attack.battleRole == BattleActionRole.Curse &&
                                                               attack.inflictedGrammarCurse == GrammarBattleCurse.I));
            Assert.IsTrue(pronounRouteAttacks.Exists(attack => attack.battleRole == BattleActionRole.Dodge &&
                                                               attack.damage == 0));

            List<EnemyAttackDefinition> articleGymAttacks = NaturalGrammarProgression.BuildEnemyAttackSet(
                SemanticZoneKind.Gym,
                "Articles",
                7);
            Assert.IsFalse(articleGymAttacks.Exists(attack => attack.battleRole == BattleActionRole.Curse));
            Assert.IsTrue(articleGymAttacks.Exists(attack => attack.battleRole == BattleActionRole.Offense &&
                                                             attack.damage >= 2));

            List<EnemyAttackDefinition> birdRouteAttacks = NaturalGrammarProgression.BuildEnemyAttackSet(
                SemanticZoneKind.Route,
                "Adjectives",
                10,
                "BIRD");
            Assert.IsTrue(birdRouteAttacks.Exists(attack => attack.battleRole == BattleActionRole.Offense &&
                                                            attack.grammarNounFamily == "BIRD" &&
                                                            attack.grammarVerb == "PECK" &&
                                                            attack.grammarCommand == "Bird pecks"));
            Assert.IsTrue(birdRouteAttacks.Exists(attack => attack.battleRole == BattleActionRole.Dodge &&
                                                            attack.grammarVerb == "FLY" &&
                                                            attack.grammarCommand == "Bird flies"));

            List<EnemyAttackDefinition> ratRouteAttacks = NaturalGrammarProgression.BuildEnemyAttackSet(
                SemanticZoneKind.Route,
                "Adjectives",
                10,
                "RAT");
            Assert.IsFalse(ratRouteAttacks.Exists(attack => attack.grammarVerb == "FLY" || attack.grammarVerb == "PECK"));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    static void AssertDialogueTasksExist(IEnumerable<string> taskIds)
    {
        Assert.IsNotNull(taskIds);
        foreach (string taskId in taskIds)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(taskId));
            Assert.IsTrue(
                NaturalGrammarProgression.DialogueTasks.ContainsKey(taskId),
                $"Missing authored dialogue task: {taskId}");
        }
    }

    [Test]
    public void GrammarWorldProgress_CombatGymDialogueDoesNotClearBeforeBattle()
    {
        var serviceGo = new GameObject("GrammarWorldProgressCombatGymTest");
        try
        {
            var service = serviceGo.AddComponent<GrammarWorldProgressService>();
            var controllerGo = new GameObject("PronounGymController");
            try
            {
                var controller = controllerGo.AddComponent<GrammarSceneController>();
                controller.sceneKind = SemanticZoneKind.Gym;
                controller.grammarTopic = "Pronouns";
                controller.grammarTopicTier = 8;
                service.RegisterCurrentScene(controller);

                bool tasksComplete = CompleteDialogueTasks(
                    service, SemanticZoneKind.Gym, "Pronouns", 8, true);

                Assert.IsTrue(tasksComplete);
                GrammarMapAreaState area = service.Data.areas.Find(candidate => candidate.areaId == service.Data.currentAreaId);
                Assert.IsNotNull(area);
                Assert.IsFalse(area.objectiveCompleted);
                CollectionAssert.DoesNotContain(service.Data.clearedGymAreaIds, area.areaId);
            }
            finally
            {
                Object.DestroyImmediate(controllerGo);
            }
        }
        finally
        {
            Object.DestroyImmediate(serviceGo);
        }
    }

    [Test]
    public void GrammarWorldProgress_FailedCombatGymEncounterDoesNotClearGym()
    {
        var serviceGo = new GameObject("GrammarWorldProgressFailedGymEncounterTest");
        try
        {
            var service = serviceGo.AddComponent<GrammarWorldProgressService>();
            var controllerGo = new GameObject("FailedGymController");
            try
            {
                string areaId = $"TEST:FAILED_GYM:{Guid.NewGuid():N}";
                var controller = controllerGo.AddComponent<GrammarSceneController>();
                controller.mapAreaId = areaId;
                controller.sceneKind = SemanticZoneKind.Gym;
                controller.grammarTopic = "Pronouns";
                controller.grammarTopicTier = 8;
                service.RegisterCurrentScene(controller);

                bool tasksComplete = CompleteDialogueTasks(
                    service, SemanticZoneKind.Gym, "Pronouns", 8, true);
                Assert.IsTrue(tasksComplete);

                InvokeEncounterOutcome(service, EncounterOutcome.Failed);

                GrammarMapAreaState area = service.Data.areas.Find(candidate => candidate.areaId == areaId);
                Assert.IsNotNull(area);
                Assert.IsFalse(area.encounterCompleted);
                Assert.IsFalse(area.objectiveCompleted);
                CollectionAssert.DoesNotContain(service.Data.clearedGymAreaIds, area.areaId);
                Assert.IsFalse(service.IsGymCleared(area.areaId));
            }
            finally
            {
                Object.DestroyImmediate(controllerGo);
            }
        }
        finally
        {
            Object.DestroyImmediate(serviceGo);
        }
    }

    [Test]
    public void GrammarWorldProgress_CompletedCombatGymEncounterClearsAfterDialogue()
    {
        var serviceGo = new GameObject("GrammarWorldProgressCompletedGymEncounterTest");
        try
        {
            var service = serviceGo.AddComponent<GrammarWorldProgressService>();
            var controllerGo = new GameObject("CompletedGymController");
            try
            {
                string areaId = $"TEST:COMPLETED_GYM:{Guid.NewGuid():N}";
                var controller = controllerGo.AddComponent<GrammarSceneController>();
                controller.mapAreaId = areaId;
                controller.sceneKind = SemanticZoneKind.Gym;
                controller.grammarTopic = "Pronouns";
                controller.grammarTopicTier = 8;
                service.RegisterCurrentScene(controller);

                bool tasksComplete = CompleteDialogueTasks(
                    service, SemanticZoneKind.Gym, "Pronouns", 8, true);
                Assert.IsTrue(tasksComplete);

                InvokeEncounterOutcome(service, EncounterOutcome.Completed);

                GrammarMapAreaState area = service.Data.areas.Find(candidate => candidate.areaId == areaId);
                Assert.IsNotNull(area);
                Assert.IsTrue(area.encounterCompleted);
                Assert.IsTrue(area.objectiveCompleted);
                CollectionAssert.Contains(service.Data.clearedGymAreaIds, area.areaId);
                Assert.IsTrue(service.IsGymCleared(area.areaId));
            }
            finally
            {
                Object.DestroyImmediate(controllerGo);
            }
        }
        finally
        {
            Object.DestroyImmediate(serviceGo);
        }
    }

    [Test]
    public void GrammarWorldProgress_NounGymDialogueDoesNotClearBeforeBossEncounter()
    {
        var serviceGo = new GameObject("GrammarWorldProgressNounGymBossGateTest");
        try
        {
            var service = serviceGo.AddComponent<GrammarWorldProgressService>();
            var controllerGo = new GameObject("NounGymController");
            try
            {
                string areaId = $"TEST:NOUN_GYM:{Guid.NewGuid():N}";
                var controller = controllerGo.AddComponent<GrammarSceneController>();
                controller.mapAreaId = areaId;
                controller.sceneKind = SemanticZoneKind.Gym;
                controller.grammarTopic = "Nouns";
                controller.grammarTopicTier = 5;
                service.RegisterCurrentScene(controller);

                bool tasksComplete = CompleteDialogueTasks(
                    service, SemanticZoneKind.Gym, "Nouns", 5, true);

                Assert.IsTrue(tasksComplete);
                GrammarMapAreaState area = service.Data.areas.Find(candidate => candidate.areaId == areaId);
                Assert.IsNotNull(area);
                Assert.IsFalse(area.encounterCompleted);
                Assert.IsFalse(area.objectiveCompleted);
                CollectionAssert.DoesNotContain(service.Data.clearedGymAreaIds, area.areaId);
            }
            finally
            {
                Object.DestroyImmediate(controllerGo);
            }
        }
        finally
        {
            Object.DestroyImmediate(serviceGo);
        }
    }

    [Test]
    public void GrammarWorldProgress_CompletedNounGymBossEncounterClearsAfterDialogue()
    {
        var serviceGo = new GameObject("GrammarWorldProgressNounGymBossCompleteTest");
        try
        {
            var service = serviceGo.AddComponent<GrammarWorldProgressService>();
            var controllerGo = new GameObject("CompletedNounGymController");
            try
            {
                string areaId = $"TEST:COMPLETED_NOUN_GYM:{Guid.NewGuid():N}";
                var controller = controllerGo.AddComponent<GrammarSceneController>();
                controller.mapAreaId = areaId;
                controller.sceneKind = SemanticZoneKind.Gym;
                controller.grammarTopic = "Nouns";
                controller.grammarTopicTier = 5;
                service.RegisterCurrentScene(controller);

                bool tasksComplete = CompleteDialogueTasks(
                    service, SemanticZoneKind.Gym, "Nouns", 5, true);
                Assert.IsTrue(tasksComplete);

                InvokeEncounterOutcome(service, EncounterOutcome.Completed);

                GrammarMapAreaState area = service.Data.areas.Find(candidate => candidate.areaId == areaId);
                Assert.IsNotNull(area);
                Assert.IsTrue(area.encounterCompleted);
                Assert.IsTrue(area.objectiveCompleted);
                CollectionAssert.Contains(service.Data.clearedGymAreaIds, area.areaId);
                Assert.IsTrue(service.IsGymCleared(area.areaId));
            }
            finally
            {
                Object.DestroyImmediate(controllerGo);
            }
        }
        finally
        {
            Object.DestroyImmediate(serviceGo);
        }
    }

    [Test]
    public void GrammarWorldProgress_CombatRouteDialogueDoesNotClearBeforeEncounter()
    {
        var serviceGo = new GameObject("GrammarWorldProgressCombatRouteTest");
        try
        {
            var service = serviceGo.AddComponent<GrammarWorldProgressService>();
            var controllerGo = new GameObject("ArticleRouteController");
            try
            {
                string areaId = $"TEST:COMBAT_ROUTE:{Guid.NewGuid():N}";
                var controller = controllerGo.AddComponent<GrammarSceneController>();
                controller.mapAreaId = areaId;
                controller.sceneKind = SemanticZoneKind.Route;
                controller.grammarTopic = "Articles";
                controller.grammarTopicTier = 7;
                service.RegisterCurrentScene(controller);

                List<LocalizedDialogueLine> routeLines = NaturalGrammarProgression.BuildGeneratedDialogueSet(
                    SemanticZoneKind.Route,
                    "Articles",
                    7,
                    true);

                bool tasksComplete = false;
                foreach (LocalizedDialogueLine routeLine in routeLines)
                {
                    tasksComplete = service.RegisterCurrentAreaDialogueTaskCompleted(
                        routeLine,
                        SemanticZoneKind.Route,
                        "Articles",
                        7);
                }

                Assert.IsTrue(tasksComplete);
                GrammarMapAreaState area = service.Data.areas.Find(candidate => candidate.areaId == service.Data.currentAreaId);
                Assert.IsNotNull(area);
                Assert.IsFalse(area.encounterCompleted);
                Assert.IsFalse(area.objectiveCompleted);
                CollectionAssert.DoesNotContain(service.Data.completedAreaIds, area.areaId);
            }
            finally
            {
                Object.DestroyImmediate(controllerGo);
            }
        }
        finally
        {
            Object.DestroyImmediate(serviceGo);
        }
    }

    [Test]
    public void GrammarWorldProgress_TownRouteGymVerticalSliceUnlocksNextTown()
    {
        string previousProfile = PlayerSaveSlots.ActiveProfileId;
        string testProfile = $"vertical-slice-{Guid.NewGuid():N}";
        PlayerSaveSlots.SelectProfile(testProfile);
        PlayerSaveSlots.DeleteActiveSlot();
        var serviceGo = new GameObject("GrammarWorldProgressVerticalSliceTest");
        var townGo = new GameObject("VerticalSliceTown");
        var routeGo = new GameObject("VerticalSliceRoute");
        var gymGo = new GameObject("VerticalSliceGym");
        try
        {
            var service = serviceGo.AddComponent<GrammarWorldProgressService>();
            string token = Guid.NewGuid().ToString("N");
            string townId = $"TEST:TOWN:NOUNS:{token}";
            string routeId = $"TEST:ROUTE:NOUNS:{token}";
            string gymId = $"TEST:GYM:NOUNS:{token}";
            string nextTownId = $"TEST:TOWN:VERBS:{token}";

            var town = townGo.AddComponent<GrammarSceneController>();
            town.mapAreaId = townId;
            town.sceneKind = SemanticZoneKind.Town;
            town.grammarTopic = "Nouns";
            town.grammarTopicTier = 5;
            town.connectedMapAreaIds = new List<string> { routeId };
            service.RegisterCurrentScene(town);

            AssertDialogueCompletion(service, SemanticZoneKind.Town, "Nouns", 5, false);
            GrammarMapAreaState townArea = FindArea(service, townId);
            GrammarMapAreaState revealedRoute = FindArea(service, routeId);
            Assert.IsTrue(townArea.objectiveCompleted);
            Assert.IsTrue(revealedRoute.visible);
            Assert.IsFalse(revealedRoute.objectiveCompleted);
            CollectionAssert.DoesNotContain(service.Data.unlockedGrammarPatterns, GrammarPhrasePattern.NounOnly.ToString());
            CollectionAssert.DoesNotContain(service.Data.unlockedConceptIds, GrammarConceptId.BasicNouns.ToString());

            var route = routeGo.AddComponent<GrammarSceneController>();
            route.mapAreaId = routeId;
            route.sceneKind = SemanticZoneKind.Route;
            route.grammarTopic = "Nouns";
            route.grammarTopicTier = 5;
            route.connectedMapAreaIds = new List<string> { townId, gymId };
            service.RegisterCurrentScene(route);

            AssertDialogueCompletion(service, SemanticZoneKind.Route, "Nouns", 5, true);
            GrammarMapAreaState routeArea = FindArea(service, routeId);
            Assert.IsFalse(routeArea.objectiveCompleted);
            Assert.IsFalse(routeArea.encounterCompleted);

            InvokeEncounterOutcome(service, EncounterOutcome.Completed);
            routeArea = FindArea(service, routeId);
            GrammarMapAreaState revealedGym = FindArea(service, gymId);
            Assert.IsTrue(routeArea.encounterCompleted);
            Assert.IsTrue(routeArea.objectiveCompleted);
            Assert.IsTrue(revealedGym.visible);
            Assert.IsFalse(revealedGym.objectiveCompleted);

            var gym = gymGo.AddComponent<GrammarSceneController>();
            gym.mapAreaId = gymId;
            gym.sceneKind = SemanticZoneKind.Gym;
            gym.grammarTopic = "Nouns";
            gym.grammarTopicTier = 5;
            gym.connectedMapAreaIds = new List<string> { routeId, nextTownId };
            service.RegisterCurrentScene(gym);

            AssertDialogueCompletion(service, SemanticZoneKind.Gym, "Nouns", 5, true);
            GrammarMapAreaState gymArea = FindArea(service, gymId);
            Assert.IsFalse(gymArea.objectiveCompleted);
            Assert.IsFalse(gymArea.encounterCompleted);

            InvokeEncounterOutcome(service, EncounterOutcome.Completed);
            gymArea = FindArea(service, gymId);
            GrammarMapAreaState nextTown = FindArea(service, nextTownId);
            Assert.IsTrue(gymArea.encounterCompleted);
            Assert.IsTrue(gymArea.objectiveCompleted);
            CollectionAssert.Contains(service.Data.clearedGymAreaIds, gymId);
            Assert.IsTrue(nextTown.visible);
            CollectionAssert.Contains(service.Data.unlockedGrammarPatterns, GrammarPhrasePattern.NounOnly.ToString());
            CollectionAssert.Contains(service.Data.unlockedConceptIds, GrammarConceptId.BasicNouns.ToString());
            CollectionAssert.Contains(service.Data.unlockedVocabulary, "RAT");
            CollectionAssert.Contains(service.Data.unlockedVocabulary, "CAT");

            var nextTownController = new GameObject("VerticalSliceNextTown").AddComponent<GrammarSceneController>();
            try
            {
                nextTownController.mapAreaId = nextTownId;
                nextTownController.sceneKind = SemanticZoneKind.Town;
                nextTownController.grammarTopic = "Verbs";
                nextTownController.grammarTopicTier = 6;
                service.RegisterCurrentScene(nextTownController);

                AssertDialogueCompletion(service, SemanticZoneKind.Town, "Verbs", 6, false);
                nextTown = FindArea(service, nextTownId);
                Assert.IsTrue(nextTown.objectiveCompleted);
                CollectionAssert.DoesNotContain(service.Data.unlockedGrammarPatterns, GrammarPhrasePattern.VerbOnly.ToString());
                CollectionAssert.DoesNotContain(service.Data.unlockedConceptIds, GrammarConceptId.BasicVerbs.ToString());
                CollectionAssert.DoesNotContain(service.Data.unlockedVocabulary, "BITE");
                CollectionAssert.DoesNotContain(service.Data.unlockedVocabulary, "FLY");
            }
            finally
            {
                Object.DestroyImmediate(nextTownController.gameObject);
            }
        }
        finally
        {
            Object.DestroyImmediate(gymGo);
            Object.DestroyImmediate(routeGo);
            Object.DestroyImmediate(townGo);
            Object.DestroyImmediate(serviceGo);
            PlayerSaveSlots.DeleteActiveSlot();
            PlayerSaveSlots.SelectProfile(previousProfile);
        }
    }

    [Test]
    public void CurriculumSessionManager_BattleCommandsAlsoSubmitSpokenPhraseEvidence()
    {
        var curriculumGo = new GameObject("BattleSpokenPhraseCurriculumTest");
        var progressGo = new GameObject("BattleSpokenPhraseProgressTest");
        try
        {
            var curriculum = curriculumGo.AddComponent<CurriculumSessionManager>();
            curriculum.LoadWorldGoalPractice();

            var service = progressGo.AddComponent<GrammarWorldProgressService>();
            string areaId = $"TEST:ROUTE:PRONOUNS:{Guid.NewGuid():N}";
            var routeGo = new GameObject("BattleSpokenPhraseRoute");
            try
            {
                var route = routeGo.AddComponent<GrammarSceneController>();
                route.mapAreaId = areaId;
                route.sceneKind = SemanticZoneKind.Route;
                route.grammarTopic = "Pronouns";
                route.grammarTopicTier = 8;
                service.RegisterCurrentScene(route);

                IReadOnlyList<PhoneticSoundSegment> segments = PronunciationProfileBuilder.BuildSegments("BITES");
                var insight = new PronunciationInsightResult(
                    "test-pronunciation",
                    "he bites",
                    "he bites",
                    "he bites",
                    true,
                    true,
                    0.91f,
                    PronunciationHintKey.GreatTry,
                    segments[0],
                    segments,
                    PronunciationProfileBuilder.BuildSyllableBeats("BITES", segments),
                    "Battle speech looked clear.");

                curriculum.RecordGrammarBattleEvent(
                    "he bites",
                    GrammarPhrasePattern.PronounVerbPresent,
                    GrammarBattleCurse.HeSheIt,
                    "BITE",
                    "Offense",
                    true,
                    "command_success",
                    ppSpent: 2,
                    enemyNounFamily: "BIRD",
                    enemyActionVerb: "PECK",
                    enemyGrammarCommand: "Bird pecks",
                    enemyGrammarPattern: GrammarPhrasePattern.NounVerbPresent.ToString(),
                    encounterMasteryTags: new List<string> { "present", "curse" },
                    pronunciationInsight: insight);

                var provider = GetPrivateField<LocalDemoCurriculumProvider>(curriculum, "provider");
                Assert.IsNotNull(provider);
                Assert.AreEqual(1, provider.GrammarBattleEvents.Count);
                Assert.AreEqual(1, provider.SpokenPhraseEvents.Count);
                CollectionAssert.AreEqual(new[] { "HE", "BITES" }, provider.GrammarBattleEvents[0].vocabularyTokens);
                CollectionAssert.Contains(provider.GrammarBattleEvents[0].masteryTags, "simple-present");
                CollectionAssert.Contains(provider.GrammarBattleEvents[0].masteryTags, "present");
                CollectionAssert.Contains(provider.GrammarBattleEvents[0].masteryTags, "curse");
                Assert.AreEqual("he bites", provider.SpokenPhraseEvents[0].phrase);
                CollectionAssert.AreEqual(new[] { "HE", "BITES" }, provider.SpokenPhraseEvents[0].vocabularyTokens);
                Assert.AreEqual(areaId, provider.SpokenPhraseEvents[0].areaId);
                Assert.AreEqual("Route", provider.SpokenPhraseEvents[0].zoneKind);
                Assert.IsTrue(provider.SpokenPhraseEvents[0].accepted);
                CollectionAssert.Contains(provider.SpokenPhraseEvents[0].masteryTags, "simple-present");
                CollectionAssert.Contains(provider.SpokenPhraseEvents[0].masteryTags, "present");
                CollectionAssert.Contains(provider.SpokenPhraseEvents[0].masteryTags, "curse");
                Assert.IsNotNull(provider.SpokenPhraseEvents[0].pronunciationInsight);
                Assert.AreEqual("test-pronunciation", provider.SpokenPhraseEvents[0].pronunciationInsight.providerName);
                Assert.AreEqual(0.91f, provider.SpokenPhraseEvents[0].pronunciationInsight.score, 0.001f);

                curriculum.RecordWrittenPhraseEvent(
                    "bite",
                    GrammarPhrasePattern.PronounVerbPresent,
                    true,
                    "",
                    SemanticZoneKind.Route,
                    areaId,
                    submittedPhrase: "bite",
                    targetPhrase: "I bite");
                Assert.AreEqual(1, provider.WrittenPhraseEvents.Count);
                Assert.AreEqual("I bite", provider.WrittenPhraseEvents[0].phrase);
                Assert.AreEqual("bite", provider.WrittenPhraseEvents[0].submittedPhrase);
                Assert.AreEqual("I bite", provider.WrittenPhraseEvents[0].targetPhrase);
                CollectionAssert.AreEqual(new[] { "I", "BITE" }, provider.WrittenPhraseEvents[0].vocabularyTokens);
                curriculum.SubmitRunSummary(new RunSummary
                {
                    reason = RunEndReason.Victory,
                    elapsedSeconds = 95f,
                    subarenasCleared = 1,
                    fullLoopsCleared = 1,
                    enemiesDefeated = 2,
                });

                Assert.AreEqual(1, provider.RunSessions.Count);
                CurriculumRunSessionRecord session = provider.RunSessions[0];
                Assert.AreEqual(1, session.spokenPhraseCount);
                Assert.AreEqual(1, session.writtenPhraseCount);
                Assert.AreEqual(1, session.grammarBattleCount);
                CollectionAssert.Contains(session.grammarPatternsPracticed, GrammarPhrasePattern.PronounVerbPresent.ToString());
                CollectionAssert.Contains(session.masteryTagsPracticed, "simple-present");
                CollectionAssert.Contains(session.masteryTagsPracticed, "present");
                CollectionAssert.Contains(session.masteryTagsPracticed, "curse");
                CollectionAssert.Contains(session.vocabularyTokens, "HE");
                CollectionAssert.Contains(session.vocabularyTokens, "BITES");
                CollectionAssert.Contains(session.vocabularyTokens, "I");
                CollectionAssert.Contains(session.vocabularyTokens, "BITE");
            }
            finally
            {
                Object.DestroyImmediate(routeGo);
            }
        }
        finally
        {
            Object.DestroyImmediate(progressGo);
            Object.DestroyImmediate(curriculumGo);
        }
    }

    static void InvokeEncounterOutcome(GrammarWorldProgressService service, EncounterOutcome outcome)
    {
        MethodInfo handler = typeof(GrammarWorldProgressService).GetMethod(
            "HandleEncounterEnded",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(handler);
        handler.Invoke(service, new object[] { new WaveDescriptor(), outcome });
    }

    static void AssertDialogueCompletion(
        GrammarWorldProgressService service,
        SemanticZoneKind zoneKind,
        string grammarTopic,
        int tier,
        bool trainerBattle)
    {
        bool tasksComplete = CompleteDialogueTasks(service, zoneKind, grammarTopic, tier, trainerBattle);
        Assert.IsTrue(tasksComplete);
    }

    static bool CompleteDialogueTasks(
        GrammarWorldProgressService service,
        SemanticZoneKind zoneKind,
        string grammarTopic,
        int tier,
        bool trainerBattle)
    {
        bool tasksComplete = false;
        foreach (LocalizedDialogueLine line in NaturalGrammarProgression.BuildGeneratedDialogueSet(
                     zoneKind, grammarTopic, tier, trainerBattle))
        {
            tasksComplete = service.RegisterCurrentAreaDialogueTaskCompleted(
                line, zoneKind, grammarTopic, tier);
        }

        return tasksComplete;
    }

    static void InvokeApplyNaturalProgressionDefaults(GrammarSceneController controller)
    {
        MethodInfo method = typeof(GrammarSceneController).GetMethod(
            "ApplyNaturalProgressionDefaults",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(method);
        method.Invoke(controller, Array.Empty<object>());
    }

    static GrammarMapAreaState FindArea(GrammarWorldProgressService service, string areaId)
    {
        GrammarMapAreaState area = service.Data.areas.Find(candidate => candidate != null && candidate.areaId == areaId);
        Assert.IsNotNull(area, $"Missing area {areaId}");
        return area;
    }

    static bool InvokeIsAcceptedResponse(LocalizedDialogueLine line, string response)
    {
        MethodInfo method = typeof(NpcDialogueUI).GetMethod(
            "IsAcceptedResponse",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(method);
        return (bool)method.Invoke(null, new object[] { line, response });
    }

    static T GetPrivateField<T>(object instance, string fieldName) where T : class
    {
        FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field);
        return field.GetValue(instance) as T;
    }

    static void SetPrivateStaticField<T>(string fieldName, T value)
    {
        FieldInfo field = typeof(T).GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(field);
        field.SetValue(null, value);
    }

    static void InvokeCastWordRecognized(WordActionHandler handler, string word)
    {
        MethodInfo method = typeof(WordActionHandler).GetMethod(
            "HandleCastWordRecognized",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(method);
        method.Invoke(handler, new object[] { word });
    }

    [Test]
    public void CreatureCombatRegistry_TrueAliasesResolveWithoutCollapsingUniqueFamilies()
    {
        var go = new GameObject("CreatureCombatRegistryTrueAliasTest");
        try
        {
            var registry = go.AddComponent<CreatureCombatRegistry>();
            registry.catalog = CreatureCombatCatalog.CreateRuntimeDefault();

            Assert.IsTrue(registry.TryParsePhrase("rat", out CreaturePhraseParseResult rat));
            Assert.AreEqual(CreaturePhraseKind.NounSummon, rat.kind);
            Assert.AreEqual("RAT", rat.noun.canonicalNoun);

            Assert.IsTrue(registry.TryParsePhrase("mutt", out CreaturePhraseParseResult mutt));
            Assert.AreEqual(CreaturePhraseKind.NounSummon, mutt.kind);
            Assert.AreEqual("DOG", mutt.noun.canonicalNoun);

            Assert.IsTrue(registry.TryParsePhrase("pooch", out CreaturePhraseParseResult pooch));
            Assert.AreEqual(CreaturePhraseKind.NounSummon, pooch.kind);
            Assert.AreEqual("DOG", pooch.noun.canonicalNoun);

            Assert.IsTrue(registry.TryParsePhrase("pup", out CreaturePhraseParseResult pup));
            Assert.AreEqual(CreaturePhraseKind.NounSummon, pup.kind);
            Assert.AreEqual("PUP", pup.noun.canonicalNoun);

            Assert.IsTrue(registry.TryParsePhrase("puppy", out CreaturePhraseParseResult puppy));
            Assert.AreEqual(CreaturePhraseKind.NounSummon, puppy.kind);
            Assert.AreEqual("PUP", puppy.noun.canonicalNoun);

            Assert.IsTrue(registry.TryParsePhrase("bird", out CreaturePhraseParseResult bird));
            Assert.AreEqual(CreaturePhraseKind.NounSummon, bird.kind);
            Assert.AreEqual("BIRD", bird.noun.canonicalNoun);

            Assert.IsTrue(registry.TryParsePhrase("duck", out CreaturePhraseParseResult duck));
            Assert.AreEqual(CreaturePhraseKind.NounSummon, duck.kind);
            Assert.AreEqual("DUCK", duck.noun.canonicalNoun);

            Assert.IsTrue(registry.TryParsePhrase("owl", out CreaturePhraseParseResult owl));
            Assert.AreEqual(CreaturePhraseKind.NounSummon, owl.kind);
            Assert.AreEqual("OWL", owl.noun.canonicalNoun);

            Assert.IsTrue(registry.TryParsePhrase("quail", out CreaturePhraseParseResult quail));
            Assert.AreEqual(CreaturePhraseKind.NounSummon, quail.kind);
            Assert.AreEqual("QUAIL", quail.noun.canonicalNoun);

            Assert.IsTrue(registry.TryParsePhrase("mug", out CreaturePhraseParseResult mug));
            Assert.AreEqual("MUG", mug.noun.canonicalNoun);

            Assert.IsTrue(registry.TryParsePhrase("hat", out CreaturePhraseParseResult hat));
            Assert.AreEqual("HAT", hat.noun.canonicalNoun);

            Assert.IsTrue(registry.TryParsePhrase("timepiece", out CreaturePhraseParseResult timepiece));
            Assert.AreEqual("WATCH", timepiece.noun.canonicalNoun);

            Assert.IsTrue(registry.TryParsePhrase("mouse", out CreaturePhraseParseResult mouse));
            Assert.AreEqual("RAT", mouse.noun.canonicalNoun);

            Assert.IsFalse(registry.TryParsePhrase("eagle", out _));
            Assert.IsFalse(registry.TryParsePhrase("rodent", out _));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void SpellTarget_CreatureActionRequiresMatchingNounFamily()
    {
        var registryGo = new GameObject("CreatureTargetFamilyRegistryTest");
        var go = new GameObject("CreatureTargetFamilyTest");
        try
        {
            var registry = registryGo.AddComponent<CreatureCombatRegistry>();
            registry.catalog = CreatureCombatCatalog.CreateRuntimeDefault();

            go.SetActive(false);
            var target = go.AddComponent<SpellTarget>();
            target.requiredCreatureNoun = "RAT";
            target.maxHp = 5;
            go.SetActive(true);

            Assert.IsFalse(target.ReceiveCreatureAction("CAT", "SCRATCH", 3));
            Assert.AreEqual(5, target.CurrentHp);
            Assert.IsFalse(target.ReceiveCreatureAction("BIRD", "SCRATCH", 3));
            Assert.AreEqual(5, target.CurrentHp);
            Assert.IsTrue(target.ReceiveCreatureAction("RAT", "SCRATCH", 3));
            Assert.AreEqual(2, target.CurrentHp);
        }
        finally
        {
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(registryGo);
        }
    }

    [Test]
    public void SpellTarget_CreatureFamilyMatchingKeepsUniquePronunciationNouns()
    {
        var registryGo = new GameObject("CreatureFamilyUniqueRegistryTest");
        var go = new GameObject("CreatureFamilyUniqueTargetTest");
        try
        {
            var registry = registryGo.AddComponent<CreatureCombatRegistry>();
            registry.catalog = CreatureCombatCatalog.CreateRuntimeDefault();

            go.SetActive(false);
            var target = go.AddComponent<SpellTarget>();
            target.requiredCreatureNoun = "DUCK";
            target.maxHp = 5;
            go.SetActive(true);

            Assert.IsFalse(target.ReceiveCreatureAction("BIRD", "BITE", 3));
            Assert.AreEqual(5, target.CurrentHp);
            Assert.IsTrue(target.ReceiveCreatureAction("DUCK", "BITE", 3));
            Assert.AreEqual(2, target.CurrentHp);

            var definition = new EnemyDefinition { creatureFamilyNoun = "DOG", weaknessSpell = "DOG" };
            Assert.IsTrue(definition.MatchesCreatureFamily("MUTT"));
            Assert.IsFalse(definition.MatchesCreatureFamily("PUP"));
        }
        finally
        {
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(registryGo);
        }
    }

    [Test]
    public void TranslatorBuddyService_AssistModesControlOutput()
    {
        var go = new GameObject("TranslatorBuddyServiceTest");
        try
        {
            var service = go.AddComponent<TranslatorBuddyService>();
            service.preferredLanguage = "hi";

            service.SetAssistMode(TranslatorAssistMode.Full);
            Assert.AreEqual("Hello", service.BuildAssistText("line-1", "Hello"));

            service.SetAssistMode(TranslatorAssistMode.Partial);
            StringAssert.Contains("Signal weak", service.BuildAssistText("line-2", "Practice"));

            service.SetAssistMode(TranslatorAssistMode.Off);
            Assert.AreEqual("", service.BuildAssistText("line-3", "Exam"));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void TranslatorBuddyService_ReadinessRequiresRestTranslationForProduction()
    {
        var go = new GameObject("TranslatorBuddyReadinessTest");
        try
        {
            var service = go.AddComponent<TranslatorBuddyService>();
            service.providerMode = TranslatorProviderMode.TextFallback;

            TranslatorBuddyReadinessReport fallbackReport = service.BuildReadinessReport(productionStrict: true);
            Assert.IsFalse(fallbackReport.isReady);
            Assert.IsTrue(fallbackReport.errors.Exists(error => error.Contains("local text fallback")));

            service.providerMode = TranslatorProviderMode.RestEndpoints;
            service.translationEndpointUrl = "http://localhost:8000/translate";
            service.ttsEndpointUrl = "http://localhost:8000/tts";

            TranslatorBuddyReadinessReport restReport = service.BuildReadinessReport(productionStrict: true);
            Assert.IsTrue(restReport.isReady);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void TranslatorBuddyService_ResponsePromptsFollowAssistMode()
    {
        var go = new GameObject("TranslatorBuddyResponsePromptTest");
        try
        {
            var service = go.AddComponent<TranslatorBuddyService>();
            var line = new LocalizedDialogueLine
            {
                lineId = "route-response",
                sourceText = "Are you ready?",
                expectedEnglishResponse = "I am ready",
            };

            service.SetAssistMode(TranslatorAssistMode.Full);
            Assert.AreEqual("Say: \"I am ready\"", service.BuildResponsePrompt(line));

            service.SetAssistMode(TranslatorAssistMode.Partial);
            string partial = service.BuildResponsePrompt(line);
            StringAssert.Contains("Signal broken", partial);
            Assert.AreNotEqual("Signal broken. Maybe say: \"I am ready\"", partial);

            line.malfunctionType = GrammarDialogueMalfunctionType.MissingWord;
            line.npcLine = "The sign lost one capital word: ____ am ready.";
            string missingPrompt = service.BuildResponsePrompt(line);
            StringAssert.Contains("Fill the blank", missingPrompt);
            StringAssert.Contains("____ am ready", missingPrompt);

            line.malfunctionType = GrammarDialogueMalfunctionType.ScrambledSentence;
            line.npcLine = "";
            StringAssert.Contains("Unscramble", service.BuildResponsePrompt(line));

            line.malfunctionType = GrammarDialogueMalfunctionType.HeardWrong;
            StringAssert.Contains("Correct it", service.BuildResponsePrompt(line));

            service.SetAssistMode(TranslatorAssistMode.Off);
            Assert.AreEqual("", service.BuildResponsePrompt(line));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void TranslatorBuddyService_RouteFeedbackAndRequestsNeverExposeExactAnswer()
    {
        var go = new GameObject("TranslatorBuddyRouteSafetyTest");
        try
        {
            var service = go.AddComponent<TranslatorBuddyService>();
            var line = new LocalizedDialogueLine
            {
                lineId = "route-safe",
                dialogueTaskId = "route-safe",
                conceptId = GrammarConceptId.Articles,
                expectedEnglishResponse = "An owl",
                overrideAssistMode = true,
                assistMode = TranslatorAssistMode.Partial,
            };

            TutorFeedbackPlan feedback = service.BuildTutorFeedback(line, "A owl", "response_mismatch");
            string status = service.BuildTutorFeedbackStatus(line, feedback);
            TranslatorBuddyHintRequest request = service.BuildHintRequest(line, "A owl", feedback);

            Assert.IsFalse(status.IndexOf("An owl", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.IsTrue(string.IsNullOrWhiteSpace(request.expectedEnglishResponse));
            Assert.IsFalse(service.BuildResponsePrompt(line).IndexOf("An owl", StringComparison.OrdinalIgnoreCase) >= 0);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void TranslatorBuddyService_LocalRouteFallbackIsAnswerSafe()
    {
        var go = new GameObject("TranslatorBuddyFallbackTest");
        try
        {
            var service = go.AddComponent<TranslatorBuddyService>();
            var line = new LocalizedDialogueLine { expectedEnglishResponse = "Big dog" };
            TranslatorBuddyHintResponse response = service.BuildLocalAiTutorFallback(line, SemanticZoneKind.Route);

            Assert.AreEqual("fallback", response.status);
            Assert.AreEqual("deterministic_fallback", response.provider);
            Assert.IsFalse(response.buddyResponse.IndexOf("Big dog", StringComparison.OrdinalIgnoreCase) >= 0);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void BuddyLanguageCatalog_ResolvesRegionalCodesAndLocales()
    {
        Assert.AreEqual("Tamil", BuddyLanguageCatalog.DisplayName("ta-IN"));
        Assert.AreEqual("Odia", BuddyLanguageCatalog.DisplayName("or_IN"));
        Assert.AreEqual("Hindi", BuddyLanguageCatalog.DisplayName("unsupported"));
        Assert.AreEqual(23, BuddyLanguageCatalog.Codes.Length);
    }

    [Test]
    public void TranslatorBuddyService_RemoteTtsRequiresExplicitCostOptIn()
    {
        var go = new GameObject("TranslatorBuddySpeechCostPolicyTest");
        try
        {
            var service = go.AddComponent<TranslatorBuddyService>();
            service.requestSpeechAudio = true;
            service.speechCostPolicy = TranslatorSpeechCostPolicy.CachedAndDeviceOnly;
            Assert.IsFalse(service.RemoteTtsAllowed);

            service.speechCostPolicy = TranslatorSpeechCostPolicy.RemoteTtsOptIn;
            Assert.IsTrue(service.RemoteTtsAllowed);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void BuddyPhonicsCueCatalog_UsesDeterministicAnchorWords()
    {
        Assert.AreEqual("apple", BuddyPhonicsCueCatalog.AnchorWordFor("short_a"));
        Assert.AreEqual("rat", BuddyPhonicsCueCatalog.AnchorWordFor("sound_r"));
        Assert.AreEqual("", BuddyPhonicsCueCatalog.AnchorWordFor("invented_sound"));
    }

    [Test]
    public void TranslatorBuddyService_BuildTutorFeedbackUsesConceptAwareWhyAndFix()
    {
        var go = new GameObject("TranslatorBuddyConceptFeedbackTest");
        try
        {
            var service = go.AddComponent<TranslatorBuddyService>();
            var cases = new[]
            {
                new
                {
                    line = new LocalizedDialogueLine
                    {
                        conceptId = GrammarConceptId.Articles,
                        expectedEnglishResponse = "An owl",
                        subskillId = "article_vowel_sound",
                    },
                    observed = "A owl",
                    expectedWhat = "article",
                    expectedWhy = "Use a before a consonant sound, an before a vowel sound, and the when the noun is specific.",
                    expectedFix = "An owl",
                },
                new
                {
                    line = new LocalizedDialogueLine
                    {
                        conceptId = GrammarConceptId.Pronouns,
                        expectedEnglishResponse = "They bite",
                        subskillId = "pronoun_replace_subject",
                    },
                    observed = "He bites",
                    expectedWhat = "pronoun",
                    expectedWhy = "Pronouns stand in for nouns so you do not repeat the full name every time.",
                    expectedFix = "They bite",
                },
                new
                {
                    line = new LocalizedDialogueLine
                    {
                        conceptId = GrammarConceptId.Plurals,
                        expectedEnglishResponse = "Puppies",
                        subskillId = "plural_ies",
                    },
                    observed = "Puppys",
                    expectedWhat = "noun number",
                    expectedWhy = "Plural endings change the noun to show more than one.",
                    expectedFix = "Puppies",
                },
                new
                {
                    line = new LocalizedDialogueLine
                    {
                        conceptId = GrammarConceptId.Adjectives,
                        expectedEnglishResponse = "Big rat",
                        subskillId = "adjective_before_noun",
                    },
                    observed = "Rat big",
                    expectedWhat = "describing word",
                    expectedWhy = "The adjective comes with the noun to describe it more clearly.",
                    expectedFix = "Big rat",
                },
                new
                {
                    line = new LocalizedDialogueLine
                    {
                        conceptId = GrammarConceptId.BasicPrepositions,
                        expectedEnglishResponse = "Rat is under the box.",
                        subskillId = "preposition_location",
                    },
                    observed = "Rat is behind the box.",
                    expectedWhat = "location word",
                    expectedWhy = "Prepositions change the location meaning of the sentence.",
                    expectedFix = "Rat is under the box.",
                },
            };

            foreach (var testCase in cases)
            {
                TutorFeedbackPlan feedback = service.BuildTutorFeedback(
                    testCase.line,
                    testCase.observed,
                    "gym_answer_mismatch");

                StringAssert.Contains(testCase.expectedWhat, feedback.whatWasWrong.ToLowerInvariant());
                Assert.AreEqual(testCase.expectedWhy, feedback.why);
                Assert.AreEqual(testCase.expectedFix, feedback.correctedResponse);
                Assert.AreEqual(TutorHintLevel.RuleHint, feedback.hintLevelShown);
                Assert.AreEqual(TutorRemediationStep.GuidedRetry, feedback.remediationStep);
            }
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void TranslatorBuddyService_RepeatedMissesEscalateToMicroLessonAndDrill()
    {
        var go = new GameObject("TranslatorBuddyEscalationTest");
        try
        {
            var service = go.AddComponent<TranslatorBuddyService>();
            var line = new LocalizedDialogueLine
            {
                conceptId = GrammarConceptId.Articles,
                expectedEnglishResponse = "An owl",
                subskillId = "article_vowel_sound",
            };

            TutorFeedbackPlan first = service.BuildTutorFeedback(line, "A owl", "gym_answer_mismatch");
            TutorFeedbackPlan second = service.BuildTutorFeedback(line, "A owl", "gym_answer_mismatch");
            TutorFeedbackPlan third = service.BuildTutorFeedback(line, "A owl", "gym_answer_mismatch");

            Assert.AreEqual(1, first.missCount);
            Assert.IsTrue(string.IsNullOrWhiteSpace(first.microLesson));
            Assert.AreEqual(TutorHintLevel.RuleHint, first.hintLevelShown);

            Assert.AreEqual(2, second.missCount);
            Assert.AreEqual(TutorHintLevel.MicroLesson, second.hintLevelShown);
            StringAssert.Contains("Rule: a rat, an owl, the cat.", second.microLesson);
            Assert.AreEqual(TutorRemediationStep.GuidedRetry, second.remediationStep);

            Assert.AreEqual(3, third.missCount);
            Assert.AreEqual(TutorHintLevel.MicroLesson, third.hintLevelShown);
            StringAssert.Contains("Practice again with: a rat / an owl / the dog.", third.microLesson);
            Assert.AreEqual(TutorRemediationStep.ExampleDrill, third.remediationStep);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void NpcDialogueUI_MissingWordRoutesAcceptBlankTokenOrFullAnswer()
    {
        LocalizedDialogueLine nounArticleLine = NaturalGrammarProgression.BuildGeneratedDialogue(
            SemanticZoneKind.Route,
            "Nouns",
            3,
            2,
            true);
        Assert.AreEqual("noun-article-summon", nounArticleLine.dialogueTaskId);
        Assert.IsTrue(InvokeIsAcceptedResponse(nounArticleLine, "A"));
        Assert.IsTrue(InvokeIsAcceptedResponse(nounArticleLine, "A rat"));
        Assert.IsTrue(InvokeIsAcceptedResponse(nounArticleLine, "The"));
        Assert.IsTrue(InvokeIsAcceptedResponse(nounArticleLine, "The rat"));
        Assert.IsFalse(InvokeIsAcceptedResponse(nounArticleLine, "An"));

        LocalizedDialogueLine adjectiveArticleLine = NaturalGrammarProgression.BuildGeneratedDialogue(
            SemanticZoneKind.Route,
            "Adjectives",
            6,
            2,
            true);
        Assert.AreEqual("adjective-article-route", adjectiveArticleLine.dialogueTaskId);
        Assert.IsTrue(InvokeIsAcceptedResponse(adjectiveArticleLine, "A"));
        Assert.IsTrue(InvokeIsAcceptedResponse(adjectiveArticleLine, "A big rat"));
        Assert.IsTrue(InvokeIsAcceptedResponse(adjectiveArticleLine, "The"));
        Assert.IsFalse(InvokeIsAcceptedResponse(adjectiveArticleLine, "Big"));

        LocalizedDialogueLine pronounLine = NaturalGrammarProgression.BuildGeneratedDialogue(
            SemanticZoneKind.Route,
            "Pronouns",
            4,
            0,
            true);
        Assert.AreEqual("pronoun-ticket-replace", pronounLine.dialogueTaskId);
        Assert.IsTrue(InvokeIsAcceptedResponse(pronounLine, "I"));
        Assert.IsTrue(InvokeIsAcceptedResponse(pronounLine, "I bite"));
        Assert.IsTrue(InvokeIsAcceptedResponse(pronounLine, "They"));
        Assert.IsFalse(InvokeIsAcceptedResponse(pronounLine, "He"));
    }

    [Test]
    public void NpcDialogueUI_AcceptedWrittenResponseClosesDialoguePanel()
    {
        var go = new GameObject("AcceptedWrittenDialogueUiTest");
        try
        {
            var ui = go.AddComponent<NpcDialogueUI>();
            var line = new LocalizedDialogueLine
            {
                lineId = "accepted-written-test",
                npcLine = "The route transcript dropped one word: a ____.",
                expectedEnglishResponse = "A rat",
                inputMode = GrammarDialogueInputMode.WriteOnly,
                malfunctionType = GrammarDialogueMalfunctionType.MissingWord,
            };

            ui.Show(null, line, null);
            TMP_InputField input = GetPrivateField<TMP_InputField>(ui, "textResponseInput");
            RectTransform panel = GetPrivateField<RectTransform>(ui, "panel");
            input.text = "A rat";
            Assert.IsTrue(panel.gameObject.activeSelf);

            MethodInfo method = typeof(NpcDialogueUI).GetMethod(
                "HandleTextResponsePressed",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            method.Invoke(ui, Array.Empty<object>());

            Assert.IsFalse(panel.gameObject.activeSelf);
        }
        finally
        {
            Object.DestroyImmediate(go);
            SetPrivateStaticField<NpcDialogueUI>("instance", null);
        }
    }

    [Test]
    public void NpcDialogueUI_WriteOnlyTaskRejectsLateSpeechRecognition()
    {
        var go = new GameObject("WriteOnlySpeechLeakDialogueUiTest");
        try
        {
            var ui = go.AddComponent<NpcDialogueUI>();
            var line = new LocalizedDialogueLine
            {
                lineId = "write-only-speech-leak-test",
                npcLine = "The route transcript dropped one word: a ____.",
                expectedEnglishResponse = "A rat",
                inputMode = GrammarDialogueInputMode.WriteOnly,
                malfunctionType = GrammarDialogueMalfunctionType.MissingWord,
            };

            ui.Show(null, line, null);
            RectTransform panel = GetPrivateField<RectTransform>(ui, "panel");
            TextMeshProUGUI status = GetPrivateField<TextMeshProUGUI>(ui, "responseStatusLabel");
            Assert.IsTrue(panel.gameObject.activeSelf);
            Assert.AreEqual("Type the clean English answer.", status.text);

            MethodInfo method = typeof(NpcDialogueUI).GetMethod(
                "HandleResponseResolved",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            method.Invoke(ui, new object[]
            {
                new VoiceUnlockRecognizer.RecognitionEvent(
                    VoiceUnlockRecognizer.VoiceInputMode.Manual,
                    true,
                    "A rat",
                    "A rat")
            });

            Assert.IsTrue(panel.gameObject.activeSelf);
            StringAssert.Contains("This task needs writing", status.text);
        }
        finally
        {
            Object.DestroyImmediate(go);
            SetPrivateStaticField<NpcDialogueUI>("instance", null);
        }
    }

    [Test]
    public void TranslatorAudioClipDecoder_DecodesPcm16Wav()
    {
        byte[] wav = BuildTinyPcm16Wav();

        Assert.IsTrue(TranslatorAudioClipDecoder.TryDecodeWav(wav, "TinyTtsClip", out AudioClip clip));
        Assert.NotNull(clip);
        Assert.AreEqual("TinyTtsClip", clip.name);
        Assert.AreEqual(8000, clip.frequency);
        Assert.AreEqual(1, clip.channels);
        Assert.AreEqual(4, clip.samples);
    }

    [Test]
    public void ProceduralGrammarSceneGenerator_TopicAndTierAffectSeed()
    {
        int articlesSeed = ProceduralGrammarSceneGenerator.ResolveDeterministicSeed(
            SemanticZoneKind.Town,
            "Articles",
            1,
            1234,
            true);
        int prepositionsSeed = ProceduralGrammarSceneGenerator.ResolveDeterministicSeed(
            SemanticZoneKind.Town,
            "Prepositions",
            1,
            1234,
            true);
        int tierTwoSeed = ProceduralGrammarSceneGenerator.ResolveDeterministicSeed(
            SemanticZoneKind.Town,
            "Articles",
            2,
            1234,
            true);

        Assert.AreNotEqual(articlesSeed, prepositionsSeed);
        Assert.AreNotEqual(articlesSeed, tierTwoSeed);
    }

    [Test]
    public void ProceduralGrammarSceneGenerator_TacticalRouteUsesAuthoredTrainerAndWildPools()
    {
        var go = new GameObject("GeneratedRoutePoolTest");
        try
        {
            var controller = go.AddComponent<GrammarSceneController>();
            controller.sceneKind = SemanticZoneKind.Route;
            controller.grammarTopic = "Verbs";
            controller.grammarTopicTier = 6;

            var generator = go.AddComponent<ProceduralGrammarSceneGenerator>();
            generator.sceneController = controller;
            generator.generateOnStart = false;
            generator.randomizeRoutesEveryGeneration = false;
            generator.routeNpcCount = 2;
            generator.routeTreasureCount = 0;
            generator.routeWildEncounterCount = 2;
            generator.Generate();

            GrammarNpc[] npcs = go.GetComponentsInChildren<GrammarNpc>(true);
            Assert.AreEqual(NaturalGrammarProgression.GetDialogueTaskIds(SemanticZoneKind.Route, "Verbs", 6).Length, npcs.Length);
            Assert.IsTrue(npcs[0].startsTrainerBattle);
            Assert.AreEqual(TranslatorAssistMode.Partial, npcs[0].translatorAssist);
            Assert.AreEqual(NaturalGrammarProgression.ResolveRouteNpcName("Verbs", 6, 0), npcs[0].displayName);
            Assert.AreEqual(NaturalGrammarProgression.ResolveTrainerBattleEnemyCount("Verbs", 6, 0), npcs[0].trainerEnemyCount);
            CollectionAssert.AreEquivalent(
                NaturalGrammarProgression.BuildTrainerBattleNounFamilies("Verbs", 6, 0),
                npcs[0].trainerEncounterNounFamilies);
            Assert.AreEqual("verb-animation-match", npcs[0].dialogueLines[0].dialogueTaskId);

            EnemyWaveTrigger[] triggers = go.GetComponentsInChildren<EnemyWaveTrigger>(true);
            Assert.AreEqual(2, triggers.Length);
            Assert.AreEqual(TranslatorAssistMode.Partial, triggers[0].translatorAssist);
            Assert.AreEqual(NaturalGrammarProgression.ResolveWildEncounterEnemyCount("Verbs", 6, 0), triggers[0].enemyCount);
            CollectionAssert.AreEquivalent(
                NaturalGrammarProgression.BuildWildEncounterNounFamilies("Verbs", 6, 0),
                triggers[0].encounterNounFamilies);
            Assert.AreEqual(NaturalGrammarProgression.ResolveWildEncounterEnemyCount("Verbs", 6, 1), triggers[1].enemyCount);
            CollectionAssert.AreEquivalent(
                NaturalGrammarProgression.BuildWildEncounterNounFamilies("Verbs", 6, 1),
                triggers[1].encounterNounFamilies);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void GrammarSceneController_DefaultNpcSpawnsHydrateFromNaturalProgression()
    {
        var go = new GameObject("NaturalNpcHydrationTest");
        try
        {
            var controller = go.AddComponent<GrammarSceneController>();
            controller.sceneKind = SemanticZoneKind.Route;
            controller.grammarTopic = "Nouns";
            controller.grammarTopicTier = 5;

            InvokeApplyNaturalProgressionDefaults(controller);

            string[] requiredTasks = NaturalGrammarProgression.GetDialogueTaskIds(SemanticZoneKind.Route, "Nouns", 5);
            Assert.AreEqual(requiredTasks.Length, controller.npcSpawns.Count);
            Assert.IsFalse(controller.npcSpawns[0].startsTrainerBattle);
            Assert.AreEqual(1, controller.npcSpawns[0].trainerEnemyCount);
            Assert.IsEmpty(controller.npcSpawns[0].trainerEncounterNounFamilies);
            Assert.AreEqual("noun-family-sort", controller.npcSpawns[0].dialogueLines[0].dialogueTaskId);
            Assert.AreEqual(TranslatorAssistMode.Partial, controller.npcSpawns[0].dialogueLines[0].assistMode);
            Assert.AreEqual(GrammarDialogueMalfunctionType.MissingWord, controller.npcSpawns[0].dialogueLines[0].malfunctionType);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void GrammarSceneController_CustomNpcSpawnsAreNotOverwrittenByNaturalProgression()
    {
        var go = new GameObject("CustomNpcHydrationTest");
        try
        {
            var controller = go.AddComponent<GrammarSceneController>();
            controller.sceneKind = SemanticZoneKind.Town;
            controller.grammarTopic = "Nouns";
            controller.grammarTopicTier = 5;
            controller.npcSpawns = new List<GrammarNpcSpawnDefinition>
            {
                new GrammarNpcSpawnDefinition
                {
                    npcId = "hand-authored-guide",
                    displayName = "Hand Authored Guide",
                    dialogueLines = new List<LocalizedDialogueLine>
                    {
                        new LocalizedDialogueLine
                        {
                            lineId = "custom-line",
                            sourceText = "Custom scene text.",
                            expectedEnglishResponse = "Custom answer",
                        },
                    },
                },
            };

            InvokeApplyNaturalProgressionDefaults(controller);

            Assert.AreEqual(1, controller.npcSpawns.Count);
            Assert.AreEqual("hand-authored-guide", controller.npcSpawns[0].npcId);
            Assert.AreEqual("custom-line", controller.npcSpawns[0].dialogueLines[0].lineId);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void ProceduralGrammarSceneGenerator_GymLeaderUsesNoAssistAndGymChecks()
    {
        var go = new GameObject("GeneratedGymLeaderTest");
        try
        {
            var controller = go.AddComponent<GrammarSceneController>();
            controller.sceneKind = SemanticZoneKind.Gym;
            controller.grammarTopic = "Verbs";
            controller.grammarTopicTier = 6;

            var generator = go.AddComponent<ProceduralGrammarSceneGenerator>();
            generator.sceneController = controller;
            generator.generateOnStart = false;
            generator.gymPropCount = 0;
            generator.Generate();

            GrammarNpc[] npcs = go.GetComponentsInChildren<GrammarNpc>(true);
            Assert.AreEqual(1, npcs.Length);
            GrammarNpc leader = npcs[0];
            Assert.AreEqual("generated-gym-leader", leader.npcId);
            Assert.IsTrue(leader.startsTrainerBattle);
            Assert.AreEqual(TranslatorAssistMode.Off, leader.translatorAssist);
            Assert.AreEqual(NaturalGrammarProgression.ResolveGymLeaderName("Verbs", 6), leader.displayName);
            Assert.GreaterOrEqual(leader.trainerEnemyCount, 4);
            CollectionAssert.AreEquivalent(
                NaturalGrammarProgression.BuildTrainerBattleNounFamilies("Verbs", 6, 1),
                leader.trainerEncounterNounFamilies);
            CollectionAssert.AreEqual(
                NaturalGrammarProgression.GetDialogueTaskIds(SemanticZoneKind.Gym, "Verbs", 6),
                leader.dialogueLines.ConvertAll(line => line.dialogueTaskId));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void ProceduralGrammarSceneGenerator_NounGymLeaderStartsBossEncounter()
    {
        var go = new GameObject("GeneratedNounGymLeaderTest");
        try
        {
            var controller = go.AddComponent<GrammarSceneController>();
            controller.sceneKind = SemanticZoneKind.Gym;
            controller.grammarTopic = "Nouns";
            controller.grammarTopicTier = 5;

            var generator = go.AddComponent<ProceduralGrammarSceneGenerator>();
            generator.sceneController = controller;
            generator.generateOnStart = false;
            generator.gymPropCount = 0;
            generator.Generate();

            GrammarNpc[] npcs = go.GetComponentsInChildren<GrammarNpc>(true);
            Assert.AreEqual(1, npcs.Length);
            GrammarNpc leader = npcs[0];
            Assert.IsTrue(leader.startsTrainerBattle);
            Assert.AreEqual(TranslatorAssistMode.Off, leader.translatorAssist);
            Assert.GreaterOrEqual(leader.trainerEnemyCount, 4);
            CollectionAssert.AreEquivalent(
                NaturalGrammarProgression.BuildTrainerBattleNounFamilies("Nouns", 5, 1),
                leader.trainerEncounterNounFamilies);
            CollectionAssert.AreEqual(
                NaturalGrammarProgression.GetDialogueTaskIds(SemanticZoneKind.Gym, "Nouns", 5),
                leader.dialogueLines.ConvertAll(line => line.dialogueTaskId));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void ProceduralGrammarSceneGenerator_AppliesRegionThemeAndMarker()
    {
        var go = new GameObject("GeneratedTownThemeTest");
        try
        {
            var controller = go.AddComponent<GrammarSceneController>();
            controller.sceneKind = SemanticZoneKind.Town;
            controller.grammarTopic = "Articles";
            controller.grammarTopicTier = 7;
            controller.translatorAssist = TranslatorAssistMode.Full;

            var generator = go.AddComponent<ProceduralGrammarSceneGenerator>();
            generator.sceneController = controller;
            generator.generateOnStart = false;
            generator.townBuildingCount = 0;
            generator.townNpcCount = 0;
            generator.Generate();

            NaturalGrammarRegion region = NaturalGrammarProgression.Resolve("Articles", 7);
            Renderer ground = Array.Find(
                go.GetComponentsInChildren<Renderer>(true),
                renderer => renderer != null && renderer.gameObject.name == "FallbackGround_Town");
            Assert.NotNull(ground);
            Color groundColor = ground.sharedMaterial != null ? ground.sharedMaterial.color : ground.material.color;
            Assert.That(groundColor.r, Is.EqualTo(region.groundTint.r).Within(0.001f));
            Assert.That(groundColor.g, Is.EqualTo(region.groundTint.g).Within(0.001f));
            Assert.That(groundColor.b, Is.EqualTo(region.groundTint.b).Within(0.001f));

            TextMeshPro title = Array.Find(
                go.GetComponentsInChildren<TextMeshPro>(true),
                text => text != null && text.gameObject.name == "SceneMarker_Title");
            TextMeshPro subtitle = Array.Find(
                go.GetComponentsInChildren<TextMeshPro>(true),
                text => text != null && text.gameObject.name == "SceneMarker_Subtitle");
            Assert.NotNull(title);
            Assert.NotNull(subtitle);
            Assert.AreEqual(region.displayName, title.text);
            StringAssert.Contains("Town", subtitle.text);
            StringAssert.Contains("Full Buddy", subtitle.text);
            StringAssert.Contains("Articles", subtitle.text);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void EnemyWaveTrigger_DoesNotConsumeWhenEncounterCannotStart()
    {
        var triggerGo = new GameObject("RetryableEnemyWaveTrigger");
        var playerGo = new GameObject("TriggerPlayer");
        var colliderGo = new GameObject("TriggerPlayerCollider");
        try
        {
            var trigger = triggerGo.AddComponent<EnemyWaveTrigger>();
            trigger.grammarTopic = "Nouns";
            trigger.grammarTopicTier = 5;

            playerGo.AddComponent<PlayerController>();
            colliderGo.transform.SetParent(playerGo.transform, false);
            var collider = colliderGo.AddComponent<BoxCollider>();

            LogAssert.Expect(LogType.Warning, "[EnemyWaveTrigger] No EnemyWaveDirector found; generated trigger cannot start an encounter.");
            MethodInfo handler = typeof(EnemyWaveTrigger).GetMethod(
                "OnTriggerEnter",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(handler);
            handler.Invoke(trigger, new object[] { collider });

            Assert.IsFalse(trigger.Consumed);
        }
        finally
        {
            Object.DestroyImmediate(colliderGo);
            Object.DestroyImmediate(playerGo);
            Object.DestroyImmediate(triggerGo);
        }
    }

    [Test]
    public void ProceduralGrammarSceneGenerator_BuildsExpectedRoadLines()
    {
        var rng = new System.Random(7);
        List<float> lines = ProceduralGrammarSceneGenerator.BuildAxisLines(3, 60f, rng);

        Assert.AreEqual(3, lines.Count);
        Assert.Less(lines[0], lines[1]);
        Assert.Less(lines[1], lines[2]);
        Assert.GreaterOrEqual(lines[0], -24f);
        Assert.LessOrEqual(lines[2], 24f);
    }

    [Test]
    public void GrammarWorldProgressService_BuildsStableAreaIds()
    {
        Assert.AreEqual(
            "TOWN:ARTICLES:8",
            GrammarWorldProgressService.BuildAreaId(SemanticZoneKind.Town, "Articles", 8));
        Assert.AreEqual(
            "GYM:BASICPREPOSITIONS:7",
            GrammarWorldProgressService.BuildAreaId(SemanticZoneKind.Gym, "basic prepositions", 7));
        Assert.AreEqual(
            "TOWN:PRONOUNS:10",
            GrammarWorldProgressService.BuildAreaId(SemanticZoneKind.Town, "Pronouns", 10));
        Assert.AreEqual(
            "GYM:ALPHABET:2",
            GrammarWorldProgressService.BuildAreaId(SemanticZoneKind.Gym, "Alphabet", 2));
        Assert.AreEqual(
            "ROUTE:VERBS:6",
            GrammarWorldProgressService.BuildAreaId(SemanticZoneKind.Route, "Verbs", 6));
        Assert.AreEqual(
            "GYM:PRONOUNS:8",
            GrammarWorldProgressService.CanonicalizeAreaId("GYM:PRONOUNS:10"));
    }

    [Test]
    public void NaturalGrammarProgression_GatesTacticalCombatUntilVerbs()
    {
        Assert.IsFalse(NaturalGrammarProgression.IsCombatUnlocked("Greetings and Survival English", 1));
        Assert.IsFalse(NaturalGrammarProgression.IsCombatUnlocked("Alphabet", 2));
        Assert.IsFalse(NaturalGrammarProgression.IsCombatUnlocked("Nouns", 5));
        Assert.IsTrue(NaturalGrammarProgression.IsCombatUnlocked("Verbs", 6));
        Assert.IsTrue(NaturalGrammarProgression.IsCombatUnlocked("Basic Prepositions", 7));
        Assert.IsFalse(NaturalGrammarProgression.IsCombatUnlocked("Plurals", 11));
        Assert.AreEqual(GrammarEncounterMode.LetterRecognition, NaturalGrammarProgression.ResolveEncounterMode("Alphabet", 2));
        Assert.AreEqual(GrammarEncounterMode.NounRecognition, NaturalGrammarProgression.ResolveEncounterMode("Nouns", 5));
        Assert.AreEqual(GrammarEncounterMode.TacticalCommand, NaturalGrammarProgression.ResolveEncounterMode("Verbs", 6));
        Assert.AreEqual(GrammarEncounterMode.TacticalCommand, NaturalGrammarProgression.ResolveEncounterMode("Basic Prepositions", 7));
        Assert.AreEqual(GrammarEncounterMode.None, NaturalGrammarProgression.ResolveEncounterMode("Plurals", 11));
        CollectionAssert.DoesNotContain(NaturalGrammarProgression.Resolve("Nouns", 5).unlockedPhrasePatterns, GrammarPhrasePattern.NounVerbPresent);
        CollectionAssert.Contains(NaturalGrammarProgression.Resolve("Verbs", 6).unlockedPhrasePatterns, GrammarPhrasePattern.NounVerbPresent);
        CollectionAssert.Contains(NaturalGrammarProgression.Resolve("Basic Prepositions", 7).unlockedPhrasePatterns, GrammarPhrasePattern.FullSentence);

        List<string> nounFamilies = NaturalGrammarProgression.BuildCurrentNounFamilies("Nouns", 5);
        Assert.GreaterOrEqual(nounFamilies.Count, 104);
        CollectionAssert.Contains(nounFamilies, "RAT");
        CollectionAssert.Contains(nounFamilies, "CAT");
        CollectionAssert.Contains(nounFamilies, "DOG");
        CollectionAssert.Contains(nounFamilies, "DUCK");
        CollectionAssert.Contains(nounFamilies, "OWL");
        CollectionAssert.Contains(nounFamilies, "QUAIL");
        CollectionAssert.Contains(nounFamilies, "PUP");
        CollectionAssert.Contains(nounFamilies, "SHOP");
        CollectionAssert.Contains(nounFamilies, "ROCK");

        CreatureCombatCatalog catalog = CreatureCombatCatalog.CreateRuntimeDefault();
        Assert.AreEqual(GrammarNounRole.Creature, catalog.nouns.Find(noun => noun.canonicalNoun == "RAT").nounRole);
        Assert.AreEqual(GrammarNounRole.Place, catalog.nouns.Find(noun => noun.canonicalNoun == "SHOP").nounRole);
        Assert.AreEqual(GrammarNounRole.Object, catalog.nouns.Find(noun => noun.canonicalNoun == "ROCK").nounRole);
        NounDefinition roof = catalog.nouns.Find(noun => noun.canonicalNoun == "ROOF");
        NounDefinition bridge = catalog.nouns.Find(noun => noun.canonicalNoun == "BRIDGE");
        Assert.NotNull(roof);
        Assert.NotNull(bridge);
        Assert.AreEqual(GrammarNounRole.Object, roof.nounRole);
        Assert.AreEqual(GrammarNounRole.Object, bridge.nounRole);
        Assert.IsFalse(roof.IsCreatureNoun);
        Assert.IsFalse(bridge.IsCreatureNoun);
        Assert.IsEmpty(roof.moveSet);
        Assert.IsEmpty(bridge.moveSet);
        CollectionAssert.DoesNotContain(NaturalGrammarProgression.BuildCurrentNounFamilies("Nouns", 5), "ROOF");
        CollectionAssert.DoesNotContain(NaturalGrammarProgression.BuildCurrentNounFamilies("Nouns", 5), "BRIDGE");
    }

    static byte[] BuildTinyPcm16Wav()
    {
        short[] samples = { 0, 1200, -1200, 0 };
        int dataBytes = samples.Length * 2;
        byte[] wav = new byte[44 + dataBytes];
        WriteAscii(wav, 0, "RIFF");
        WriteInt(wav, 4, 36 + dataBytes);
        WriteAscii(wav, 8, "WAVE");
        WriteAscii(wav, 12, "fmt ");
        WriteInt(wav, 16, 16);
        WriteShort(wav, 20, 1);
        WriteShort(wav, 22, 1);
        WriteInt(wav, 24, 8000);
        WriteInt(wav, 28, 8000 * 2);
        WriteShort(wav, 32, 2);
        WriteShort(wav, 34, 16);
        WriteAscii(wav, 36, "data");
        WriteInt(wav, 40, dataBytes);
        for (int i = 0; i < samples.Length; i++)
            WriteShort(wav, 44 + i * 2, samples[i]);
        return wav;
    }

    static void WriteAscii(byte[] buffer, int offset, string value)
    {
        for (int i = 0; i < value.Length; i++)
            buffer[offset + i] = (byte)value[i];
    }

    static void WriteInt(byte[] buffer, int offset, int value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        Array.Copy(bytes, 0, buffer, offset, bytes.Length);
    }

    static void WriteShort(byte[] buffer, int offset, short value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        Array.Copy(bytes, 0, buffer, offset, bytes.Length);
    }

    [Test]
    public void RunProgression_FinalStageAndSummaryHaveStableContracts()
    {
        CurriculumSessionManager curriculum = CurriculumSessionManager.EnsureExists();
        bool originalSchoolModeEnabled = curriculum.schoolModeEnabled;
        var go = new GameObject("RunProgressionTest");
        try
        {
            // This is an isolated economy/progression unit test. School mode
            // correctly blocks anonymous world-goal runs, so opt out here
            // rather than weakening that production access gate.
            curriculum.schoolModeEnabled = false;
            var manager = go.AddComponent<RunProgressionManager>();
            manager.finalStageNumber = 3;
            Assert.IsFalse(manager.ShouldEndAfterStage(2));
            Assert.IsTrue(manager.ShouldEndAfterStage(3));

            manager.EnsureRunActive();
            manager.AddCoins(12);
            Assert.IsTrue(manager.TrySpendCoins(5));
            manager.RecordEnemyDefeated();
            manager.EndRun(RunEndReason.Defeat);

            Assert.IsFalse(manager.RunActive);
            Assert.NotNull(manager.LastRunSummary);
            Assert.AreEqual(RunEndReason.Defeat, manager.LastRunSummary.reason);
            Assert.AreEqual(1, manager.LastRunSummary.enemiesDefeated);
            Assert.AreEqual(12, manager.LastRunSummary.coinsCollected);
            Assert.AreEqual(5, manager.LastRunSummary.coinsSpent);
            Assert.AreEqual(7, manager.LastRunSummary.CoinsRemaining);
        }
        finally
        {
            curriculum.schoolModeEnabled = originalSchoolModeEnabled;
            Object.DestroyImmediate(go);
        }
    }
}
#endif
