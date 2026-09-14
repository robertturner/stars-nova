using System;
using Avalonia;
using Avalonia.Media;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// One leg of the selected fleet's pending route overlay on the Star Map - ports the WinForms
/// StarMap.DrawFleetRoute fix (see StarMap.cs): the first leg (route origin = the fleet's current
/// position) is drawn distinctly from later legs, each leg carries an arrowhead showing direction
/// of travel, and each waypoint is marked with a dot (a larger, differently-colored one at the
/// final destination) - satisfying client-interface.md's requirement that "current position,
/// route origin, intermediate waypoints, destination, and leg direction are visually
/// distinguishable."
/// </summary>
public class StarMapRouteLegViewModel
{
    public Point Start { get; }

    public Point End { get; }

    public IBrush LineColor { get; }

    public double LineThickness { get; }

    public IBrush MarkerColor { get; }

    public double MarkerDiameter { get; }

    public double MarkerLeft { get; }

    public double MarkerTop { get; }

    /// <summary>
    /// The arrowhead triangle's 3 points, already rotated and positioned in absolute Canvas
    /// coordinates via plain trigonometry - computed here rather than via a Polygon
    /// RenderTransform/RenderTransformOrigin, since a fixed-shape Polygon positioned by
    /// Canvas.Left/Top and then rotated around a RenderTransformOrigin left the arrow's tip
    /// slightly off the intended point (the origin's coordinate space - relative to the
    /// polygon's own possibly axis-straddling geometry bounds, e.g. this arrow's points span
    /// y:[-4,4] - doesn't line up 1:1 with a Canvas.Left/Top offset that assumed a symmetric
    /// bounding box centered on that position). Computing the final points directly removes
    /// that ambiguity entirely.
    /// </summary>
    public Points ArrowPoints { get; }

    public StarMapRouteLegViewModel(double startX, double startY, double endX, double endY, bool isFirstLeg, bool isFinalLeg)
    {
        Start = new Point(startX, startY);
        End = new Point(endX, endY);

        LineColor = isFirstLeg ? Brushes.Yellow : Brushes.Cyan;
        LineThickness = isFirstLeg ? 3 : 2;

        MarkerColor = isFinalLeg ? Brushes.Yellow : Brushes.Cyan;
        MarkerDiameter = isFinalLeg ? 10 : 6;
        MarkerLeft = endX - (MarkerDiameter / 2);
        MarkerTop = endY - (MarkerDiameter / 2);

        double dx = endX - startX;
        double dy = endY - startY;
        double length = Math.Sqrt((dx * dx) + (dy * dy));
        // Unit vector along the leg's direction of travel (default to "pointing right" for a
        // zero-length leg, which never actually renders a visible line anyway).
        double ux = length > 0 ? dx / length : 1;
        double uy = length > 0 ? dy / length : 0;
        // The perpendicular unit vector - direction rotated 90 degrees - spans the arrowhead's
        // own base width either side of the leg's centerline.
        double px = -uy;
        double py = ux;

        const double arrowLength = 10;
        const double arrowHalfWidth = 4;
        const double pullBack = 8; // keeps the tip off the destination marker dot

        double tipX = endX - (ux * pullBack);
        double tipY = endY - (uy * pullBack);
        double backCenterX = tipX - (ux * arrowLength);
        double backCenterY = tipY - (uy * arrowLength);

        ArrowPoints = new Points
        {
            new Point(tipX, tipY),
            new Point(backCenterX + (px * arrowHalfWidth), backCenterY + (py * arrowHalfWidth)),
            new Point(backCenterX - (px * arrowHalfWidth), backCenterY - (py * arrowHalfWidth)),
        };
    }
}
