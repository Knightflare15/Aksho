using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class GrammarConceptManager
{
    readonly ContentDatabase database;

    public GrammarConceptManager(ContentDatabase database)
    {
        this.database = database;
    }

    public GrammarConcept GetConcept(string conceptId)
    {
        return database != null && database.TryGetConcept(conceptId, out GrammarConcept concept) ? concept : null;
    }
}

public sealed class SupportLevelManager
{
    public SupportLevel DetermineSupportLevel(ConceptProgress conceptProgress)
    {
        if (conceptProgress == null || conceptProgress.attempts == 0)
            return SupportLevel.HighSupport;

        if (conceptProgress.consecutiveWrongAnswers >= 2)
            return SupportLevel.HighSupport;

        if (conceptProgress.masteryScore >= 0.85f && conceptProgress.correctAttempts >= 5)
            return SupportLevel.AudioOnly;

        if (conceptProgress.masteryScore >= 0.6f && conceptProgress.correctAttempts >= 3)
            return SupportLevel.LowSupport;

        if (conceptProgress.masteryScore >= 0.2f && conceptProgress.correctAttempts >= 1)
            return SupportLevel.MediumSupport;

        return SupportLevel.HighSupport;
    }
}

public sealed class HintManager
{
    public HintData GetNextHint(DialogueLine line, int hintIndex)
    {
        if (line == null)
            return null;

        if (line.hints != null && hintIndex >= 0 && hintIndex < line.hints.Count)
            return line.hints[hintIndex];

        if (!string.IsNullOrWhiteSpace(line.localLanguageHint) && hintIndex == (line.hints != null ? line.hints.Count : 0))
            return new HintData { text = line.localLanguageHint, isLocalLanguageHint = true };

        return null;
    }
}

public sealed class AnswerCheckRequest
{
    public DialogueLine line;
    public TaskType taskType;
    public SupportLevel supportLevel;
    public string learnerAnswer = "";
}

public sealed class AnswerChecker
{
    public AnswerCheckResult CheckAnswer(AnswerCheckRequest request)
    {
        if (request == null || request.line == null)
        {
            return new AnswerCheckResult
            {
                feedbackMessage = "No task is active.",
                detectedErrorType = AnswerErrorType.UnsupportedTask,
                shouldShowHint = true,
            };
        }

        string normalizedAnswer = Normalize(request.learnerAnswer);
        if (string.IsNullOrWhiteSpace(normalizedAnswer))
        {
            return new AnswerCheckResult
            {
                feedbackMessage = "Please try an answer.",
                detectedErrorType = AnswerErrorType.EmptyAnswer,
                shouldShowHint = true,
            };
        }

        switch (request.taskType)
        {
            case TaskType.SentenceJumble:
                return CheckSentenceJumble(request.line, normalizedAnswer);
            case TaskType.FillBlank:
                return CheckAgainstAllowedSet(request.line, normalizedAnswer, includePartialAnswers: true, wrongWordOrderMessage: false);
            case TaskType.FullSentenceListen:
            case TaskType.BlankReconstruction:
            case TaskType.SpokenAnswer:
            case TaskType.WrittenAnswer:
            case TaskType.SpokenAndWrittenAnswer:
                return CheckAgainstAllowedSet(request.line, normalizedAnswer, includePartialAnswers: ShouldAllowPartial(request.supportLevel), wrongWordOrderMessage: request.taskType == TaskType.BlankReconstruction);
            default:
                return new AnswerCheckResult
                {
                    feedbackMessage = "This task type is not supported yet.",
                    detectedErrorType = AnswerErrorType.UnsupportedTask,
                    shouldShowHint = true,
                };
        }
    }

    AnswerCheckResult CheckSentenceJumble(DialogueLine line, string normalizedAnswer)
    {
        string canonicalExpected = ResolvePrimaryExpectedAnswer(line);
        DialogueJumbleEvaluation evaluation = DialogueSentenceJumble.Evaluate(
            Tokenize(normalizedAnswer),
            canonicalExpected);
        if (evaluation.isCorrect)
        {
            return new AnswerCheckResult
            {
                isCorrect = true,
                score = 1f,
                feedbackMessage = "Correct order.",
                matchedAnswer = canonicalExpected,
                jumbleEvaluation = evaluation,
            };
        }

        List<string> answerTokens = Tokenize(normalizedAnswer);
        List<string> expectedTokens = Tokenize(canonicalExpected);
        if (answerTokens.Count == expectedTokens.Count && HaveSameTokenBag(answerTokens, expectedTokens))
        {
            return new AnswerCheckResult
            {
                score = expectedTokens.Count > 0
                    ? (float)evaluation.correctPositionCount / expectedTokens.Count
                    : 0f,
                feedbackMessage = DialogueSentenceJumble.BuildFeedback(evaluation),
                detectedErrorType = AnswerErrorType.WrongWordOrder,
                shouldShowHint = true,
                jumbleEvaluation = evaluation,
            };
        }

        return new AnswerCheckResult
        {
            score = expectedTokens.Count > 0
                ? (float)evaluation.correctPositionCount / expectedTokens.Count
                : 0f,
            feedbackMessage = DialogueSentenceJumble.BuildFeedback(evaluation),
            detectedErrorType = evaluation.submittedWordCount < evaluation.expectedWordCount && evaluation.distractorCount == 0
                ? AnswerErrorType.MissingWord
                : AnswerErrorType.IncorrectAnswer,
            shouldShowHint = true,
            jumbleEvaluation = evaluation,
        };
    }

    AnswerCheckResult CheckAgainstAllowedSet(DialogueLine line, string normalizedAnswer, bool includePartialAnswers, bool wrongWordOrderMessage)
    {
        foreach (string expected in BuildAcceptedAnswers(line, includePartialAnswers))
        {
            string normalizedExpected = Normalize(expected);
            if (normalizedAnswer == normalizedExpected)
            {
                float score = line.partialAcceptedAnswers.Exists(value => Normalize(value) == normalizedAnswer) ? 0.7f : 1f;
                return new AnswerCheckResult
                {
                    isCorrect = true,
                    score = score,
                    feedbackMessage = score >= 1f ? "Correct." : "Accepted for this support level.",
                    matchedAnswer = expected,
                };
            }
        }

        if (wrongWordOrderMessage)
        {
            List<string> answerTokens = Tokenize(normalizedAnswer);
            foreach (string expected in BuildAcceptedAnswers(line, includePartialAnswers: false))
            {
                List<string> expectedTokens = Tokenize(expected);
                if (answerTokens.Count == expectedTokens.Count && HaveSameTokenBag(answerTokens, expectedTokens))
                {
                    return new AnswerCheckResult
                    {
                        feedbackMessage = "The words are there, but the order is off.",
                        detectedErrorType = AnswerErrorType.WrongWordOrder,
                        shouldShowHint = true,
                    };
                }
            }
        }

        return new AnswerCheckResult
        {
            feedbackMessage = "That answer does not match yet.",
            detectedErrorType = line.partialAcceptedAnswers != null && line.partialAcceptedAnswers.Count > 0
                ? AnswerErrorType.MissingWord
                : AnswerErrorType.IncorrectAnswer,
            shouldShowHint = true,
        };
    }

    static List<string> BuildAcceptedAnswers(DialogueLine line, bool includePartialAnswers)
    {
        var accepted = new List<string>();
        if (line == null)
            return accepted;

        AddAll(accepted, line.expectedAnswers);
        AddAll(accepted, line.allowedAnswers, excludePartialAnswers: !includePartialAnswers, partialAnswers: line.partialAcceptedAnswers);
        if (includePartialAnswers)
            AddAll(accepted, line.partialAcceptedAnswers);
        return accepted;
    }

    static void AddAll(
        List<string> destination,
        List<string> values,
        bool excludePartialAnswers = false,
        List<string> partialAnswers = null)
    {
        if (destination == null || values == null)
            return;

        foreach (string value in values)
        {
            string normalized = Normalize(value);
            if (excludePartialAnswers && partialAnswers != null &&
                partialAnswers.Exists(partial => Normalize(partial) == normalized))
                continue;
            if (!string.IsNullOrWhiteSpace(normalized) && !destination.Exists(existing => Normalize(existing) == normalized))
                destination.Add(value);
        }
    }

    static string ResolvePrimaryExpectedAnswer(DialogueLine line)
    {
        if (line == null || line.expectedAnswers == null || line.expectedAnswers.Count == 0)
            return "";
        return Normalize(line.expectedAnswers[0]);
    }

    static bool HaveSameTokenBag(List<string> left, List<string> right)
    {
        if (left == null || right == null || left.Count != right.Count)
            return false;

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (string value in left)
        {
            counts.TryGetValue(value, out int current);
            counts[value] = current + 1;
        }

        foreach (string value in right)
        {
            if (!counts.TryGetValue(value, out int current) || current <= 0)
                return false;
            counts[value] = current - 1;
        }

        return true;
    }

    static bool ShouldAllowPartial(SupportLevel supportLevel)
    {
        return supportLevel == SupportLevel.HighSupport || supportLevel == SupportLevel.MediumSupport;
    }

    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        string trimmed = value.Trim().ToLowerInvariant();
        var builder = new System.Text.StringBuilder(trimmed.Length);
        bool previousWasSpace = false;
        foreach (char character in trimmed)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasSpace = false;
            }
            else if (char.IsWhiteSpace(character))
            {
                if (!previousWasSpace)
                {
                    builder.Append(' ');
                    previousWasSpace = true;
                }
            }
        }

        return builder.ToString().Trim();
    }

    public static List<string> Tokenize(string value)
    {
        var result = new List<string>();
        string normalized = Normalize(value);
        if (string.IsNullOrWhiteSpace(normalized))
            return result;

        string[] parts = normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string part in parts)
            result.Add(part);
        return result;
    }
}

public sealed class ProgressTracker
{
    readonly Dictionary<string, ConceptProgress> conceptLookup = new Dictionary<string, ConceptProgress>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, WordProgressRecord> wordLookup = new Dictionary<string, WordProgressRecord>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<TaskType, TaskTypeProgressRecord> taskLookup = new Dictionary<TaskType, TaskTypeProgressRecord>();
    readonly ILearnerProgressPersistence persistence;

    public ProgressTracker(ILearnerProgressPersistence persistence = null)
    {
        this.persistence = persistence;
    }

    public ConceptProgress GetConceptProgress(string conceptId)
    {
        if (string.IsNullOrWhiteSpace(conceptId))
            return null;

        conceptLookup.TryGetValue(conceptId, out ConceptProgress progress);
        return progress;
    }

    public void RecordHintUsed(string conceptId)
    {
        ConceptProgress progress = GetOrCreateConcept(conceptId);
        progress.hintUsageCount++;
        progress.isStrong = false;
    }

    public ConceptProgress RecordAttempt(DialogueLine line, TaskType taskType, SupportLevel supportLevel, AnswerCheckResult result, bool usedHint)
    {
        string conceptId = line != null ? line.conceptId : "";
        ConceptProgress conceptProgress = GetOrCreateConcept(conceptId);
        conceptProgress.attempts++;
        conceptProgress.lastSupportLevelUsed = supportLevel;

        TaskTypeProgressRecord taskProgress = GetOrCreateTask(taskType);
        taskProgress.attempts++;

        if (result != null && result.isCorrect)
        {
            conceptProgress.correctAttempts++;
            conceptProgress.consecutiveWrongAnswers = 0;
            taskProgress.correctAttempts++;
            conceptProgress.masteryScore = Mathf.Clamp01(conceptProgress.masteryScore + (usedHint ? 0.08f : 0.2f));
        }
        else
        {
            conceptProgress.wrongAttempts++;
            conceptProgress.consecutiveWrongAnswers++;
            taskProgress.wrongAttempts++;
            conceptProgress.masteryScore = Mathf.Clamp01(conceptProgress.masteryScore - 0.04f);
            PushRecentMistake(conceptProgress, result != null ? result.feedbackMessage : "Wrong answer");
        }

        // Hint usage is recorded when the hint is actually presented. The
        // attempt carries usedHint only to reduce mastery gain, avoiding a
        // duplicate analytics count when a learner answers after that hint.

        conceptProgress.isWeak = conceptProgress.consecutiveWrongAnswers >= 2 || (conceptProgress.attempts >= 3 && conceptProgress.masteryScore < 0.35f);
        conceptProgress.isStrong = conceptProgress.correctAttempts >= 3 && conceptProgress.masteryScore >= 0.75f && conceptProgress.consecutiveWrongAnswers == 0;

        if (line != null && line.targetWords != null)
        {
            foreach (string word in line.targetWords)
            {
                WordProgressRecord wordProgress = GetOrCreateWord(word);
                wordProgress.attempts++;
                if (result != null && result.isCorrect)
                    wordProgress.correctAttempts++;
                else
                    wordProgress.wrongAttempts++;
            }
        }

        return conceptProgress;
    }

    public LearnerProgressSummary BuildSummary()
    {
        return new LearnerProgressSummary
        {
            concepts = new List<ConceptProgress>(conceptLookup.Values),
            words = new List<WordProgressRecord>(wordLookup.Values),
            taskTypes = new List<TaskTypeProgressRecord>(taskLookup.Values),
        };
    }

    public void Save(string learnerId)
    {
        if (persistence != null)
            persistence.Save(learnerId, BuildSummary());
    }

    ConceptProgress GetOrCreateConcept(string conceptId)
    {
        string key = string.IsNullOrWhiteSpace(conceptId) ? "unknown" : conceptId.Trim();
        if (conceptLookup.TryGetValue(key, out ConceptProgress progress))
            return progress;

        progress = new ConceptProgress { conceptId = key, masteryScore = 0f };
        conceptLookup[key] = progress;
        return progress;
    }

    WordProgressRecord GetOrCreateWord(string word)
    {
        string key = AnswerChecker.Normalize(word);
        if (wordLookup.TryGetValue(key, out WordProgressRecord progress))
            return progress;

        progress = new WordProgressRecord { word = key };
        wordLookup[key] = progress;
        return progress;
    }

    TaskTypeProgressRecord GetOrCreateTask(TaskType taskType)
    {
        if (taskLookup.TryGetValue(taskType, out TaskTypeProgressRecord progress))
            return progress;

        progress = new TaskTypeProgressRecord { taskType = taskType };
        taskLookup[taskType] = progress;
        return progress;
    }

    static void PushRecentMistake(ConceptProgress progress, string message)
    {
        if (progress == null || string.IsNullOrWhiteSpace(message))
            return;

        progress.recentMistakes.Add(message);
        if (progress.recentMistakes.Count > 5)
            progress.recentMistakes.RemoveAt(0);
    }
}

public sealed class EnergyManager
{
    public int CurrentEnergy { get; private set; } = 20;

    public bool TryConsumeTaskEnergy(int amount = 1)
    {
        if (CurrentEnergy < Mathf.Max(1, amount))
            return false;

        CurrentEnergy -= Mathf.Max(1, amount);
        return true;
    }
}

public sealed class TeacherFeedbackManager
{
    public TeacherFeedbackSummary BuildSummary(ProgressTracker tracker)
    {
        LearnerProgressSummary summary = tracker != null ? tracker.BuildSummary() : new LearnerProgressSummary();
        var feedback = new TeacherFeedbackSummary();

        int totalAttempts = 0;
        int totalCorrect = 0;
        foreach (ConceptProgress concept in summary.concepts)
        {
            if (concept == null)
                continue;

            totalAttempts += concept.attempts;
            totalCorrect += concept.correctAttempts;
            feedback.hintsUsed += concept.hintUsageCount;
            if (concept.isWeak)
                feedback.weakConcepts.Add(concept.conceptId);
            if (concept.isStrong)
                feedback.strongConcepts.Add(concept.conceptId);
            foreach (string mistake in concept.recentMistakes)
            {
                feedback.recentMistakes.Add($"{concept.conceptId}: {mistake}");
                if (feedback.recentMistakes.Count >= 6)
                    break;
            }
        }

        feedback.accuracy = totalAttempts > 0 ? (float)totalCorrect / totalAttempts : 0f;
        return feedback;
    }
}
