using System.Drawing.Drawing2D;

namespace HowToFish.RouletteTrainer.App;

internal sealed class GlowPanel : Panel
{
    internal int Radius { get; set; } = 20;
    internal Color BorderColor { get; set; } = Color.FromArgb(73, 62, 124);
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Rounded(ClientRectangle, Radius);
        using var fill = new SolidBrush(BackColor);
        using var border = new Pen(BorderColor);
        e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path);
    }
    internal static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var d = radius * 2; var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d - 1, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d - 1, r.Bottom - d - 1, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d - 1, d, d, 90, 90);
        p.CloseFigure(); return p;
    }
}

internal sealed class ModeButton : Button
{
    internal Color Accent { get; set; } = Color.White;
    internal string Subtitle { get; set; } = "";
    internal bool Active { get; set; }
    internal ModeButton() { FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand; TabStop = false; }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = GlowPanel.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 15);
        var fillColor = Active ? Mix(Accent, Color.FromArgb(31, 25, 68), .28f) : BackColor;
        using var fill = new SolidBrush(fillColor); using var border = new Pen(Active ? Accent : Color.FromArgb(80, 67, 133), Active ? 3f : 1f);
        e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path);
        using var stripe = new SolidBrush(Accent); e.Graphics.FillRectangle(stripe, 14, 15, 7, Height - 30);
        TextRenderer.DrawText(e.Graphics, Text, new Font("Segoe UI Semibold", 12f), new Rectangle(34, 15, Width - 42, 28), ForeColor, TextFormatFlags.Left);
        TextRenderer.DrawText(e.Graphics, Subtitle, new Font("Segoe UI", 8.5f), new Rectangle(34, 48, Width - 42, 36), Color.FromArgb(210, 204, 234), TextFormatFlags.Left | TextFormatFlags.WordBreak);
        if (Active) TextRenderer.DrawText(e.Graphics, "✓ ACTIVE", new Font("Segoe UI Semibold", 7.5f), new Rectangle(34, Height - 28, Width - 42, 18), Accent, TextFormatFlags.Left);
    }
    private static Color Mix(Color a, Color b, float amount) => Color.FromArgb((int)(a.R * amount + b.R * (1 - amount)), (int)(a.G * amount + b.G * (1 - amount)), (int)(a.B * amount + b.B * (1 - amount)));
}
