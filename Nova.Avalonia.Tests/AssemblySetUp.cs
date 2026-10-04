using NUnit.Framework;

namespace Nova.Avalonia.Tests;

/// <summary>Removes this run's generated games once every test has finished.</summary>
[SetUpFixture]
public sealed class AssemblySetUp
{
    [OneTimeTearDown]
    public void DeleteScratchGames()
    {
        TestGame.CleanUp();
    }
}
