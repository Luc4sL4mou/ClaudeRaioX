using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace ClaudeRaioX;

// Desenho neon (halo largo e fraco + núcleo fino e claro) e caminhos para as luzes que correm.
static class Neon
{
    public static readonly Color Cyan = Color.FromArgb(0, 229, 255), Pink = Color.FromArgb(255, 46, 170), Violet = Color.FromArgb(160, 100, 255);

    public static Color Mix(Color a, Color b, float t) =>
        Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    public static void Line(Graphics g, Color c, float k, params PointF[] pts)
    {
        if (k <= 0.02f || pts.Length < 2) return;
        foreach (var (w, a) in new[] { (13f, 16), (7f, 40), (3.5f, 110) })
        {
            using var p = new Pen(Color.FromArgb(Math.Clamp((int)(a * k), 0, 255), c), w) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLines(p, pts);
        }
        using var core = new Pen(Color.FromArgb(Math.Clamp((int)(255 * k), 0, 255), Mix(c, Color.White, 0.5f)), 1.6f) { LineJoin = LineJoin.Round };
        g.DrawLines(core, pts);
    }

    public static void Dot(Graphics g, Color c, float k, PointF p, float r)
    {
        foreach (var (rr, a) in new[] { (r * 4, 25), (r * 2.2f, 70), (r, 255) })
        {
            using var b = new SolidBrush(Color.FromArgb(Math.Clamp((int)(a * k), 0, 255), rr == r ? Mix(c, Color.White, 0.6f) : c));
            g.FillEllipse(b, p.X - rr, p.Y - rr, rr * 2, rr * 2);
        }
    }

    public static void Chip(Graphics g, Color c, float k, RectangleF r)
    {
        using var b = new SolidBrush(Color.FromArgb(Math.Clamp((int)(60 * k), 0, 255), c));
        g.FillRectangle(b, r);
        Line(g, c, k * 0.8f, new(r.Left, r.Top), new(r.Right, r.Top), new(r.Right, r.Bottom), new(r.Left, r.Bottom), new(r.Left, r.Top));
    }

    public static float Length(PointF[] pts, bool closed)
    {
        float len = 0;
        for (var i = 1; i < pts.Length; i++) len += Dist(pts[i - 1], pts[i]);
        return closed ? len + Dist(pts[^1], pts[0]) : len;
    }

    // Ponto a uma distância s ao longo do caminho (para as luzes que correm)
    public static PointF At(PointF[] pts, float s, bool closed)
    {
        var total = Length(pts, closed);
        if (total <= 0) return pts[0];
        s = ((s % total) + total) % total;
        var n = closed ? pts.Length : pts.Length - 1;
        for (var i = 0; i < n; i++)
        {
            PointF a = pts[i], b = pts[(i + 1) % pts.Length];
            var d = Dist(a, b);
            if (s <= d) return new(a.X + (b.X - a.X) * s / d, a.Y + (b.Y - a.Y) * s / d);
            s -= d;
        }
        return pts[^1];
    }

    static float Dist(PointF a, PointF b) => MathF.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
}

// Base das animações: um relógio, brilho que pulsa e "piscadas" aleatórias por grupo de linhas, como neon falhando.
public abstract class NeonPanel : Panel
{
    readonly System.Windows.Forms.Timer timer = new() { Interval = 50 };
    protected readonly Random Rnd = new();
    protected float Time;
    readonly int[] flicker = new int[12];

    protected NeonPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        timer.Tick += (_, _) =>
        {
            Time += 0.05f;
            for (var i = 0; i < flicker.Length; i++)
                if (flicker[i] > 0) flicker[i]--;
                else if (Rnd.NextDouble() < 0.008) flicker[i] = Rnd.Next(2, 7);
            Invalidate();
        };
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Running { get => timer.Enabled; set => timer.Enabled = value; }

    // Intensidade do grupo: pulsa devagar; numa piscada cai e volta, alternando
    protected float K(int group) => flicker[group] > 0
        ? (flicker[group] % 2 == 0 ? 0.12f : 0.55f)
        : 0.78f + 0.22f * MathF.Sin(Time * 2.1f + group * 1.3f);

    protected override void Dispose(bool disposing)
    {
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
    }
}

// Moldura de circuito em volta do conteúdo (referência: moldura neon ciano/rosa com cantos chanfrados).
public class FramePanel : NeonPanel
{
    const float C = 26; // chanfro dos cantos
    bool on;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool On
    {
        get => on;
        set { on = value; Padding = value ? new Padding(20) : new Padding(0); Running = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!on || Width < 200 || Height < 200) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float l = 7, t = 7, r = Width - 8, b = Height - 8, w = r - l, h = b - t;
        PointF P(float x, float y) => new(x, y);

        // contorno principal em 4 trechos: ciano à esquerda, rosa à direita
        Neon.Line(g, Neon.Cyan, K(0), P(l, b - C - 70), P(l, t + C), P(l + C, t), P(l + w * 0.44f, t));
        Neon.Line(g, Neon.Pink, K(1), P(l + w * 0.56f, t), P(r - C, t), P(r, t + C), P(r, t + h * 0.62f));
        Neon.Line(g, Neon.Pink, K(2), P(r, t + h * 0.62f + 40), P(r, b - C), P(r - C, b), P(l + w * 0.56f, b));
        Neon.Line(g, Neon.Cyan, K(3), P(l + w * 0.44f, b), P(l + C, b), P(l, b - C), P(l, b - C - 34));

        // enfeite do topo: degrau com um anel no meio
        float x1 = l + w * 0.44f, x2 = l + w * 0.56f, mx = (x1 + x2) / 2;
        Neon.Line(g, Neon.Violet, K(4), P(x1, t), P(x1 + 10, t + 8), P(x2 - 10, t + 8), P(x2, t));
        Neon.Dot(g, Neon.Cyan, K(4), P(mx, t + 8), 3.5f);
        for (var i = 1; i <= 3; i++) Neon.Line(g, Neon.Pink, K(4) * 0.8f, P(x2 + 14 * i, t + 12), P(x2 + 14 * i + 7, t + 12));

        // trilhas internas paralelas nos cantos
        Neon.Line(g, Neon.Cyan, K(5), P(l + 7, t + C + 90), P(l + 7, t + C + 3), P(l + C + 3, t + 7), P(l + C + 150, t + 7));
        Neon.Line(g, Neon.Violet, K(6), P(r - C - 120, t + 7), P(r - C - 3, t + 7), P(r - 7, t + C + 3), P(r - 7, t + C + 60));
        Neon.Line(g, Neon.Pink, K(7), P(r - 7, b - C - 80), P(r - 7, b - C - 3), P(r - C - 3, b - 7), P(r - C - 170, b - 7));
        Neon.Line(g, Neon.Violet, K(8), P(l + C + 110, b - 7), P(l + C + 3, b - 7), P(l + 7, b - C - 3), P(l + 7, b - C - 50));
        Neon.Line(g, Neon.Cyan, K(8), P(l + 13, b - C - 90), P(l + 13, b - C - 60));

        // chips e pontos nas laterais (como na referência)
        Neon.Chip(g, Neon.Cyan, K(9), new RectangleF(l - 3, b - C - 64, 6, 10));
        Neon.Chip(g, Neon.Cyan, K(9), new RectangleF(l - 3, b - C - 50, 6, 10));
        Neon.Dot(g, Neon.Pink, K(10), P(r, t + h * 0.62f + 12), 2.5f);
        Neon.Dot(g, Neon.Pink, K(10), P(r, t + h * 0.62f + 26), 2.5f);
        Neon.Dot(g, Neon.Cyan, K(11), P(l + 13, t + h * 0.35f), 2.5f);
        Neon.Line(g, Neon.Cyan, K(11), P(l + 13, t + h * 0.35f + 10), P(l + 13, t + h * 0.35f + 60));
        for (var i = 0; i < 4; i++) Neon.Line(g, Neon.Pink, K(6) * 0.9f, P(r - C - 140 - i * 12, t + 13), P(r - C - 134 - i * 12, t + 13));

        // duas luzes correndo pela moldura em sentidos opostos
        var loop = new[] { P(l + C, t), P(r - C, t), P(r, t + C), P(r, b - C), P(r - C, b), P(l + C, b), P(l, b - C), P(l, t + C) };
        Neon.Dot(g, Neon.Cyan, 1, Neon.At(loop, Time * 260, true), 2.8f);
        Neon.Dot(g, Neon.Pink, 1, Neon.At(loop, -Time * 190 + 600, true), 2.8f);
    }
}

// Faixa do título no tema Cyberpunk: letreiro neon que tremula e trilhas de circuito com pulsos de dados.
public class NeonHeader : NeonPanel
{
    static readonly FontFamily Family = new("Consolas");
    public string Title = "";
    public string Subtitle = "";

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float cy = Height / 2f;

        // letreiro: contorno com halo rosa e miolo claro; tremula junto com o grupo 0
        using var path = new GraphicsPath();
        path.AddString(Title, Family, (int)FontStyle.Bold, 38, new PointF(28, cy - 30), StringFormat.GenericDefault);
        var k = K(0);
        foreach (var (wd, a) in new[] { (14f, 14), (8f, 35), (4f, 90) })
        {
            using var p = new Pen(Color.FromArgb((int)(a * k), Neon.Pink), wd) { LineJoin = LineJoin.Round };
            g.DrawPath(p, path);
        }
        using (var fill = new SolidBrush(Color.FromArgb(Math.Clamp((int)(255 * k), 30, 255), Neon.Mix(Neon.Pink, Color.White, 0.65f)))) g.FillPath(fill, path);
        var right = path.GetBounds().Right;
        TextRenderer.DrawText(g, Subtitle, new Font("Consolas", 9, FontStyle.Bold), new Point(32, (int)cy + 18), Neon.Mix(Neon.Cyan, Color.Black, 0.15f));

        // trilhas de circuito à direita do título
        float x0 = right + 36, end = Width - 30;
        if (end - x0 < 300) return;
        var t1 = new PointF[] { new(x0, cy - 12), new(x0 + 90, cy - 12), new(x0 + 112, cy - 30), new(end - 220, cy - 30), new(end - 200, cy - 12), new(end - 60, cy - 12) };
        var t2 = new PointF[] { new(x0, cy + 6), new(end - 150, cy + 6), new(end - 128, cy + 26), new(end, cy + 26) };
        var t3 = new PointF[] { new(x0 + 40, cy + 24), new(x0 + 260, cy + 24), new(x0 + 280, cy + 38), new(x0 + 520, cy + 38) };
        Neon.Line(g, Neon.Cyan, K(1), t1);
        Neon.Line(g, Neon.Pink, K(2), t2);
        Neon.Line(g, Neon.Violet, K(3), t3);
        Neon.Dot(g, Neon.Cyan, K(1), t1[^1], 3.5f);
        Neon.Dot(g, Neon.Pink, K(2), t2[^1], 3.5f);
        Neon.Dot(g, Neon.Violet, K(3), t3[^1], 3f);
        for (var i = 0; i < 6; i++) Neon.Chip(g, i % 2 == 0 ? Neon.Cyan : Neon.Violet, K(4 + i % 3), new RectangleF(end - 190 + i * 16, cy - 46, 9, 6));

        // pulsos de dados correndo pelas trilhas
        Neon.Dot(g, Neon.Cyan, 1, Neon.At(t1, Time * 420, false), 2.6f);
        Neon.Dot(g, Neon.Pink, 1, Neon.At(t2, Time * 330 + 200, false), 2.6f);
        Neon.Dot(g, Neon.Violet, 1, Neon.At(t3, Time * 250, false), 2.2f);
    }
}
