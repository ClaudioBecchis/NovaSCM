using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace NovaSCMAgent.Tests;

public class StepExecutorTests
{
    private static StepExecutor MakeExecutor() =>
        new(NullLogger<StepExecutor>.Instance);

    private static JsonObject Step(string tipo, string? platform = null, string parametri = "{}") =>
        new()
        {
            ["tipo"]      = tipo,
            ["platform"]  = platform ?? "all",
            ["parametri"] = parametri,
        };

    [Fact]
    public async Task UnknownTipo_ReturnsFalse()
    {
        var exec   = MakeExecutor();
        var result = await exec.ExecuteAsync(Step("nonexistent_tipo"), CancellationToken.None);
        Assert.False(result.Ok);
        Assert.Contains("sconosciuto", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PlatformMismatch_ReturnsSkipped()
    {
        var exec = MakeExecutor();
        // Richiede la piattaforma opposta a quella corrente
        var wrongPlatform = OperatingSystem.IsWindows() ? "linux" : "windows";
        var result = await exec.ExecuteAsync(Step("winget_install", wrongPlatform), CancellationToken.None);
        Assert.Null(result.Ok);   // null = skipped
        Assert.Contains("Skipped", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Message_ReturnsOk()
    {
        var exec   = MakeExecutor();
        var result = await exec.ExecuteAsync(
            Step("message", parametri: """{"text":"hello test"}"""),
            CancellationToken.None);
        Assert.True(result.Ok);
        Assert.Contains("hello test", result.Output);
    }

    [Fact]
    public async Task RegSet_SkippedOnLinux()
    {
        if (OperatingSystem.IsWindows()) return;  // test rilevante solo su Linux
        var exec   = MakeExecutor();
        var result = await exec.ExecuteAsync(
            Step("reg_set", parametri: """{"path":"HKLM\test","name":"k","value":"v"}"""),
            CancellationToken.None);
        Assert.Null(result.Ok);
    }

    [Fact]
    public async Task SystemdService_SkippedOnWindows()
    {
        if (OperatingSystem.IsLinux()) return;  // test rilevante solo su Windows
        var exec   = MakeExecutor();
        var result = await exec.ExecuteAsync(
            Step("systemd_service", parametri: """{"name":"test","action":"status"}"""),
            CancellationToken.None);
        Assert.Null(result.Ok);
    }

    [Fact]
    public async Task WingetInstall_MissingId_ReturnsFalse()
    {
        if (!OperatingSystem.IsWindows()) return;
        var exec   = MakeExecutor();
        var result = await exec.ExecuteAsync(
            Step("winget_install", parametri: "{}"),
            CancellationToken.None);
        Assert.False(result.Ok);
        Assert.Contains("mancante", result.Output, StringComparison.OrdinalIgnoreCase);
    }
}
