using Microsoft.Extensions.Options;
using SecurityManager.Agent;
using SecurityManager.Agent.Sync;
using SecurityManager.Enforcement;
using SecurityManager.Enforcement.Firewall;
using SecurityManager.Shared.Infrastructure.Security;

// Usage:
//   SecurityManager.Agent[.exe]           run (as a Windows service, a systemd service, or in the foreground)
//   SecurityManager.Agent[.exe] --check   print what would change, then exit (makes no changes)

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});
builder.Services.AddWindowsService(o => o.ServiceName = "SecurityManager.Agent");
builder.Services.AddSystemd();

builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection(AgentOptions.Section));
builder.Services.Configure<EnforcementOptions>(builder.Configuration.GetSection(AgentOptions.Section));
var agent = builder.Configuration.GetSection(AgentOptions.Section).Get<AgentOptions>() ?? new AgentOptions();

builder.Services.AddJwtTokenFactory(builder.Configuration);
builder.Services.AddHttpClient<EnforcementClient>(c =>
{
    c.BaseAddress = new Uri(agent.FirewallServiceUrl);
    c.Timeout = TimeSpan.FromSeconds(15);
}).AddServiceAuthentication("agent");

// Windows Firewall on Windows, nftables on Linux.
builder.Services.AddSingleton<IHostFirewall>(_ => HostFirewallFactory.Create(agent.NftPath));
builder.Services.AddSingleton<EnforcementSynchronizer>();

if (args.Contains("--check"))
{
    // Audit a single cycle and print the result.
    builder.Services.PostConfigure<AgentOptions>(o => o.Mode = "Audit");
    builder.Services.PostConfigure<EnforcementOptions>(o => o.Mode = "Audit");
    using var host = builder.Build();
    var client = host.Services.GetRequiredService<EnforcementClient>();
    var desired = await client.GetDesiredStateAsync(CancellationToken.None);
    var result = host.Services.GetRequiredService<EnforcementSynchronizer>().Apply(desired);
    Console.WriteLine($"Desired state {desired.Version}: {desired.Rules.Count} rule(s), enforcement {(desired.Enabled ? "on" : "off")}");
    foreach (var r in desired.Rules) Console.WriteLine($"  {r.Action,-5} {r.Direction,-3} {r.Protocol,-6} remote={Short(r.RemoteAddresses)} local-ports={r.LocalPorts}  ({r.Origin})");
    Console.WriteLine($"Firewall: {host.Services.GetRequiredService<IHostFirewall>().Name}");
    Console.WriteLine($"Firewall profiles: {string.Join(", ", result.Profiles.Select(p => $"{p.Name}={(p.Enabled ? "on" : "OFF")}"))}");
    Console.WriteLine($"Installed managed rules: {result.ManagedRules}; status: {result.Status}");
    foreach (var w in result.Warnings) Console.WriteLine($"  warning: {w}");
    foreach (var e in result.Errors) Console.WriteLine($"  error: {e}");
    return;
}

builder.Services.AddHostedService<EnforcementWorker>();
builder.Build().Run();

static string Short(string s) => s.Length <= 60 ? s : s[..57] + "...";
