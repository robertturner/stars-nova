#region Copyright Notice
// ============================================================================
// Copyright (C) 2026 The Stars-Nova Project
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

namespace Nova.Client
{
    using System.Collections.Generic;

    using Nova.Common;

    /// <summary>
    /// Production catalog and queue captions where the item type, not the unit class, decides
    /// the wording (production-queue.md section 6 "Captions": 130 "Min Terraform", 131 "Max
    /// Terraform", 138 "Terraform Environment"), and the catalog rules for the terraform items:
    /// the manual "Terraform Environment" is offered only on a planet with terraform headroom
    /// (row 37, TerraformProductionUnit.CatalogOffersTerraformEnvironment); the two auto entries
    /// are always offered, Min and Max being chosen by TerraformProductionUnit.MinimumOnly.
    /// </summary>
    public static class ProductionCaptions
    {
        public const string TerraformEnvironment = "Terraform Environment";

        public const string MinTerraform = "Min Terraform";

        public const string MaxTerraform = "Max Terraform";

        /// <summary>The auto terraform choice's labels, Max first (MinimumOnly false).</summary>
        public static readonly IReadOnlyList<string> TerraformAutoChoices = new[] { MaxTerraform, MinTerraform };

        /// <summary>The caption of a queued order.</summary>
        public static string Of(ProductionOrder order)
        {
            if (order?.Unit is TerraformProductionUnit terraform)
            {
                return Terraform(order.IsAutoBuild, terraform.MinimumOnly);
            }

            return order?.Name ?? string.Empty;
        }

        /// <summary>The caption of a terraform item: manual, or auto Min / Max.</summary>
        public static string Terraform(bool autoBuild, bool minimumOnly)
        {
            if (!autoBuild)
            {
                return TerraformEnvironment;
            }

            return minimumOnly ? MinTerraform : MaxTerraform;
        }
    }
}
