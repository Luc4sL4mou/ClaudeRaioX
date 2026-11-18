namespace ClaudeRaioX;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Contains("--selftest")) return SelfTest.Run();
        ApplicationConfiguration.Initialize();
#pragma warning disable WFO5001
        Application.SetColorMode(SystemColorMode.Dark); // barra de título e scrollbars escuras para o tema cyberpunk
#pragma warning restore WFO5001
        Application.Run(new MainForm());
        return 0;
    }
}

// Roda o leitor contra uma pasta .claude falsa. Uso: ClaudeRaioX.exe --selftest (código de saída 0 = ok)
static class SelfTest
{
    public static int Run()
    {
        var home = Path.Combine(Path.GetTempPath(), "raiox-test-" + Guid.NewGuid().ToString("N"));
        var log = Path.Combine(Path.GetTempPath(), "raiox-selftest.txt");
        try
        {
            var c = Path.Combine(home, ".claude");
            Put(c, "skills/minha/SKILL.md", "---\nname: minha\ndescription: >\n  faz algo\n  útil\n---\ncorpo");
            Put(c, "agents/ag.md", "---\nname: ag\ndescription: um agente\nmodel: sonnet\n---\n");
            Put(c, "commands/git/push.md", "# Faz push");
            Put(c, "CLAUDE.md", "# Preferências");
            var plug = Path.Combine(home, "plug");
            Put(plug, ".claude-plugin/plugin.json", "{\"hooks\":[\"./h/a.json\"]}");
            Put(plug, "h/a.json", "{\"hooks\":{\"SessionStart\":[{\"hooks\":[{\"type\":\"command\",\"command\":\"start\"}]}]}}");
            Put(c, "plugins/installed_plugins.json", "{\"plugins\":{\"code-review@claude-plugins-official\":[{\"installPath\":" + System.Text.Json.JsonSerializer.Serialize(plug) + ",\"version\":\"1\"}]}}");
            Put(c, "settings.json", "{\"enabledPlugins\":{\"code-review@claude-plugins-official\":true},\"hooks\":{\"Stop\":[{\"hooks\":[{\"type\":\"command\",\"command\":\"echo oi\"}]}]}}");
            // mesma resposta repetida em 2 linhas (um bloco por linha): deve contar uma vez; 1M de entrada no Opus 5.5 = US$ 4
            var ts = DateTime.UtcNow.ToString("o");
            var line = "{\"timestamp\":\"" + ts + "\",\"type\":\"assistant\",\"message\":{\"id\":\"m1\",\"model\":\"claude-opus-5-5\",\"role\":\"assistant\","
                + "\"content\":[{\"type\":\"tool_use\",\"name\":\"mcp__claude_ai_Claude_Docs__batch\"}],\"usage\":{\"input_tokens\":1000000,\"output_tokens\":0}}}";
            Put(c, "projects/proj/s1.jsonl", line + "\n" + line + "\n");
            Put(c, "skills/minha/run.py", "print(1)");
            Put(home, ".claude.json", "{\"mcpServers\":{\"x\":{\"command\":\"cmd\",\"env\":{\"TOKEN\":\"segredo123\"}}},"
                + "\"skillUsage\":{\"minha\":{\"usageCount\":3,\"lastUsedAt\":1700000000000}}}");

            var s = Scanner.Scan(home);
            Check(s.Tabs["Skills"].Single().Info == "faz algo útil", "descrição multilinha da skill");
            Check(s.Tabs["Skills"][0].Extra.StartsWith("3×"), "contagem de uso da skill");
            Check(s.Tabs["Agentes"].Single().Extra == "sonnet", "modelo do agente");
            Check(s.Tabs["Comandos"].Single().Name == "/git:push", "nome do comando em subpasta");
            Check(s.Tabs["Hooks"].Any(h => h.Info == "echo oi" && h.Origin == "usuário"), "hook do settings.json");
            Check(s.Tabs["Hooks"].Any(h => h.Info == "start" && h.Origin == "plugin code-review"), "hook declarado no plugin.json");
            Check(s.Tabs["Plugins"].Single().Origin == "ativo", "plugin ativo");
            Check(s.Tabs["Memória"].Single().Info == "Preferências", "CLAUDE.md global");
            var mcp = s.Tabs["MCP"].Single();
            Check(!mcp.Detail.Contains("segredo123"), "env do MCP mascarado");
            Check(mcp.Extra == "comando ok", "comando do MCP achado no PATH");
            Check(Math.Abs(s.Daily[^1].Cost - 4.0) < 0.001, "custo do dia deduplicado (esperado US$ 4)");
            Check(s.Tabs["Uso diário"].Single().Extra.StartsWith("US$ 4.00") || s.Tabs["Uso diário"].Single().Extra.StartsWith("US$ 4,00"), "linha do dia");
            Check(s.Overview.Contains("Claude Docs (1 sessões)"), "conector do claude.ai visto na sessão");
            Check(s.Tabs["Segurança"].Any(a => a.Name == "servidor MCP x" && a.Info.Contains("TOKEN")), "alerta de segredo em texto puro");
            Check(s.Tabs["Segurança"].Any(a => a.Name == "skill minha" && a.Extra == "Baixa"), "alerta de skill com script");
            Check(s.Tabs["Segurança"].All(a => !a.Detail.Contains("segredo123")), "alerta não expõe o segredo");
            Check(s.Tabs["Espaço"].Any(d => d.Name == "projects"), "pasta projects na aba Espaço");
            var recs = Recommendations.For("Plugins", s).ToList();
            Check(recs.Count > 0 && recs.All(r => r.Name != "code-review"), "recomendação some quando o plugin já está instalado");
            var square = new PointF[] { new(0, 0), new(10, 0), new(10, 10), new(0, 10) };
            Check(Neon.Length(square, true) == 40 && Neon.At(square, 15, true) == new PointF(10, 5) && Neon.At(square, -5, true) == new PointF(0, 5), "caminho das luzes neon");
            Check(!Report.Html(s, Theme.Cyberpunk).Contains("segredo123"), "relatório sem segredo");
            Check(Report.Html(s, Theme.Matrix).Contains("background:#000000") && Theme.Matrix.Next == Theme.Cyberpunk, "tema Matrix no relatório e ciclo de temas");
            File.WriteAllText(log, "OK");
            return 0;
        }
        catch (Exception e) { File.WriteAllText(log, "FALHA: " + e.Message); return 1; }
        finally { try { Directory.Delete(home, true); } catch { } }
    }

    static void Put(string root, string rel, string text)
    {
        var f = Path.Combine(root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(f)!);
        File.WriteAllText(f, text);
    }

    static void Check(bool ok, string what) { if (!ok) throw new Exception(what); }
}
