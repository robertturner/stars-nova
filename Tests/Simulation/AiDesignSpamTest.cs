namespace Nova.Tests.Simulation
{
    using System.Collections.Generic;
    using System.Linq;

    using Nova.Ai;
    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Sim;

    using NUnit.Framework;

    /// <summary>
    /// SIM-2: Automitrons (category 2) used to add an identical "Medium Freighter [colonizer]
    /// T&lt;year&gt;" design every turn and pass the 16-design cap by turn ~17.
    /// </summary>
    [TestFixture]
    [Category("Simulation")]
    public class AiDesignSpamTest
    {
        [Test]
        public void AnAutomitronsGame_DoesNotReAddTheSameColonizerDesignEveryTurn()
        {
            SimulationConfig config = SimulationTestSupport.SmallConfig(seed: 7, players: 3, turns: 8);
            List<string> notes = new List<string>();
            SimulationRunner runner = new SimulationRunner(config)
            {
                AfterTurn = context =>
                {
                    EmpireData empire = context.State.AllEmpires[3];
                    List<ShipDesign> colonizers = empire.Designs.Values.Where(d => AiDesignRoleTag.TagOf(d) == "colonizer").OrderBy(d => d.Key).ToList();
                    for (int i = 1; i < colonizers.Count; i++)
                    {
                        notes.Add("turn " + context.Turn + ": " + colonizers[i - 1].Name + " vs " + colonizers[i].Name + " same build " + DesignBuilder.SameBuild(colonizers[i - 1], colonizers[i]));
                    }
                },
            };

            SimulationResult result = runner.Run();
            TestContext.WriteLine(string.Join("\n", notes));
            Assert.IsNull(result.FatalError, result.FatalError);
            int first = result.Turns.First().Empires.Single(e => e.EmpireId == 3).ShipDesigns;
            int last = result.Turns.Last().Empires.Single(e => e.EmpireId == 3).ShipDesigns;
            Assert.LessOrEqual(last - first, 2, "ship designs grew from " + first + " to " + last + " in 8 turns: " + string.Join(" | ", notes.Take(5)));
        }
    }
}
