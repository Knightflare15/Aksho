using System.Collections.Generic;
using NUnit.Framework;

public sealed class LearnerStateMergerTests
{
    [Test]
    public void RemoteConceptWithMoreEvidenceReplacesLocalAggregate()
    {
        BuddyLearnerStateRecord local = State("student-1", Concept("Articles", 2, 0.4f, "2026-07-10T10:00:00Z"));
        BuddyLearnerStateRecord remote = State("student-1", Concept("Articles", 5, 0.8f, "2026-07-11T10:00:00Z"));

        BuddyLearnerStateRecord merged = LearnerStateMerger.Merge(local, remote, "student-1");

        Assert.AreEqual(5, merged.concepts[0].attempts);
        Assert.AreEqual(0.8f, merged.concepts[0].masteryEstimate);
    }

    [Test]
    public void NewerOfflineLocalEvidenceIsNotOverwrittenByOlderServerAggregate()
    {
        BuddyLearnerStateRecord local = State("student-1", Concept("Articles", 6, 0.85f, "2026-07-12T10:00:00Z"));
        BuddyLearnerStateRecord remote = State("student-1", Concept("Articles", 5, 0.8f, "2026-07-11T10:00:00Z"));

        BuddyLearnerStateRecord merged = LearnerStateMerger.Merge(local, remote, "student-1");

        Assert.AreEqual(6, merged.concepts[0].attempts);
        Assert.AreEqual(0.85f, merged.concepts[0].masteryEstimate);
    }

    [Test]
    public void RecentAttemptsAreDeduplicatedByImmutableEventId()
    {
        BuddyLearnerStateRecord local = State("student-1");
        local.recentAttempts.Add(Attempt("same", "2026-07-10T10:00:00Z"));
        local.recentAttempts.Add(Attempt("local", "2026-07-12T10:00:00Z"));
        BuddyLearnerStateRecord remote = State("student-1");
        remote.recentAttempts.Add(Attempt("same", "2026-07-10T10:00:00Z"));
        remote.recentAttempts.Add(Attempt("remote", "2026-07-11T10:00:00Z"));

        BuddyLearnerStateRecord merged = LearnerStateMerger.Merge(local, remote, "student-1");

        Assert.AreEqual(3, merged.recentAttempts.Count);
        Assert.AreEqual("local", merged.recentAttempts[2].eventId);
    }

    [Test]
    public void StateForAnotherStudentIsRejected()
    {
        BuddyLearnerStateRecord local = State("student-1", Concept("Articles", 2, 0.4f, "2026-07-10T10:00:00Z"));
        BuddyLearnerStateRecord remote = State("student-2", Concept("Articles", 8, 0.9f, "2026-07-12T10:00:00Z"));

        BuddyLearnerStateRecord merged = LearnerStateMerger.Merge(local, remote, "student-1");

        Assert.AreEqual(2, merged.concepts[0].attempts);
    }

    static BuddyLearnerStateRecord State(string studentId, params BuddyConceptLearningStateRecord[] concepts)
    {
        return new BuddyLearnerStateRecord
        {
            studentId = studentId,
            concepts = new List<BuddyConceptLearningStateRecord>(concepts),
            recentAttempts = new List<BuddyLearningAttemptRecord>()
        };
    }

    static BuddyConceptLearningStateRecord Concept(string id, int attempts, float mastery, string practicedAt)
    {
        return new BuddyConceptLearningStateRecord
        {
            conceptId = id,
            attempts = attempts,
            masteryEstimate = mastery,
            lastPracticedAtUtc = practicedAt
        };
    }

    static BuddyLearningAttemptRecord Attempt(string id, string createdAt)
    {
        return new BuddyLearningAttemptRecord { eventId = id, createdAtUtc = createdAt };
    }
}
