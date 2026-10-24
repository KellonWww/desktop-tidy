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
            bool horizontalBand = pointer.X >= screen.Left + CornerGuard && pointer.X < screen.Right - CornerGuard;
            if (verticalBand && pointer.X < screen.Left + EdgePixels) return NotesEdge.Left;
            if (verticalBand && pointer.X >= screen.Right - EdgePixels) return NotesEdge.Right;
            if (horizontalBand && pointer.Y < screen.Top + EdgePixels) return NotesEdge.Top;
            if (horizontalBand && pointer.Y >= screen.Bottom - EdgePixels) return NotesEdge.Bottom;
            return NotesEdge.None;
        }
        internal static NotesEdge Nearest(Rectangle work, Point pointer)
        {
            int left = Math.Abs(pointer.X - work.Left), right = Math.Abs(pointer.X - work.Right);
            int top = Math.Abs(pointer.Y - work.Top), bottom = Math.Abs(pointer.Y - work.Bottom);
            int distance = Math.Min(Math.Min(left, right), Math.Min(top, bottom));
            if (distance == left) return NotesEdge.Left;
            if (distance == right) return NotesEdge.Right;
            return distance == top ? NotesEdge.Top : NotesEdge.Bottom;
        }
        internal static string Name(NotesEdge side)
        {
            return side == NotesEdge.Top ? "顶部" : side == NotesEdge.Right ? "右侧" : side == NotesEdge.Bottom ? "底部" : "左侧";
        }
        internal static Rectangle HandleBounds(Rectangle work, NotesEdge side, float scale)
        {
            bool vertical = side == NotesEdge.Left || side == NotesEdge.Right;
            int width = Math.Min(work.Width, Math.Max(1, (int)((vertical ? 28 : 96) * scale)));
            int height = Math.Min(work.Height, Math.Max(1, (int)((vertical ? 88 : 28) * scale)));
            int x = side == NotesEdge.Left ? work.Left : side == NotesEdge.Right ? work.Right - width : work.Left + (work.Width - width) / 2;
            int y = side == NotesEdge.Top ? work.Top : side == NotesEdge.Bottom ? work.Bottom - height : work.Top + (work.Height - height) / 2;
            return new Rectangle(x, y, width, height);
        }
        internal static Rectangle PanelBounds(Rectangle work, NotesEdge side, float scale)
        {
            int margin = Math.Min(12, Math.Max(0, Math.Min(work.Width, work.Height) / 20));
            int availableWidth = Math.Max(1, work.Width - margin * 2), availableHeight = Math.Max(1, work.Height - margin * 2);
            int width = Math.Min(availableWidth, (int)(440 * scale));
            int height = Math.Min(availableHeight, (int)(760 * scale));
            int x = side == NotesEdge.Right ? work.Right - margin - width :
                side == NotesEdge.Left ? work.Left + margin : work.Left + (work.Width - width) / 2;
            int y = side == NotesEdge.Top ? work.Top + margin :
                side == NotesEdge.Bottom ? work.Bottom - margin - height : work.Top + (work.Height - height) / 2;
            return new Rectangle(x, y, width, height);
        }
    }

    // Pure timing policy keeps pointer jitter and manual collapse independent of native windows.
    internal sealed class NotesEdgeTrigger
    {
        internal const int RevealDelay = 300;
        internal const int HideDelay = 600;
        internal int HoverDelay = RevealDelay;
        private string candidate;
        private long entered, left = -1;
        private bool suppressed;
        internal void Suppress() { suppressed = true; candidate = null; left = -1; }
        internal bool ShouldReveal(string edge, long now)
        {
            if (edge == null) { candidate = null; suppressed = false; return false; }
            if (suppressed) return false;
            if (candidate != edge) { candidate = edge; entered = now; return false; }
            if (now - entered < HoverDelay) return false;
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
