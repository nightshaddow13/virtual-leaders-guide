namespace VirtualLeadersGuide.AppHost.Tests;

public sealed class WorktreePortsShould : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("vlg-worktree-ports-");

    public void Dispose() => _root.Delete(recursive: true);

    [Fact]
    public void ReturnZero_WhenGitIsADirectory_ForOffsetFor()
    {
        _root.CreateSubdirectory(".git");

        Assert.Equal(0, WorktreePorts.OffsetFor(_root.CreateSubdirectory("src").FullName, null));
    }

    [Fact]
    public void ReturnAStableNonZeroOffset_WhenGitIsAFile_ForOffsetFor()
    {
        File.WriteAllText(Path.Combine(_root.FullName, ".git"), "gitdir: elsewhere");

        var fromRoot = WorktreePorts.OffsetFor(_root.FullName, null);
        var fromChild = WorktreePorts.OffsetFor(_root.CreateSubdirectory("src").FullName, null);

        Assert.InRange(fromRoot, 10, 900);
        Assert.Equal(0, fromRoot % 10);
        Assert.Equal(fromRoot, fromChild);
    }

    [Fact]
    public void ReturnTheOverride_WhenOneIsSet_ForOffsetFor() =>
        Assert.Equal(40, WorktreePorts.OffsetFor(_root.FullName, "40"));

    [Fact]
    public void ShiftEveryPort_WhenGivenAUrlList_ForShift() =>
        Assert.Equal(
            "https://localhost:17201;http://localhost:15283/",
            WorktreePorts.Shift("https://localhost:17191;http://localhost:15273/", 10));
}
