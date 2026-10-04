namespace Nova.Server.TurnSteps
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Nova.Common;

    /// <summary>
    /// Yearly minefield decay (turn-generation step 18, straight after fleet movement and before
    /// production). behavior-specs-8/turn-generation-engine.md section 3 corrects the old
    /// "regrowth" reading: a minefield's strength only ever FALLS on its own and rises solely through
    /// mine-laying orders.
    /// </summary>
    /// <remarks>
    /// The yearly loss rate is 2 + 4 x S percent, where S is the number of stars lying inside the
    /// field's circle (the field's radius is the square root of its mine count, so a star is inside
    /// when its squared distance does not exceed the mine count) and the multiplier 4 is replaced by
    /// 1 when the field's owner is Space Demolition. The rate is capped at 50%. The absolute loss is
    /// that percentage of the mine count, but never fewer mines than the percentage figure itself and
    /// never fewer than 10 (except for a Speed Bump field); a field whose strength is not above
    /// the loss is deleted.
    /// The owner's "detonate" flag (Minefield.Detonate) adds 25 percentage points and triggers the
    /// detonation damage pass first (behavior-specs-10/turn-generation-engine.md section 3).
    /// The previous implementation lived inside the per-fleet minefield collision check, so it decayed
    /// a field once per moving fleet rather than once a year and could modify the collection while
    /// enumerating it.
    /// </remarks>
    public class MinefieldDecayStep : ITurnStep
    {
        private readonly ISet<long> fleetsThatSawAction;

        public MinefieldDecayStep()
            : this(null)
        {
        }

        /// <summary>
        /// A step that also adds every fleet caught by a detonating field to
        /// <paramref name="fleetsThatSawAction"/> (the turn generator's "saw action" set, read by
        /// RepairStep): the shared minefield routine sets the same fleet flag bit 0x40 for a
        /// detonation as for a movement hit (FUN_10b0_312a :74699, turn-generation-engine.md
        /// section 11 "Which fleets"), so such a fleet gets no repair this year.
        /// </summary>
        public MinefieldDecayStep(ISet<long> fleetsThatSawAction)
        {
            this.fleetsThatSawAction = fleetsThatSawAction;
        }

        public void Process(ServerData serverState)
        {
            Process(serverState, null);
        }

        /// <summary>
        /// Decays every field, or only those whose key is in <paramref name="onlyThese"/> when it
        /// is non-null. The turn generator passes the fields that existed BEFORE this turn's
        /// movement: Lay Mine Field is a post-battle task in the original, so a field laid this
        /// turn is not decayed in the turn it appears.
        /// </summary>
        public void Process(ServerData serverState, ISet<long> onlyThese)
        {
            Process(serverState, onlyThese, null);
        }

        /// <summary>
        /// As <see cref="Process(ServerData, ISet{long})"/>. Every fleet's "already hit by a
        /// field this turn" mark is cleared at the start of the pass; a field carrying the
        /// detonate flag first damages every not-yet-hit fleet inside its circle
        /// (<see cref="CheckForMinefields.Detonate"/>) and marks it, then decays. Fleets destroyed
        /// by a detonation are cleaned up at the end of the pass.
        /// </summary>
        public void Process(ServerData serverState, ISet<long> onlyThese, Random random)
        {
            HashSet<long> alreadyHit = new HashSet<long>();
            CheckForMinefields minefieldRoutine = null;
            bool anyCaught = false;

            foreach (Minefield minefield in serverState.AllMinefields.Values.ToList())
            {
                if (onlyThese != null && !onlyThese.Contains(minefield.Key))
                {
                    continue;
                }

                if (minefield.Detonate)
                {
                    // No injected random: the game's seeded "MinefieldDecay" stream (repeatable).
                    minefieldRoutine = minefieldRoutine ?? new CheckForMinefields(serverState, random ?? serverState.CreateRandom("MinefieldDecay"));
                    anyCaught |= minefieldRoutine.Detonate(minefield, alreadyHit) > 0;

                    if (minefield.NumberOfMines <= 0 || !serverState.AllMinefields.ContainsKey(minefield.Key))
                    {
                        serverState.AllMinefields.Remove(minefield.Key);
                        continue;
                    }
                }

                if (!ApplyDecay(minefield, serverState))
                {
                    serverState.AllMinefields.Remove(minefield.Key);
                }
            }

            if (fleetsThatSawAction != null)
            {
                fleetsThatSawAction.UnionWith(alreadyHit);
            }

            if (anyCaught)
            {
                serverState.CleanupFleets();
            }
        }

        /// <summary>The yearly loss rate, in percent, for a field with the given star count.</summary>
        public static int LossPercent(int starsInsideField, bool ownerIsSpaceDemolition)
        {
            return LossPercent(starsInsideField, ownerIsSpaceDemolition, false);
        }

        /// <summary>
        /// The yearly loss rate: 2 + 4 x S percent (multiplier 1 for a Space Demolition owner),
        /// capped at 50%, then raised by 25 percentage points if the field carries the owner's
        /// detonate flag (turn-generation-engine.md section 3).
        /// </summary>
        public static int LossPercent(int starsInsideField, bool ownerIsSpaceDemolition, bool detonating)
        {
            int multiplier = ownerIsSpaceDemolition ? 1 : 4;
            return Math.Min(50, 2 + (multiplier * starsInsideField)) + (detonating ? 25 : 0);
        }

        /// <summary>The number of mines a field loses this year. The floor of 10 does not apply
        /// to a Speed Bump field (behavior-specs-10/turn-generation-engine.md section 3).</summary>
        public static int MinesLost(int numberOfMines, int lossPercent, MinefieldType fieldType = MinefieldType.Standard)
        {
            int loss = numberOfMines * lossPercent / 100;
            loss = Math.Max(loss, lossPercent);
            return fieldType == MinefieldType.SpeedBump ? loss : Math.Max(loss, 10);
        }

        /// <summary>Decays the field in place; false when it has decayed away completely.</summary>
        private static bool ApplyDecay(Minefield minefield, ServerData serverState)
        {
            long squaredRadius = minefield.NumberOfMines;
            int starsInside = 0;
            foreach (Star star in serverState.AllStars.Values)
            {
                if (PointUtilities.DistanceSquare(star.Position, minefield.Position) <= squaredRadius)
                {
                    starsInside++;
                }
            }

            bool spaceDemolition = false;
            EmpireData owner;
            if (serverState.AllEmpires.TryGetValue(minefield.Owner, out owner) && owner.Race != null)
            {
                spaceDemolition = owner.Race.HasTrait("SD");
            }

            int loss = MinesLost(minefield.NumberOfMines, LossPercent(starsInside, spaceDemolition, minefield.Detonate), minefield.FieldType);
            if (minefield.NumberOfMines <= loss)
            {
                return false;
            }

            minefield.NumberOfMines -= loss;
            return true;
        }
    }
}
