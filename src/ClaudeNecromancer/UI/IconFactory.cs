using System.Drawing.Drawing2D;

namespace ClaudeNecromancer.UI;

/// <summary>
/// Draws the tray/window icon at runtime.
///
/// Generated rather than shipped as an .ico so the project builds from source with no binary
/// assets, and so the icon can change colour to reflect state.
/// </summary>
public static class IconFactory
{
    public static Icon Create(Color accent, int size = 32)
    {
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var pad = size * 0.06f;
            var rect = new RectangleF(pad, pad, size - pad * 2, size - pad * 2);

            using (var body = new SolidBrush(Color.FromArgb(28, 30, 38)))
            using (var path = RoundedRect(rect, size * 0.24f))
            {
                g.FillPath(body, path);
            }

            // A heartbeat trace: the app's whole job is keeping a pulse on these sessions.
            var w = rect.Width;
            var midY = rect.Top + rect.Height * 0.55f;
            var points = new[]
            {
                new PointF(rect.Left + w * 0.14f, midY),
                new PointF(rect.Left + w * 0.34f, midY),
                new PointF(rect.Left + w * 0.44f, midY - rect.Height * 0.26f),
                new PointF(rect.Left + w * 0.56f, midY + rect.Height * 0.20f),
                new PointF(rect.Left + w * 0.66f, midY),
                new PointF(rect.Left + w * 0.86f, midY),
            };

            using var pen = new Pen(accent, Math.Max(2f, size * 0.085f))
            {
                LineJoin = LineJoin.Round,
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            g.DrawLines(pen, points);
        }

        // Icon.FromHandle does not own the HICON, so clone and release to avoid leaking a GDI handle.
        var hIcon = bitmap.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(hIcon);
            return (Icon)temp.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(hIcon);
        }
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal static class NativeMethods
{
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(IntPtr handle);
}
