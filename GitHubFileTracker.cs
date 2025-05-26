using System;
using System.Collections.Generic;
using UnityEditor;

//public static class GitHubFileTracker
//{
//    public static HashSet<string> autoDetectedFiles = new HashSet<string>();
//    public static HashSet<string> deletedFiles = new HashSet<string>();

//    // 👇 Add this line
//    public static HashSet<string> manuallyRemovedFiles = new HashSet<string>();
//}

[InitializeOnLoad]
public static class GitHubFileTracker
{
    public static HashSet<string> alreadyPushedFiles = new();
    public static HashSet<string> manuallyRemovedFiles = new();

    static GitHubFileTracker()
    {
        LoadPushedFiles();
    }

    public static void SavePushedFiles()
    {
        EditorPrefs.SetString("GitHubPushedFiles", string.Join(";", alreadyPushedFiles));
    }

    public static void LoadPushedFiles()
    {
        string saved = EditorPrefs.GetString("GitHubPushedFiles", "");
        alreadyPushedFiles = new HashSet<string>(saved.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
    }
}
