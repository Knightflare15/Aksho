using UnityEngine;

public partial class TranslatorBuddyService : MonoBehaviour
{
    public bool CanStartBuddyRealtimeCall(LocalizedDialogueLine dialogueLine, SemanticZoneKind zoneKind)
    {
        return string.IsNullOrEmpty(GetBuddyCallBlockReason(dialogueLine, zoneKind));
    }

    string GetBuddyCallBlockReason(LocalizedDialogueLine dialogueLine, SemanticZoneKind zoneKind)
    {
        if (!enableAiTutor) return "buddy_disabled";
        if (dialogueLine == null) return "missing_dialogue_line";
        if (!dialogueLine.allowAiHint) return "buddy_not_allowed_for_line";
        if (zoneKind == SemanticZoneKind.Gym) return "gym_is_model_blocked";

        TranslatorAssistMode effectiveMode = dialogueLine.overrideAssistMode
            ? dialogueLine.assistMode
            : currentAssistMode;
        if (effectiveMode == TranslatorAssistMode.Off) return "assist_mode_off";

        CurriculumSessionManager curriculum = CurriculumSessionManager.EnsureExists();
        if (!curriculum.HasStudentSession) return "no_authenticated_student_session";
        if (string.IsNullOrWhiteSpace(BuddyRealtimeFunctionUrl.Resolve(curriculum))) return "missing_firebase_functions_url";
        if (string.IsNullOrWhiteSpace(curriculum.studentIdToken)) return "missing_firebase_id_token";
        return "";
    }

    public TranslatorBuddyHintResponse BuildLocalAiTutorFallback(
        LocalizedDialogueLine dialogueLine,
        SemanticZoneKind zoneKind)
    {
        bool town = zoneKind == SemanticZoneKind.Town;
        string message = town
            ? "Buddy is offline for a moment. Read the teaching note and try the English sentence slowly."
            : "Buddy is offline for a moment. Use the grammar clue and make one small change before trying again.";
        return new TranslatorBuddyHintResponse
        {
            buddyResponse = message,
            learnerText = message,
            speechText = message,
            status = "fallback",
            fallbackReason = "client_offline",
            hintLevel = town ? "rule_hint" : "clue",
            provider = "deterministic_fallback",
            errorCategory = "",
            teacherNote = "Local Buddy fallback was shown.",
            reportable = false,
        };
    }
}
