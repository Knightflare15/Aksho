using System;
using System.Collections.Generic;

/// <summary>
/// Reconciles the server aggregate with the device cache without adding aggregate
/// counters together. Both sides can contain the same uploaded events, so a sum
/// would double-count. The record with stronger evidence wins per concept, while
/// recent events are merged by immutable event id.
/// </summary>
public static class LearnerStateMerger
{
    const int RecentAttemptLimit = 20;

    public static BuddyLearnerStateRecord Merge(
        BuddyLearnerStateRecord local,
        BuddyLearnerStateRecord remote,
        string expectedStudentId)
    {
        if (local == null)
            local = NewState(expectedStudentId);
        if (remote == null || !SameStudent(remote.studentId, expectedStudentId))
            return Normalize(local, expectedStudentId);

        Normalize(local, expectedStudentId);
        Normalize(remote, expectedStudentId);
        local.schemaVersion = Math.Max(local.schemaVersion, remote.schemaVersion);
        local.sourceEventCount = Math.Max(local.sourceEventCount, remote.sourceEventCount);
        local.correctAttemptCount = Math.Max(local.correctAttemptCount, remote.correctAttemptCount);
        local.independentCorrectAttemptCount = Math.Max(local.independentCorrectAttemptCount, remote.independentCorrectAttemptCount);
        local.totalHints = Math.Max(local.totalHints, remote.totalHints);
        local.lastEventAtUtc = Latest(local.lastEventAtUtc, remote.lastEventAtUtc);
        local.updatedAtUtc = Latest(local.updatedAtUtc, remote.updatedAtUtc);

        var localByConcept = new Dictionary<string, BuddyConceptLearningStateRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (BuddyConceptLearningStateRecord concept in local.concepts)
        {
            if (concept != null && !string.IsNullOrWhiteSpace(concept.conceptId))
                localByConcept[concept.conceptId.Trim()] = concept;
        }

        foreach (BuddyConceptLearningStateRecord remoteConcept in remote.concepts)
        {
            if (remoteConcept == null || string.IsNullOrWhiteSpace(remoteConcept.conceptId))
                continue;
            string conceptId = remoteConcept.conceptId.Trim();
            if (!localByConcept.TryGetValue(conceptId, out BuddyConceptLearningStateRecord localConcept))
            {
                local.concepts.Add(remoteConcept);
                localByConcept[conceptId] = remoteConcept;
                continue;
            }

            if (PreferRemote(localConcept, remoteConcept))
            {
                int index = local.concepts.IndexOf(localConcept);
                if (index >= 0)
                    local.concepts[index] = remoteConcept;
                localByConcept[conceptId] = remoteConcept;
            }
        }

        local.recentAttempts = MergeRecentAttempts(local.recentAttempts, remote.recentAttempts);
        return local;
    }

    static bool PreferRemote(BuddyConceptLearningStateRecord local, BuddyConceptLearningStateRecord remote)
    {
        if (remote.attempts != local.attempts)
            return remote.attempts > local.attempts;
        return CompareUtc(remote.lastPracticedAtUtc, local.lastPracticedAtUtc) > 0;
    }

    static List<BuddyLearningAttemptRecord> MergeRecentAttempts(
        List<BuddyLearningAttemptRecord> local,
        List<BuddyLearningAttemptRecord> remote)
    {
        var byId = new Dictionary<string, BuddyLearningAttemptRecord>(StringComparer.OrdinalIgnoreCase);
        AddAttempts(byId, local);
        AddAttempts(byId, remote);
        var merged = new List<BuddyLearningAttemptRecord>(byId.Values);
        merged.Sort((left, right) => CompareUtc(left?.createdAtUtc, right?.createdAtUtc));
        if (merged.Count > RecentAttemptLimit)
            merged.RemoveRange(0, merged.Count - RecentAttemptLimit);
        return merged;
    }

    static void AddAttempts(
        Dictionary<string, BuddyLearningAttemptRecord> target,
        IEnumerable<BuddyLearningAttemptRecord> attempts)
    {
        if (attempts == null)
            return;
        foreach (BuddyLearningAttemptRecord attempt in attempts)
        {
            if (attempt == null || string.IsNullOrWhiteSpace(attempt.eventId))
                continue;
            string id = attempt.eventId.Trim();
            if (!target.TryGetValue(id, out BuddyLearningAttemptRecord existing) ||
                CompareUtc(attempt.createdAtUtc, existing.createdAtUtc) > 0)
                target[id] = attempt;
        }
    }

    static BuddyLearnerStateRecord Normalize(BuddyLearnerStateRecord state, string studentId)
    {
        state.studentId = string.IsNullOrWhiteSpace(studentId) ? state.studentId : studentId.Trim();
        state.concepts ??= new List<BuddyConceptLearningStateRecord>();
        state.strengthConceptIds ??= new List<string>();
        state.needConceptIds ??= new List<string>();
        state.recurringErrorTags ??= new List<string>();
        state.recentAttempts ??= new List<BuddyLearningAttemptRecord>();
        return state;
    }

    static BuddyLearnerStateRecord NewState(string studentId)
    {
        return new BuddyLearnerStateRecord { studentId = studentId ?? "" };
    }

    static bool SameStudent(string actual, string expected)
    {
        return !string.IsNullOrWhiteSpace(actual) &&
            !string.IsNullOrWhiteSpace(expected) &&
            string.Equals(actual.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    static string Latest(string left, string right)
    {
        return CompareUtc(left, right) >= 0 ? left : right;
    }

    static int CompareUtc(string left, string right)
    {
        bool hasLeft = DateTime.TryParse(left, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime leftUtc);
        bool hasRight = DateTime.TryParse(right, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime rightUtc);
        if (!hasLeft) return hasRight ? -1 : 0;
        if (!hasRight) return 1;
        return DateTime.Compare(leftUtc.ToUniversalTime(), rightUtc.ToUniversalTime());
    }
}
