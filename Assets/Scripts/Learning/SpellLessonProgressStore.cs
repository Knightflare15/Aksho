using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Persists a lightweight per-word summary for the first lesson slices.
/// This is intentionally simple so we can start gathering useful learning
/// signals before the full adaptive system lands.
/// </summary>
public class SpellLessonProgressStore
{
    [Serializable]
    private class ProgressFile
    {
        public List<WordProgress> words = new List<WordProgress>();
    }

    [Serializable]
    private class WordProgress
    {
        public string word;
        public int attempts;
        public int successes;
        public float totalDurationSeconds;
        public float totalSpeechUnlockSeconds;
        public int totalWrongLetters;
        public int totalGuideCorrections;
        public int totalGiftedLetters;
        public string lastPlayedAtUtc;
    }

    public struct RunRecord
    {
        public string word;
        public bool success;
        public float durationSeconds;
        public float speechUnlockSeconds;
        public int wrongLetters;
        public int guideCorrections;
        public int giftedLetters;
    }

    private static string SavePath =>
        PlayerSaveSlots.GetSaveFilePath("spell_lesson_progress.json");

    public static string SaveFilePath => SavePath;

    public static void DeleteProgressSave()
    {
        try
        {
            if (File.Exists(SavePath))
                File.Delete(SavePath);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[SpellLessonProgressStore] Delete failed: " + ex.Message);
        }
    }

    public void Record(RunRecord runRecord)
    {
        var file = Load();
        var stats = file.words.Find(entry => entry.word == runRecord.word);
        if (stats == null)
        {
            stats = new WordProgress { word = runRecord.word };
            file.words.Add(stats);
        }

        stats.attempts++;
        if (runRecord.success)
            stats.successes++;

        stats.totalDurationSeconds += runRecord.durationSeconds;
        stats.totalSpeechUnlockSeconds += runRecord.speechUnlockSeconds;
        stats.totalWrongLetters += runRecord.wrongLetters;
        stats.totalGuideCorrections += runRecord.guideCorrections;
        stats.totalGiftedLetters += runRecord.giftedLetters;
        stats.lastPlayedAtUtc = DateTime.UtcNow.ToString("o");

        Save(file);
    }

    private static ProgressFile Load()
    {
        if (!File.Exists(SavePath))
            return new ProgressFile();

        try
        {
            return JsonUtility.FromJson<ProgressFile>(File.ReadAllText(SavePath)) ?? new ProgressFile();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[SpellLessonProgressStore] Load failed: " + ex.Message);
            return new ProgressFile();
        }
    }

    private static void Save(ProgressFile file)
    {
        try
        {
            PlayerSaveSlots.EnsureActiveSlotDirectory();
            File.WriteAllText(SavePath, JsonUtility.ToJson(file, true));
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[SpellLessonProgressStore] Save failed: " + ex.Message);
        }
    }
}
