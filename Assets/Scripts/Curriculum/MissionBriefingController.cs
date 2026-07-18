using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class MissionBriefingController : MonoBehaviour
{
    public CurriculumSessionManager curriculumSession;
    public RunProgressionManager runProgression;
    public TextMeshProUGUI titleLabel;
    public TextMeshProUGUI lettersLabel;
    public TextMeshProUGUI wordsLabel;
    public TextMeshProUGUI durationLabel;
    public Button startRunButton;
    public bool loadMissionOnEnable = true;

    MissionAssignment mission;

    void Awake()
    {
        curriculumSession ??= CurriculumSessionManager.EnsureExists();
        runProgression ??= RunProgressionManager.EnsureExists();
        if (startRunButton != null)
            startRunButton.onClick.AddListener(StartRun);
    }

    void OnEnable()
    {
        if (loadMissionOnEnable)
            LoadMission();
    }

    public void LoadMission()
    {
        curriculumSession ??= CurriculumSessionManager.EnsureExists();
        mission = curriculumSession.LoadWorldGoalPractice();
        Refresh();
    }

    public void StartRun()
    {
        if (mission == null)
            LoadMission();

        runProgression ??= RunProgressionManager.EnsureExists();
        runProgression.StartWorldGoalPractice();
    }

    void Refresh()
    {
        if (mission == null)
            return;

        WorldGoalAssignment goal = curriculumSession != null ? curriculumSession.CurrentWorldGoal : null;
        if (goal != null)
        {
            if (titleLabel != null)
                titleLabel.text = "Class Focus";
            if (lettersLabel != null)
                lettersLabel.text = "Suggested checkpoint: " + HumanizeArea(goal.targetGymId);
            if (wordsLabel != null)
                wordsLabel.text = "Practice: " + Join(goal.focusVocabulary);
            if (durationLabel != null)
                durationLabel.text = $"Suggested by: {Fallback(goal.dueDate, "this week")} - Run time: {Mathf.RoundToInt(mission.missionDurationSeconds / 60f)} minutes";
            return;
        }

        LearnerScheduleRecommendation recommendation = curriculumSession != null
            ? curriculumSession.RefreshLearnerRecommendation(false)
            : null;
        if (recommendation != null && !string.IsNullOrWhiteSpace(recommendation.conceptId))
        {
            if (titleLabel != null)
                titleLabel.text = recommendation.isReviewDue ? "Memory Review" : "Your Next Best Practice";
            if (lettersLabel != null)
                lettersLabel.text = $"Go to: {Fallback(recommendation.regionDisplayName, recommendation.conceptId)}";
            if (wordsLabel != null)
                wordsLabel.text = $"Activity: {HumanizeActivity(recommendation.activityType)} · {TitleCase(recommendation.modality)}";
            if (durationLabel != null)
                durationLabel.text = recommendation.reason;
            return;
        }

        if (titleLabel != null)
            titleLabel.text = "Legacy Daily Mission";
        if (lettersLabel != null)
            lettersLabel.text = "Letters: " + Join(mission.lettersForToday);
        if (wordsLabel != null)
            wordsLabel.text = "Words: " + Join(mission.wordsForToday);
        if (durationLabel != null)
            durationLabel.text = $"Time: {Mathf.RoundToInt(mission.missionDurationSeconds / 60f)} minutes";
    }

    static string Join(System.Collections.Generic.List<string> values)
    {
        if (values == null || values.Count == 0)
            return "None";

        var builder = new StringBuilder();
        for (int i = 0; i < values.Count; i++)
        {
            if (i > 0)
                builder.Append(", ");
            builder.Append(values[i]);
        }
        return builder.ToString();
    }

    static string HumanizeArea(string areaId)
    {
        if (string.IsNullOrWhiteSpace(areaId))
            return "Next gym";

        string[] parts = areaId.Split(':');
        if (parts.Length < 2)
            return areaId;

        string kind = TitleCase(parts[0]);
        string topic = SplitAllCaps(parts[1]);
        return string.IsNullOrWhiteSpace(topic) ? kind : $"{topic} {kind}";
    }

    static string SplitAllCaps(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        string normalized = value.Trim().Replace("_", " ");
        string[] known =
        {
            "GREETINGSANDSURVIVALENGLISH",
            "ALPHABET",
            "VOWELSANDCONSONANTS",
            "SENTENCESTARTANDFULLSTOP",
            "NOUNS",
            "VERBS",
            "ARTICLES",
            "PRONOUNS",
            "PLURALS",
            "ADJECTIVES",
            "BASICPREPOSITIONS",
        };
        foreach (string token in known)
        {
            if (!string.Equals(normalized, token, System.StringComparison.OrdinalIgnoreCase))
                continue;
            return token switch
            {
                "GREETINGSANDSURVIVALENGLISH" => "Greetings",
                "ALPHABET" => "Alphabet",
                "VOWELSANDCONSONANTS" => "Vowels and Consonants",
                "SENTENCESTARTANDFULLSTOP" => "Sentence Start and Full Stop",
                "NOUNS" => "Nouns",
                "VERBS" => "Verbs",
                "ARTICLES" => "Articles",
                "PRONOUNS" => "Pronouns",
                "PLURALS" => "Plurals",
                "ADJECTIVES" => "Adjectives",
                "BASICPREPOSITIONS" => "Basic Prepositions",
                _ => normalized,
            };
        }

        return TitleCase(normalized.ToLowerInvariant());
    }

    static string TitleCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        string lower = value.Trim().ToLowerInvariant();
        return char.ToUpperInvariant(lower[0]) + lower.Substring(1);
    }

    static string Fallback(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    static string HumanizeActivity(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Practice";
        return TitleCase(value.Replace('_', ' '));
    }
}
