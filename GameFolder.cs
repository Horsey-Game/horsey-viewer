namespace horsey
{
    // Finds the Horsey Game folder: first from Steam's library list, then from a folder picked before.
    public static class GameFolder
    {
        private const string SteamLibraries = @"C:\Program Files (x86)\Steam\steamapps\libraryfolders.vdf";
        private static readonly string SavedPathFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "horsey-viewer", "gamefolder.txt");

        // The game folder the viewer is using. Set once at startup, before any window opens.
        public static string Root { get; set; } = "";

        public static bool IsGameFolder(string? dir)
            => !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "Horsey.exe"));

        // The game folder, or null if Steam does not list it and nothing was picked before.
        public static string? Find()
        {
            foreach (string candidate in SteamCandidates())
            {
                if (IsGameFolder(candidate)) return candidate;
            }
            string? saved = SavedPath();
            return IsGameFolder(saved) ? saved : null;
        }

        public static void Remember(string dir)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SavedPathFile)!);
            File.WriteAllText(SavedPathFile, dir);
        }

        private static string? SavedPath()
        {
            try
            {
                return File.Exists(SavedPathFile) ? File.ReadAllText(SavedPathFile).Trim() : null;
            }
            catch (IOException)
            {
                return null;
            }
        }

        // Steam's libraryfolders.vdf lists each library as:  "path"  "D:\\SteamLibrary"
        private static IEnumerable<string> SteamCandidates()
        {
            if (!File.Exists(SteamLibraries)) yield break;
            foreach (string line in File.ReadLines(SteamLibraries))
            {
                string[] parts = line.Split('"');
                if (parts.Length >= 4 && parts[1] == "path")
                {
                    string library = parts[3].Replace("\\\\", "\\");
                    yield return Path.Combine(library, @"steamapps\common\Horsey Game");
                }
            }
        }
    }
}
