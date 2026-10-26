using System.IO;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests;

/// <summary>
/// Pins two things about the tree that no compiler checks. .editorconfig asks for UTF-8 without a
/// byte order mark and CRLF line endings; the build enforces neither, and the 2026-10-26 review found
/// ten files with a BOM and an index stored LF with nothing to say what a checkout should get.
/// </summary>
public sealed class SourceHygieneTests
{
    private static readonly byte[] ByteOrderMark = [0xEF, 0xBB, 0xBF];

    [Fact]
    public void NoSourceFile_CarriesAByteOrderMark()
    {
        var offenders = SourceFiles()
            .Where(path => StartsWithByteOrderMark(path))
            .Select(path => Path.GetRelativePath(RepoFile.Root, path))
            .ToList();

        offenders.ShouldBeEmpty();
    }

    /// <summary>
    /// The index holds LF, as git normalizes it, and .editorconfig asks every editor for CRLF: without
    /// a .gitattributes a clone with core.autocrlf off checks out LF, every save then rewrites whole
    /// files, and the version script's CRLF write shows up as a whole-file diff.
    /// </summary>
    [Fact]
    public void GitAttributes_GiveEveryCheckoutTheLineEndingsEditorconfigAsksFor()
    {
        var attributes = RepoFile.Text(".gitattributes");

        attributes.ShouldContain("* text=auto eol=crlf");
    }

    private static IEnumerable<string> SourceFiles() =>
        new[] { "src", "tests" }
            .Select(folder => Path.Combine(RepoFile.Root, folder))
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories))
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static bool StartsWithByteOrderMark(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> head = stackalloc byte[3];
        return stream.Read(head) == 3 && head.SequenceEqual(ByteOrderMark);
    }
}
