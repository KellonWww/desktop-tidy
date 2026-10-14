using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Windows.Forms;

namespace DesktopTidy.UI
{
    // Shared vector mark: no per-paint bitmap allocation.
    internal static class AppBrand
    {
        internal static readonly Icon Icon = LoadIcon();
        private static Icon LoadIcon()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DesktopTidy.AppIcon"))
            {
                if (stream == null) throw new InvalidOperationException("Application icon resource is missing.");
                using (var icon = new Icon(stream, new Size(32, 32))) return (Icon)icon.Clone();
            }
        }
        internal static void Draw(Graphics graphics, Rectangle bounds)
        {
            var state = graphics.Save();
            try
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TranslateTransform(bounds.X, bounds.Y);
                graphics.ScaleTransform(bounds.Width / 1024F, bounds.Height / 1024F);
                SurfaceDrawing.Fill(graphics, new Rectangle(0, 0, 1024, 1024), 240, Color.FromArgb(39, 101, 106));
                SurfaceDrawing.Fill(graphics, new Rectangle(224, 224, 232, 576), 64, Color.FromArgb(237, 245, 245));
                SurfaceDrawing.Fill(graphics, new Rectangle(520, 224, 280, 240), 64, Color.FromArgb(237, 245, 245));
                SurfaceDrawing.Fill(graphics, new Rectangle(520, 528, 280, 272), 64, Color.FromArgb(153, 206, 205));
            }
            finally { graphics.Restore(state); }
        }
    }
    internal sealed class BrandMark : Control
    {
        internal BrandMark()
        {
            AccessibleName = "桌面整理 Logo"; TabStop = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); AppBrand.Draw(e.Graphics, ClientRectangle);
        }
    }
}
