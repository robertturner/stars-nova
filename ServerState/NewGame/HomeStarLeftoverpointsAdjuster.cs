using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Nova.Server.NewGame
{
    using Nova.Common;

    /// <summary>
    /// Applies a race's leftover advantage points to one of its home worlds - the authoritative
    /// table of docs/behavior-specs-10/new-game-setup.md section 3, "Leftover advantage points at
    /// game start" (`FUN_1078_1334`, `:51024`-`51136`). Exactly one option applies (points are
    /// never split), p is capped at 50, and every division rounds down. Runs after the home
    /// world has its 10 mines / 10 factories / 10 defences and its floored concentrations.
    /// </summary>
    public class HomeStarLeftoverpointsAdjuster
    {
        /// <summary>The most leftover points that count (computer players always use this).</summary>
        public const int MaximumLeftoverPoints = 50;

        /// <param name="isComputerPlayer">
        /// A computer player always uses p = 50, whatever its design scores. Nova has no AI
        /// skill tiers, so the spec's extra rule "a computer player of skill tier 2 or higher
        /// choosing Surface minerals also gets the Mineral concentrations bonus with p = 50" is
        /// not implemented. TODO: apply it once AI skill tiers exist.
        /// </param>
        public static void Adjust(Star star, Race race, bool isComputerPlayer = false)
        {
            string leftoverAdvantagePointsTarget = race.LeftoverPointTarget;
            int p = isComputerPlayer ? MaximumLeftoverPoints : Math.Min(MaximumLeftoverPoints, race.GetLeftoverAdvantagePoints());

            if (p > 0)
            {
                // The WinForms race designer stores the plural label, the Avalonia one the
                // singular - both mean option 1.
                if (leftoverAdvantagePointsTarget == "Mineral concentration" || leftoverAdvantagePointsTarget == "Mineral concentrations")
                {
                    AddConcentrations(star, p);
                }
                else if (leftoverAdvantagePointsTarget == "Mines")
                {
                    star.Mines += p / 2;
                }
                else if (leftoverAdvantagePointsTarget == "Factories")
                {
                    star.Factories += p / 5;
                }
                else if (leftoverAdvantagePointsTarget == "Defenses")
                {
                    // p / 10 rounded half up: p = 4 gives none, p = 5 one, p = 50 five.
                    star.Defenses += (p + 5) / 10;
                }
                else
                {
                    // "Surface minerals" (and anything unrecognised, as the original treats
                    // its unreachable values 5 and 6).
                    AddSurfaceMinerals(star, p);
                }
            }

            // After the bonus an Alternate Reality race's mines, factories and defences are set
            // to zero (`:51129`-`51136`), so options 2-4 give it nothing.
            if (race.HasTrait("AR"))
            {
                star.Mines = 0;
                star.Factories = 0;
                star.Defenses = 0;
            }
        }

        /// <summary>
        /// Option 1: e = p / 2 (1 when p is 1 or 2); the lowest concentration (ties: the first
        /// in Ironium, Boranium, Germanium order) gains e, then all three gain (e + 1) / 2.
        /// p = 7: lowest +5, others +2; p = 50: lowest +38, others +13.
        /// </summary>
        private static void AddConcentrations(Star star, int p)
        {
            int e = (p <= 2) ? 1 : p / 2;
            Resources concentration = star.MineralConcentration;

            int lowest = Math.Min(concentration.Ironium, Math.Min(concentration.Boranium, concentration.Germanium));
            if (concentration.Ironium == lowest)
            {
                concentration.Ironium += e;
            }
            else if (concentration.Boranium == lowest)
            {
                concentration.Boranium += e;
            }
            else
            {
                concentration.Germanium += e;
            }

            int all = (e + 1) / 2;
            concentration.Ironium += all;
            concentration.Boranium += all;
            concentration.Germanium += all;
        }

        /// <summary>
        /// Option 0: 10 x p kT in total. Each surface stock gains 10p / 4; the smallest stock
        /// (ties: Germanium, then Boranium) gains a further 10p / 4 plus the remainder. p = 7:
        /// +36 to the poorest and +17 to each other; p = 50: +250 / +125 / +125.
        /// </summary>
        private static void AddSurfaceMinerals(Star star, int p)
        {
            int total = 10 * p;
            int share = total / 4;
            int remainder = total - (4 * share);
            Resources stock = star.ResourcesOnHand;

            int smallest = Math.Min(stock.Ironium, Math.Min(stock.Boranium, stock.Germanium));
            if (stock.Germanium == smallest)
            {
                stock.Germanium += share + remainder;
            }
            else if (stock.Boranium == smallest)
            {
                stock.Boranium += share + remainder;
            }
            else
            {
                stock.Ironium += share + remainder;
            }

            stock.Ironium += share;
            stock.Boranium += share;
            stock.Germanium += share;
        }
    }
}
