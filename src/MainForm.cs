using System.Diagnostics;
using System.Net;
using System.Text;

namespace ClaudeRaioX;

public class MainForm : Form
{
    static Theme T = Theme.Load();
    static readonly Font Mono = new("Consolas", 10), MonoBold = new("Consolas", 10, FontStyle.Bold);

    static readonly Dictionary<string, string[]> Headers = new()
    {
        ["Uso diário"] = ["DIA", "MODELO PRINCIPAL", "TOKENS", "CUSTO ESTIMADO"],
        ["Segurança"] = ["ALVO", "ORIGEM", "MOTIVO", "GRAVIDADE"],
        ["Espaço"] = ["PASTA", "PODE APAGAR?", "O QUE É", "TAMANHO"],
        ["Projetos"] = ["PASTA", "STATUS", "ÚLTIMA SESSÃO", "HISTÓRICO"],
    };
    static readonly string[] RecTabs = { "Skills", "Agentes", "Plugins" };
    const string AppTitle = "◢ CLAUDE//RAIO-X";

    readonly TabControl tabs = new() { Dock = DockStyle.Fill, DrawMode = TabDrawMode.OwnerDrawFixed, Padding = new(14, 5), Font = MonoBold };
    readonly TextBox search = new() { PlaceholderText = "Filtrar por nome, origem ou descrição (Ctrl+F)", Width = 380, BorderStyle = BorderStyle.FixedSingle };
    readonly Button refresh = new() { Text = "Atualizar (F5)", AutoSize = true, FlatStyle = FlatStyle.Flat };
    readonly Button export = new() { Text = "Exportar relatório", AutoSize = true, FlatStyle = FlatStyle.Flat };
    readonly Button theme = new() { AutoSize = true, FlatStyle = FlatStyle.Flat };
    readonly Label status = new() { AutoSize = true, Padding = new(8, 6, 0, 0) };
    readonly FlowLayoutPanel top = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new(8) };
    readonly RainPanel rain = new() { Dock = DockStyle.Top, Height = 96, Title = "CLAUDE//RAIO-X" };
    readonly NeonHeader neonHeader = new() { Dock = DockStyle.Top, Height = 104, Title = "CLAUDE//RAIO-X", Subtitle = "SYS://CLAUDE_CODE · INVENTÁRIO LOCAL" };
    readonly FramePanel frame = new() { Dock = DockStyle.Fill };
    readonly TextBox overview = MakeText();
    readonly Panel chart = new() { Dock = DockStyle.Top, Height = 230 };
    readonly Dictionary<string, (TabPage page, ListView list, TextBox detail, SplitContainer split)> views = new();
    readonly Dictionary<string, FlowLayoutPanel> recPanels = new();
    Snapshot snap = new();

    public MainForm()
    {
        Text = "Claude Raio-X";
        Size = new(1250, 780);
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        KeyPreview = true;
        Font = Mono;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        tabs.DrawItem += (_, e) =>
        {
            // a faixa atrás das abas é pintada pelo sistema em cinza claro; cobre com a cor do tema,
            // sem pintar por cima das abas (a selecionada é desenhada por último e apagaria as outras)
            if (e.Index == 0)
                using (var strip = new SolidBrush(T.Bg))
                using (var area = new Region(new Rectangle(0, 0, tabs.Width, tabs.GetTabRect(0).Bottom + 3)))
                {
                    for (var i = 0; i < tabs.TabCount; i++) area.Exclude(tabs.GetTabRect(i));
                    e.Graphics.FillRegion(strip, area);
                }
            var sel = e.Index == tabs.SelectedIndex;
            using var b = new SolidBrush(sel ? T.Hot : T.Panel);
            e.Graphics.FillRectangle(b, e.Bounds);
            TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, MonoBold, e.Bounds, sel ? T.Bg : T.Fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };
        var ov = new TabPage("Visão geral");
        ov.Controls.Add(overview);
        tabs.TabPages.Add(ov);
        foreach (var name in Scanner.TabNames) AddTab(name);

        top.Controls.AddRange([search, refresh, export, theme, status]);
        // o Windows desenha uma borda clara em volta das páginas que não aceita cor;
        // as abas ficam num painel 4 px menor de cada lado (exceto o topo) para esconder essa borda
        var tabHost = new Panel { Dock = DockStyle.Fill };
        tabs.Dock = DockStyle.None;
        tabHost.Resize += (_, _) => tabs.Bounds = new Rectangle(-4, 0, tabHost.Width + 8, tabHost.Height + 4);
        tabHost.Controls.Add(tabs);
        frame.Controls.Add(tabHost);
        frame.Controls.Add(top);
        frame.Controls.Add(neonHeader);
        frame.Controls.Add(rain);
        Controls.Add(frame);

        chart.Paint += (_, e) => DrawChart(e.Graphics, chart.ClientRectangle);
        chart.Resize += (_, _) => chart.Invalidate();
        search.TextChanged += (_, _) => Populate();
        refresh.Click += async (_, _) => await Reload();
        export.Click += (_, _) => Export();
        theme.Click += (_, _) => { T = T.Next; Theme.Save(T); ApplyTheme(); };
        KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.F5) await Reload();
            if (e.Control && e.KeyCode == Keys.F) search.Focus();
        };
        var splitDone = new HashSet<SplitContainer>();
        tabs.SelectedIndexChanged += (_, _) =>
        {
            var v = views.Values.FirstOrDefault(v => v.page == tabs.SelectedTab);
            if (v.split != null && splitDone.Add(v.split)) SetSplit(v.split);
        };
        Resize += (_, _) =>
        {
            var visible = WindowState != FormWindowState.Minimized;
            rain.Running = T.Rain && visible;
            neonHeader.Running = frame.Running = !T.Rain && visible;
        };
        Shown += async (_, _) =>
        {
            foreach (var v in views.Values) SetSplit(v.split);
            await Reload();
        };
        ApplyTheme();
    }

    void ApplyTheme()
    {
        SuspendLayout();
        BackColor = T.Bg;
        ForeColor = T.Fg;
        top.BackColor = T.Bg;
        frame.BackColor = neonHeader.BackColor = tabs.Parent!.BackColor = T.Bg;
        rain.Visible = rain.Running = T.Rain;
        neonHeader.Visible = neonHeader.Running = !T.Rain;
        frame.On = !T.Rain; // moldura neon só no Cyberpunk
        search.BackColor = T.Panel;
        search.ForeColor = T.Fg;
        foreach (var b in new[] { refresh, export, theme })
        {
            b.FlatAppearance.BorderColor = T.Hot;
            b.ForeColor = T.Hot;
            b.BackColor = T.Bg;
        }
        theme.Text = $"Tema: {T.Name} ▸";
        status.ForeColor = T.Dim;
        overview.BackColor = T.Bg;
        overview.ForeColor = T.Fg;
        chart.BackColor = T.Bg;
        foreach (TabPage p in tabs.TabPages) p.BackColor = T.Bg;
        foreach (var (_, (_, list, detail, split)) in views)
        {
            list.BackColor = T.Bg;
            list.ForeColor = T.Fg;
            detail.BackColor = T.Bg;
            detail.ForeColor = T.Soft;
            split.BackColor = T.Hot;
            split.Panel1.BackColor = split.Panel2.BackColor = T.Bg;
        }
        BuildRecs();
        ResumeLayout();
        Invalidate(true);
    }

    void AddTab(string name)
    {
        var list = new ListView
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, OwnerDraw = true,
            BorderStyle = BorderStyle.None, ShowItemToolTips = true,
        };
        var heads = Headers.GetValueOrDefault(name) ?? ["NOME", "ORIGEM", "DESCRIÇÃO / INFO", "USO / STATUS"];
        foreach (var (h, w) in heads.Zip(new[] { 260, 190, 560, 220 })) list.Columns.Add(h, w);
        list.Resize += (_, _) => list.Columns[2].Width = Math.Max(300, list.ClientSize.Width - list.Columns[0].Width - list.Columns[1].Width - list.Columns[3].Width - 4);
        list.DrawColumnHeader += DrawHeader;
        list.DrawItem += (_, e) => e.DrawDefault = false;
        list.DrawSubItem += DrawCell;
        var detail = MakeText();
        list.SelectedIndexChanged += (_, _) => detail.Text = Selected(list) is { } it
            ? $"{it.Name}\r\nOrigem: {it.Origin}\r\nArquivo: {it.Path}\r\n\r\n{it.Info}\r\n\r\n{it.Detail.Replace("\r", "").Replace("\n", "\r\n")}" : "";
        list.DoubleClick += (_, _) => { if (Selected(list) is { Path: not "" } it) Process.Start("explorer.exe", $"/select,\"{it.Path}\""); };
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 3 };
        split.Panel1.Controls.Add(list);
        split.Panel2.Controls.Add(detail);
        var page = new TabPage(name);
        page.Controls.Add(split);
        if (name == "Uso diário") page.Controls.Add(chart);
        if (RecTabs.Contains(name))
        {
            var recs = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new(10, 6, 10, 8),
            };
            recPanels[name] = recs;
            page.Controls.Add(recs);
        }
        tabs.TabPages.Add(page);
        views[name] = (page, list, detail, split);
    }

    // Abas que ainda não foram mostradas podem ter altura mínima; aí a divisão fica para depois.
    static void SetSplit(SplitContainer s)
    {
        var max = s.Height - s.Panel2MinSize - s.SplitterWidth;
        if (max > s.Panel1MinSize) s.SplitterDistance = Math.Clamp(s.Height * 3 / 5, s.Panel1MinSize, max);
    }

    static Item? Selected(ListView l) => l.SelectedItems.Count > 0 ? l.SelectedItems[0].Tag as Item : null;

    static TextBox MakeText()
    {
        var t = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
            Font = Mono, WordWrap = true, BorderStyle = BorderStyle.None,
        };
        // o Windows seleciona todo o texto quando a caixa recebe foco; desfaz depois que ele termina
        t.Enter += (_, _) => t.BeginInvoke(() => t.Select(0, 0));
        return t;
    }

    static void DrawHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        using var b = new SolidBrush(T.Panel);
        e.Graphics.FillRectangle(b, e.Bounds);
        using var p = new Pen(T.Hot, 2);
        e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        TextRenderer.DrawText(e.Graphics, e.Header!.Text, MonoBold, Rectangle.Inflate(e.Bounds, -6, 0), T.Head,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    static void DrawCell(object? sender, DrawListViewSubItemEventArgs e)
    {
        var sel = e.Item!.Selected;
        using var b = new SolidBrush(sel ? T.Hot : e.ItemIndex % 2 == 0 ? T.Bg : T.Panel);
        e.Graphics.FillRectangle(b, e.Bounds);
        var text = e.SubItem!.Text;
        var color = sel ? T.Bg : e.ColumnIndex switch { 0 => T.Fg, 1 => T.Hot, 3 => text is "Alta" ? T.Hot : T.Head, _ => T.Soft };
        TextRenderer.DrawText(e.Graphics, text, e.ColumnIndex == 0 ? MonoBold : Mono, Rectangle.Inflate(e.Bounds, -6, 0), color,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }

    void DrawChart(Graphics g, Rectangle r)
    {
        g.Clear(T.Bg);
        var data = snap.Daily;
        var total = data.Sum(d => d.Cost);
        TextRenderer.DrawText(g, $"CUSTO ESTIMADO POR DIA · ÚLTIMOS 30 DIAS · TOTAL US$ {total:F2}  (preço de API, para comparar com o plano)",
            MonoBold, new Point(12, 8), T.Head);
        if (data.Count == 0) return;
        var max = Math.Max(0.01, data.Max(d => d.Cost));
        var area = new Rectangle(70, 36, r.Width - 90, r.Height - 70);
        using var grid = new Pen(T.Panel);
        for (var i = 0; i <= 4; i++)
        {
            var y = area.Bottom - area.Height * i / 4;
            g.DrawLine(grid, area.Left, y, area.Right, y);
            TextRenderer.DrawText(g, $"{max * i / 4:F0}", Mono, new Rectangle(0, y - 8, 62, 16), T.Dim, TextFormatFlags.Right);
        }
        var w = area.Width / (float)data.Count;
        using var bar = new SolidBrush(T.Fg);
        using var today = new SolidBrush(T.Rain ? T.Head : T.Hot);
        for (var i = 0; i < data.Count; i++)
        {
            var h = (float)(data[i].Cost / max * area.Height);
            var x = area.Left + i * w + 2;
            g.FillRectangle(i == data.Count - 1 ? today : bar, x, area.Bottom - h, Math.Max(2, w - 4), h);
            if (i % 5 == 0 || i == data.Count - 1)
                TextRenderer.DrawText(g, data[i].Day.ToString("dd/MM"), Mono, new Point((int)x, area.Bottom + 6), T.Dim);
        }
    }

    async Task Reload()
    {
        refresh.Enabled = export.Enabled = false;
        status.Text = "Lendo… (o histórico de sessões pode levar alguns segundos)";
        try
        {
            // RAIOX_HOME: lê outra pasta (usado para gravar a demonstração com dados fictícios)
            var home = Environment.GetEnvironmentVariable("RAIOX_HOME") is { Length: > 0 } h ? h : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var sw = Stopwatch.StartNew();
            snap = await Task.Run(() => Scanner.Scan(home));
            overview.Text = snap.Overview.Replace("\r", "").Replace("\n", "\r\n");
            Populate();
            BuildRecs();
            chart.Invalidate();
            status.Text = $"Atualizado às {DateTime.Now:HH:mm:ss} em {sw.Elapsed.TotalSeconds:F1} s · duplo clique abre o arquivo no Explorer";
        }
        catch (Exception ex) { status.Text = "Erro ao ler: " + ex.Message; }
        finally { refresh.Enabled = export.Enabled = true; }
    }

    void BuildRecs()
    {
        foreach (var (tab, panel) in recPanels)
        {
            panel.SuspendLayout();
            panel.Controls.Clear();
            panel.BackColor = T.Panel;
            var recs = Recommendations.For(tab, snap).ToList();
            panel.Controls.Add(new Label
            {
                AutoSize = true, ForeColor = T.Hot, Font = MonoBold,
                Text = recs.Count == 0 ? "★ Você já tem tudo o que recomendamos aqui." : "★ RECOMENDADOS PARA VOCÊ · um clique abre o link",
            });
            foreach (var rec in recs)
            {
                var link = new LinkLabel
                {
                    AutoSize = true, Text = $"{rec.Name} — {rec.Why}", LinkArea = new LinkArea(0, rec.Name.Length),
                    LinkColor = T.Head, ActiveLinkColor = T.Hot, VisitedLinkColor = T.Fg, ForeColor = T.Soft, Font = Mono,
                    LinkBehavior = LinkBehavior.HoverUnderline, Padding = new(0, 2, 0, 0),
                };
                link.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(rec.Url) { UseShellExecute = true });
                panel.Controls.Add(link);
            }
            panel.ResumeLayout();
        }
    }

    void Populate()
    {
        var q = search.Text.Trim();
        foreach (var (name, (page, list, detail, _)) in views)
        {
            var items = snap.Tabs.GetValueOrDefault(name) ?? [];
            var shown = items.Where(i => q == "" || $"{i.Name} {i.Origin} {i.Info} {i.Extra}".Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var i in shown) list.Items.Add(new ListViewItem([i.Name, i.Origin, i.Info, i.Extra]) { Tag = i, ToolTipText = i.Info });
            list.EndUpdate();
            detail.Text = "";
            page.Text = $"{name} ({shown.Count})";
        }
    }

    void Export()
    {
        using var dlg = new SaveFileDialog
        {
            Title = "Exportar relatório · contém nome da conta, projetos e caminhos: revise antes de compartilhar",
            Filter = "Página HTML|*.html", FileName = $"ClaudeRaioX-{DateTime.Now:yyyy-MM-dd}.html",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dlg.FileName, Report.Html(snap, T), Encoding.UTF8);
            Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
            status.Text = "Relatório salvo em " + dlg.FileName;
        }
        catch (Exception ex) { MessageBox.Show(this, "Não deu para salvar: " + ex.Message, "Claude Raio-X", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}

public static class Report
{
    public static string Html(Snapshot s, Theme t)
    {
        static string E(string x) => WebUtility.HtmlEncode(x);
        string H(Color c) => Theme.Hex(c);
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"pt-BR\"><meta charset=\"utf-8\"><title>Claude Raio-X</title><style>")
          .Append($"body{{background:{H(t.Bg)};color:{H(t.Soft)};font:14px Consolas,monospace;margin:24px}}h1{{color:{H(t.Hot)}}}")
          .Append($"h2{{color:{H(t.Fg)};border-bottom:2px solid {H(t.Hot)};padding-bottom:4px;margin-top:32px}}")
          .Append($"pre{{background:{H(t.Panel)};padding:12px;white-space:pre-wrap}}table{{border-collapse:collapse;width:100%}}th{{color:{H(t.Head)};text-align:left;background:{H(t.Panel)}}}")
          .Append($"td,th{{padding:4px 8px;vertical-align:top;border-bottom:1px solid {H(t.Panel)}}}td:first-child{{color:{H(t.Fg)};font-weight:bold}}td:nth-child(2){{color:{H(t.Hot)}}}</style>")
          .Append($"<h1>◢ CLAUDE//RAIO-X</h1><p>Gerado em {DateTime.Now:dd/MM/yyyy HH:mm} · tema {E(t.Name)}</p><h2>Visão geral</h2><pre>{E(s.Overview)}</pre>");
        foreach (var (tab, items) in s.Tabs)
        {
            sb.Append($"<h2>{E(tab)} ({items.Count})</h2><table><tr><th>Nome</th><th>Origem</th><th>Descrição / info</th><th>Uso / status</th></tr>");
            foreach (var i in items) sb.Append($"<tr><td>{E(i.Name)}</td><td>{E(i.Origin)}</td><td>{E(i.Info)}</td><td>{E(i.Extra)}</td></tr>");
            sb.Append("</table>");
        }
        return sb.Append("</html>").ToString();
    }
}
