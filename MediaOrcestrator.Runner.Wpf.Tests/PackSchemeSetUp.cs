using KeepShell.Testing;

namespace MediaOrcestrator.Runner.Wpf.Tests;

[SetUpFixture]
public sealed class PackSchemeSetUp
{
    [OneTimeSetUp]
    public void Ensure() => PackScheme.Ensure();
}
