using System;
using System.Drawing;

namespace DesktopTidy.Core
{
    internal enum NotesEdge { None, Left, Top, Right, Bottom }

    internal static class NotesEdgeGeometry
    {
        internal const int EdgePixels = 3;
        internal const int CornerGuard = 40;
        internal static NotesEdge Detect(Rectangle screen, Point pointer)
        {
            if (!screen.Contains(pointer)) return NotesEdge.None;
            bool verticalBand = pointer.Y >= screen.Top + CornerGuard && pointer.Y < screen.Bottom - CornerGuard;
            if (verticalBand && pointer.X < screen.Left + EdgePixels) return NotesEdge.Left;
            return NotesEdge.None;
        }
        internal static Rectangle PanelBounds(Rectangle work, NotesEdge side, float scale)
        {
            int margin = Math.Min(12, Math.Max(0, Math.Min(work.Width, work.Height) / 20));
            int availableWidth = Math.Max(1, work.Width - margin * 2), availableHeight = Math.Max(1, work.Height - margin * 2);
            // Only the left drawer is exposed; retain the enum for animation compatibility.
            int width = Math.Min(availableWidth, (int)(440 * scale));
            int height = Math.Min(availableHeight, (int)(760 * scale));
            int x = work.Left + margin, y = work.Top + (work.Height - height) / 2;
            return new Rectangle(x, y, width, height);
        }
    }

    // Pure timing policy keeps pointer jitter and manual collapse independent of native windows.
    internal sealed class NotesEdgeTrigger
    {
        internal const int RevealDelay = 300;
        internal const int HideDelay = 600;
        private string candidate;
        private long entered, left = -1;
        private bool suppressed;
        internal void Suppress() { suppressed = true; candidate = null; left = -1; }
        internal bool ShouldReveal(string edge, long now)
        {
            if (edge == null) { candidate = null; suppressed = false; return false; }
            if (suppressed) return false;
            if (candidate != edge) { candidate = edge; entered = now; return false; }
            if (now - entered < RevealDelay) return false;
            candidate = null; return true;
        }
        internal bool ShouldHide(bool pointerInside, bool interacting, long now)
        {
            if (pointerInside || interacting) { left = -1; return false; }
            if (left < 0) { left = now; return false; }
            return now - left >= HideDelay;
        }
    }
}
