// Add this at the top if not already present
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using Unity.Plastic.Newtonsoft.Json;
using System.Threading.Tasks;
using System;

public class GitHubUpdater : EditorWindow
{
    private string RepositoryOwner;
    private string RepositoryName;
    private string Token;

    private const string OwnerKey = "GitHubUploader_RepoOwner";
    private const string RepoKey = "GitHubUploader_RepoName";
    private const string TokenKey = "GitHubUploader_Token";


    private VersionHistory versionHistory = new VersionHistory();
    private bool showHistory = false;

    private List<string> selectedFiles = new List<string>(); // Files to push
    private HashSet<string> alreadyPushedFiles = new HashSet<string>(); // Track pushed files
    private string pushedFilesPath => Path.Combine(Application.persistentDataPath, "pushed_files.json");
    private string historyFilePath => Path.Combine(Application.persistentDataPath, "GitHubUpdater_History.json");

    private string version = "1.0";
    private string whatsNew = "";
    private float progress = 0f;
    private bool isPushing = false;
    private bool pushCompleted = false;

    private Vector2 selectedFilesScrollPos = Vector2.zero;
    private Vector2 historyScrollPos = Vector2.zero;

    private FileHashData fileHashData = new FileHashData();
    private string fileHashPath => Path.Combine(Application.persistentDataPath, "GitHubUpdater_FileHashes.json");
    private string uploadStatusLabel = "";

    private GUIStyle titleStyle;
    private GUIStyle sectionHeaderStyle;
    private GUIStyle buttonStyle;
    private GUIStyle labelStyle;
    private GUIStyle greenLabelStyle;
    private GUIStyle boxStyle;

    private Vector2 scrollPos;


    [MenuItem("Tools/GitHub Updater")]
    public static void ShowWindow()
    {
        var window = GetWindow<GitHubUpdater>("GitHub Updater");
        window.LoadHistory();
        window.LoadPushedFiles();
        window.LoadFileHashes();

    }
    private void OnEnable()
    {
        RepositoryOwner = EditorPrefs.GetString(OwnerKey, "your-default-owner");
        RepositoryName = EditorPrefs.GetString(RepoKey, "your-default-repo");
        Token = EditorPrefs.GetString(TokenKey, "your-default-token");
    }

    private void OnDisable()
    {
        EditorPrefs.SetString(OwnerKey, RepositoryOwner);
        EditorPrefs.SetString(RepoKey, RepositoryName);
        EditorPrefs.SetString(TokenKey, Token);
    }
    private void DeleteGitHubSettings()
    {
        EditorPrefs.DeleteKey("your-default-token");
        EditorPrefs.DeleteKey("your-default-owner");
        EditorPrefs.DeleteKey("your-default-repo");

        Token = "";
        RepositoryOwner = "";
        RepositoryName = "";

        Debug.Log("GitHub settings deleted.");
    }
    private void LoadFileHashes()
    {
        if (File.Exists(fileHashPath))
        {
            string json = File.ReadAllText(fileHashPath);
            fileHashData = JsonConvert.DeserializeObject<FileHashData>(json);
        }
    }

    private void SaveFileHashes()
    {
        string json = JsonConvert.SerializeObject(fileHashData, Formatting.Indented);
        File.WriteAllText(fileHashPath, json);
    }

    private void LoadHistory()
    {
        if (File.Exists(historyFilePath))
        {
            string json = File.ReadAllText(historyFilePath);
            versionHistory = JsonConvert.DeserializeObject<VersionHistory>(json);
        }
    }

    private void LoadPushedFiles()
    {
        if (File.Exists(pushedFilesPath))
        {
            string json = File.ReadAllText(pushedFilesPath);
            alreadyPushedFiles = JsonConvert.DeserializeObject<HashSet<string>>(json);
        }
    }
    private void InitStyles()
    {
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(EditorStyles.boldLabel);
            titleStyle.fontSize = 18;
            titleStyle.alignment = TextAnchor.MiddleCenter;
            titleStyle.margin = new RectOffset(0, 0, 10, 10);

            sectionHeaderStyle = new GUIStyle(EditorStyles.boldLabel);
            sectionHeaderStyle.fontSize = 14;
            sectionHeaderStyle.normal.textColor = Color.cyan;

            buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.fontSize = 12;
            buttonStyle.fixedHeight = 28;

            labelStyle = new GUIStyle(EditorStyles.label);
            labelStyle.wordWrap = true;

            greenLabelStyle = new GUIStyle(labelStyle);
            greenLabelStyle.normal.textColor = Color.green;

            boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.padding = new RectOffset(8, 8, 8, 8);
        }
    }
    private void AddSavedAutoTrackedFilesToSelected()
    {
        // Load the saved auto tracked files into GitHubFileTracker.autoTrackedFiles
        GitHubFileTracker.LoadAutoTrackedFilesFromDisk();

        // Add each loaded file to selectedFiles if it's not already there
        foreach (string path in GitHubFileTracker.autoTrackedFiles)
        {
            if (!selectedFiles.Contains(path))
            {
                selectedFiles.Add(path);
            }
        }
    }

    private void OnGUI()
    {
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        InitStyles();

        GUILayout.Space(8);
        GUILayout.Label("GitHub Updater", titleStyle);

        SyncAutoDetectedFiles();

        EditorGUILayout.LabelField("GitHub Settings", EditorStyles.boldLabel);
        RepositoryOwner = EditorGUILayout.TextField("Repository Owner", RepositoryOwner);
        RepositoryName = EditorGUILayout.TextField("Repository Name", RepositoryName);
        Token = EditorGUILayout.TextField("Token", Token);

        EditorGUILayout.Space();

        if (GUILayout.Button("Save GitHub Settings"))
        {
            EditorPrefs.SetString(OwnerKey, RepositoryOwner);
            EditorPrefs.SetString(RepoKey, RepositoryName);
            EditorPrefs.SetString(TokenKey, Token);
            Debug.Log("GitHub settings saved.");
        }

        if (GUILayout.Button("Delete GitHub Settings"))
        {
            DeleteGitHubSettings();
        }

        EditorGUILayout.BeginVertical(boxStyle);

        GUILayout.Label("Select Files or Folder to Upload", sectionHeaderStyle);

        GUILayout.BeginHorizontal();

        if (GUILayout.Button("Add Files", buttonStyle))
        {
            string filePath = EditorUtility.OpenFilePanel("Select File", Application.dataPath, "*");
            if (!string.IsNullOrEmpty(filePath)) AddFile(filePath);
        }
        if (GUILayout.Button("Add Folder", buttonStyle))
        {
            string folderPath = EditorUtility.OpenFolderPanel("Select Folder", Application.dataPath, "");
            if (!string.IsNullOrEmpty(folderPath)) AddFolder(folderPath);
        }
        if (GUILayout.Button("Show New Changes", buttonStyle))
        {
            AddSavedAutoTrackedFilesToSelected();
        }
        GUILayout.EndHorizontal();


        GUILayout.Space(10);
        DrawDragAndDropArea();

        GUILayout.Space(8);

        GUILayout.Label("Selected Files", sectionHeaderStyle);
        selectedFilesScrollPos = EditorGUILayout.BeginScrollView(selectedFilesScrollPos, GUILayout.Height(160));
        if (selectedFiles.Count == 0)
        {
            GUILayout.Label("No files selected.", labelStyle);
        }
        else
        {
            for (int i = 0; i < selectedFiles.Count; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(selectedFiles[i], labelStyle, GUILayout.ExpandWidth(true));
                GUI.backgroundColor = Color.red;
                if (GUILayout.Button("✕", GUILayout.Width(26), GUILayout.Height(18)))
                {
                    GitHubFileTracker.manuallyRemovedFiles.Add(selectedFiles[i]);
                    selectedFiles.RemoveAt(i);
                    i--;
                }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();
            }
        }
        EditorGUILayout.EndScrollView();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Clear Selected Files", buttonStyle))
        {
            selectedFiles.Clear();
        }
        if (GUILayout.Button("Show Current Version", buttonStyle))
        {
            version = GetCurrentVersionFromFile();
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(10);

        version = EditorGUILayout.TextField("Version:", version);
        GUILayout.Label("What's New:", sectionHeaderStyle);
        whatsNew = EditorGUILayout.TextArea(whatsNew, GUILayout.Height(80));

        GUILayout.Space(10);

        EditorGUILayout.Space();
       


        EditorGUI.BeginDisabledGroup(isPushing);

        if (GUILayout.Button("Push to GitHub", GUILayout.Height(30)))
        {
            _ = PushToGitHubAsync();
        }
        EditorGUI.EndDisabledGroup();

        if (isPushing)
        {
            GUILayout.Space(10);
            EditorGUILayout.LabelField(uploadStatusLabel, sectionHeaderStyle);
            Rect progressRect = GUILayoutUtility.GetRect(18, 18);
            EditorGUI.ProgressBar(progressRect, progress, $"{Mathf.RoundToInt(progress * 100)}%");
            GUILayout.Space(10);
        }
        else if (pushCompleted)
        {
            GUILayout.Space(10);
            GUILayout.Label("Push Completed!", greenLabelStyle);
        }

        GUILayout.Space(10);
        if (GUILayout.Button(showHistory ? "Hide History" : "Show History", buttonStyle))
        {
            showHistory = !showHistory;
        }

        if (showHistory)
        {
            GUILayout.Label("Push History", sectionHeaderStyle);
            historyScrollPos = EditorGUILayout.BeginScrollView(historyScrollPos, GUILayout.Height(200));
            if (versionHistory.entries.Count == 0)
            {
                GUILayout.Label("No history available.", labelStyle);
            }
            else
            {
                foreach (var entry in versionHistory.entries)
                {
                    EditorGUILayout.BeginVertical(boxStyle);
                    GUILayout.Label($"Version: {entry.version}", EditorStyles.boldLabel);
                    GUILayout.Label($"Date: {entry.dateTime}", EditorStyles.miniLabel);
                    GUILayout.Label("Notes:", EditorStyles.boldLabel);
                    GUILayout.Label(entry.whatsNew, labelStyle);
                    EditorGUILayout.EndVertical();
                    GUILayout.Space(4);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.EndScrollView();
    }

    private void DrawDragAndDropArea()
    {
        Rect dropArea = GUILayoutUtility.GetRect(0, 50, GUILayout.ExpandWidth(true));
        GUI.Box(dropArea, "Drag & Drop Files or Folders Here", boxStyle);

        Event evt = Event.current;
        if (!dropArea.Contains(evt.mousePosition)) return;

        switch (evt.type)
        {
            case EventType.DragUpdated:
            case EventType.DragPerform:
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    foreach (var obj in DragAndDrop.objectReferences)
                    {
                        string path = AssetDatabase.GetAssetPath(obj);
                        if (Directory.Exists(path))
                            AddFolder(Path.Combine(Application.dataPath, path.Replace("Assets/", "")));
                        else
                            AddFile(Path.Combine(Application.dataPath, path.Replace("Assets/", "")));
                    }
                }
                evt.Use();
                break;
        }
    }
    private void SyncAutoDetectedFiles()
    {
        List<string> newlyAutoTracked = new List<string>();

        foreach (string path in GitHubFileTracker.autoDetectedFiles)
        {
            if (!selectedFiles.Contains(path) &&
                !GitHubFileTracker.manuallyRemovedFiles.Contains(path))
            {
                selectedFiles.Add(path);
                newlyAutoTracked.Add(path);

                string metaPath = path + ".meta";
                if (!selectedFiles.Contains(metaPath) &&
                    !GitHubFileTracker.manuallyRemovedFiles.Contains(metaPath))
                {
                    selectedFiles.Add(metaPath);
                    newlyAutoTracked.Add(metaPath);
                }
            }
        }

        foreach (string deletedPath in GitHubFileTracker.deletedFiles)
        {
            selectedFiles.Remove(deletedPath);
            selectedFiles.Remove(deletedPath + ".meta");
            newlyAutoTracked.Remove(deletedPath);
            newlyAutoTracked.Remove(deletedPath + ".meta");
        }

        SaveAutoTrackedFiles(newlyAutoTracked);

        GitHubFileTracker.autoDetectedFiles.Clear();
        GitHubFileTracker.deletedFiles.Clear();
    }
    private void SaveAutoTrackedFiles(List<string> autoTracked)
    {
        foreach (string path in autoTracked)
        {
            if (!GitHubFileTracker.autoTrackedFiles.Contains(path))
            {
                GitHubFileTracker.autoTrackedFiles.Add(path);
            }
        }

        GitHubFileTracker.SaveAutoTrackedFilesToDisk();
    }


    private void HandleDragAndDrop()
    {
        Event evt = Event.current;
        if (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform)
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (var obj in DragAndDrop.objectReferences)
                {
                    string path = AssetDatabase.GetAssetPath(obj);
                    if (Directory.Exists(path))
                        AddFolder(Path.Combine(Application.dataPath, path.Replace("Assets/", "")));
                    else
                        AddFile(Path.Combine(Application.dataPath, path.Replace("Assets/", "")));
                }
            }
            evt.Use();
        }
    }

    private void AddFile(string absolutePath)
    {
        string relativePath = "Assets" + absolutePath.Replace(Application.dataPath, "").Replace("\\", "/");

        if (!selectedFiles.Contains(relativePath))
        {
            selectedFiles.Add(relativePath);
        }

        string metaPath = absolutePath + ".meta";
        string metaRelative = relativePath + ".meta";

        if (File.Exists(metaPath) && !selectedFiles.Contains(metaRelative))
        {
            selectedFiles.Add(metaRelative);
        }
    }

    private void AddFolder(string folderPath)
    {
        if (!string.IsNullOrEmpty(folderPath))
        {
            string[] files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories);
            foreach (string file in files) AddFile(file);
        }
    }

    private string GetCurrentVersionFromFile()
    {
        string path = "Assets/version.txt";
        return File.Exists(path) ? File.ReadAllLines(path)[0] : "Unknown";
    }
    private async Task PushToGitHubAsync()
    {
        isPushing = true;
        pushCompleted = false;
        progress = 0f;
        uploadStatusLabel = "Starting upload...";
        Repaint();

        string commitMessage = "Updating files to version " + version + " " + whatsNew;
        string repoOwner = RepositoryOwner;
        string repoName = RepositoryName;
        string token = Token;

        bool versionUpdated = PushVersionFile(repoOwner, repoName, token, version, commitMessage);

        if (versionUpdated)
        {
            // Collect all files: main + meta
            List<string> filesToUpload = new List<string>();
            foreach (string filePath in selectedFiles)
            {
                filesToUpload.Add(filePath);

                string metaPath = filePath + ".meta";
                string absMetaPath = Path.Combine(Application.dataPath, metaPath.Replace("Assets/", ""));
                if (File.Exists(absMetaPath))
                    filesToUpload.Add(metaPath);
            }

            int total = filesToUpload.Count;
            int processed = 0;

            foreach (string filePath in filesToUpload)
            {
                string absPath = Path.Combine(Application.dataPath, filePath.Replace("Assets/", ""));
                if (!File.Exists(absPath))
                {
                    Debug.LogWarning($"Skipped missing file: {filePath}");
                    continue;
                }

                // Hash only for main files
                if (!filePath.EndsWith(".meta"))
                {
                    string fileHash = GetFileHash(absPath);
                    fileHashData.fileHashes[filePath] = fileHash;
                }

                uploadStatusLabel = $"Uploading {processed + 1}/{total} files...";
                Repaint();

                bool success = await Task.Run(() => PushFileToGitHub(repoOwner, repoName, token, filePath, commitMessage));

                if (success)
                {
                    alreadyPushedFiles.Add(filePath);

                    if (GitHubFileTracker.autoTrackedFiles.Contains(filePath))
                    {
                        GitHubFileTracker.autoTrackedFiles.Remove(filePath);
                        GitHubFileTracker.SaveAutoTrackedFilesToDisk();
                    }
                }
                else
                {
                    Debug.LogError($"Failed to upload: {filePath}");
                }

                processed++;
                progress = (float)processed / total;
                uploadStatusLabel = $"Uploading {processed}/{total} files...";
                Repaint();

                // Save progress after each file
                File.WriteAllText(pushedFilesPath, JsonConvert.SerializeObject(alreadyPushedFiles, Formatting.Indented));
            }

            SaveHistoryEntry(version, whatsNew);
        }
        else
        {
            Debug.LogError("Version update failed. Files not pushed.");
            uploadStatusLabel = "Upload failed.";
        }

        SaveFileHashes();
        selectedFiles.Clear();
        GitHubFileTracker.manuallyRemovedFiles.Clear();

        isPushing = false;
        pushCompleted = true;
        uploadStatusLabel = "Upload completed!";
        Repaint();
    }
    private void SaveHistoryEntry(string version, string whatsNew)
    {
        versionHistory.entries.Insert(0, new VersionHistoryEntry
        {
            version = version,
            whatsNew = whatsNew,
            dateTime = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        });

        string json = JsonConvert.SerializeObject(versionHistory, Formatting.Indented);
        File.WriteAllText(historyFilePath, json);
    }

    private bool PushVersionFile(string owner, string repo, string token, string version, string message)
    {
        string path = "Assets/version.txt";
        string absPath = Path.Combine(Application.dataPath, "version.txt");
        File.WriteAllText(absPath, $"{version}\n\nWhat's New:\n{whatsNew}");
        AssetDatabase.ImportAsset(path);
        AssetDatabase.Refresh();

        return PushFileToGitHub(owner, repo, token, path, message);
    }
    private bool PushFileToGitHub(string owner, string repo, string token, string relativePath, string message)
    {
        string absPath = Path.Combine(Application.dataPath, relativePath.Replace("Assets/", ""));

        if (!File.Exists(absPath))
        {
            Debug.LogError($"File not found: {absPath}");
            return false;
        }

        string repoPath = relativePath.Replace("Assets/", "").TrimStart('/');

        if (!UploadToGitHub(owner, repo, token, absPath, repoPath, message))
            return false;

        return true;
    }

    private string GetFileSHA(string owner, string repo, string path, string token)
    {
        try
        {
            string url = $"https://api.github.com/repos/{owner}/{repo}/contents/{path}";
            using (WebClient client = new WebClient())
            {
                client.Headers.Add("Authorization", "token " + token);
                client.Headers.Add("User-Agent", "UnityGitHubUploader");
                string response = client.DownloadString(url);
                var json = JsonConvert.DeserializeObject<Dictionary<string, object>>(response);
                return json.ContainsKey("sha") ? json["sha"].ToString() : null;
            }
        }
        catch { return null; }
    }
    private string GetFileHash(string filePath)
    {
        using (var md5 = System.Security.Cryptography.MD5.Create())
        {
            using (var stream = File.OpenRead(filePath))
            {
                var hash = md5.ComputeHash(stream);
                return System.BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }
    }

    private bool UploadToGitHub(string owner, string repo, string token, string localPath, string repoPath, string message)
    {
        string content = System.Convert.ToBase64String(File.ReadAllBytes(localPath));
        string sha = GetFileSHA(owner, repo, repoPath, token);

        var payload = new Dictionary<string, object>
        {
            { "message", message },
            { "content", content }
        };

        if (!string.IsNullOrEmpty(sha)) payload["sha"] = sha;

        string json = JsonConvert.SerializeObject(payload);
        string url = $"https://api.github.com/repos/{owner}/{repo}/contents/{repoPath}";

        try
        {
            using (WebClient client = new WebClient())
            {
                client.Headers.Add("Authorization", "token " + token);
                client.Headers.Add("User-Agent", "UnityGitHubUploader");
                client.Headers.Add("Content-Type", "application/json");
                client.UploadString(url, "PUT", json);
                return true;
            }
        }
        catch (WebException ex)
        {
            Debug.LogError($"Upload failed for {repoPath}: {ex.Message}");
            return false;
        }
    }
}

[System.Serializable]
public class VersionHistoryEntry
{
    public string version;
    public string whatsNew;
    public string dateTime;
}

[System.Serializable]
public class VersionHistory
{
    public List<VersionHistoryEntry> entries = new List<VersionHistoryEntry>();
}


//// Add this at the top if not already present
//using UnityEditor;
//using UnityEngine;
//using System.Collections.Generic;
//using System.IO;
//using System.Net;
//using System.Text;
//using Unity.Plastic.Newtonsoft.Json;
//using System.Threading.Tasks;

//public class GitHubUpdater : EditorWindow
//{
//    private VersionHistory versionHistory = new VersionHistory();
//    private bool showHistory = false;

//    private List<string> selectedFiles = new List<string>(); // Files to push
//    private HashSet<string> alreadyPushedFiles = new HashSet<string>(); // Track pushed files
//    private string pushedFilesPath => Path.Combine(Application.persistentDataPath, "pushed_files.json");
//    private string historyFilePath => Path.Combine(Application.persistentDataPath, "GitHubUpdater_History.json");

//    private string version = "1.0";
//    private string whatsNew = "";
//    private float progress = 0f;
//    private bool isPushing = false;
//    private bool pushCompleted = false;

//    private Vector2 selectedFilesScrollPos = Vector2.zero;
//    private Vector2 historyScrollPos = Vector2.zero;

//    [MenuItem("Tools/GitHub Updater")]
//    public static void ShowWindow()
//    {
//        var window = GetWindow<GitHubUpdater>("GitHub Updater");
//        window.LoadHistory();
//        window.LoadPushedFiles();
//    }

//    private void LoadHistory()
//    {
//        if (File.Exists(historyFilePath))
//        {
//            string json = File.ReadAllText(historyFilePath);
//            versionHistory = JsonConvert.DeserializeObject<VersionHistory>(json);
//        }
//    }

//    private void LoadPushedFiles()
//    {
//        if (File.Exists(pushedFilesPath))
//        {
//            string json = File.ReadAllText(pushedFilesPath);
//            alreadyPushedFiles = JsonConvert.DeserializeObject<HashSet<string>>(json);
//        }
//    }

//    private void OnGUI()
//    {
//        SyncAutoDetectedFiles();

//        GUILayout.Label("Select Files or Folder to Upload", EditorStyles.boldLabel);

//        if (GUILayout.Button("Add Files"))
//        {
//            string filePath = EditorUtility.OpenFilePanel("Select File", Application.dataPath, "*");
//            if (!string.IsNullOrEmpty(filePath)) AddFile(filePath);
//        }

//        if (GUILayout.Button("Add Folder"))
//        {
//            string folderPath = EditorUtility.OpenFolderPanel("Select Folder", Application.dataPath, "");
//            if (!string.IsNullOrEmpty(folderPath)) AddFolder(folderPath);
//        }

//        if (GUILayout.Button("Show New Changes"))
//        {
//            ScanForNewChanges();
//        }

//        GUILayout.Label("Selected Files:", EditorStyles.boldLabel);

//        selectedFilesScrollPos = EditorGUILayout.BeginScrollView(selectedFilesScrollPos, GUILayout.Height(150));
//        for (int i = 0; i < selectedFiles.Count; i++)
//        {
//            GUILayout.BeginHorizontal();
//            GUILayout.Label(selectedFiles[i], GUILayout.ExpandWidth(true));
//            if (GUILayout.Button("❌", GUILayout.Width(25)))
//            {
//                GitHubFileTracker.manuallyRemovedFiles.Add(selectedFiles[i]);
//                selectedFiles.RemoveAt(i);
//                i--;
//            }
//            GUILayout.EndHorizontal();
//        }
//        EditorGUILayout.EndScrollView();

//        if (GUILayout.Button("Clear Selected Files"))
//        {
//            selectedFiles.Clear();
//           // GitHubFileTracker.manuallyRemovedFiles.Clear();

//        }

//        if (GUILayout.Button("Show Current Version"))
//        {
//            version = GetCurrentVersionFromFile();
//        }

//        version = EditorGUILayout.TextField("Version:", version);
//        whatsNew = EditorGUILayout.TextArea(whatsNew, GUILayout.Height(100));

//        if (GUILayout.Button("Push to GitHub") && !isPushing)
//        {
//            _ = PushToGitHubAsync();
//        }

//        if (isPushing)
//        {
//            EditorGUI.ProgressBar(new Rect(10, position.height - 40, position.width - 20, 20), progress, "Uploading...");
//            Repaint();
//        }
//        else if (pushCompleted)
//        {
//            GUIStyle style = new GUIStyle(GUI.skin.label) { normal = { textColor = Color.green } };
//            GUILayout.Label("Push Completed!", style);
//        }

//        HandleDragAndDrop();

//        GUILayout.Space(10);
//        if (GUILayout.Button(showHistory ? "Hide History" : "Show History"))
//        {
//            showHistory = !showHistory;
//        }

//        if (showHistory)
//        {
//            GUILayout.Label("Push History", EditorStyles.boldLabel);
//            historyScrollPos = EditorGUILayout.BeginScrollView(historyScrollPos, GUILayout.Height(200));
//            foreach (var entry in versionHistory.entries)
//            {
//                EditorGUILayout.BeginVertical("box");
//                GUILayout.Label("Version: " + entry.version, EditorStyles.boldLabel);
//                GUILayout.Label("Date: " + entry.dateTime);
//                GUILayout.Label("Notes:\n" + entry.whatsNew);
//                EditorGUILayout.EndVertical();
//            }
//            EditorGUILayout.EndScrollView();
//        }
//    }

//    private void ScanForNewChanges()
//    {
//        string[] allFiles = Directory.GetFiles(Application.dataPath, "*.*", SearchOption.AllDirectories);

//        foreach (string absPath in allFiles)
//        {
//            if (absPath.EndsWith(".meta")) continue;

//            string relativePath = "Assets" + absPath.Replace(Application.dataPath, "").Replace("\\", "/");

//            if (!alreadyPushedFiles.Contains(relativePath) &&
//                !GitHubFileTracker.manuallyRemovedFiles.Contains(relativePath) &&
//                !selectedFiles.Contains(relativePath))
//            {
//                selectedFiles.Add(relativePath);

//                string metaRelative = relativePath + ".meta";
//                if (File.Exists(Path.Combine(Application.dataPath, metaRelative.Replace("Assets/", ""))) &&
//                    !selectedFiles.Contains(metaRelative) &&
//                    !alreadyPushedFiles.Contains(metaRelative) &&
//                    !GitHubFileTracker.manuallyRemovedFiles.Contains(metaRelative))
//                {
//                    selectedFiles.Add(metaRelative);
//                }
//            }
//        }
//    }


//    private void SyncAutoDetectedFiles()
//    {
//        foreach (string path in GitHubFileTracker.autoDetectedFiles)
//        {
//            if (!selectedFiles.Contains(path) &&
//                !GitHubFileTracker.manuallyRemovedFiles.Contains(path))
//            {
//                selectedFiles.Add(path);
//                string metaPath = path + ".meta";
//                if (!selectedFiles.Contains(metaPath) &&
//                    !GitHubFileTracker.manuallyRemovedFiles.Contains(metaPath))
//                {
//                    selectedFiles.Add(metaPath);
//                }
//            }
//        }

//        foreach (string deletedPath in GitHubFileTracker.deletedFiles)
//        {
//            selectedFiles.Remove(deletedPath);
//            selectedFiles.Remove(deletedPath + ".meta");
//        }

//        GitHubFileTracker.autoDetectedFiles.Clear();
//        GitHubFileTracker.deletedFiles.Clear();
//    }

//    private void HandleDragAndDrop()
//    {
//        Event evt = Event.current;
//        if (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform)
//        {
//            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
//            if (evt.type == EventType.DragPerform)
//            {
//                DragAndDrop.AcceptDrag();
//                foreach (var obj in DragAndDrop.objectReferences)
//                {
//                    string path = AssetDatabase.GetAssetPath(obj);
//                    if (Directory.Exists(path))
//                        AddFolder(Path.Combine(Application.dataPath, path.Replace("Assets/", "")));
//                    else
//                        AddFile(Path.Combine(Application.dataPath, path.Replace("Assets/", "")));
//                }
//            }
//            evt.Use();
//        }
//    }

//    private void AddFile(string absolutePath)
//    {
//        string relativePath = "Assets" + absolutePath.Replace(Application.dataPath, "").Replace("\\", "/");

//        if (!selectedFiles.Contains(relativePath))
//        {
//            selectedFiles.Add(relativePath);
//        }

//        string metaPath = absolutePath + ".meta";
//        string metaRelative = relativePath + ".meta";

//        if (File.Exists(metaPath) && !selectedFiles.Contains(metaRelative))
//        {
//            selectedFiles.Add(metaRelative);
//        }
//    }

//    private void AddFolder(string folderPath)
//    {
//        if (!string.IsNullOrEmpty(folderPath))
//        {
//            string[] files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories);
//            foreach (string file in files) AddFile(file);
//        }
//    }

//    private string GetCurrentVersionFromFile()
//    {
//        string path = "Assets/version.txt";
//        return File.Exists(path) ? File.ReadAllLines(path)[0] : "Unknown";
//    }

//    private async Task PushToGitHubAsync()
//    {
//        isPushing = true;
//        pushCompleted = false;
//        progress = 0f;
//        Repaint();

//        string commitMessage = "Updating files to version " + version;
//        string repoOwner = GitHubConfig_Info.RepositoryOwner;
//        string repoName = GitHubConfig_Info.RepositoryName;
//        string token = GitHubConfig_Info.Token;

//        bool versionUpdated = PushVersionFile(repoOwner, repoName, token, version, commitMessage);

//        if (versionUpdated)
//        {
//            int total = selectedFiles.Count;
//            int processed = 0;

//            //foreach (string filePath in selectedFiles)
//            //{
//            //    await Task.Run(() => PushFileToGitHub(repoOwner, repoName, token, filePath, commitMessage));
//            //    alreadyPushedFiles.Add(filePath);
//            //    processed++;
//            //    progress = (float)processed / total;
//            //    Repaint();
//            //}

//            //foreach (string filePath in selectedFiles)
//            //{
//            //    await Task.Run(() => PushFileToGitHub(repoOwner, repoName, token, filePath, commitMessage));
//            //   // alreadyPushedFiles.Add(filePath);

//            //    GitHubFileTracker.alreadyPushedFiles.Add(filePath);
//            //    GitHubFileTracker.SavePushedFiles();


//            //    // Add corresponding .meta file if it exists
//            //    string metaFile = filePath + ".meta";
//            //    if (File.Exists(Path.Combine(Application.dataPath, metaFile.Replace("Assets/", ""))))
//            //    {
//            //        alreadyPushedFiles.Add(metaFile);
//            //    }

//            //    processed++;
//            //    progress = (float)processed / total;
//            //    Repaint();
//            //}

//            foreach (string filePath in selectedFiles)
//            {
//                await Task.Run(() => PushFileToGitHub(repoOwner, repoName, token, filePath, commitMessage));

//                // Add file and its meta (if exists) to alreadyPushedFiles and persist
//                GitHubFileTracker.alreadyPushedFiles.Add(filePath);

//                string metaFile = filePath + ".meta";
//                if (File.Exists(metaFile))
//                {
//                    GitHubFileTracker.alreadyPushedFiles.Add(metaFile);
//                }

//                GitHubFileTracker.SavePushedFiles(); // Save once per file (fine for now, can optimize later)

//                processed++;
//                progress = (float)processed / total;
//                Repaint();
//            }



//            File.WriteAllText(pushedFilesPath, JsonConvert.SerializeObject(alreadyPushedFiles, Formatting.Indented));
//        }
//        else
//        {
//            Debug.LogError("Version update failed. Files not pushed.");
//        }

//        selectedFiles.Clear();
//        GitHubFileTracker.manuallyRemovedFiles.Clear();
//        isPushing = false;
//        pushCompleted = true;
//        SaveHistoryEntry(version, whatsNew);
//        Repaint();
//    }

//    private void SaveHistoryEntry(string version, string whatsNew)
//    {
//        versionHistory.entries.Insert(0, new VersionHistoryEntry
//        {
//            version = version,
//            whatsNew = whatsNew,
//            dateTime = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
//        });

//        string json = JsonConvert.SerializeObject(versionHistory, Formatting.Indented);
//        File.WriteAllText(historyFilePath, json);
//    }

//    private bool PushVersionFile(string owner, string repo, string token, string version, string message)
//    {
//        string path = "Assets/version.txt";
//        string absPath = Path.Combine(Application.dataPath, "version.txt");
//        File.WriteAllText(absPath, $"{version}\n\nWhat's New:\n{whatsNew}");
//        AssetDatabase.ImportAsset(path);
//        AssetDatabase.Refresh();

//        return PushFileToGitHub(owner, repo, token, path, message);
//    }

//    private bool PushFileToGitHub(string owner, string repo, string token, string path, string message)
//    {
//        string repoPath = path.Replace("Assets/", "").TrimStart('/');
//        string absPath = Path.Combine(Application.dataPath, path.Replace("Assets/", ""));

//        if (!File.Exists(absPath))
//        {
//            Debug.LogError($"File not found: {absPath}");
//            return false;
//        }

//        if (!UploadToGitHub(owner, repo, token, absPath, repoPath, message))
//            return false;

//        string metaPath = absPath + ".meta";
//        if (File.Exists(metaPath))
//        {
//            string metaRepoPath = repoPath + ".meta";
//            UploadToGitHub(owner, repo, token, metaPath, metaRepoPath, message);
//        }

//        return true;
//    }

//    private string GetFileSHA(string owner, string repo, string path, string token)
//    {
//        try
//        {
//            string url = $"https://api.github.com/repos/{owner}/{repo}/contents/{path}";
//            using (WebClient client = new WebClient())
//            {
//                client.Headers.Add("Authorization", "token " + token);
//                client.Headers.Add("User-Agent", "UnityGitHubUploader");
//                string response = client.DownloadString(url);
//                var json = JsonConvert.DeserializeObject<Dictionary<string, object>>(response);
//                return json.ContainsKey("sha") ? json["sha"].ToString() : null;
//            }
//        }
//        catch { return null; }
//    }

//    private bool UploadToGitHub(string owner, string repo, string token, string localPath, string repoPath, string message)
//    {
//        string content = System.Convert.ToBase64String(File.ReadAllBytes(localPath));
//        string sha = GetFileSHA(owner, repo, repoPath, token);

//        var payload = new Dictionary<string, object>
//        {
//            { "message", message },
//            { "content", content }
//        };

//        if (!string.IsNullOrEmpty(sha)) payload["sha"] = sha;

//        string json = JsonConvert.SerializeObject(payload);
//        string url = $"https://api.github.com/repos/{owner}/{repo}/contents/{repoPath}";

//        try
//        {
//            using (WebClient client = new WebClient())
//            {
//                client.Headers.Add("Authorization", "token " + token);
//                client.Headers.Add("User-Agent", "UnityGitHubUploader");
//                client.Headers.Add("Content-Type", "application/json");
//                client.UploadString(url, "PUT", json);
//                return true;
//            }
//        }
//        catch (WebException ex)
//        {
//            Debug.LogError($"Upload failed for {repoPath}: {ex.Message}");
//            return false;
//        }
//    }
//}

//[System.Serializable]
//public class VersionHistoryEntry
//{
//    public string version;
//    public string whatsNew;
//    public string dateTime;
//}

//[System.Serializable]
//public class VersionHistory
//{
//    public List<VersionHistoryEntry> entries = new List<VersionHistoryEntry>();
//}








//using UnityEditor;
//using UnityEngine;
//using System.Collections.Generic;
//using System.IO;
//using System.Net;
//using System.Text;
//using Unity.Plastic.Newtonsoft.Json;
//using System.Threading.Tasks;

//public class GitHubUpdater : EditorWindow
//{
//    private VersionHistory versionHistory = new VersionHistory();
//    private bool showHistory = false;

//    private string historyFilePath => Path.Combine(Application.persistentDataPath, "GitHubUpdater_History.json");


//    private List<string> selectedFiles = new List<string>(); // Store relative paths
//    private string version = "1.0"; // Current version
//    private string whatsNew = ""; // "What's New" section
//    private float progress = 0f;
//    private bool isPushing = false;
//    private bool pushCompleted = false;

//    private Vector2 selectedFilesScrollPos = Vector2.zero;
//    private Vector2 historyScrollPos = Vector2.zero;


//    [MenuItem("Tools/GitHub Updater")]
//    public static void ShowWindow()
//    {
//      //  GetWindow<GitHubUpdater>("GitHub Updater");
//        GetWindow<GitHubUpdater>("GitHub Updater").LoadHistory();

//    }
//    private void LoadHistory()
//    {
//        if (File.Exists(historyFilePath))
//        {
//            string json = File.ReadAllText(historyFilePath);
//            versionHistory = JsonConvert.DeserializeObject<VersionHistory>(json);
//        }
//    }
//    private void OnGUI()
//    {
//        SyncAutoDetectedFiles(); // <--- Add this line at the top

//        GUILayout.Label("Select Files or Folder to Upload", EditorStyles.boldLabel);

//        // Add File (Multi-file selection)
//        if (GUILayout.Button("Add Files"))
//        {
//            string filePath = EditorUtility.OpenFilePanel("Select File", Application.dataPath, "*");
//            if (!string.IsNullOrEmpty(filePath))
//            {
//                AddFile(filePath);
//            }
//        }

//        // Add Folder
//        if (GUILayout.Button("Add Folder"))
//        {
//            string folderPath = EditorUtility.OpenFolderPanel("Select a Folder", Application.dataPath, "");
//            if (!string.IsNullOrEmpty(folderPath))
//            {
//                AddFolder(folderPath);
//            }
//        }

//        //GUILayout.Label("Selected Files:", EditorStyles.boldLabel);
//        //foreach (var filePath in selectedFiles)
//        //{
//        //    GUILayout.Label(filePath);
//        //}

//        //GUILayout.Label("Selected Files:", EditorStyles.boldLabel);

//        //for (int i = 0; i < selectedFiles.Count; i++)
//        //{
//        //    GUILayout.BeginHorizontal();
//        //    GUILayout.Label(selectedFiles[i], GUILayout.ExpandWidth(true));

//        //    if (GUILayout.Button("❌", GUILayout.Width(25)))
//        //    {
//        //        GitHubFileTracker.manuallyRemovedFiles.Add(selectedFiles[i]);
//        //        selectedFiles.RemoveAt(i);
//        //        i--; // Adjust index after removal
//        //    }

//        //    GUILayout.EndHorizontal();
//        //}

//        GUILayout.Label("Selected Files:", EditorStyles.boldLabel);

//        selectedFilesScrollPos = EditorGUILayout.BeginScrollView(selectedFilesScrollPos, GUILayout.Height(150)); // Adjust height as needed

//        for (int i = 0; i < selectedFiles.Count; i++)
//        {
//            GUILayout.BeginHorizontal();
//            GUILayout.Label(selectedFiles[i], GUILayout.ExpandWidth(true));

//            if (GUILayout.Button("❌", GUILayout.Width(25)))
//            {
//                GitHubFileTracker.manuallyRemovedFiles.Add(selectedFiles[i]);
//                selectedFiles.RemoveAt(i);
//                i--; // Adjust index after removal
//            }

//            GUILayout.EndHorizontal();
//        }

//        EditorGUILayout.EndScrollView();



//        GUILayout.Space(5);
//        if (GUILayout.Button("Clear Selected Files"))
//        {
//            selectedFiles.Clear();
//            GitHubFileTracker.manuallyRemovedFiles.Clear(); // Optional
//        }

//        // Restore "Show Current Version" Button
//        if (GUILayout.Button("Show Current Version"))
//        {
//            version = GetCurrentVersionFromFile();
//        }

//        version = EditorGUILayout.TextField("Version:", version);

//        // "What's New" Text Area
//        whatsNew = EditorGUILayout.TextArea(whatsNew, GUILayout.Height(100));

//        if (GUILayout.Button("Push to GitHub") && !isPushing)
//        {
//            _ = PushToGitHubAsync();
//        }

//        if (isPushing)
//        {
//            EditorGUI.ProgressBar(new Rect(10, position.height - 40, position.width - 20, 20), progress, "Uploading...");
//            Repaint();
//        }
//        else if (pushCompleted)
//        {
//            GUIStyle style = new GUIStyle(GUI.skin.label);
//            style.normal.textColor = Color.green;
//            GUILayout.Label("Push Completed!", style);
//        }

//        // Handle Drag and Drop
//        HandleDragAndDrop();

//        GUILayout.Space(10);
//        if (GUILayout.Button(showHistory ? "Hide History" : "Show History"))
//        {
//            showHistory = !showHistory;
//        }

//        if (showHistory)
//        {
//            GUILayout.Label("Push History", EditorStyles.boldLabel);

//            historyScrollPos = EditorGUILayout.BeginScrollView(historyScrollPos, GUILayout.Height(200)); // Adjust height as needed

//            foreach (var entry in versionHistory.entries)
//            {
//                EditorGUILayout.BeginVertical("box");
//                GUILayout.Label("Version: " + entry.version, EditorStyles.boldLabel);
//                GUILayout.Label("Date: " + entry.dateTime);
//                GUILayout.Label("Notes:\n" + entry.whatsNew);
//                EditorGUILayout.EndVertical();
//            }

//            EditorGUILayout.EndScrollView();
//        }


//    }
//    private void SyncAutoDetectedFiles()
//    {
//        // Add new or changed files, but only if NOT manually removed
//        foreach (string path in GitHubFileTracker.autoDetectedFiles)
//        {
//            if (!selectedFiles.Contains(path) &&
//                !GitHubFileTracker.manuallyRemovedFiles.Contains(path))
//            {
//                selectedFiles.Add(path);

//                // Add .meta file if it exists and not manually removed
//                string metaPath = path + ".meta";
//                if (!selectedFiles.Contains(metaPath) &&
//                    !GitHubFileTracker.manuallyRemovedFiles.Contains(metaPath))
//                {
//                    selectedFiles.Add(metaPath);
//                }
//            }
//        }

//        // Remove deleted files
//        foreach (string deletedPath in GitHubFileTracker.deletedFiles)
//        {
//            selectedFiles.Remove(deletedPath);
//            selectedFiles.Remove(deletedPath + ".meta");
//        }

//        GitHubFileTracker.autoDetectedFiles.Clear();
//        GitHubFileTracker.deletedFiles.Clear();
//    }
//    private void ScanForNewChanges()
//    {
//        string[] allFiles = Directory.GetFiles(Application.dataPath, "*.*", SearchOption.AllDirectories);

//        foreach (string absPath in allFiles)
//        {
//            // Skip .meta files directly (we'll add them via AddFile automatically)
//            if (absPath.EndsWith(".meta")) continue;

//            // Convert absolute path to Unity relative path
//            string relativePath = "Assets" + absPath.Replace(Application.dataPath, "").Replace("\\", "/");

//            // If not already added and not manually removed
//            if (!selectedFiles.Contains(relativePath) && !GitHubFileTracker.manuallyRemovedFiles.Contains(relativePath))
//            {
//                AddFile(absPath); // Add file and its .meta file
//            }
//        }
//    }




//    private void HandleDragAndDrop()
//    {
//        Event evt = Event.current;
//        if (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform)
//        {
//            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
//            if (evt.type == EventType.DragPerform)
//            {
//                DragAndDrop.AcceptDrag();
//                foreach (var obj in DragAndDrop.objectReferences)
//                {
//                    string path = AssetDatabase.GetAssetPath(obj);
//                    if (Directory.Exists(path))
//                    {
//                        AddFolder(Path.Combine(Application.dataPath, path.Replace("Assets/", "")));
//                    }
//                    else
//                    {
//                        AddFile(Path.Combine(Application.dataPath, path.Replace("Assets/", "")));
//                    }
//                }
//            }
//            evt.Use();
//        }
//    }

//    private void AddFile(string absolutePath)
//    {
//        if (!string.IsNullOrEmpty(absolutePath))
//        {
//            string relativePath = "Assets" + absolutePath.Replace(Application.dataPath, "").Replace("\\", "/");

//            if (!selectedFiles.Contains(relativePath))
//            {
//                selectedFiles.Add(relativePath);
//            }

//            // Ensure the .meta file is also added
//            string metaFilePath = absolutePath + ".meta";
//            string metaRelativePath = relativePath + ".meta";

//            if (File.Exists(metaFilePath) && !selectedFiles.Contains(metaRelativePath))
//            {
//                selectedFiles.Add(metaRelativePath);
//            }
//        }
//    }



//    private void AddFolder(string folderPath)
//    {
//        if (!string.IsNullOrEmpty(folderPath))
//        {
//            string[] files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories);
//            foreach (string file in files)
//            {
//                AddFile(file); // This will now correctly add .meta files
//            }

//            // Ensure .meta files are added explicitly
//            string[] metaFiles = Directory.GetFiles(folderPath, "*.meta", SearchOption.AllDirectories);
//            foreach (string metaFile in metaFiles)
//            {
//                AddFile(metaFile);
//            }
//        }
//    }




//    private string GetCurrentVersionFromFile()
//    {
//        string versionFilePath = "Assets/version.txt";
//        if (File.Exists(versionFilePath))
//        {
//            string[] lines = File.ReadAllLines(versionFilePath);
//            if (lines.Length > 0)
//            {
//                return lines[0]; // Assuming the first line contains the version number
//            }
//        }
//        return "Unknown Version";
//    }

//    private async Task PushToGitHubAsync()
//    {
//        isPushing = true;
//        pushCompleted = false;
//        progress = 0f;
//        Repaint();

//        string commitMessage = "Updating files to version " + version;
//        string repoOwner = GitHubConfig_Info.RepositoryOwner;
//        string repoName = GitHubConfig_Info.RepositoryName;
//        string token = GitHubConfig_Info.Token;

//        bool versionUpdated = PushVersionFile(repoOwner, repoName, token, version, commitMessage);

//        if (versionUpdated)
//        {
//            Debug.Log("Version updated successfully. Now updating selected files...");
//            int totalFiles = selectedFiles.Count;
//            int processedFiles = 0;

//            foreach (var filePath in selectedFiles)
//            {
//                await Task.Run(() => PushFileToGitHub(repoOwner, repoName, token, filePath, commitMessage));
//                processedFiles++;
//                progress = (float)processedFiles / totalFiles;
//                Repaint();
//            }
//        }
//        else
//        {
//            Debug.LogError("Failed to update version. Files not pushed.");
//        }

//        selectedFiles.Clear(); // Clear files after push
//        GitHubFileTracker.manuallyRemovedFiles.Clear(); // Optional: clear manual skip list too

//        isPushing = false;
//        pushCompleted = true;
//        SaveHistoryEntry(version, whatsNew);
//        Repaint();
//    }
//    private void SaveHistoryEntry(string version, string whatsNew)
//    {
//        versionHistory.entries.Insert(0, new VersionHistoryEntry
//        {
//            version = version,
//            whatsNew = whatsNew,
//            dateTime = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
//        });

//        string json = JsonConvert.SerializeObject(versionHistory, Formatting.Indented);
//        File.WriteAllText(historyFilePath, json);
//    }

//    private bool PushVersionFile(string repoOwner, string repoName, string token, string version, string commitMessage)
//    {
//        string versionFilePath = "Assets/version.txt";
//        string localPath = Path.Combine(Application.dataPath, "version.txt");

//        string versionContent = $"{version}\n\nWhat's New:\n{whatsNew}";
//        File.WriteAllText(localPath, versionContent);
//        AssetDatabase.ImportAsset(versionFilePath);
//        AssetDatabase.Refresh();

//        return PushFileToGitHub(repoOwner, repoName, token, versionFilePath, commitMessage);
//    }

//    private bool PushFileToGitHub(string repoOwner, string repoName, string token, string filePath, string commitMessage)
//    {
//        string repoPath = filePath.Replace("Assets/", "").TrimStart('/');
//        string absoluteFilePath = Path.Combine(Application.dataPath, filePath.Replace("Assets/", ""));

//        if (!File.Exists(absoluteFilePath))
//        {
//            Debug.LogError($"[FILE ERROR] File not found: {absoluteFilePath}");
//            return false;
//        }

//        // Push main file
//        if (!UploadToGitHub(repoOwner, repoName, token, absoluteFilePath, repoPath, commitMessage))
//        {
//            Debug.LogError($"Failed to upload {repoPath}");
//            return false;
//        }

//        // Check and push .meta file
//        string metaFilePath = absoluteFilePath + ".meta";
//        string metaRepoPath = repoPath + ".meta";

//        if (File.Exists(metaFilePath))
//        {
//            Debug.Log($"[GitHub Upload] Pushing meta file: {metaRepoPath}");
//            UploadToGitHub(repoOwner, repoName, token, metaFilePath, metaRepoPath, commitMessage);
//        }

//        return true;
//    }

//    private string GetFileSHA(string owner, string repo, string filePath, string token)
//    {
//        try
//        {
//            string url = $"https://api.github.com/repos/{owner}/{repo}/contents/{filePath}";
//            using (WebClient client = new WebClient())
//            {
//                client.Headers.Add("Authorization", "token " + token);
//                client.Headers.Add("User-Agent", "UnityGitHubUploader");
//                string response = client.DownloadString(url);

//                var jsonResponse = JsonConvert.DeserializeObject<Dictionary<string, object>>(response);
//                return jsonResponse.ContainsKey("sha") ? jsonResponse["sha"].ToString() : null;
//            }
//        }
//        catch (WebException)
//        {
//            return null;
//        }
//    }
//    private bool UploadToGitHub(string repoOwner, string repoName, string token, string localFilePath, string repoFilePath, string commitMessage)
//    {
//        string fileContent = System.Convert.ToBase64String(File.ReadAllBytes(localFilePath));
//        string sha = GetFileSHA(repoOwner, repoName, repoFilePath, token);

//        var payload = new Dictionary<string, object>
//    {
//        { "message", commitMessage },
//        { "content", fileContent }
//    };

//        if (!string.IsNullOrEmpty(sha))
//        {
//            payload["sha"] = sha;
//        }

//        string jsonPayload = JsonConvert.SerializeObject(payload);
//        string url = $"https://api.github.com/repos/{repoOwner}/{repoName}/contents/{repoFilePath}";

//        Debug.Log($"[GitHub Upload] Uploading: {repoFilePath} to {url}");

//        try
//        {
//            using (WebClient client = new WebClient())
//            {
//                client.Headers.Add("Authorization", "token " + token);
//                client.Headers.Add("User-Agent", "UnityGitHubUploader");
//                client.Headers.Add("Content-Type", "application/json");
//                client.UploadString(url, "PUT", jsonPayload);
//                return true;
//            }
//        }
//        catch (WebException ex)
//        {
//            Debug.LogError($"[GitHub Upload] Failed to upload {repoFilePath}: {ex.Message}");
//            return false;
//        }
//    }

//}
//[System.Serializable]
//public class VersionHistoryEntry
//{
//    public string version;
//    public string whatsNew;
//    public string dateTime;
//}

//[System.Serializable]
//public class VersionHistory
//{
//    public List<VersionHistoryEntry> entries = new List<VersionHistoryEntry>();
//}




//using UnityEditor;
//using UnityEngine;
//using System.Collections.Generic;
//using System.IO;
//using System.Net;
//using System.Text;
//using Unity.Plastic.Newtonsoft.Json;
//using System.Threading.Tasks;

//public class GitHubUpdater : EditorWindow
//{
//    private List<string> selectedFiles = new List<string>(); // Store relative paths
//    private string version = "1.0"; // Current version
//    private string whatsNew = ""; // "What's New" section
//    private float progress = 0f;
//    private bool isPushing = false;
//    private bool pushCompleted = false;

//    [MenuItem("Tools/GitHub Updater")]
//    public static void ShowWindow()
//    {
//        GetWindow<GitHubUpdater>("GitHub Updater");
//    }

//    private void OnGUI()
//    {
//        GUILayout.Label("Select Files or Folder to Upload", EditorStyles.boldLabel);

//        // Add File (Multi-file selection)
//        if (GUILayout.Button("Add Files"))
//        {
//            string filePath = EditorUtility.OpenFilePanel("Select File", Application.dataPath, "*");
//            if (!string.IsNullOrEmpty(filePath))
//            {
//                AddFile(filePath);
//            }
//        }

//        // Add Folder
//        if (GUILayout.Button("Add Folder"))
//        {
//            string folderPath = EditorUtility.OpenFolderPanel("Select a Folder", Application.dataPath, "");
//            if (!string.IsNullOrEmpty(folderPath))
//            {
//                AddFolder(folderPath);
//            }
//        }

//        GUILayout.Label("Selected Files:", EditorStyles.boldLabel);
//        foreach (var filePath in selectedFiles)
//        {
//            GUILayout.Label(filePath);
//        }

//        // Restore "Show Current Version" Button
//        if (GUILayout.Button("Show Current Version"))
//        {
//            version = GetCurrentVersionFromFile();
//        }

//        version = EditorGUILayout.TextField("Version:", version);

//        // "What's New" Text Area
//        whatsNew = EditorGUILayout.TextArea(whatsNew, GUILayout.Height(100));

//        if (GUILayout.Button("Push to GitHub") && !isPushing)
//        {
//            _ = PushToGitHubAsync();
//        }

//        if (isPushing)
//        {
//            EditorGUI.ProgressBar(new Rect(10, position.height - 40, position.width - 20, 20), progress, "Uploading...");
//            Repaint();
//        }
//        else if (pushCompleted)
//        {
//            GUIStyle style = new GUIStyle(GUI.skin.label);
//            style.normal.textColor = Color.green;
//            GUILayout.Label("Push Completed!", style);
//        }

//        // Handle Drag and Drop
//        HandleDragAndDrop();
//    }

//    private void HandleDragAndDrop()
//    {
//        Event evt = Event.current;
//        if (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform)
//        {
//            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
//            if (evt.type == EventType.DragPerform)
//            {
//                DragAndDrop.AcceptDrag();
//                foreach (var obj in DragAndDrop.objectReferences)
//                {
//                    string path = AssetDatabase.GetAssetPath(obj);
//                    if (Directory.Exists(path))
//                    {
//                        AddFolder(Path.Combine(Application.dataPath, path.Replace("Assets/", "")));
//                    }
//                    else
//                    {
//                        AddFile(Path.Combine(Application.dataPath, path.Replace("Assets/", "")));
//                    }
//                }
//            }
//            evt.Use();
//        }
//    }

//    private void AddFile(string absolutePath)
//    {
//        if (!string.IsNullOrEmpty(absolutePath))
//        {
//            string relativePath = "Assets" + absolutePath.Replace(Application.dataPath, "").Replace("\\", "/");

//            if (!selectedFiles.Contains(relativePath))
//            {
//                selectedFiles.Add(relativePath);
//            }

//            // Ensure the .meta file is also added
//            string metaFilePath = absolutePath + ".meta";
//            string metaRelativePath = relativePath + ".meta";

//            if (File.Exists(metaFilePath) && !selectedFiles.Contains(metaRelativePath))
//            {
//                selectedFiles.Add(metaRelativePath);
//            }
//        }
//    }



//    private void AddFolder(string folderPath)
//    {
//        if (!string.IsNullOrEmpty(folderPath))
//        {
//            string[] files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories);
//            foreach (string file in files)
//            {
//                AddFile(file); // This will now correctly add .meta files
//            }

//            // Ensure .meta files are added explicitly
//            string[] metaFiles = Directory.GetFiles(folderPath, "*.meta", SearchOption.AllDirectories);
//            foreach (string metaFile in metaFiles)
//            {
//                AddFile(metaFile);
//            }
//        }
//    }




//    private string GetCurrentVersionFromFile()
//    {
//        string versionFilePath = "Assets/version.txt";
//        if (File.Exists(versionFilePath))
//        {
//            string[] lines = File.ReadAllLines(versionFilePath);
//            if (lines.Length > 0)
//            {
//                return lines[0]; // Assuming the first line contains the version number
//            }
//        }
//        return "Unknown Version";
//    }

//    private async Task PushToGitHubAsync()
//    {
//        isPushing = true;
//        pushCompleted = false;
//        progress = 0f;
//        Repaint();

//        string commitMessage = "Updating files to version " + version;
//        string repoOwner = GitHubConfig.RepositoryOwner;
//        string repoName = GitHubConfig.RepositoryName;
//        string token = GitHubConfig.Token;

//        bool versionUpdated = PushVersionFile(repoOwner, repoName, token, version, commitMessage);

//        if (versionUpdated)
//        {
//            Debug.Log("Version updated successfully. Now updating selected files...");
//            int totalFiles = selectedFiles.Count;
//            int processedFiles = 0;

//            foreach (var filePath in selectedFiles)
//            {
//                await Task.Run(() => PushFileToGitHub(repoOwner, repoName, token, filePath, commitMessage));
//                processedFiles++;
//                progress = (float)processedFiles / totalFiles;
//                Repaint();
//            }
//        }
//        else
//        {
//            Debug.LogError("Failed to update version. Files not pushed.");
//        }

//        isPushing = false;
//        pushCompleted = true;
//        Repaint();
//    }

//    private bool PushVersionFile(string repoOwner, string repoName, string token, string version, string commitMessage)
//    {
//        string versionFilePath = "Assets/version.txt";
//        string localPath = Path.Combine(Application.dataPath, "version.txt");

//        string versionContent = $"{version}\n\nWhat's New:\n{whatsNew}";
//        File.WriteAllText(localPath, versionContent);
//        AssetDatabase.ImportAsset(versionFilePath);
//        AssetDatabase.Refresh();

//        return PushFileToGitHub(repoOwner, repoName, token, versionFilePath, commitMessage);
//    }

//    private bool PushFileToGitHub(string repoOwner, string repoName, string token, string filePath, string commitMessage)
//    {
//        string repoPath = filePath.Replace("Assets/", "").TrimStart('/');
//        string absoluteFilePath = Path.Combine(Application.dataPath, filePath.Replace("Assets/", ""));

//        if (!File.Exists(absoluteFilePath))
//        {
//            Debug.LogError($"[FILE ERROR] File not found: {absoluteFilePath}");
//            return false;
//        }

//        // Push main file
//        if (!UploadToGitHub(repoOwner, repoName, token, absoluteFilePath, repoPath, commitMessage))
//        {
//            Debug.LogError($"Failed to upload {repoPath}");
//            return false;
//        }

//        // Check and push .meta file
//        string metaFilePath = absoluteFilePath + ".meta";
//        string metaRepoPath = repoPath + ".meta";

//        if (File.Exists(metaFilePath))
//        {
//            Debug.Log($"[GitHub Upload] Pushing meta file: {metaRepoPath}");
//            UploadToGitHub(repoOwner, repoName, token, metaFilePath, metaRepoPath, commitMessage);
//        }

//        return true;
//    }

//    private string GetFileSHA(string owner, string repo, string filePath, string token)
//    {
//        try
//        {
//            string url = $"https://api.github.com/repos/{owner}/{repo}/contents/{filePath}";
//            using (WebClient client = new WebClient())
//            {
//                client.Headers.Add("Authorization", "token " + token);
//                client.Headers.Add("User-Agent", "UnityGitHubUploader");
//                string response = client.DownloadString(url);

//                var jsonResponse = JsonConvert.DeserializeObject<Dictionary<string, object>>(response);
//                return jsonResponse.ContainsKey("sha") ? jsonResponse["sha"].ToString() : null;
//            }
//        }
//        catch (WebException)
//        {
//            return null;
//        }
//    }
//    private bool UploadToGitHub(string repoOwner, string repoName, string token, string localFilePath, string repoFilePath, string commitMessage)
//    {
//        string fileContent = System.Convert.ToBase64String(File.ReadAllBytes(localFilePath));
//        string sha = GetFileSHA(repoOwner, repoName, repoFilePath, token);

//        var payload = new Dictionary<string, object>
//    {
//        { "message", commitMessage },
//        { "content", fileContent }
//    };

//        if (!string.IsNullOrEmpty(sha))
//        {
//            payload["sha"] = sha;
//        }

//        string jsonPayload = JsonConvert.SerializeObject(payload);
//        string url = $"https://api.github.com/repos/{repoOwner}/{repoName}/contents/{repoFilePath}";

//        Debug.Log($"[GitHub Upload] Uploading: {repoFilePath} to {url}");

//        try
//        {
//            using (WebClient client = new WebClient())
//            {
//                client.Headers.Add("Authorization", "token " + token);
//                client.Headers.Add("User-Agent", "UnityGitHubUploader");
//                client.Headers.Add("Content-Type", "application/json");
//                client.UploadString(url, "PUT", jsonPayload);
//                return true;
//            }
//        }
//        catch (WebException ex)
//        {
//            Debug.LogError($"[GitHub Upload] Failed to upload {repoFilePath}: {ex.Message}");
//            return false;
//        }
//    }

//}







//using UnityEditor;
//using UnityEngine;
//using System.Collections.Generic;
//using System.IO;
//using System.Net;
//using System.Text;
//using Unity.Plastic.Newtonsoft.Json;
//using System.Threading.Tasks;

//public class GitHubUpdater : EditorWindow
//{
//    private List<string> selectedFiles = new List<string>(); // Store relative paths
//    private string version = "1.0"; // Current version
//    private string whatsNew = ""; // "What's New" section
//    private float progress = 0f;
//    private bool isPushing = false;
//    private bool pushCompleted = false;

//    [MenuItem("Tools/GitHub Updater")]
//    public static void ShowWindow()
//    {
//        GetWindow<GitHubUpdater>("GitHub Updater");
//    }

//    private void OnGUI()
//    {
//        GUILayout.Label("Select Files or Folder to Upload", EditorStyles.boldLabel);

//        if (GUILayout.Button("Add File"))
//        {
//            string absolutePath = EditorUtility.OpenFilePanel("Select a File", Application.dataPath, "");
//            AddFile(absolutePath);
//        }

//        if (GUILayout.Button("Add Folder"))
//        {
//            string folderPath = EditorUtility.OpenFolderPanel("Select a Folder", Application.dataPath, "");
//            if (!string.IsNullOrEmpty(folderPath))
//            {
//                AddFolder(folderPath);
//            }
//        }

//        GUILayout.Label("Selected Files:", EditorStyles.boldLabel);
//        foreach (var filePath in selectedFiles)
//        {
//            GUILayout.Label(filePath);
//        }

//        // Add Show Current Version Button and display current version
//        if (GUILayout.Button("Show Current Version"))
//        {
//            version = GetCurrentVersionFromFile();
//        }

//        version = EditorGUILayout.TextField("Version:", version);

//        // "What's New" Text Area
//        whatsNew = EditorGUILayout.TextArea(whatsNew, GUILayout.Height(100));

//        if (GUILayout.Button("Push to GitHub") && !isPushing)
//        {
//            _ = PushToGitHubAsync();
//        }

//        if (isPushing)
//        {
//            EditorGUI.ProgressBar(new Rect(10, position.height - 40, position.width - 20, 20), progress, "Uploading...");
//            Repaint();
//        }
//        else if (pushCompleted)
//        {
//            GUIStyle style = new GUIStyle(GUI.skin.label);
//            style.normal.textColor = Color.green;
//            GUILayout.Label("Push Completed!", style);
//        }
//    }

//    private void AddFile(string absolutePath)
//    {
//        if (!string.IsNullOrEmpty(absolutePath))
//        {
//            string relativePath = "Assets" + absolutePath.Replace(Application.dataPath, "");
//            if (!selectedFiles.Contains(relativePath))
//            {
//                selectedFiles.Add(relativePath);
//            }
//        }
//    }

//    private void AddFolder(string folderPath)
//    {
//        if (!string.IsNullOrEmpty(folderPath))
//        {
//            string[] files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories);
//            foreach (string file in files)
//            {
//                AddFile(file);
//            }
//        }
//    }

//    private string GetCurrentVersionFromFile()
//    {
//        string versionFilePath = "Assets/version.txt";
//        if (File.Exists(versionFilePath))
//        {
//            string[] lines = File.ReadAllLines(versionFilePath);
//            if (lines.Length > 0)
//            {
//                return lines[0]; // Assuming the first line contains the version number
//            }
//        }
//        return "Unknown Version";
//    }

//    private async Task PushToGitHubAsync()
//    {
//        isPushing = true;
//        pushCompleted = false;
//        progress = 0f;
//        Repaint();

//        string commitMessage = "Updating files to version " + version;
//        string repoOwner = GitHubConfig.RepositoryOwner;
//        string repoName = GitHubConfig.RepositoryName;
//        string token = GitHubConfig.Token;

//        bool versionUpdated = PushVersionFile(repoOwner, repoName, token, version, commitMessage);

//        if (versionUpdated)
//        {
//            Debug.Log("Version updated successfully. Now updating selected files...");
//            int totalFiles = selectedFiles.Count;
//            int processedFiles = 0;

//            foreach (var filePath in selectedFiles)
//            {
//                await Task.Run(() => PushFileToGitHub(repoOwner, repoName, token, filePath, commitMessage));
//                processedFiles++;
//                progress = (float)processedFiles / totalFiles;
//                Repaint();
//            }
//        }
//        else
//        {
//            Debug.LogError("Failed to update version. Files not pushed.");
//        }

//        isPushing = false;
//        pushCompleted = true;
//        Repaint();
//    }

//    private bool PushVersionFile(string repoOwner, string repoName, string token, string version, string commitMessage)
//    {
//        string versionFilePath = "Assets/version.txt";
//        string localPath = Path.Combine(Application.dataPath, "version.txt");

//        // Write version number and "What's New" section to local version.txt file
//        string versionContent = $"{version}\n\nWhat's New:\n{whatsNew}";
//        File.WriteAllText(localPath, versionContent);
//        AssetDatabase.ImportAsset(versionFilePath);
//        AssetDatabase.Refresh();

//        return PushFileToGitHub(repoOwner, repoName, token, versionFilePath, commitMessage);
//    }

//    private bool PushFileToGitHub(string repoOwner, string repoName, string token, string filePath, string commitMessage)
//    {
//        string repoPath = filePath.Replace("Assets/", "");
//        string absoluteFilePath = Path.Combine(Application.dataPath, filePath.Replace("Assets/", ""));

//        if (!File.Exists(absoluteFilePath))
//        {
//            Debug.LogError($"[FILE ERROR] File not found: {absoluteFilePath}");
//            return false;
//        }

//        string fileContent = System.Convert.ToBase64String(File.ReadAllBytes(absoluteFilePath));
//        string sha = GetFileSHA(repoOwner, repoName, repoPath, token);

//        var payload = new Dictionary<string, object>
//        {
//            { "message", commitMessage },
//            { "content", fileContent }
//        };

//        if (!string.IsNullOrEmpty(sha))
//        {
//            payload["sha"] = sha;
//        }

//        string jsonPayload = JsonConvert.SerializeObject(payload);
//        string url = $"https://api.github.com/repos/{repoOwner}/{repoName}/contents/{repoPath}";

//        return UploadToGitHub(url, jsonPayload, token);
//    }

//    private string GetFileSHA(string owner, string repo, string filePath, string token)
//    {
//        try
//        {
//            string url = $"https://api.github.com/repos/{owner}/{repo}/contents/{filePath}";
//            using (WebClient client = new WebClient())
//            {
//                client.Headers.Add("Authorization", "token " + token);
//                client.Headers.Add("User-Agent", "UnityGitHubUploader");
//                string response = client.DownloadString(url);

//                var jsonResponse = JsonConvert.DeserializeObject<Dictionary<string, object>>(response);
//                return jsonResponse.ContainsKey("sha") ? jsonResponse["sha"].ToString() : null;
//            }
//        }
//        catch (WebException)
//        {
//            return null;
//        }
//    }

//    private bool UploadToGitHub(string url, string jsonPayload, string token)
//    {
//        try
//        {
//            using (WebClient client = new WebClient())
//            {
//                client.Headers.Add("Authorization", "token " + token);
//                client.Headers.Add("User-Agent", "UnityGitHubUploader");
//                client.Headers.Add("Content-Type", "application/json");
//                client.UploadString(url, "PUT", jsonPayload);
//                return true;
//            }
//        }
//        catch (WebException)
//        {
//            return false;
//        }
//    }
//}



//using UnityEditor;
//using UnityEngine;
//using System.Collections.Generic;
//using System.IO;
//using System.Net;
//using System.Text;
//using Unity.Plastic.Newtonsoft.Json;
//using System.Threading.Tasks;

//public class GitHubUpdater : EditorWindow
//{
//    private List<string> selectedFiles = new List<string>(); // Store relative paths
//    private string version = "1.0"; // Current version
//    private string whatsNew = ""; // "What's New" section
//    private float progress = 0f;
//    private bool isPushing = false;
//    private bool pushCompleted = false;

//    [MenuItem("Tools/GitHub Updater")]
//    public static void ShowWindow()
//    {
//        GetWindow<GitHubUpdater>("GitHub Updater");
//    }

//    private void OnGUI()
//    {
//        GUILayout.Label("Select Files or Folder to Upload", EditorStyles.boldLabel);

//        if (GUILayout.Button("Add File"))
//        {
//            string absolutePath = EditorUtility.OpenFilePanel("Select a File", Application.dataPath, "");
//            AddFile(absolutePath);
//        }

//        if (GUILayout.Button("Add Folder"))
//        {
//            string folderPath = EditorUtility.OpenFolderPanel("Select a Folder", Application.dataPath, "");
//            if (!string.IsNullOrEmpty(folderPath))
//            {
//                AddFolder(folderPath);
//            }
//        }

//        GUILayout.Label("Selected Files:", EditorStyles.boldLabel);
//        foreach (var filePath in selectedFiles)
//        {
//            GUILayout.Label(filePath);
//        }

//        version = EditorGUILayout.TextField("Version:", version);

//        // "What's New" Text Area
//        whatsNew = EditorGUILayout.TextArea(whatsNew, GUILayout.Height(100));

//        if (GUILayout.Button("Push to GitHub") && !isPushing)
//        {
//            _ = PushToGitHubAsync();
//        }

//        if (isPushing)
//        {
//            EditorGUI.ProgressBar(new Rect(10, position.height - 40, position.width - 20, 20), progress, "Uploading...");
//            Repaint();
//        }
//        else if (pushCompleted)
//        {
//            GUIStyle style = new GUIStyle(GUI.skin.label);
//            style.normal.textColor = Color.green;
//            GUILayout.Label("Push Completed!", style);
//        }
//    }

//    private void AddFile(string absolutePath)
//    {
//        if (!string.IsNullOrEmpty(absolutePath))
//        {
//            string relativePath = "Assets" + absolutePath.Replace(Application.dataPath, "");
//            if (!selectedFiles.Contains(relativePath))
//            {
//                selectedFiles.Add(relativePath);
//            }
//        }
//    }

//    private void AddFolder(string folderPath)
//    {
//        if (!string.IsNullOrEmpty(folderPath))
//        {
//            string[] files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories);
//            foreach (string file in files)
//            {
//                if (!file.EndsWith(".meta")) // Exclude .meta files for now
//                    AddFile(file);
//            }
//        }
//    }

//    private async Task PushToGitHubAsync()
//    {
//        isPushing = true;
//        pushCompleted = false;
//        progress = 0f;
//        Repaint();

//        string commitMessage = "Updating files to version " + version;
//        string repoOwner = GitHubConfig.RepositoryOwner;
//        string repoName = GitHubConfig.RepositoryName;
//        string token = GitHubConfig.Token;

//        bool versionUpdated = PushVersionFile(repoOwner, repoName, token, version, commitMessage);

//        if (versionUpdated)
//        {
//            Debug.Log("Version updated successfully. Now updating selected files...");
//            int totalFiles = selectedFiles.Count;
//            int processedFiles = 0;

//            foreach (var filePath in selectedFiles)
//            {
//                await Task.Run(() => PushFileToGitHub(repoOwner, repoName, token, filePath, commitMessage));
//                processedFiles++;
//                progress = (float)processedFiles / totalFiles;
//                Repaint();
//            }
//        }
//        else
//        {
//            Debug.LogError("Failed to update version. Files not pushed.");
//        }

//        isPushing = false;
//        pushCompleted = true;
//        Repaint();
//    }

//    private bool PushVersionFile(string repoOwner, string repoName, string token, string version, string commitMessage)
//    {
//        string versionFilePath = "Assets/version.txt";
//        string localPath = Path.Combine(Application.dataPath, "version.txt");

//        // Write version number and "What's New" section to local version.txt file
//        string versionContent = $"{version}\n\nWhat's New:\n{whatsNew}";
//        File.WriteAllText(localPath, versionContent);
//        AssetDatabase.ImportAsset(versionFilePath);
//        AssetDatabase.Refresh();

//        return PushFileToGitHub(repoOwner, repoName, token, versionFilePath, commitMessage);
//    }

//    private bool PushFileToGitHub(string repoOwner, string repoName, string token, string filePath, string commitMessage)
//    {
//        string repoPath = filePath.Replace("Assets/", "");
//        string absoluteFilePath = Path.Combine(Application.dataPath, filePath.Replace("Assets/", ""));

//        if (!File.Exists(absoluteFilePath))
//        {
//            Debug.LogError($"[FILE ERROR] File not found: {absoluteFilePath}");
//            return false;
//        }

//        string fileContent = System.Convert.ToBase64String(File.ReadAllBytes(absoluteFilePath));
//        string sha = GetFileSHA(repoOwner, repoName, repoPath, token);

//        var payload = new Dictionary<string, object>
//        {
//            { "message", commitMessage },
//            { "content", fileContent }
//        };

//        if (!string.IsNullOrEmpty(sha))
//        {
//            payload["sha"] = sha;
//        }

//        string jsonPayload = JsonConvert.SerializeObject(payload);
//        string url = $"https://api.github.com/repos/{repoOwner}/{repoName}/contents/{repoPath}";

//        return UploadToGitHub(url, jsonPayload, token);
//    }

//    private string GetFileSHA(string owner, string repo, string filePath, string token)
//    {
//        try
//        {
//            string url = $"https://api.github.com/repos/{owner}/{repo}/contents/{filePath}";
//            using (WebClient client = new WebClient())
//            {
//                client.Headers.Add("Authorization", "token " + token);
//                client.Headers.Add("User-Agent", "UnityGitHubUploader");
//                string response = client.DownloadString(url);

//                var jsonResponse = JsonConvert.DeserializeObject<Dictionary<string, object>>(response);
//                return jsonResponse.ContainsKey("sha") ? jsonResponse["sha"].ToString() : null;
//            }
//        }
//        catch (WebException)
//        {
//            return null;
//        }
//    }

//    private bool UploadToGitHub(string url, string jsonPayload, string token)
//    {
//        try
//        {
//            using (WebClient client = new WebClient())
//            {
//                client.Headers.Add("Authorization", "token " + token);
//                client.Headers.Add("User-Agent", "UnityGitHubUploader");
//                client.Headers.Add("Content-Type", "application/json");
//                client.UploadString(url, "PUT", jsonPayload);
//                return true;
//            }
//        }
//        catch (WebException)
//        {
//            return false;
//        }
//    }
//}


//using UnityEditor;
//using UnityEngine;
//using System.Collections.Generic;
//using System.IO;
//using System.Net;
//using System.Text;
//using Unity.Plastic.Newtonsoft.Json;
//using System.Threading.Tasks;

//public class GitHubUpdater : EditorWindow
//{
//    private List<string> selectedFiles = new List<string>(); // Store relative paths
//    private string version = "1.0"; // Current version
//    private float progress = 0f;
//    private bool isPushing = false;
//    private bool pushCompleted = false;

//    [MenuItem("Tools/GitHub Updater")]
//    public static void ShowWindow()
//    {
//        GetWindow<GitHubUpdater>("GitHub Updater");
//    }

//    private void OnGUI()
//    {
//        GUILayout.Label("Select Files or Folder to Upload", EditorStyles.boldLabel);

//        if (GUILayout.Button("Add File"))
//        {
//            string absolutePath = EditorUtility.OpenFilePanel("Select a File", Application.dataPath, "");
//            AddFile(absolutePath);
//        }

//        if (GUILayout.Button("Add Folder"))
//        {
//            string folderPath = EditorUtility.OpenFolderPanel("Select a Folder", Application.dataPath, "");
//            if (!string.IsNullOrEmpty(folderPath))
//            {
//                AddFolder(folderPath);
//            }
//        }

//        GUILayout.Label("Selected Files:", EditorStyles.boldLabel);
//        foreach (var filePath in selectedFiles)
//        {
//            GUILayout.Label(filePath);
//        }

//        version = EditorGUILayout.TextField("Version:", version);

//        if (GUILayout.Button("Push to GitHub") && !isPushing)
//        {
//            _ = PushToGitHubAsync();
//        }

//        if (isPushing)
//        {
//            EditorGUI.ProgressBar(new Rect(10, position.height - 40, position.width - 20, 20), progress, "Uploading...");
//            Repaint();
//        }
//        else if (pushCompleted)
//        {
//            GUIStyle style = new GUIStyle(GUI.skin.label);
//            style.normal.textColor = Color.green;
//            GUILayout.Label("Push Completed!", style);
//        }
//    }

//    private void AddFile(string absolutePath)
//    {
//        if (!string.IsNullOrEmpty(absolutePath))
//        {
//            string relativePath = "Assets" + absolutePath.Replace(Application.dataPath, "");
//            if (!selectedFiles.Contains(relativePath))
//            {
//                selectedFiles.Add(relativePath);
//            }
//        }
//    }

//    private void AddFolder(string folderPath)
//    {
//        if (!string.IsNullOrEmpty(folderPath))
//        {
//            string[] files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories);
//            foreach (string file in files)
//            {
//                if (!file.EndsWith(".meta")) // Exclude .meta files for now
//                    AddFile(file);
//            }
//        }
//    }

//    private async Task PushToGitHubAsync()
//    {
//        isPushing = true;
//        pushCompleted = false;
//        progress = 0f;
//        Repaint();

//        string commitMessage = "Updating files to version " + version;
//        string repoOwner = GitHubConfig.RepositoryOwner;
//        string repoName = GitHubConfig.RepositoryName;
//        string token = GitHubConfig.Token;

//        bool versionUpdated = PushVersionFile(repoOwner, repoName, token, version, commitMessage);

//        if (versionUpdated)
//        {
//            Debug.Log("Version updated successfully. Now updating selected files...");
//            int totalFiles = selectedFiles.Count;
//            int processedFiles = 0;

//            foreach (var filePath in selectedFiles)
//            {
//                await Task.Run(() => PushFileToGitHub(repoOwner, repoName, token, filePath, commitMessage));
//                processedFiles++;
//                progress = (float)processedFiles / totalFiles;
//                Repaint();
//            }
//        }
//        else
//        {
//            Debug.LogError("Failed to update version. Files not pushed.");
//        }

//        isPushing = false;
//        pushCompleted = true;
//        Repaint();
//    }

//    private bool PushVersionFile(string repoOwner, string repoName, string token, string version, string commitMessage)
//    {
//        string versionFilePath = "Assets/version.txt";
//        string localPath = Path.Combine(Application.dataPath, "version.txt");

//        // Write version number to local version.txt file
//        File.WriteAllText(localPath, version);
//        AssetDatabase.ImportAsset(versionFilePath);
//        AssetDatabase.Refresh();

//        return PushFileToGitHub(repoOwner, repoName, token, versionFilePath, commitMessage);
//    }

//    private bool PushFileToGitHub(string repoOwner, string repoName, string token, string filePath, string commitMessage)
//    {
//        string repoPath = filePath.Replace("Assets/", "");
//        string absoluteFilePath = Path.Combine(Application.dataPath, filePath.Replace("Assets/", ""));

//        if (!File.Exists(absoluteFilePath))
//        {
//            Debug.LogError($"[FILE ERROR] File not found: {absoluteFilePath}");
//            return false;
//        }

//        string fileContent = System.Convert.ToBase64String(File.ReadAllBytes(absoluteFilePath));
//        string sha = GetFileSHA(repoOwner, repoName, repoPath, token);

//        var payload = new Dictionary<string, object>
//        {
//            { "message", commitMessage },
//            { "content", fileContent }
//        };

//        if (!string.IsNullOrEmpty(sha))
//        {
//            payload["sha"] = sha;
//        }

//        string jsonPayload = JsonConvert.SerializeObject(payload);
//        string url = $"https://api.github.com/repos/{repoOwner}/{repoName}/contents/{repoPath}";

//        return UploadToGitHub(url, jsonPayload, token);
//    }

//    private string GetFileSHA(string owner, string repo, string filePath, string token)
//    {
//        try
//        {
//            string url = $"https://api.github.com/repos/{owner}/{repo}/contents/{filePath}";
//            using (WebClient client = new WebClient())
//            {
//                client.Headers.Add("Authorization", "token " + token);
//                client.Headers.Add("User-Agent", "UnityGitHubUploader");
//                string response = client.DownloadString(url);

//                var jsonResponse = JsonConvert.DeserializeObject<Dictionary<string, object>>(response);
//                return jsonResponse.ContainsKey("sha") ? jsonResponse["sha"].ToString() : null;
//            }
//        }
//        catch (WebException)
//        {
//            return null;
//        }
//    }

//    private bool UploadToGitHub(string url, string jsonPayload, string token)
//    {
//        try
//        {
//            using (WebClient client = new WebClient())
//            {
//                client.Headers.Add("Authorization", "token " + token);
//                client.Headers.Add("User-Agent", "UnityGitHubUploader");
//                client.Headers.Add("Content-Type", "application/json");
//                client.UploadString(url, "PUT", jsonPayload);
//                return true;
//            }
//        }
//        catch (WebException)
//        {
//            return false;
//        }
//    }
//}



//using UnityEditor;
//using UnityEngine;
//using System.Collections.Generic;
//using System.IO;
//using System.Net;
//using System.Text;
//using Unity.Plastic.Newtonsoft.Json;

//public class GitHubUpdater : EditorWindow
//{
//    private List<string> selectedFiles = new List<string>(); // Store relative paths
//    private string version = "1.0"; // Current version

//    [MenuItem("Tools/GitHub Updater")]
//    public static void ShowWindow()
//    {
//        GetWindow<GitHubUpdater>("GitHub Updater");
//    }

//    private void OnGUI()
//    {
//        GUILayout.Label("Select Files or Folder to Upload", EditorStyles.boldLabel);

//        if (GUILayout.Button("Add File"))
//        {
//            string absolutePath = EditorUtility.OpenFilePanel("Select a File", Application.dataPath, "");
//            AddFile(absolutePath);
//        }

//        if (GUILayout.Button("Add Folder"))
//        {
//            string folderPath = EditorUtility.OpenFolderPanel("Select a Folder", Application.dataPath, "");
//            if (!string.IsNullOrEmpty(folderPath))
//            {
//                AddFolder(folderPath);
//            }
//        }

//        GUILayout.Label("Selected Files:", EditorStyles.boldLabel);
//        foreach (var filePath in selectedFiles)
//        {
//            GUILayout.Label(filePath);
//        }

//        version = EditorGUILayout.TextField("Version:", version);

//        if (GUILayout.Button("Push to GitHub"))
//        {
//            PushToGitHub();
//        }
//    }

//    private void AddFile(string absolutePath)
//    {
//        if (!string.IsNullOrEmpty(absolutePath))
//        {
//            string relativePath = "Assets" + absolutePath.Replace(Application.dataPath, "");
//            if (!selectedFiles.Contains(relativePath))
//            {
//                selectedFiles.Add(relativePath);
//            }
//        }
//    }

//    private void AddFolder(string folderPath)
//    {
//        if (!string.IsNullOrEmpty(folderPath))
//        {
//            string[] files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories);
//            foreach (string file in files)
//            {
//                if (!file.EndsWith(".meta")) // Exclude .meta files for now
//                    AddFile(file);
//            }
//        }
//    }

//    private void PushToGitHub()
//    {
//        string commitMessage = "Updating files to version " + version;
//        string repoOwner = GitHubConfig.RepositoryOwner;
//        string repoName = GitHubConfig.RepositoryName;
//        string token = GitHubConfig.Token;

//        bool versionUpdated = PushVersionFile(repoOwner, repoName, token, version, commitMessage);

//        if (versionUpdated)
//        {
//            Debug.Log("Version updated successfully. Now updating selected files...");
//            foreach (var filePath in selectedFiles)
//            {
//                PushFileToGitHub(repoOwner, repoName, token, filePath, commitMessage);
//            }
//        }
//        else
//        {
//            Debug.LogError("Failed to update version. Files not pushed.");
//        }
//    }

//    private bool PushVersionFile(string repoOwner, string repoName, string token, string version, string commitMessage)
//    {
//        string versionFilePath = "Assets/version.txt";
//        string localPath = Path.Combine(Application.dataPath, "version.txt");

//        // Write version number to local version.txt file
//        File.WriteAllText(localPath, version);
//        AssetDatabase.ImportAsset(versionFilePath);
//        AssetDatabase.Refresh();

//        return PushFileToGitHub(repoOwner, repoName, token, versionFilePath, commitMessage);
//    }

//    private bool PushFileToGitHub(string repoOwner, string repoName, string token, string filePath, string commitMessage)
//    {
//        string repoPath = filePath.Replace("Assets/", "");
//        string absoluteFilePath = Path.Combine(Application.dataPath, filePath.Replace("Assets/", ""));

//        if (!File.Exists(absoluteFilePath))
//        {
//            Debug.LogError($"[FILE ERROR] File not found: {absoluteFilePath}");
//            return false;
//        }

//        string fileContent = System.Convert.ToBase64String(File.ReadAllBytes(absoluteFilePath));
//        string sha = GetFileSHA(repoOwner, repoName, repoPath, token);

//        if (!string.IsNullOrEmpty(sha))
//        {
//            Debug.Log($"[FILE EXISTS] {repoPath} SHA: {sha}");
//        }
//        else
//        {
//            Debug.Log($"[NEW FILE] {repoPath} will be created.");
//        }

//        var payload = new Dictionary<string, object>
//    {
//        { "message", commitMessage },
//        { "content", fileContent }
//    };

//        if (!string.IsNullOrEmpty(sha))
//        {
//            payload["sha"] = sha;
//        }

//        string jsonPayload = JsonConvert.SerializeObject(payload);
//        string url = $"https://api.github.com/repos/{repoOwner}/{repoName}/contents/{repoPath}";

//        bool uploadSuccess = UploadToGitHub(url, jsonPayload, token);

//        if (uploadSuccess)
//            Debug.Log($"[UPLOAD SUCCESS] {repoPath} updated.");
//        else
//            Debug.LogError($"[UPLOAD FAILED] {repoPath} was not updated.");

//        return uploadSuccess;
//    }


//    private string GetFileSHA(string owner, string repo, string filePath, string token)
//    {
//        try
//        {
//            string url = $"https://api.github.com/repos/{owner}/{repo}/contents/{filePath}";
//            using (WebClient client = new WebClient())
//            {
//                client.Headers.Add("Authorization", "token " + token);
//                client.Headers.Add("User-Agent", "UnityGitHubUploader");
//                string response = client.DownloadString(url);

//                var jsonResponse = JsonConvert.DeserializeObject<Dictionary<string, object>>(response);
//                if (jsonResponse.ContainsKey("sha"))
//                {
//                    string sha = jsonResponse["sha"].ToString();
//                    Debug.Log($"[SHA FOUND] {filePath} → SHA: {sha}");
//                    return sha;
//                }
//                else
//                {
//                    Debug.LogWarning($"[SHA MISSING] Could not retrieve SHA for {filePath}");
//                }
//            }
//        }
//        catch (WebException ex)
//        {
//            Debug.LogWarning($"[SHA ERROR] {filePath} does not exist on GitHub or request failed: {ex.Message}");
//        }
//        return null;
//    }


//    private bool UploadToGitHub(string url, string jsonPayload, string token)
//    {
//        try
//        {
//            using (WebClient client = new WebClient())
//            {
//                client.Headers.Add("Authorization", "token " + token);
//                client.Headers.Add("User-Agent", "UnityGitHubUploader");
//                client.Headers.Add("Content-Type", "application/json");

//                string response = client.UploadString(url, "PUT", jsonPayload);
//                Debug.Log($"File successfully uploaded/updated: {url}");
//                return true;
//            }
//        }
//        catch (WebException ex)
//        {
//            Debug.LogError($"GitHub upload failed for {url}: {ex.Message}");
//            return false;
//        }
//    }
//}



//using UnityEditor;
//using UnityEngine;
//using System.Collections.Generic;
//using System.IO;
//using System.Net;
//using System.Text;
//using Unity.Plastic.Newtonsoft.Json; // Install Newtonsoft JSON via Package Manager

//public class GitHubUpdater : EditorWindow
//{
//    private List<string> selectedFiles = new List<string>(); // Store relative paths
//    private string version = "1.0"; // Current version

//    [MenuItem("Tools/GitHub Updater")]
//    public static void ShowWindow()
//    {
//        GetWindow<GitHubUpdater>("GitHub Updater");
//    }

//    private void OnGUI()
//    {
//        GUILayout.Label("Select Files or Folder to Upload", EditorStyles.boldLabel);

//        if (GUILayout.Button("Add File"))
//        {
//            string absolutePath = EditorUtility.OpenFilePanel("Select a File", Application.dataPath, "");
//            AddFile(absolutePath);
//        }

//        if (GUILayout.Button("Add Folder"))
//        {
//            string folderPath = EditorUtility.OpenFolderPanel("Select a Folder", Application.dataPath, "");
//            if (!string.IsNullOrEmpty(folderPath))
//            {
//                AddFolder(folderPath);
//            }
//        }

//        GUILayout.Label("Selected Files:", EditorStyles.boldLabel);
//        foreach (var filePath in selectedFiles)
//        {
//            GUILayout.Label(filePath);
//        }

//        version = EditorGUILayout.TextField("Version:", version);

//        if (GUILayout.Button("Push to GitHub"))
//        {
//            PushToGitHub();
//        }
//    }

//    private void AddFile(string absolutePath)
//    {
//        if (!string.IsNullOrEmpty(absolutePath))
//        {
//            string relativePath = "Assets" + absolutePath.Replace(Application.dataPath, "");
//            if (!selectedFiles.Contains(relativePath))
//            {
//                selectedFiles.Add(relativePath);
//            }
//        }
//    }

//    private void AddFolder(string folderPath)
//    {
//        if (!string.IsNullOrEmpty(folderPath))
//        {
//            string[] files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories);
//            foreach (string file in files)
//            {
//                AddFile(file);
//            }
//        }
//    }

//    private void PushToGitHub()
//    {
//        string commitMessage = "Updating files to version " + version;
//        string repoOwner = GitHubConfig.RepositoryOwner;
//        string repoName = GitHubConfig.RepositoryName;
//        string token = GitHubConfig.Token;

//        foreach (var filePath in selectedFiles)
//        {
//            string repoPath = filePath.Replace("Assets/", "");
//            string absoluteFilePath = Path.Combine(Application.dataPath, filePath.Replace("Assets/", ""));

//            if (!File.Exists(absoluteFilePath))
//            {
//                Debug.LogError($"File not found: {absoluteFilePath}");
//                continue;
//            }

//            string fileContent = System.Convert.ToBase64String(File.ReadAllBytes(absoluteFilePath));

//            string sha = GetFileSHA(repoOwner, repoName, repoPath, token);

//            var payload = new Dictionary<string, object>
//            {
//                { "message", commitMessage },
//                { "content", fileContent }
//            };

//            if (!string.IsNullOrEmpty(sha))
//            {
//                payload["sha"] = sha;
//                Debug.Log($"Updating existing file: {repoPath}");
//            }
//            else
//            {
//                Debug.Log($"Creating new file: {repoPath}");
//            }

//            string jsonPayload = JsonConvert.SerializeObject(payload);
//            string url = $"https://api.github.com/repos/{repoOwner}/{repoName}/contents/{repoPath}";

//            UploadToGitHub(url, jsonPayload, token);
//        }
//    }

//    private string GetFileSHA(string owner, string repo, string filePath, string token)
//    {
//        try
//        {
//            string url = $"https://api.github.com/repos/{owner}/{repo}/contents/{filePath}";
//            using (WebClient client = new WebClient())
//            {
//                client.Headers.Add("Authorization", "token " + token);
//                client.Headers.Add("User-Agent", "UnityGitHubUploader");
//                string response = client.DownloadString(url);

//                var jsonResponse = JsonConvert.DeserializeObject<Dictionary<string, object>>(response);
//                if (jsonResponse.ContainsKey("sha"))
//                {
//                    return jsonResponse["sha"].ToString();
//                }
//            }
//        }
//        catch (WebException ex)
//        {
//            Debug.LogWarning($"File {filePath} does not exist on GitHub. It will be created. {ex.Message}");
//        }
//        return null;
//    }

//    private void UploadToGitHub(string url, string jsonPayload, string token)
//    {
//        try
//        {
//            using (WebClient client = new WebClient())
//            {
//                client.Headers.Add("Authorization", "token " + token);
//                client.Headers.Add("User-Agent", "UnityGitHubUploader");
//                client.Headers.Add("Content-Type", "application/json");

//                string response = client.UploadString(url, "PUT", jsonPayload);
//                Debug.Log($"File successfully uploaded/updated: {url}");
//            }
//        }
//        catch (WebException ex)
//        {
//            Debug.LogError($"GitHub upload failed for {url}: {ex.Message}");
//        }
//    }
//}






//using UnityEditor;
//using UnityEngine;
//using System.Collections.Generic;
//using System.IO;
//using System.Net;
//using System.Text;
//using Unity.Plastic.Newtonsoft.Json; // Install Newtonsoft JSON via Package Manager

//public class GitHubUpdater : EditorWindow
//{
//    private List<string> selectedFiles = new List<string>(); // Store relative paths
//    private string version = "1.0"; // Current version

//    [MenuItem("Tools/GitHub Updater")]
//    public static void ShowWindow()
//    {
//        GetWindow<GitHubUpdater>("GitHub Updater");
//    }

//    private void OnGUI()
//    {
//        GUILayout.Label("Select Files to Upload", EditorStyles.boldLabel);

//        if (GUILayout.Button("Add Files"))
//        {
//            string absolutePath = EditorUtility.OpenFilePanel("Select Files", Application.dataPath, "");
//            if (!string.IsNullOrEmpty(absolutePath))
//            {
//                string relativePath = "Assets" + absolutePath.Replace(Application.dataPath, "");
//                if (!selectedFiles.Contains(relativePath))
//                {
//                    selectedFiles.Add(relativePath);
//                }
//            }
//        }

//        GUILayout.Label("Selected Files:", EditorStyles.boldLabel);
//        foreach (var filePath in selectedFiles)
//        {
//            GUILayout.Label(filePath);
//        }

//        version = EditorGUILayout.TextField("Version:", version);

//        if (GUILayout.Button("Push to GitHub"))
//        {
//            PushToGitHub();
//        }
//    }
//    private void PushToGitHub()
//    {
//        string commitMessage = "Updating files to version " + version;
//        string repoOwner = GitHubConfig.RepositoryOwner;
//        string repoName = GitHubConfig.RepositoryName;
//        string token = GitHubConfig.Token;

//        foreach (var filePath in selectedFiles)
//        {
//            string fileName = Path.GetFileName(filePath);
//            string repoPath = filePath.Replace("Assets/", ""); // Convert Unity path to GitHub repo path
//            string absoluteFilePath = Path.Combine(Application.dataPath, filePath.Replace("Assets/", ""));

//            if (!File.Exists(absoluteFilePath))
//            {
//                Debug.LogError($"File not found: {absoluteFilePath}");
//                continue;
//            }

//            string fileContent = System.Convert.ToBase64String(File.ReadAllBytes(absoluteFilePath));

//            // ✅ Check if file exists on GitHub and get its SHA
//            string sha = GetFileSHA(repoOwner, repoName, repoPath, token);

//            // ✅ If the file exists, include SHA for update
//            var payload = new Dictionary<string, object>
//        {
//            { "message", commitMessage },
//            { "content", fileContent }
//        };

//            if (!string.IsNullOrEmpty(sha))
//            {
//                payload["sha"] = sha; // Required for updating existing files
//                Debug.Log($"Updating existing file: {repoPath}");
//            }
//            else
//            {
//                Debug.Log($"Creating new file: {repoPath}");
//            }

//            string jsonPayload = JsonConvert.SerializeObject(payload);
//            string url = $"https://api.github.com/repos/{repoOwner}/{repoName}/contents/{repoPath}";

//            UploadToGitHub(url, jsonPayload, token);
//        }
//    }

//    //private void PushToGitHub()
//    //{
//    //    string commitMessage = "Updating files to version " + version;
//    //    string repoOwner = GitHubConfig.RepositoryOwner;
//    //    string repoName = GitHubConfig.RepositoryName;
//    //    string token = GitHubConfig.Token;

//    //    foreach (var filePath in selectedFiles)
//    //    {
//    //        string fileName = Path.GetFileName(filePath);
//    //        string repoPath = filePath.Replace("Assets/", ""); // Remove "Assets/" for GitHub repo path
//    //        string absoluteFilePath = Path.Combine(Application.dataPath, filePath.Replace("Assets/", ""));

//    //        if (!File.Exists(absoluteFilePath))
//    //        {
//    //            Debug.LogError($"File not found: {absoluteFilePath}");
//    //            continue;
//    //        }

//    //        string fileContent = System.Convert.ToBase64String(File.ReadAllBytes(absoluteFilePath));

//    //        // Check if file exists on GitHub (needed to get SHA)
//    //        string sha = GetFileSHA(repoOwner, repoName, repoPath, token);

//    //        // Prepare JSON payload
//    //        var payload = new
//    //        {
//    //            message = commitMessage,
//    //            content = fileContent,
//    //            sha = sha // Required if updating an existing file
//    //        };
//    //        string jsonPayload = JsonConvert.SerializeObject(payload);

//    //        string url = $"https://api.github.com/repos/{repoOwner}/{repoName}/contents/{repoPath}";
//    //        UploadToGitHub(url, jsonPayload, token);
//    //    }
//    //}

//    private string GetFileSHA(string owner, string repo, string filePath, string token)
//    {
//        try
//        {
//            string url = $"https://api.github.com/repos/{owner}/{repo}/contents/{filePath}";
//            using (WebClient client = new WebClient())
//            {
//                client.Headers.Add("Authorization", "token " + token);
//                client.Headers.Add("User-Agent", "UnityGitHubUploader");
//                string response = client.DownloadString(url);

//                var jsonResponse = JsonConvert.DeserializeObject<Dictionary<string, object>>(response);
//                if (jsonResponse.ContainsKey("sha"))
//                {
//                    return jsonResponse["sha"].ToString();
//                }
//            }
//        }
//        catch (WebException ex)
//        {
//            Debug.LogWarning($"File {filePath} does not exist on GitHub. It will be created. {ex.Message}");
//        }
//        return null;
//    }


//    //private string GetFileSHA(string owner, string repo, string filePath, string token)
//    //{
//    //    try
//    //    {
//    //        string url = $"https://api.github.com/repos/{owner}/{repo}/contents/{filePath}";
//    //        using (WebClient client = new WebClient())
//    //        {
//    //            client.Headers.Add("Authorization", "token " + token);
//    //            client.Headers.Add("User-Agent", "UnityGitHubUploader");
//    //            string response = client.DownloadString(url);

//    //            var jsonResponse = JsonConvert.DeserializeObject<Dictionary<string, object>>(response);
//    //            if (jsonResponse.ContainsKey("sha"))
//    //            {
//    //                return jsonResponse["sha"].ToString();
//    //            }
//    //        }
//    //    }
//    //    catch (WebException ex)
//    //    {
//    //        Debug.LogWarning("File does not exist on GitHub, creating a new one.");
//    //    }
//    //    return null;
//    //}

//    private void UploadToGitHub(string url, string jsonPayload, string token)
//    {
//        try
//        {
//            using (WebClient client = new WebClient())
//            {
//                client.Headers.Add("Authorization", "token " + token);
//                client.Headers.Add("User-Agent", "UnityGitHubUploader");
//                client.Headers.Add("Content-Type", "application/json");

//                string response = client.UploadString(url, "PUT", jsonPayload);
//                Debug.Log($"File successfully uploaded/updated: {url}");
//            }
//        }
//        catch (WebException ex)
//        {
//            Debug.LogError($"GitHub upload failed for {url}: {ex.Message}");
//        }
//    }


//    //private void UploadToGitHub(string url, string jsonPayload, string token)
//    //{
//    //    try
//    //    {
//    //        using (WebClient client = new WebClient())
//    //        {
//    //            client.Headers.Add("Authorization", "token " + token);
//    //            client.Headers.Add("User-Agent", "UnityGitHubUploader");
//    //            client.Headers.Add("Content-Type", "application/json");
//    //            client.UploadString(url, "PUT", jsonPayload);
//    //            Debug.Log("File uploaded successfully: " + url);
//    //        }
//    //    }
//    //    catch (WebException ex)
//    //    {
//    //        Debug.LogError("GitHub upload failed: " + ex.Message);
//    //    }
//    //}
//}
