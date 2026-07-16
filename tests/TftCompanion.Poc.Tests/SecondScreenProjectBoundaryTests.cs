using System.Reflection;
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
}
