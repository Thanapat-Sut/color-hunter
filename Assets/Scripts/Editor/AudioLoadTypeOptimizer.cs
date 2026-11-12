using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Startup speed: every AudioClip was "Decompress On Load", so opening a game scene decoded all
/// its BGM + voice to PCM before the first frame. This switches only the clips used by the game
/// scenes enabled in Build Settings:
///   length >= StreamSeconds   -> Streaming            (BGM)
///   length >= CompressSeconds -> Compressed In Memory (voice, long stingers)
///   shorter                   -> unchanged            (short SFX must start instantly)
/// The previous load types are saved to D:\casual-game\tmp\audio-loadtype-backup-*.csv and
/// Tools > Boot > Revert Audio Load Types restores the newest backup.
/// </summary>
public static class AudioLoadTypeOptimizer
{
    const float StreamSeconds = 20f;
    const float CompressSeconds = 1.5f;
    static string BackupDir => Path.GetFullPath(Path.Combine(Application.dataPath, "../../tmp"));

    [MenuItem("Tools/Boot/Optimize Audio Load Types (enabled game scenes)")]
    static void Optimize()
    {
        var scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled && Path.GetFileNameWithoutExtension(s.path) != "Boot")
            .Select(s => s.path).ToArray();
        if (scenes.Length == 0) { Debug.LogWarning("[AudioOpt] no game scene enabled in Build Settings"); return; }

        var clips = AssetDatabase.GetDependencies(scenes, true)
            .Where(p => AssetImporter.GetAtPath(p) is AudioImporter).Distinct().ToList();

        var backup = new List<string> { "path,loadType" };
        int streamed = 0, compressed = 0;
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var path in clips)
            {
                var importer = (AudioImporter)AssetImporter.GetAtPath(path);
                var settings = importer.defaultSampleSettings;
                if (settings.loadType != AudioClipLoadType.DecompressOnLoad) continue;

                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null) continue;

                AudioClipLoadType target;
                if (clip.length >= StreamSeconds) { target = AudioClipLoadType.Streaming; streamed++; }
                else if (clip.length >= CompressSeconds) { target = AudioClipLoadType.CompressedInMemory; compressed++; }
                else continue;

                backup.Add($"\"{path}\",{settings.loadType}");
                settings.loadType = target;
                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        string file = WriteBackup(backup);
        Debug.Log($"[AudioOpt] {string.Join(", ", scenes.Select(Path.GetFileNameWithoutExtension))}: " +
                  $"{clips.Count} clips, {streamed} -> Streaming, {compressed} -> Compressed In Memory. Backup: {file}");
    }

    [MenuItem("Tools/Boot/Revert Audio Load Types (newest backup)")]
    static void Revert()
    {
        var file = Directory.Exists(BackupDir)
            ? Directory.GetFiles(BackupDir, "audio-loadtype-backup-*.csv").OrderBy(f => f).LastOrDefault()
            : null;
        if (file == null) { Debug.LogWarning($"[AudioOpt] no backup in {BackupDir}"); return; }

        int restored = 0;
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var line in File.ReadAllLines(file).Skip(1))
            {
                int comma = line.LastIndexOf(',');
                string path = line.Substring(0, comma).Trim('"');
                if (!(AssetImporter.GetAtPath(path) is AudioImporter importer)) continue;
                if (!System.Enum.TryParse(line.Substring(comma + 1), out AudioClipLoadType loadType)) continue;

                var settings = importer.defaultSampleSettings;
                settings.loadType = loadType;
                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
                restored++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }
        File.Move(file, file + ".reverted");
        Debug.Log($"[AudioOpt] restored {restored} clips from {file}");
    }

    static string WriteBackup(List<string> lines)
    {
        Directory.CreateDirectory(BackupDir);
        string file = Path.Combine(BackupDir, $"audio-loadtype-backup-{System.DateTime.Now:yyyyMMdd-HHmmss}.csv");
        File.WriteAllLines(file, lines);
        return file;
    }
}
