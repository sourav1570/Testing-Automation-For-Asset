using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using Unity.Plastic.Newtonsoft.Json;
using System.Threading.Tasks;

public class GitHubUpdater : EditorWindow
{
    private List<string> selectedFiles = new List<string>(); // Store relative paths
    private string version = "1.0"; // Current version
    private string whatsNew = ""; // "What's New" section
    private float progress = 0f;
    private bool isPushing = false;
    private bool pushCompleted = false;

    [MenuItem("Tools/GitHub Updater")]
    public static void ShowWindow()
    {
        GetWindow<GitHubUpdater>("GitHub Updater");
    }

    private void OnGUI()
    {
        GUILayout.Label("Select Files or Folder to Upload", EditorStyles.boldLabel);

        if (GUILayout.Button("Add File"))
        {
            string absolutePath = EditorUtility.OpenFilePanel("Select a File", Application.dataPath, "");
            AddFile(absolutePath);
        }

        if (GUILayout.Button("Add Folder"))
        {
            string folderPath = EditorUtility.OpenFolderPanel("Select a Folder", Application.dataPath, "");
            if (!string.IsNullOrEmpty(folderPath))
            {
                AddFolder(folderPath);
            }
        }

        GUILayout.Label("Selected Files:", EditorStyles.boldLabel);
        foreach (var filePath in selectedFiles)
        {
            GUILayout.Label(filePath);
        }

        // Add Show Current Version Button and display current version
        if (GUILayout.Button("Show Current Version"))
        {
            version = GetCurrentVersionFromFile();
        }

        version = EditorGUILayout.TextField("Version:", version);

        // "What's New" Text Area
        whatsNew = EditorGUILayout.TextArea(whatsNew, GUILayout.Height(100));

        if (GUILayout.Button("Push to GitHub") && !isPushing)
        {
            _ = PushToGitHubAsync();
        }

        if (isPushing)
        {
            EditorGUI.ProgressBar(new Rect(10, position.height - 40, position.width - 20, 20), progress, "Uploading...");
            Repaint();
        }
        else if (pushCompleted)
        {
            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.normal.textColor = Color.green;
            GUILayout.Label("Push Completed!", style);
        }
    }

    private void AddFile(string absolutePath)
    {
        if (!string.IsNullOrEmpty(absolutePath))
        {
            string relativePath = "Assets" + absolutePath.Replace(Application.dataPath, "");
            if (!selectedFiles.Contains(relativePath))
            {
                selectedFiles.Add(relativePath);
            }
        }
    }

    private void AddFolder(string folderPath)
    {
        if (!string.IsNullOrEmpty(folderPath))
        {
            string[] files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories);
            foreach (string file in files)
            {
                if (!file.EndsWith(".meta")) // Exclude .meta files for now
                    AddFile(file);
            }
        }
    }

    private string GetCurrentVersionFromFile()
    {
        string versionFilePath = "Assets/version.txt";
        if (File.Exists(versionFilePath))
        {
            string[] lines = File.ReadAllLines(versionFilePath);
            if (lines.Length > 0)
            {
                return lines[0]; // Assuming the first line contains the version number
            }
        }
        return "Unknown Version";
    }

    private async Task PushToGitHubAsync()
    {
        isPushing = true;
        pushCompleted = false;
        progress = 0f;
        Repaint();

        string commitMessage = "Updating files to version " + version;
        string repoOwner = GitHubConfig.RepositoryOwner;
        string repoName = GitHubConfig.RepositoryName;
        string token = GitHubConfig.Token;

        bool versionUpdated = PushVersionFile(repoOwner, repoName, token, version, commitMessage);

        if (versionUpdated)
        {
            Debug.Log("Version updated successfully. Now updating selected files...");
            int totalFiles = selectedFiles.Count;
            int processedFiles = 0;

            foreach (var filePath in selectedFiles)
            {
                await Task.Run(() => PushFileToGitHub(repoOwner, repoName, token, filePath, commitMessage));
                processedFiles++;
                progress = (float)processedFiles / totalFiles;
                Repaint();
            }
        }
        else
        {
            Debug.LogError("Failed to update version. Files not pushed.");
        }

        isPushing = false;
        pushCompleted = true;
        Repaint();
    }

    private bool PushVersionFile(string repoOwner, string repoName, string token, string version, string commitMessage)
    {
        string versionFilePath = "Assets/version.txt";
        string localPath = Path.Combine(Application.dataPath, "version.txt");

        // Write version number and "What's New" section to local version.txt file
        string versionContent = $"{version}\n\nWhat's New:\n{whatsNew}";
        File.WriteAllText(localPath, versionContent);
        AssetDatabase.ImportAsset(versionFilePath);
        AssetDatabase.Refresh();

        return PushFileToGitHub(repoOwner, repoName, token, versionFilePath, commitMessage);
    }

    private bool PushFileToGitHub(string repoOwner, string repoName, string token, string filePath, string commitMessage)
    {
        string repoPath = filePath.Replace("Assets/", "");
        string absoluteFilePath = Path.Combine(Application.dataPath, filePath.Replace("Assets/", ""));

        if (!File.Exists(absoluteFilePath))
        {
            Debug.LogError($"[FILE ERROR] File not found: {absoluteFilePath}");
            return false;
        }

        string fileContent = System.Convert.ToBase64String(File.ReadAllBytes(absoluteFilePath));
        string sha = GetFileSHA(repoOwner, repoName, repoPath, token);

        var payload = new Dictionary<string, object>
        {
            { "message", commitMessage },
            { "content", fileContent }
        };

        if (!string.IsNullOrEmpty(sha))
        {
            payload["sha"] = sha;
        }

        string jsonPayload = JsonConvert.SerializeObject(payload);
        string url = $"https://api.github.com/repos/{repoOwner}/{repoName}/contents/{repoPath}";

        return UploadToGitHub(url, jsonPayload, token);
    }

    private string GetFileSHA(string owner, string repo, string filePath, string token)
    {
        try
        {
            string url = $"https://api.github.com/repos/{owner}/{repo}/contents/{filePath}";
            using (WebClient client = new WebClient())
            {
                client.Headers.Add("Authorization", "token " + token);
                client.Headers.Add("User-Agent", "UnityGitHubUploader");
                string response = client.DownloadString(url);

                var jsonResponse = JsonConvert.DeserializeObject<Dictionary<string, object>>(response);
                return jsonResponse.ContainsKey("sha") ? jsonResponse["sha"].ToString() : null;
            }
        }
        catch (WebException)
        {
            return null;
        }
    }

    private bool UploadToGitHub(string url, string jsonPayload, string token)
    {
        try
        {
            using (WebClient client = new WebClient())
            {
                client.Headers.Add("Authorization", "token " + token);
                client.Headers.Add("User-Agent", "UnityGitHubUploader");
                client.Headers.Add("Content-Type", "application/json");
                client.UploadString(url, "PUT", jsonPayload);
                return true;
            }
        }
        catch (WebException)
        {
            return false;
        }
    }
}



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
