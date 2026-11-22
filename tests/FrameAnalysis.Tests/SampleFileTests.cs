using FrameAnalysis.Core.Analysis;
using FrameAnalysis.Core.Data;

namespace FrameAnalysis.Tests;

/// <summary>The validation frames in samples/validation must keep loading and solving.</summary>
public class SampleFileTests
{
    private static string SampleDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FrameAnalysis.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "samples", "validation");
    }

    public static TheoryData<string> Files()
    {
        var data = new TheoryData<string>();
        foreach (var f in Directory.GetFiles(SampleDir(), "*.frame.json").Order()) data.Add(Path.GetFileName(f));
        return data;
    }

    [Fact]
    public void AllTwelveSamplesArePresent() => Assert.Equal(12, Files().Count);

    [Theory]
    [MemberData(nameof(Files))]
    public void SampleLoadsAndSolves(string file)
    {
        var (model, problems) = FrameModelJson.Load(File.ReadAllText(Path.Combine(SampleDir(), file)));
        Assert.Empty(problems);
        var results = FrameSolver.Analyze(model);
        Assert.Equal(model.LoadCases.Count, results.Cases.Count);
        Assert.Equal(model.Combinations.Count, results.Combinations.Count);
        Assert.True(File.Exists(Path.Combine(SampleDir(), file.Replace(".frame.json", ".results.md"))));
    }
}
