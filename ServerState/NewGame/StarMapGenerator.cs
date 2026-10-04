#region Copyright Notice
// ============================================================================
// Copyright (C) 2009, 2010, 2011 The Stars-Nova Project
//
// This file is part of Stars-Nova.
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

namespace Nova.Server.NewGame
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// This class is used to generate stars map.
    /// </summary>
    /// <remarks>
    /// The approach is based on emulation 
    /// of two-dimensional random value with the specified probability density 
    /// function. After each star is placed the density function is changed 
    /// accordingly to reduce function.
    /// </remarks>
    public class StarMapGenerator 
    {
        // the number of failed attempts to stop after
        private const int FailuresThreshold = 5000;

        // Keeps every star/homeworld at least this many map units (== screen pixels at zoom 1,
        // and scaled together with the marker itself at any other zoom - see
        // StarMapDocumentView.axaml's LayoutTransformControl) away from the map's own edge.
        // Without this, a star could land at e.g. Y=0, and its marker's own decorations - the
        // gold selection ring, starbase/stargate/mass-driver dots, which are drawn at small
        // negative offsets from the star's logical position (Canvas.Left/Top down to -9) - would
        // then draw into negative Panel coordinates that the map's ScrollViewer can never scroll
        // to, since it has no negative scroll range. That's the real cause of a reported "dead
        // space that cuts off the selection ring" bug: not a layout margin, but a star spawning
        // close enough to the edge that part of its own marker is permanently unreachable. 20
        // comfortably covers the marker's largest offset (the -9 stargate indicator, plus the
        // name label's height below the star) while staying a small fraction of a typical map.
        private const int EdgeMargin = 20;

        // map settings
        private readonly int mapWidth;
        private readonly int mapHeight;
        private readonly int starSeparation;
        private readonly int starDensity;
        private readonly int starUniformity;
        private int numPlayers;

        // non-normalized probability density function
        // values are between 0 and 1
        private readonly double[,] density;

        private readonly Random random;

        // List of stars positions int[2]; int[0] - x, int[1] - y
        private readonly List<int[]> stars = new List<int[]>();
        private readonly List<int[]> homeworlds = new List<int[]>();

        // the width and height of the frame where the density values will be updated after placing the star
        private int updateFrameSize;

        // values calculated from map parameters, which define the shape of reduce function
        private double baseDensity;
        private double maxRadius;
        private int minSeparation;
  
        
        /// <summary>
        /// Initializes a new instance of the StarsMapGenerator class.
        /// </summary>
        /// <param name="mapWidth">Width of the map in ly.</param>
        /// <param name="mapHeight">Height of the map in ly.</param>
        /// <param name="random">
        /// The Random to draw star/homeworld positions from. Optional (defaults to a freshly-
        /// seeded one) so existing callers are unaffected; pass in a shared, seeded Random to
        /// make map generation reproducible - see GameSettings.Seed.
        /// </param>
        public StarMapGenerator(int mapWidth, int mapHeight, int starSeparation, int starDensity, int starUniformity, Random random = null)
        {
            this.mapWidth = mapWidth;
            this.mapHeight = mapHeight;

            this.starSeparation = starSeparation;
            this.starDensity = starDensity;
            this.starUniformity = starUniformity;

            this.random = random ?? new Random();

            this.density = new double[mapWidth, mapHeight];
        }


        /// <summary>
        /// When set, ordinary stars are placed by the discrete wizard algorithm of
        /// behavior-specs-11/new-game-setup.md section 3 instead of the density function: this
        /// many stars in all (home worlds included, since in the original the home worlds are
        /// picked from the galaxy's own stars), from count + count / 7 candidate positions
        /// (capped at 999) sorted by x and swept at the fixed <see cref="PresetStarSeparation"/>,
        /// then trimmed at random to the count - see PlaceStarsByCount. Null keeps the free
        /// density-function generator.
        /// </summary>
        public int? TargetStarCount { get; set; }

        /// <summary>
        /// The wizard galaxy's minimum star separation in light-years (behavior-specs-11/
        /// new-game-setup.md section 3): a fixed 12, not derived from the diameter and not the
        /// free map's StarSeparation setting. Candidates at a squared distance of 144 or less
        /// from a survivor are removed, so surviving stars are more than 12 ly apart (squared
        /// distance 145 or more) before clumping.
        /// </summary>
        public const int PresetStarSeparation = 12;

        /// <summary>
        /// "Galaxy Clumping": run the single relaxation pass (RelaxStars) after placement.
        /// </summary>
        public bool Clumping { get; set; }


        /// <summary>
        /// Generate stars and homeworlds.
        /// Note that the number of generated stars will be a random value.
        /// </summary>
        public void Generate(int numPlayers)
        {
            this.numPlayers = numPlayers;
            // Initial uniform density
            this.SetStandardDensity();
            this.SetHomeworldReducer();
            this.PlaceHomeworlds();

            if (this.TargetStarCount.HasValue)
            {
                this.PlaceStarsByCount(this.TargetStarCount.Value);
            }
            else
            {
                // Reset the density for Star generation
                this.SetStandardDensity();
                this.SetStandardReducer();
                // Account for already placed homeworlds in the density function.
                this.AccountHomeworlds();
                this.PlaceStars();
            }

            if (this.Clumping)
            {
                RelaxStars(this.stars, this.homeworlds, this.random);
            }
        }


        /// <summary>
        /// The wizard galaxy's star placement (behavior-specs-11/new-game-setup.md section 3,
        /// `:50307`-`50363`): count + count / 7 candidate positions (capped at 999) are drawn,
        /// sorted by x and swept in that order - a later candidate at a squared distance of at
        /// most 144 (<see cref="PresetStarSeparation"/> squared) from a surviving earlier one is
        /// removed. If more candidates survive than wanted, random survivors are removed one at
        /// a time until exactly the wanted number remain; if fewer survive, the galaxy simply has
        /// fewer stars. The home worlds already placed count toward the total.
        /// </summary>
        /// <remarks>
        /// Port choices: the original chooses its home worlds among the placed stars, this port
        /// places them first, so the wanted number is the total less the home worlds and a
        /// candidate within the separation of a home world is swept away too. The original
        /// draws coordinates 10 .. D - 10; this port keeps its EdgeMargin of 20 for the map
        /// view's sake (see the constant's own comment).
        /// </remarks>
        private void PlaceStarsByCount(int totalStars)
        {
            int ordinaryStars = Math.Max(0, totalStars - this.homeworlds.Count);
            int candidateCount = Math.Min(Nova.Common.GameSettings.MaximumStars, totalStars + (totalStars / 7));
            const long maximumRemovedSquared = PresetStarSeparation * PresetStarSeparation;

            List<int[]> candidates = new List<int[]>(candidateCount);
            for (int i = 0; i < candidateCount; i++)
            {
                int x = NextCoordinate(this.mapWidth);
                int y = NextCoordinate(this.mapHeight);
                candidates.Add(new int[] { x, y });
            }

            List<int[]> survivors = new List<int[]>(candidateCount);
            foreach (int[] candidate in SortedByX(candidates))
            {
                if (IsWithin(candidate[0], candidate[1], survivors, maximumRemovedSquared)
                    || IsWithin(candidate[0], candidate[1], this.homeworlds, maximumRemovedSquared))
                {
                    continue;
                }

                survivors.Add(candidate);
            }

            while (survivors.Count > ordinaryStars)
            {
                survivors.RemoveAt(this.random.Next(survivors.Count));
            }

            this.stars.AddRange(survivors);
        }

        /// <summary>A stable sort by x (ties keep their order), as the original's x-sort of the candidates.</summary>
        private static List<int[]> SortedByX(List<int[]> positions)
        {
            return positions.OrderBy(position => position[0]).ToList();
        }

        /// <summary>True when (x, y) is at a squared distance of at most <paramref name="maximumSquared"/> from any of <paramref name="positions"/>.</summary>
        private static bool IsWithin(int x, int y, List<int[]> positions, long maximumSquared)
        {
            foreach (int[] position in positions)
            {
                long dx = position[0] - x;
                long dy = position[1] - y;
                if ((dx * dx) + (dy * dy) <= maximumSquared)
                {
                    return true;
                }
            }
            return false;
        }


        /// <summary>
        /// The "Galaxy Clumping" relaxation pass (behavior-specs-11/new-game-setup.md section 3,
        /// `:50364`-`50441`): one pass of exactly as many steps as there are stars to move. Each
        /// step picks a random star (with replacement, so some stars move several times and some
        /// never), finds its nearest other star by squared distance on the current positions
        /// (ties go to the lower index) and moves only the picked star, by truncating integer
        /// division, according to that squared distance d: d &lt;= 144 no move; 145-324
        /// (4 x own + neighbour) / 5; 325-625 (2 x own + neighbour) / 3; 626-1,600
        /// (own + neighbour) / 2; 1,601 or more (own + 2 x neighbour) / 3, with no outer limit.
        /// The stars are then re-sorted by x. Clumping can bring stars closer than 12 ly.
        /// </summary>
        /// <param name="stars">The stars to move; re-sorted by x (stable) on return.</param>
        /// <param name="fixedStars">
        /// Further stars that count as neighbours but are never moved (this port's home worlds,
        /// which are placed separately from the ordinary stars and so are never picked). May be
        /// null. Port choice: the original clumps before its home worlds are chosen, so there
        /// every star can move.
        /// </param>
        /// <param name="random">Draws the picked star's index, one draw per step.</param>
        /// <remarks>A picked star with no neighbour at all is left alone.</remarks>
        public static void RelaxStars(List<int[]> stars, List<int[]> fixedStars, Random random)
        {
            int steps = stars.Count;
            for (int step = 0; step < steps; step++)
            {
                int i = random.Next(stars.Count);
                int[] self = stars[i];
                int[] nearest = null;
                long nearestSquared = long.MaxValue;

                for (int j = 0; j < stars.Count; j++)
                {
                    if (j != i)
                    {
                        Consider(self, stars[j], ref nearest, ref nearestSquared);
                    }
                }
                if (fixedStars != null)
                {
                    foreach (int[] other in fixedStars)
                    {
                        Consider(self, other, ref nearest, ref nearestSquared);
                    }
                }

                if (nearest == null)
                {
                    continue;
                }

                if (nearestSquared <= 144)
                {
                    // No move.
                }
                else if (nearestSquared <= 324)
                {
                    self[0] = ((4 * self[0]) + nearest[0]) / 5;
                    self[1] = ((4 * self[1]) + nearest[1]) / 5;
                }
                else if (nearestSquared <= 625)
                {
                    self[0] = ((2 * self[0]) + nearest[0]) / 3;
                    self[1] = ((2 * self[1]) + nearest[1]) / 3;
                }
                else if (nearestSquared <= 1600)
                {
                    self[0] = (self[0] + nearest[0]) / 2;
                    self[1] = (self[1] + nearest[1]) / 2;
                }
                else
                {
                    self[0] = (self[0] + (2 * nearest[0])) / 3;
                    self[1] = (self[1] + (2 * nearest[1])) / 3;
                }
            }

            List<int[]> sorted = SortedByX(stars);
            stars.Clear();
            stars.AddRange(sorted);
        }

        private static void Consider(int[] self, int[] other, ref int[] nearest, ref long nearestSquared)
        {
            long dx = other[0] - self[0];
            long dy = other[1] - self[1];
            long squared = (dx * dx) + (dy * dy);
            if (squared < nearestSquared)
            {
                nearestSquared = squared;
                nearest = other;
            }
        }
           
        
        /// <summary>
        /// Sets an uniform density of 1 across the universe.
        /// </summary>
        private void SetStandardDensity()
        {
            // initialize density function
            // the initial values can influence the shape of generated map
            for (int i = 0; i < this.mapWidth; ++i)
            {
                for (int j = 0; j < this.mapHeight; ++j)
                {
                    this.density[i, j] = 1.0;
                }
            }
        }
        
        
        /// <summary>
        /// Sets the parameteres for the density reduce function, for
        /// generating a standard StarMap.
        /// </summary>
        private void SetStandardReducer()
        {
            this.baseDensity = ((2.0 * (this.starUniformity - 1)) + (0.11 * (100 - this.starUniformity))) / 99.0;
            this.maxRadius = ((100.0 * (this.starUniformity - 1)) + (400 * (100 - this.starUniformity))) / 99.0;

            // middle value of uniformity produces low density (~ x0.63), so equalizing this a bit with border values
            double densityBalancer = ((1 * Math.Abs(50.0 - this.starUniformity)) / 50.0) + (0.63 * (1 - (Math.Abs(50.0 - this.starUniformity) / 50.0)));

            this.baseDensity *= densityBalancer;
            this.maxRadius *= densityBalancer;

            double densityApplied = (0.5 * ((this.starDensity - 1) / 99.0)) + ((2.0 * (100 - this.starDensity)) / 99.0);

            this.baseDensity *= densityApplied;
            this.maxRadius *= densityApplied;

            this.updateFrameSize = (int)Math.Ceiling(this.maxRadius);
            
            this.minSeparation = this.starSeparation;
        }
  
        
        /// <summary>
        /// Sets the parameters for the density reduce function, for
        /// generating homeworlds.
        /// </summary>
        private void SetHomeworldReducer()
        {   
            this.baseDensity = 1.0;
            
            int playerFactor = (int)Math.Floor(Math.Sqrt(this.numPlayers)) + 1;
            
            this.minSeparation = (int)Math.Min(this.mapWidth, this.mapHeight) /
                (2 * playerFactor);
            

            // This could do with some explanation ??? (priority 4).
            this.maxRadius = Math.Abs(mapWidth - mapHeight) / (mapWidth + mapHeight) *
                this.minSeparation * this.numPlayers / (2.0 - (this.starDensity / 100)) + 
                (Math.Sqrt((Math.Pow(playerFactor, 2.0) - numPlayers) / (Math.Pow(playerFactor, 2.0))) * minSeparation);
            
            this.updateFrameSize = (int)Math.Max(Math.Ceiling(this.maxRadius), Math.Ceiling((double)this.minSeparation));
        }
  
        
        /// <summary>
        /// Picks a random coordinate along one axis, kept at least EdgeMargin away from both
        /// ends of that axis's dimension (falling back to the dimension's own midpoint region if
        /// it's too small to fit a margin on both sides, rather than throwing).
        /// </summary>
        private int NextCoordinate(int dimension)
        {
            int margin = Math.Min(EdgeMargin, (dimension - 1) / 2);
            int usable = dimension - (2 * margin);
            return margin + this.random.Next(usable);
        }


        /// <summary>
        /// Genetate a star map.
        /// </summary>
        private void PlaceStars()
        {
            while (true)
            {
                int x = 0;
                int y = 0;

                // count failed attempts to generate star
                int count = 0;
                while (true)
                {
                    x = NextCoordinate(this.mapWidth);
                    y = NextCoordinate(this.mapHeight);
                    double height = this.random.NextDouble();
                    if (height <= this.density[x, y])
                    {   // the star can be placed at this position
                        break;
                    }
                    count++;

                    // if we have exceeded maximum number of attempts
                    // then the map is filled with stars and we finish
                    if (count > FailuresThreshold)
                    {
                        return;
                    }
                }

                this.stars.Add(new int[] { x, y });
                UpdateDensities(x, y);
            }
        }
        
        
        /// <summary>
        /// Generate Homeworld locations. 
        /// </summary>
        private void PlaceHomeworlds()
        {
            int x = 0;
            int y = 0;
            double height = 0;
            
            for (int q = 0; q < this.numPlayers; ++q)
            {
                while (true)
                {
                    x = NextCoordinate(this.mapWidth);
                    y = NextCoordinate(this.mapHeight);
                    height = this.random.NextDouble();

                    if (height <= this.density[x, y])
                    {   // the star can be placed at this position
                        break;
                    }
                }

                this.homeworlds.Add(new int[] { x, y });
                UpdateDensities(x, y);
            }    
        }
        
        
        /// <summary>
        /// Update Density after the star has been placed at co-ordinate (x,y).
        /// </summary>
        /// <param name="x">X ordinate of newly place star.</param>
        /// <param name="y">Y ordinate of newly place star.</param>
        private void UpdateDensities(int x, int y)
        {
            for (int i = (x - (this.updateFrameSize / 2) > 0) ? (x - (this.updateFrameSize / 2)) : 0; i <= ((x + (this.updateFrameSize / 2) < this.mapWidth) ? (x + (this.updateFrameSize / 2)) : (this.mapWidth - 1)); ++i)
            {
                for (int j = (y - (this.updateFrameSize / 2) > 0) ? (y - (this.updateFrameSize / 2)) : 0; j <= ((y + (this.updateFrameSize / 2) < this.mapHeight) ? (y + (this.updateFrameSize / 2)) : (this.mapHeight - 1)); ++j)
                {
                    double d = Math.Sqrt(((x - i) * (x - i)) + ((y - j) * (y - j)));
                    this.density[i, j] -= Reduce(d);
                }
            }
        }
  
        
        /// <summary>
        /// This function defines the amount the density function value should be 
        /// reduced by at current point based on the distance between current point and
        /// the star.
        /// </summary>
        /// <param name="distance">Distance between the current point and the star.</param>
        /// <returns>Returning 1 means the density function value will be reduced down to zero 
        /// at the current point. Returning 0 means value will not be changed.</returns>
        private double Reduce(double distance)
        {   
            if (distance < this.minSeparation) 
            {
                return 1.0;
            }
            else if (distance < this.maxRadius)
            {
                return (this.maxRadius - distance) / (this.maxRadius - this.minSeparation) * this.baseDensity;
            }
            
            return 0.0;
        }
        
        
        /// <summary>
        /// Reduces the density function around already placed homeworlds.
        /// Do this before generating the rest of the StarMap.
        /// </summary>
        private void AccountHomeworlds()
        {
            foreach (int[] homeWorldPosition in this.homeworlds)
            {
                UpdateDensities(homeWorldPosition[0], homeWorldPosition[1]);
            }
        }

        
        /// <summary>
        /// Returns(only) the list of stars as a List of int[2]; int[0] is x, int[1] is y.
        /// </summary>
        public List<int[]> Stars
        {
            get
            {
                return this.stars;
            }
            
            set
            {
            }
        }
        
        /// <summary>
        /// Returns(only) the list of homeworlds as a List of int[2]; int[0] is x, int[1] is y.
        /// </summary>
        public List<int[]> Homeworlds
        {
            get
            {
                return this.homeworlds;
            }
            
            set 
            {
            }
        }
    }
}
