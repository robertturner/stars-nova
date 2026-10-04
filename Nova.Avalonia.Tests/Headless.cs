using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NUnit.Framework;

namespace Nova.Avalonia.Tests;

/// <summary>Small helpers for hosting a view in a headless window and inspecting what it drew.</summary>
public static class Headless
{
    /// <summary>Hosts <paramref name="content"/> in a shown window of the given size and lets
    /// layout, bindings and one render pass run.</summary>
    public static Window Show(Control content, double width = 1280, double height = 800)
    {
        var window = new Window { Width = width, Height = height, Content = content };
        window.Show();
        Pump();
        return window;
    }

    /// <summary>Runs queued dispatcher work and one render tick.</summary>
    public static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Every visual of type <typeparamref name="T"/> under <paramref name="root"/>.</summary>
    public static List<T> All<T>(Visual root)
        where T : Visual
    {
        return root.GetVisualDescendants().OfType<T>().ToList();
    }

    /// <summary>Every visible TextBlock's text under <paramref name="root"/>.</summary>
    public static List<string> Texts(Visual root)
    {
        return All<TextBlock>(root)
            .Where(block => block.IsEffectivelyVisible && !string.IsNullOrEmpty(block.Text))
            .Select(block => block.Text!)
            .ToList();
    }

    /// <summary>
    /// Captures the window's current frame and returns how many distinct colours it holds (a
    /// sample of pixels on a grid) - a blank or failed render is one flat colour.
    /// </summary>
    public static int DistinctColours(Window window)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        WriteableBitmap? frame = window.CaptureRenderedFrame();
        Assert.That(frame, Is.Not.Null, "the headless renderer produced no frame");
        return DistinctColours(frame!);
    }

    public static int DistinctColours(WriteableBitmap frame)
    {
        using ILockedFramebuffer buffer = frame.Lock();
        int width = buffer.Size.Width;
        int height = buffer.Size.Height;
        var colours = new HashSet<int>();
        int[] row = new int[width];
        for (int y = 0; y < height; y += 4)
        {
            System.Runtime.InteropServices.Marshal.Copy(buffer.Address + (y * buffer.RowBytes), row, 0, width);
            for (int x = 0; x < width; x += 4)
            {
                colours.Add(row[x]);
            }
        }

        return colours.Count;
    }
}
