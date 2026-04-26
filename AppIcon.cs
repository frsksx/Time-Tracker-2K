using System.Runtime.InteropServices;

namespace TimeTracker2K;

internal static class AppIcon
{
    public static Icon Create()
    {
        using var bitmap = new Bitmap(64, 64);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        using var shadowBrush = new SolidBrush(Color.FromArgb(54, 0, 0, 0));
        graphics.FillEllipse(shadowBrush, 8, 9, 48, 48);

        using var faceBrush = new SolidBrush(Color.FromArgb(17, 120, 111));
        graphics.FillEllipse(faceBrush, 7, 6, 50, 50);

        using var innerBrush = new SolidBrush(Color.FromArgb(235, 250, 247));
        graphics.FillEllipse(innerBrush, 15, 14, 34, 34);

        using var accentPen = new Pen(Color.FromArgb(245, 174, 65), 6)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round
        };
        graphics.DrawArc(accentPen, 10, 9, 44, 44, -88, 142);

        using var handPen = new Pen(Color.FromArgb(26, 59, 73), 5)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round
        };
        graphics.DrawLine(handPen, 32, 32, 32, 20);
        graphics.DrawLine(handPen, 32, 32, 43, 37);

        using var centerBrush = new SolidBrush(Color.FromArgb(26, 59, 73));
        graphics.FillEllipse(centerBrush, 28, 28, 8, 8);

        var handle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
