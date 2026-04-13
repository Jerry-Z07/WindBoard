namespace WindBoard.Tests;

internal static class RepoRootLocator
{
    internal static DirectoryInfo? Find()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        for (int i = 0; i < 20 && current is not null; i++)
        {
            if (File.Exists(Path.Combine(current.FullName, "WindBoard.slnx")))
            {
                return current;
            }

            current = current.Parent;
        }

        return null;
    }
}
