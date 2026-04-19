namespace OEPLLib;

public static class OeplBundledFonts
{
    public static string SansRegular => Resolve("NotoSans-Regular.ttf");

    public static string SansBold => Resolve("NotoSans-Bold.ttf");

    private static string Resolve(string fileName)
    {
        foreach (var root in GetCandidateRoots())
        {
            var directPath = Path.Combine(root, "Assets", "Fonts", fileName);
            if (File.Exists(directPath))
            {
                return directPath;
            }

            var repoPath = Path.Combine(root, "OEPLLib", "Assets", "Fonts", fileName);
            if (File.Exists(repoPath))
            {
                return repoPath;
            }
        }

        throw new FileNotFoundException($"Bundled font '{fileName}' was not found in the application output or source tree.");
    }

    private static IEnumerable<string> GetCandidateRoots()
    {
        yield return AppContext.BaseDirectory;

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 6 && current is not null; i++)
        {
            yield return current.FullName;
            current = current.Parent;
        }
    }
}
