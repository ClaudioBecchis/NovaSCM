using Xunit;

namespace NovaSCMAgent.Tests;

public class WorkerTests
{
    // EvaluateCondition è private — testata tramite riflessione
    private static bool EvalCondition(string? cond)
    {
        var method = typeof(Worker).GetMethod(
            "EvaluateCondition",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return (bool)method!.Invoke(null, [cond])!;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EvaluateCondition_NullOrEmpty_ReturnsTrue(string? cond)
    {
        Assert.True(EvalCondition(cond));
    }

    [Fact]
    public void EvaluateCondition_Windows_MatchesCurrentOs()
    {
        Assert.Equal(OperatingSystem.IsWindows(), EvalCondition("windows"));
    }

    [Fact]
    public void EvaluateCondition_Linux_MatchesCurrentOs()
    {
        Assert.Equal(OperatingSystem.IsLinux(), EvalCondition("linux"));
    }

    [Fact]
    public void EvaluateCondition_OsEquals_MatchesCurrentOs()
    {
        var expected = OperatingSystem.IsWindows() ? "os=windows" : "os=linux";
        Assert.True(EvalCondition(expected));
    }

    [Fact]
    public void EvaluateCondition_OsEquals_WrongOs_ReturnsFalse()
    {
        var wrong = OperatingSystem.IsWindows() ? "os=linux" : "os=windows";
        Assert.False(EvalCondition(wrong));
    }

    [Fact]
    public void EvaluateCondition_Hostname_MatchesCurrentMachine()
    {
        var cond = $"hostname={Environment.MachineName}";
        Assert.True(EvalCondition(cond));
    }

    [Fact]
    public void EvaluateCondition_Hostname_WrongName_ReturnsFalse()
    {
        Assert.False(EvalCondition("hostname=__definitely_not_this_host__"));
    }

    [Fact]
    public void EvaluateCondition_UnknownCondition_ReturnsTrue()
    {
        Assert.True(EvalCondition("unknown_condition_xyz"));
    }

    // ShouldStopOnError è private — testata tramite riflessione, stesso pattern
    private static bool StopOnError(string suErr)
    {
        var method = typeof(Worker).GetMethod(
            "ShouldStopOnError",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return (bool)method!.Invoke(null, [suErr])!;
    }

    [Theory]
    [InlineData("continua")]
    [InlineData("continue")]
    [InlineData("CONTINUE")]
    [InlineData(" continua ")]
    public void ShouldStopOnError_ContinuaOrContinue_ReturnsFalse(string suErr)
    {
        // BUG regressione: case/spazi non normalizzati facevano fermare il
        // workflow anche quando l'intento era proseguire.
        Assert.False(StopOnError(suErr));
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("retry")]
    [InlineData("")]
    [InlineData("typo_sconosciuto")]
    public void ShouldStopOnError_AnythingElse_ReturnsTrue(string suErr)
    {
        // BUG regressione: prima qualunque valore diverso da "stop" veniva
        // trattato come "continua" silenziosamente (fail-open). Whitelist
        // esplicita: solo continua/continue fanno proseguire, tutto il resto
        // ferma il workflow (fail-safe).
        Assert.True(StopOnError(suErr));
    }
}
