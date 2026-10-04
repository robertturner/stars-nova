namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;
    using System.Xml;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Commands;
    using Nova.Server;

    /// <summary>
    /// The minefield Detonate order: a turn order applied during turn generation
    /// (behavior-specs-10/turn-generation-engine.md section 3, "Minefields are edited via the same
    /// order-queuing mechanism"; save-turn-file-format.md opcode 0x23 "requires the acting race to
    /// own it" and toggles one bit; race-traits.md section 2, Space Demolition "can remotely
    /// detonate its own standard minefields").
    /// </summary>
    [TestFixture]
    public class DetonateCommandTest
    {
        private sealed class ParseOnlyTurnGenerator : TurnGenerator
        {
            public ParseOnlyTurnGenerator(ServerData serverState) : base(serverState)
            {
            }

            public void Parse()
            {
                ParseCommands();
            }
        }

        private static EmpireData MakeEmpire(ushort id, string primaryTrait)
        {
            EmpireData empire = new EmpireData { Id = id, Race = new Race() };
            empire.Race.Traits.SetPrimary(primaryTrait);
            return empire;
        }

        private static Minefield MakeField(EmpireData owner, MinefieldType type = MinefieldType.Standard)
        {
            Minefield field = new Minefield { NumberOfMines = 400, FieldType = type };
            field.Key = owner.GetNextMinefieldKey();
            return field;
        }

        [Test]
        public void SpaceDemolition_SetsAndClearsTheFlag_OnItsOwnStandardField()
        {
            EmpireData empire = MakeEmpire(1, "SD");
            Minefield field = MakeField(empire);
            Dictionary<long, Minefield> fields = new Dictionary<long, Minefield> { { field.Key, field } };

            Assert.IsTrue(new DetonateCommand(field.Key, true).ApplyToMinefields(fields, empire));
            Assert.IsTrue(field.Detonate);

            Assert.IsTrue(new DetonateCommand(field.Key, false).ApplyToMinefields(fields, empire));
            Assert.IsFalse(field.Detonate);
        }

        [Test]
        public void OtherRaces_CannotDetonate()
        {
            EmpireData empire = MakeEmpire(1, "HE");
            Minefield field = MakeField(empire);
            Dictionary<long, Minefield> fields = new Dictionary<long, Minefield> { { field.Key, field } };
            DetonateCommand command = new DetonateCommand(field.Key, true);

            Assert.IsFalse(command.IsValid(empire));
            Assert.IsFalse(command.ApplyToMinefields(fields, empire));
            Assert.IsFalse(field.Detonate);
        }

        [Test]
        public void AnotherRacesField_CannotBeDetonated()
        {
            EmpireData owner = MakeEmpire(1, "SD");
            EmpireData other = MakeEmpire(2, "SD");
            Minefield field = MakeField(owner);
            Dictionary<long, Minefield> fields = new Dictionary<long, Minefield> { { field.Key, field } };
            DetonateCommand command = new DetonateCommand(field.Key, true);

            Assert.IsFalse(command.IsValid(other), "the acting race must own the field");
            Assert.IsFalse(command.ApplyToMinefields(fields, other));
            Assert.IsFalse(field.Detonate);
        }

        [Test]
        public void HeavyAndSpeedBumpFields_AreNotDetonated()
        {
            EmpireData empire = MakeEmpire(1, "SD");
            foreach (MinefieldType type in new[] { MinefieldType.Heavy, MinefieldType.SpeedBump })
            {
                Minefield field = MakeField(empire, type);
                Dictionary<long, Minefield> fields = new Dictionary<long, Minefield> { { field.Key, field } };

                Assert.IsFalse(new DetonateCommand(field.Key, true).ApplyToMinefields(fields, empire), type.ToString());
                Assert.IsFalse(field.Detonate);
            }
        }

        [Test]
        public void TheOrder_SurvivesTheOrdersFile_AndIsAppliedByTurnGeneration()
        {
            ServerData serverState = new ServerData();
            EmpireData empire = MakeEmpire(1, "SD");
            serverState.AllEmpires[empire.Id] = empire;
            Minefield field = MakeField(empire);
            serverState.AllMinefields[field.Key] = field;

            XmlDocument xmldoc = new XmlDocument();
            DetonateCommand read = new DetonateCommand(new DetonateCommand(field.Key, true).ToXml(xmldoc));
            Assert.AreEqual(field.Key, read.MinefieldKey);
            Assert.IsTrue(read.Detonate);

            Stack<ICommand> commands = new Stack<ICommand>();
            commands.Push(read);
            serverState.AllCommands[empire.Id] = commands;

            new ParseOnlyTurnGenerator(serverState).Parse();

            Assert.IsTrue(field.Detonate);
        }
    }
}
