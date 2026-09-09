using System.Drawing.Drawing2D;

namespace QuickShot;

/// <summary>
/// Bảng màu + helper vẽ dùng chung cho toàn bộ UI: nền tối, accent gradient
/// xanh dương -> tím, và các easing curve cho hoạt cảnh.
/// </summary>
internal static class Theme
{
    public static readonly Color AccentStart = Color.FromArgb(59, 130, 246);   // #3B82F6
    public static readonly Color AccentEnd = Color.FromArgb(139, 92, 246);     // #8B5CF6
    public static readonly Color PanelBackground = Color.FromArgb(20, 22, 28); // #14161C
    public static readonly Color TextPrimary = Color.FromArgb(244, 244, 246);  // #F4F4F6
    public static readonly Color TextSecondary = Color.FromArgb(160, 164, 176);

    public static LinearGradientBrush AccentBrush(RectangleF rect, float angle = 45f) =>
        new(rect, AccentStart, AccentEnd, angle);

    public static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        if (d > rect.Width) d = Math.Max(0, rect.Width);
        if (d > rect.Height) d = Math.Max(0, rect.Height);

        if (d <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }

        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    // Viền glow mềm: nhiều lớp rounded-rect càng ra ngoài càng mờ, mô phỏng blur
    // mà không cần bitmap blur thật (đắt hơn nhiều lần cho một hoạt cảnh chạy mỗi tick).
    public static void DrawGlowBorder(Graphics g, RectangleF rect, float radius, float intensity, int layers = 4, float maxSpread = 6f)
    {
        if (rect.Width < 2 || rect.Height < 2 || intensity <= 0) return;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        for (int i = layers; i >= 1; i--)
        {
            float spread = maxSpread * i / layers;
            float alpha = intensity * (1f - (float)i / (layers + 1)) * 0.5f;
            var outer = RectangleF.Inflate(rect, spread, spread);
            using var path = RoundedRect(outer, radius + spread);
            using var pen = new Pen(Color.FromArgb((int)(Math.Clamp(alpha, 0f, 1f) * 255), AccentStart), 2f);
            g.DrawPath(pen, path);
        }

        using var borderPath = RoundedRect(rect, radius);
        using var borderBrush = AccentBrush(rect);
        using var borderPen = new Pen(borderBrush, 2f);
        g.DrawPath(borderPen, borderPath);
    }

    public static double EaseOutCubic(double t) => 1 - Math.Pow(1 - t, 3);
    public static double EaseInCubic(double t) => t * t * t;
    public static double EaseOutQuad(double t) => 1 - (1 - t) * (1 - t);
}
