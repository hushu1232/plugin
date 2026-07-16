using System.Reflection;
using System.IO;
using NUnit.Framework;
using TftCompanion.SecondScreen.Composition;

namespace TftCompanion.Poc.Tests;

[TestFixture]
public sealed class SecondScreenProjectBoundaryTests
{
    [Test]
    public void second_screen_assembly_references_core_but_not_host_overwolf_or_alife()
    {
        Assembly assembly = typeof(SecondScreenComposition).Assembly;
        string[] references = assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(assembly.GetName().Name, Is.EqualTo("TftCompanion.SecondScreen"));
            Assert.That(references, Does.Contain("TftCompanion.Poc.Core"));
            Assert.That(references, Does.Not.Contain("TftCompanion.Poc.Host"));
            Assert.That(references, Does.Not.Contain("Microsoft.AspNetCore.App"));
            Assert.That(references, Does.Not.Contain("Alife.Platform"));
            Assert.That(references, Does.Not.Contain("Alife.Function.WebBridge"));
            Assert.That(references, Does.Not.Contain("Alife.Function.Speech"));
            Assert.That(references, Does.Not.Contain("Alife.Function.DataAgent"));
        });
    }

    [Test]
    public void second_screen_ui_stays_projection_only_and_ordinary()
    {
        string root = FindRepositoryRoot();
        string sourceRoot = Path.Combine(root, "src", "TftCompanion.SecondScreen");
        string viewModelSource = File.ReadAllText(Path.Combine(sourceRoot, "ViewModels", "SecondScreenViewModel.cs"));
        string compositionSource = File.ReadAllText(Path.Combine(sourceRoot, "Composition", "SecondScreenComposition.cs"));
        string appSource = File.ReadAllText(Path.Combine(sourceRoot, "App.xaml.cs"));
        string windowSource = File.ReadAllText(Path.Combine(sourceRoot, "MainWindow.xaml.cs"));
        string windowXaml = File.ReadAllText(Path.Combine(sourceRoot, "MainWindow.xaml"));
        string productionSource = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(sourceRoot, "*.*", SearchOption.AllDirectories)
                .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                .Select(File.ReadAllText));

        string[] prohibitedCapabilities =
        [
            "Overwolf", "GEP", "WebSocket", "HttpClient", "System.Net", "Microsoft.AspNetCore", "SQLite", "DataAgent", "RAG", "LLM",
            "TTS", "OCR", "Screenshot", "Capture", "SendInput", "SetForegroundWindow", "GetForegroundWindow",
            "Process.GetProcesses", "FileSystemWatcher", "Registry", "NotifyIcon", "Topmost", "AllowsTransparency"
        ];

        Assert.Multiple(() =>
        {
            Assert.That(viewModelSource, Does.Not.Contain("EmbeddedFixtureExpressionSkill"));
            Assert.That(viewModelSource, Does.Not.Contain("TryRender("));
            Assert.That(viewModelSource, Does.Not.Contain("SemanticAdvice"));
            Assert.That(viewModelSource, Does.Not.Contain("ReasonCode"));
            Assert.That(windowXaml, Does.Not.Contain("TextBox"));
            Assert.That(windowXaml, Does.Not.Contain("ListBox"));
            Assert.That(windowXaml, Does.Not.Contain("Topmost"));
            Assert.That(windowXaml, Does.Not.Contain("AllowsTransparency"));
            Assert.That(windowXaml, Does.Not.Contain("Expires:"));
            Assert.That(windowXaml, Does.Contain("Title=\"TFT Companion 路 Manual / FixtureOnly\""));
            Assert.That(windowXaml, Does.Contain("StartNewSessionCommand"));
            Assert.That(windowXaml, Does.Contain("SubmitCheckpointCommand"));
            Assert.That(windowXaml, Does.Contain("ClearCurrentAdviceCommand"));
            Assert.That(windowXaml, Does.Contain("EnableDDriveRecoveryCommand"));
            Assert.That(CountOccurrences(windowXaml, "<TextBlock"), Is.EqualTo(1));
            Assert.That(windowSource, Does.Contain("Activated"));
            Assert.That(windowSource, Does.Contain("DispatcherTimer"));
            Assert.That(windowSource, Does.Contain("Stop()"));
            Assert.That(windowSource, Does.Contain("viewModel.PropertyChanged"));
            Assert.That(windowSource, Does.Contain("ScheduleExpiry"));
            Assert.That(windowSource, Does.Contain("PropertyChanged -="));
            Assert.That(windowSource, Does.Not.Contain("SetForegroundWindow"));
            Assert.That(appSource, Does.Contain("OnStartup"));
            Assert.That(appSource, Does.Contain("RestoreAndRefresh"));
            Assert.That(compositionSource, Does.Contain("WindowsManualSessionRecoveryFileSystem"));
            Assert.That(compositionSource, Does.Contain("ManualSessionRecoveryCodec"));
            Assert.That(compositionSource, Does.Contain("ManualSessionRecoveryStore"));
            Assert.That(compositionSource, Does.Contain("ManualSessionController"));
            Assert.That(compositionSource, Does.Contain("SecondScreenPresentationSkill"));
            Assert.That(compositionSource, Does.Contain("SecondScreenViewModel"));

            foreach (string prohibited in prohibitedCapabilities)
            {
                Assert.That(productionSource, Does.Not.Contain(prohibited), prohibited);
            }
        });
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TftCompanion.Poc.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the TFT Companion repository root.");
    }

    private static int CountOccurrences(string value, string token)
    {
        int count = 0;
        int offset = 0;
        while ((offset = value.IndexOf(token, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += token.Length;
        }

        return count;
    }
}
