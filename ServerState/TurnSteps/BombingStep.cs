using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Nova.Server.TurnSteps
{
    using Nova.Common;
    using Nova.Common.Waypoints;

    /// <summary>
    /// Orbital bombardment, run once per turn at the end of the battle pass
    /// (behavior-specs-10/turn-generation-engine.md §1 step 23b, §4). The gate, pooling of
    /// same-owner fleets and the whole bombing arithmetic live in <see cref="Bombing.BombAll"/>;
    /// fleets are no longer pre-filtered on HasBombers here, since Retro Bombs, Multi Contained
    /// Munitions and Orbital Construction Modules bomb without counting as "bombers".
    /// </summary>
    class BombingStep : ITurnStep
    {
        public void Process(ServerData serverState)
        {
            new Bombing(serverState).BombAll();
        }
    }
}
