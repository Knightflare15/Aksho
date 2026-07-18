public readonly struct CurriculumProviderContext
{
    public readonly CurriculumProviderMode Mode;
    public readonly string FirebaseProjectId;
    public readonly string FirebaseStorageBucket;
    public readonly string FirebaseFunctionsBaseUrl;
    public readonly string PronunciationAnalysisBaseUrl;
    public readonly string SchoolId;
    public readonly string ClassId;
    public readonly string StudentIdToken;

    public CurriculumProviderContext(
        CurriculumProviderMode mode,
        string firebaseProjectId,
        string firebaseStorageBucket,
        string firebaseFunctionsBaseUrl,
        string pronunciationAnalysisBaseUrl,
        string schoolId,
        string classId,
        string studentIdToken)
    {
        Mode = mode;
        FirebaseProjectId = firebaseProjectId ?? "";
        FirebaseStorageBucket = firebaseStorageBucket ?? "";
        FirebaseFunctionsBaseUrl = firebaseFunctionsBaseUrl ?? "";
        PronunciationAnalysisBaseUrl = pronunciationAnalysisBaseUrl ?? "";
        SchoolId = schoolId ?? "";
        ClassId = classId ?? "";
        StudentIdToken = studentIdToken ?? "";
    }
}

public interface ICurriculumProviderFactory
{
    ICurriculumAccessProvider Create(CurriculumProviderContext context);
}

/// <summary>
/// The only production composition point for curriculum persistence. A new
/// backend can be introduced by injecting another factory into
/// CurriculumSessionManager instead of modifying the game loop.
/// </summary>
public sealed class DefaultCurriculumProviderFactory : ICurriculumProviderFactory
{
    public ICurriculumAccessProvider Create(CurriculumProviderContext context)
    {
        if (context.Mode == CurriculumProviderMode.FirebaseRest)
        {
            return new FirebaseCurriculumProvider(
                context.FirebaseProjectId,
                context.FirebaseStorageBucket,
                context.FirebaseFunctionsBaseUrl,
                context.PronunciationAnalysisBaseUrl,
                context.SchoolId,
                context.ClassId,
                context.StudentIdToken);
        }

        return new LocalDemoCurriculumProvider();
    }
}
