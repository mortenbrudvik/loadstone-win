using System.IO;
using System.Text.RegularExpressions;
using Shouldly;

namespace Loadstone.UI.Tests;

/// <summary>
/// Reads files out of the working tree by repository-relative path. Several checks in this assembly
/// assert against files no compiler ever sees — the packaging scripts and manifests — and they all
/// need the same two things: find the repository root above the test assembly, and pull one capture
/// out of a file.
/// </summary>
internal static class RepoFile
{
    public static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Loadstone.slnx")))
            {
                dir = dir.Parent;
            }

            dir.ShouldNotBeNull("could not find the repository root above the test assembly");
            return dir.FullName;
        }
    }

    public static string Text(string relativePath) => File.ReadAllText(Path.Combine(Root, relativePath));

    public static string Match(string relativePath, string pattern)
    {
        var match = Regex.Match(Text(relativePath), pattern);
        match.Success.ShouldBeTrue($"{relativePath} did not match {pattern}");
        return match.Groups[1].Value;
    }
}
