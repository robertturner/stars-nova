#region Copyright Notice
// ============================================================================
// Copyright (C) 2009-2012 The Stars-Nova Project
//
// This file is part of Stars! Nova.
// See <http://sourceforge.net/projects/stars-nova/>.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License version 2 as
// published by the Free Software Foundation.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>
// ===========================================================================
#endregion

namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;

    using Nova.Ai;
    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;

    using NUnit.Framework;

    [TestFixture]
    public class ColonizerCommitmentAdvisorTest
    {
        private static Fleet MakeFleetWithCargoCapacity(int cargoCapacity)
        {
            Fleet fleet = new Fleet(1);
            ShipDesign design = new ShipDesign(1);
            design.Blueprint = new Component();
            Hull hull = new Hull();
            hull.BaseCargo = cargoCapacity;
            hull.Modules = new List<HullModule>();
            design.Blueprint.Properties.Add("Hull", hull);
            design.Update(); // CargoCapacity reads Summary, which only Update() populates
            ShipToken token = new ShipToken(design, 1);
            fleet.Composition.Add(token.Key, token);
            return fleet;
        }

        [Test]
        public void IsColonizerCapable_FalseBelowTheCargoCapacityThreshold()
        {
            Fleet smallFleet = MakeFleetWithCargoCapacity(ColonizerCommitmentAdvisor.CargoCapacityThreshold - 1);
            Assert.IsFalse(ColonizerCommitmentAdvisor.IsColonizerCapable(smallFleet));
        }

        [Test]
        public void IsColonizerCapable_TrueAtOrAboveTheCargoCapacityThreshold()
        {
            Fleet exactFleet = MakeFleetWithCargoCapacity(ColonizerCommitmentAdvisor.CargoCapacityThreshold);
            Assert.IsTrue(ColonizerCommitmentAdvisor.IsColonizerCapable(exactFleet));

            Fleet bigFleet = MakeFleetWithCargoCapacity(ColonizerCommitmentAdvisor.CargoCapacityThreshold * 2);
            Assert.IsTrue(ColonizerCommitmentAdvisor.IsColonizerCapable(bigFleet));
        }

        [Test]
        public void IsColonizerCapable_IgnoresCurrentlyLoadedCargo_OnlyCapacityMatters()
        {
            // The decision this ports happens before Colonise() has loaded anything, so a
            // freshly-built colony ship with zero CURRENT cargo must still pass here as long as
            // its hold is big enough - only capacity should ever be checked, never Cargo.Mass.
            Fleet fleet = MakeFleetWithCargoCapacity(ColonizerCommitmentAdvisor.CargoCapacityThreshold);
            Assert.AreEqual(0, fleet.Cargo.Mass);
            Assert.IsTrue(ColonizerCommitmentAdvisor.IsColonizerCapable(fleet));
        }

        [Test]
        public void IsWithinFundingRange_TrueAtOrInsideTheThreshold()
        {
            NovaPoint fundingSource = new NovaPoint(0, 0);

            // Exactly 100 units away: squared distance is exactly 10,000.
            NovaPoint atTheEdge = new NovaPoint(100, 0);
            Assert.IsTrue(ColonizerCommitmentAdvisor.IsWithinFundingRange(atTheEdge, fundingSource));

            NovaPoint closer = new NovaPoint(30, 40); // squared distance 2,500
            Assert.IsTrue(ColonizerCommitmentAdvisor.IsWithinFundingRange(closer, fundingSource));
        }

        [Test]
        public void IsWithinFundingRange_FalseBeyondTheThreshold()
        {
            NovaPoint fundingSource = new NovaPoint(0, 0);
            NovaPoint tooFar = new NovaPoint(101, 0);
            Assert.IsFalse(ColonizerCommitmentAdvisor.IsWithinFundingRange(tooFar, fundingSource));
        }
    }
}
