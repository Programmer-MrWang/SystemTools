using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Platform;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Controls;
using ClassIsland.Core.Models.Ruleset;
using ClassIsland.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SystemTools.Themes.ClassWidgets;

var appDirectory = @"E:\ClassIsland2.1.1.1\app-2.1.1.1-0";
Assembly.LoadFrom(Path.Combine(appDirectory, "ClassIsland.Core.dll"));
Assembly.LoadFrom(Path.Combine(appDirectory, "ClassIsland.dll"));

AppBuilder.Configure<SmokeApp>()
    .UsePlatformDetect()
    .SetupWithoutStarting();

var services = new ServiceCollection();
services.AddSingleton<IRulesetService, StubRulesetService>();
IAppHost.Host = new StubHost(services.BuildServiceProvider());

var presenter = new ComponentPresenter
{
    IsRootComponent = true,
    IsOnMainWindow = true
};
var card = new ClassWidgetsCard();
var contentHost = new Border { Child = card };
presenter.Content = contentHost;
var root = new Window { Content = presenter, Width = 1000, Height = 200 };
root.Show();
root.Measure(new Size(1000, 200));
root.Arrange(new Rect(root.DesiredSize));

Console.WriteLine($"Cold-start content initially null: {card.HostedContent == null}");
var hosted = new TextBlock { Text = "hosted-test-content" };
presenter.PresentingContent = hosted;
Console.WriteLine($"Delayed hosted content observed: {ReferenceEquals(card.HostedContent, hosted)}");
Console.WriteLine($"Hosted content type: {card.HostedContent?.GetType().FullName ?? "<null>"}");

internal sealed class SmokeApp : Application;

internal sealed class StubHost(IServiceProvider services) : IHost
{
    public IServiceProvider Services { get; } = services;
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void Dispose() { }
}

internal sealed class StubRulesetService : IRulesetService
{
    public event EventHandler? ForegroundWindowChanged;
    public event EventHandler? StatusUpdated;
    public bool IsRulesetSatisfied(Ruleset ruleset) => false;
    public void RegisterRuleHandler(string id, RuleRegistryInfo.HandleDelegate handler) { }
    public void NotifyStatusChanged() => StatusUpdated?.Invoke(this, EventArgs.Empty);
}
