using NovaSCMAgent;

var builder = Host.CreateApplicationBuilder(args);

// BUG-7: AgentConfig usa metodi statici (Load/LoadState/SaveState) — nessuna registrazione DI necessaria
builder.Services.AddSingleton<ApiClient>();
builder.Services.AddSingleton<StepExecutor>();
builder.Services.AddHostedService<Worker>();

// Gira come Windows Service (no-op su Linux)
builder.Services.AddWindowsService(opt => opt.ServiceName = "NovaSCMAgent");
// Gira come systemd service (no-op su Windows)
builder.Services.AddSystemd();

var host = builder.Build();
host.Run();
