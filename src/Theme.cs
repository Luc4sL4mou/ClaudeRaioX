namespace ClaudeRaioX;

// Papéis de cor: Fg = texto principal, Hot = destaque/seleção, Head = cabeçalhos, Soft = texto secundário, Dim = legendas.
public record Theme(string Name, Color Bg, Color Panel, Color Fg, Color Hot, Color Head, Color Soft, Color Dim, bool Rain)
{
    // Cores da moldura neon de referência: ciano, magenta e lilás sobre quase preto
    public static readonly Theme Cyberpunk = new("Cyberpunk", Color.FromArgb(6, 5, 14), Color.FromArgb(20, 13, 38), Color.FromArgb(0, 229, 255),
        Color.FromArgb(255, 46, 170), Color.FromArgb(200, 150, 255), Color.FromArgb(205, 210, 255), Color.FromArgb(125, 115, 175), false);

    public static readonly Theme Matrix = new("Matrix", Color.Black, Color.FromArgb(0, 22, 4), Color.FromArgb(0, 255, 65),
        Color.FromArgb(0, 179, 44), Color.FromArgb(210, 255, 210), Color.FromArgb(70, 210, 110), Color.FromArgb(40, 150, 70), true);

    public static readonly Theme[] All = { Cyberpunk, Matrix };

    // Fica ao lado do exe (na pasta do projeto, não no C:)
    static string FilePath => Path.Combine(AppContext.BaseDirectory, "tema.txt");

    public static Theme Load()
    {
        try { var n = File.ReadAllText(FilePath).Trim(); return All.FirstOrDefault(t => t.Name == n) ?? Cyberpunk; }
        catch { return Cyberpunk; }
    }

    public static void Save(Theme t)
    {
        try { File.WriteAllText(FilePath, t.Name); } catch { }
    }

    public Theme Next => All[(Array.IndexOf(All, this) + 1) % All.Length];

    public static string Hex(Color c) => $"#{c.R:x2}{c.G:x2}{c.B:x2}";
}

// Faixa com a "chuva digital": colunas de caracteres caindo, cabeça clara e rastro que apaga.
public class RainPanel : Panel
{
    // Referência: GIF escolhido pelo usuário (colunas densas de números e letras ocidentais em verde)
    const int Cell = 12;
    const string Glyphs = "0123456789012345678901234567890123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ:.=*+-<>|";
    static readonly Font GlyphFont = new("Consolas", 9, FontStyle.Bold);
    static readonly Font TitleFont = new("Consolas", 26, FontStyle.Bold);
    readonly System.Windows.Forms.Timer timer = new() { Interval = 55 };
    readonly Random rnd = new();
    Bitmap? buffer;
    int[] row = [];
    double[] speed = [];
    char[] head = [];
    public string Title = "";

    public RainPanel()
    {
        DoubleBuffered = true;
        timer.Tick += (_, _) => { Step(); Invalidate(); };
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Running { get => timer.Enabled; set => timer.Enabled = value; }

    void Reset()
    {
        buffer?.Dispose();
        // 24 bits: o texto GDI em bitmap com alfa sai transparente
        buffer = new Bitmap(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height), System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(buffer)) g.Clear(Color.Black);
        var cols = ClientSize.Width / Cell + 1;
        row = Enumerable.Range(0, cols).Select(_ => rnd.Next(-12, ClientSize.Height / Cell)).ToArray();
        speed = Enumerable.Range(0, cols).Select(_ => 0.5 + rnd.NextDouble() * 0.5).ToArray();
        head = new char[cols];
    }

    void Step()
    {
        if (buffer == null || buffer.Size != ClientSize) Reset();
        using var g = Graphics.FromImage(buffer!);
        using (var fade = new SolidBrush(Color.FromArgb(22, 0, 0, 0))) g.FillRectangle(fade, ClientRectangle);
        var rows = ClientSize.Height / Cell + 1;
        for (var c = 0; c < row.Length; c++)
        {
            if (rnd.NextDouble() > speed[c]) continue;
            var x = c * Cell;
            if (row[c] >= 0 && row[c] < rows) // a cabeça antiga vira rastro verde
            {
                g.FillRectangle(Brushes.Black, x, row[c] * Cell, Cell, Cell);
                TextRenderer.DrawText(g, head[c].ToString(), GlyphFont, new Point(x, row[c] * Cell), Color.FromArgb(0, 150 + rnd.Next(80), 40), Color.Black);
            }
            row[c]++;
            if (row[c] > rows + rnd.Next(0, 20)) { row[c] = -rnd.Next(0, 8); continue; }
            head[c] = Glyphs[rnd.Next(Glyphs.Length)];
            if (row[c] >= 0)
                TextRenderer.DrawText(g, head[c].ToString(), GlyphFont, new Point(x, row[c] * Cell), Color.FromArgb(150, 255, 150), Color.Black);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (buffer == null) { e.Graphics.Clear(Color.Black); return; }
        e.Graphics.DrawImage(buffer, 0, 0);
        if (Title == "") return;
        var size = TextRenderer.MeasureText(Title, TitleFont);
        var box = new Rectangle(24, (Height - size.Height) / 2 - 6, size.Width + 24, size.Height + 12);
        using (var shade = new SolidBrush(Color.FromArgb(200, 0, 0, 0))) e.Graphics.FillRectangle(shade, box);
        using (var edge = new Pen(Color.FromArgb(0, 255, 65))) e.Graphics.DrawRectangle(edge, box);
        // brilho: o mesmo texto deslocado em verde por baixo
        foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
            TextRenderer.DrawText(e.Graphics, Title, TitleFont, new Point(box.X + 12 + dx, box.Y + 6 + dy), Color.FromArgb(0, 160, 40));
        TextRenderer.DrawText(e.Graphics, Title, TitleFont, new Point(box.X + 12, box.Y + 6), Color.FromArgb(225, 255, 225));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { timer.Dispose(); buffer?.Dispose(); }
        base.Dispose(disposing);
    }
}
