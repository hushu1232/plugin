using System.Reflection;
using NUnit.Framework;
using TftCompanion.Poc.Core.LocalSimulation;

namespace TftCompanion.Poc.Tests;

[TestFixture]
public sealed class ProjectBoundaryTests
{
    [Test]
    public void poc_assemblies_are_present_and_do_not_reference_alife()
    {
        Type coreType = Type.GetType(
            "TftCompanion.Poc.Core.Protocol.ProtocolConstants, TftCompanion.Poc.Core",
            throwOnError: false)!;

        Assert.That(coreType, Is.Not.Null, "ProtocolConstants has not been created.");

        string[] references = coreType.Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        Assert.That(references, Does.Not.Contain("Alife.Platform"));
        Assert.That(references, Does.Not.Contain("Alife.Function.WebBridge"));
        Assert.That(references, Does.Not.Contain("Alife.Function.Speech"));
        Assert.That(references, Does.Not.Contain("Alife.Function.DataAgent"));
    }

    [Test]
    public void local_simulation_stays_in_core_without_host_or_alife_dependencies()
    {
        Assembly coreAssembly = typeof(ManualScenarioDraft).Assembly;
        string[] references = coreAssembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(coreAssembly.GetName().Name, Is.EqualTo("TftCompanion.Poc.Core"));
            Assert.That(references, Does.Not.Contain("TftCompanion.Poc.Host"));
            Assert.That(references, Does.Not.Contain("Microsoft.AspNetCore.App"));
            Assert.That(references, Does.Not.Contain("Alife.Platform"));
            Assert.That(references, Does.Not.Contain("Alife.Function.WebBridge"));
            Assert.That(references, Does.Not.Contain("Alife.Function.Speech"));
            Assert.That(references, Does.Not.Contain("Alife.Function.DataAgent"));
        });
    }
}
