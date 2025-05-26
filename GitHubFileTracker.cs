using System.Collections.Generic;

public static class GitHubFileTracker
{
    public static HashSet<string> autoDetectedFiles = new HashSet<string>();
    public static HashSet<string> deletedFiles = new HashSet<string>();

    // 👇 Add this line
    public static HashSet<string> manuallyRemovedFiles = new HashSet<string>();
}
