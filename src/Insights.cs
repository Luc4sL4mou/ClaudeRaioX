using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ClaudeRaioX;

// Uso por dia (histórico das sessões), alertas de segurança e espaço em disco.
public static class Insights
{
    // US$ por milhão de tokens: entrada, saída, leitura de cache. Gravação de cache = 1,25× entrada (5 min) ou 2× (1 h).
    // shortcut: tabela fixa com preços da API em 2026-11; atualizar quando sair modelo novo.
    static readonly (string key, double inp, double outp, double read)[] Prices =
    {
        ("fable-5-2", 8, 40, 0.20), ("mythos-5-2", 8, 40, 0.20), ("fable-5-1", 10, 50, 0.25), ("mythos-5-1", 10, 50, 0.25),
        ("fable-5", 10, 50, 1), ("mythos-5", 10, 50, 1),
        ("opus-5-5", 4, 20, 0.20), ("opus-5", 5, 25, 0.50), ("opus-4", 5, 25, 0.50),
        ("sonnet-5-5", 2, 10, 0.10), ("sonnet-5", 2, 10, 0.20), ("sonnet-4", 3, 15, 0.30),
        ("haiku-5-5", 0.10, 0.50, 0.01), ("haiku-4", 1, 5, 0.10),
    };

    public static double Cost(string model, long inp, long outp, long read, long w5m, long w1h)
    {
        var p = Prices.FirstOrDefault(p => model.Contains(p.key));
        if (p.key == null) return 0;
        return (inp * p.inp + outp * p.outp + read * p.read + w5m * p.inp * 1.25 + w1h * p.inp * 2) / 1e6;
    }

    class Day
    {
        public long In, Out, Read, Write;
        public double Cost;
        public HashSet<string> Sessions = new();
        public Dictionary<string, double> Models = new();
    }

    public class UsageResult
    {
        public List<Item> Days = new();
        public List<(DateTime Day, double Cost)> Daily = new();
        public Dictionary<string, int> Connectors = new();
        public Dictionary<string, int> McpCalls = new();
        public double Total30;
    }

    static readonly Regex ConnectorRx = new(@"mcp__claude_ai_([A-Za-z0-9_]+?)__", RegexOptions.Compiled);

    public static UsageResult ScanUsage(string projDir)
    {
        var r = new UsageResult();
        var days = new Dictionary<DateTime, Day>();
        var seen = new HashSet<string>();
        if (Directory.Exists(projDir))
            foreach (var f in Directory.EnumerateFiles(projDir, "*.jsonl", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
            {
                // subagentes ficam em <sessão>/subagents/*.jsonl e contam como a mesma sessão
                var session = f.Contains(@"\subagents\") ? Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(f))!) : Path.GetFileNameWithoutExtension(f);
                var conns = new HashSet<string>();
                try
                {
                    using var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var sr = new StreamReader(fs);
                    string? line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (line.Contains("mcp__claude_ai_"))
                            foreach (Match m in ConnectorRx.Matches(line)) conns.Add(m.Groups[1].Value.Replace('_', ' '));
                        if (line.Contains("\"usage\"") && line.Contains("\"assistant\"")) ParseLine(line, session, days, seen, r);
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                foreach (var c in conns) r.Connectors[c] = r.Connectors.GetValueOrDefault(c) + 1;
            }

        foreach (var (date, d) in days.OrderByDescending(x => x.Key))
        {
            var model = d.Models.OrderByDescending(m => m.Value).Select(m => m.Key.Replace("claude-", "")).FirstOrDefault() ?? "";
            r.Days.Add(new(date.ToString("yyyy-MM-dd ddd"), model,
                $"entrada {d.In:N0} · saída {d.Out:N0} · cache lido {d.Read:N0} · cache gravado {d.Write:N0}",
                $"US$ {d.Cost:F2} · {d.Sessions.Count} sessões", "",
                $"Modelos (custo estimado):\n{string.Join("\n", d.Models.OrderByDescending(m => m.Value).Select(m => $"  {m.Key}: US$ {m.Value:F2}"))}"));
        }
        for (var i = 29; i >= 0; i--)
        {
            var day = DateTime.Today.AddDays(-i);
            var cost = days.TryGetValue(day, out var d) ? d.Cost : 0;
            r.Daily.Add((day, cost));
            r.Total30 += cost;
        }
        return r;
    }

    static void ParseLine(string line, string session, Dictionary<DateTime, Day> days, HashSet<string> seen, UsageResult r)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object) return;
            if (msg.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                foreach (var c in content.EnumerateArray())
                    if (c.ValueKind == JsonValueKind.Object && c.TryGetProperty("type", out var t) && t.GetString() == "tool_use"
                        && c.TryGetProperty("name", out var n) && n.GetString() is { } name && name.StartsWith("mcp__"))
                    {
                        var server = name.Split("__")[1].Replace("claude_ai_", "claude.ai: ").Replace('_', ' ');
                        r.McpCalls[server] = r.McpCalls.GetValueOrDefault(server) + 1;
                    }
            // a mesma resposta aparece em várias linhas (uma por bloco) com o mesmo usage: contar uma vez
            if (!msg.TryGetProperty("usage", out var u) || u.ValueKind != JsonValueKind.Object
                || !msg.TryGetProperty("id", out var id) || !seen.Add(id.GetString() ?? "")) return;
            if (!root.TryGetProperty("timestamp", out var ts) || !DateTime.TryParse(ts.GetString(), out var when)) return;
            var model = msg.TryGetProperty("model", out var mo) ? mo.GetString() ?? "" : "";
            long inp = L(u, "input_tokens"), outp = L(u, "output_tokens"), read = L(u, "cache_read_input_tokens"), write = L(u, "cache_creation_input_tokens");
            var w1h = u.TryGetProperty("cache_creation", out var cc) && cc.ValueKind == JsonValueKind.Object ? L(cc, "ephemeral_1h_input_tokens") : 0;
            var cost = Cost(model, inp, outp, read, write - w1h, w1h);
            if (!days.TryGetValue(when.Date, out var d)) days[when.Date] = d = new Day();
            d.In += inp; d.Out += outp; d.Read += read; d.Write += write; d.Cost += cost;
            d.Sessions.Add(session);
            if (model != "" && !model.StartsWith('<')) d.Models[model] = d.Models.GetValueOrDefault(model) + cost;
        }
        catch (JsonException) { }
        catch (InvalidOperationException) { }
    }

    static long L(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;

    // ---------- Segurança ----------
    static readonly (Regex rx, string sev, string why)[] Rules =
    {
        (new(@"(curl|wget|iwr|irm|Invoke-WebRequest|Invoke-RestMethod)\b.*\|\s*(sh|bash|iex|powershell|pwsh|Invoke-Expression)\b", RegexOptions.IgnoreCase), "Alta", "baixa algo da internet e executa direto"),
        (new(@"(\s-enc\b|-EncodedCommand|FromBase64String|base64\s+(-d|--decode))", RegexOptions.IgnoreCase), "Alta", "executa comando ofuscado (base64)"),
        (new(@"\b(iex|Invoke-Expression)\b", RegexOptions.IgnoreCase), "Média", "executa texto como código (Invoke-Expression)"),
        (new(@"(rm\s+-rf|Remove-Item\b.*-Recurse|\bdel\s+/s|\bformat\s+[a-z]:)", RegexOptions.IgnoreCase), "Média", "apaga arquivos em massa"),
        (new(@"\bnpx\s+(-y|--yes)\b|\buvx\b|\bpip install\b", RegexOptions.IgnoreCase), "Média", "baixa e roda um pacote da internet a cada início (versão pode mudar sozinha)"),
        (new(@"https?://", RegexOptions.IgnoreCase), "Baixa", "acessa a internet"),
    };

    static readonly string[] ScriptExt = { ".py", ".ps1", ".sh", ".js", ".mjs", ".bat", ".cmd", ".vbs", ".exe", ".dll" };

    public static List<Item> Security(Snapshot s)
    {
        var list = new List<Item>(s.Tabs["Segurança"]); // alertas de segredo já vieram do Scanner
        foreach (var (tab, kind) in new[] { ("Hooks", "hook"), ("MCP", "servidor MCP") })
            foreach (var it in s.Tabs[tab])
                foreach (var (rx, sev, why) in Rules)
                    if (rx.IsMatch(it.Info)) { list.Add(new($"{kind} {it.Name}", it.Origin, why + ": " + it.Info, sev, it.Path, it.Detail)); break; }

        foreach (var sk in s.Tabs["Skills"])
        {
            var dir = Path.GetDirectoryName(sk.Path);
            if (dir == null || !Directory.Exists(dir)) continue;
            var scripts = Directory.EnumerateFiles(dir, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                .Where(f => ScriptExt.Contains(Path.GetExtension(f).ToLowerInvariant())).ToList();
            if (scripts.Count == 0) continue;
            var binary = scripts.Any(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
            list.Add(new($"skill {sk.Name}", sk.Origin,
                $"traz {scripts.Count} script(s) que rodam na sua máquina quando a skill é usada: {string.Join(", ", scripts.Take(4).Select(Path.GetFileName))}",
                binary ? "Média" : "Baixa", scripts[0], string.Join("\n", scripts)));
        }

        foreach (var cfg in s.Tabs["Configurações"])
        {
            JsonNode? n;
            try { n = JsonNode.Parse(cfg.Detail); } catch { continue; }
            var allow = (n?["permissions"]?["allow"] as JsonArray)?.Select(a => a?.ToString() ?? "") ?? [];
            foreach (var a in allow.Where(a => a is "Bash" or "Bash(*)" or "Bash(*:*)" or "PowerShell" or "PowerShell(*)"))
                list.Add(new($"permissão {a}", cfg.Origin, "libera qualquer comando de terminal sem perguntar", "Alta", cfg.Path, cfg.Detail));
            if (n?["permissions"]?["defaultMode"]?.ToString() == "bypassPermissions")
                list.Add(new("defaultMode: bypassPermissions", cfg.Origin, "todas as ações rodam sem pedir confirmação", "Alta", cfg.Path, cfg.Detail));
            if (n?["skipDangerousModePermissionPrompt"]?.ToString() == "true")
                list.Add(new("skipDangerousModePermissionPrompt", cfg.Origin, "o aviso do modo perigoso foi desligado", "Média", cfg.Path, cfg.Detail));
        }

        foreach (var p in s.Tabs["Plugins"].Where(p => !p.Name.EndsWith("@claude-plugins-official")))
            list.Add(new($"plugin {p.Name}", p.Origin, "plugin de terceiros: roda código de fora da Anthropic; revise antes de atualizar", "Baixa", p.Path, p.Detail));

        var rank = new Dictionary<string, int> { ["Alta"] = 0, ["Média"] = 1, ["Baixa"] = 2 };
        return list.OrderBy(i => rank.GetValueOrDefault(i.Extra, 3)).ThenBy(i => i.Name).ToList();
    }

    // ---------- Espaço ----------
    static readonly Dictionary<string, (string level, string why)> DiskHints = new(StringComparer.OrdinalIgnoreCase)
    {
        ["projects"] = ("cuidado", "Histórico das conversas. Apagar perde o /resume e as estatísticas de uso; dá para apagar só sessões antigas."),
        ["file-history"] = ("sim, os antigos", "Cópias dos arquivos antes de cada edição, usadas para desfazer (rewind). Sessões antigas podem ir."),
        ["plugins"] = ("não", "Plugins instalados. Para liberar espaço, desinstale os que não usa (/plugin)."),
        ["skills"] = ("não", "Suas skills."),
        ["agents"] = ("não", "Seus agentes."),
        ["commands"] = ("não", "Seus comandos."),
        ["cache"] = ("sim", "Cache; o Claude Code recria quando precisar."),
        ["shell-snapshots"] = ("sim", "Retrato do ambiente do terminal; recriado a cada sessão."),
        ["session-env"] = ("sim", "Variáveis de ambiente de sessões passadas."),
        ["debug"] = ("sim", "Logs de depuração."),
        ["todos"] = ("sim, os antigos", "Listas de tarefas de sessões passadas."),
        ["backups"] = ("sim, os antigos", "Cópias de segurança do .claude.json."),
        ["downloads"] = ("sim", "Downloads temporários."),
        ["paste-cache"] = ("sim", "Textos colados em sessões passadas."),
        ["image-cache"] = ("sim", "Imagens coladas em sessões passadas."),
        ["telemetry"] = ("sim", "Eventos de telemetria pendentes."),
        ["statsig"] = ("sim", "Cache de configurações remotas."),
        ["sessions"] = ("sim, os antigos", "Metadados de sessões."),
    };

    public static (List<Item> items, long total) Disk(string claude)
    {
        var items = new List<(long size, Item item)>();
        long total = 0;
        if (!Directory.Exists(claude)) return ([], 0);
        foreach (var d in Directory.GetDirectories(claude))
        {
            var (size, count) = Measure(d);
            total += size;
            var (level, why) = DiskHints.GetValueOrDefault(Path.GetFileName(d), ("?", "Sem recomendação: pasta interna do Claude Code ou de um plugin."));
            items.Add((size, new(Path.GetFileName(d), level, why, $"{size / 1048576.0:F1} MB · {count} arquivos", d, "")));
        }
        foreach (var f in Directory.GetFiles(claude)) total += new FileInfo(f).Length;
        var proj = Path.Combine(claude, "projects");
        if (Directory.Exists(proj))
            foreach (var d in Directory.GetDirectories(proj).Select(d => (d, m: Measure(d))).OrderByDescending(x => x.m.size).Take(10))
                items.Add((d.m.size, new(@"projects\" + Path.GetFileName(d.d), "cuidado", "Conversas deste projeto (apagar perde o histórico dele).",
                    $"{d.m.size / 1048576.0:F1} MB · {d.m.count} arquivos", d.d, "")));
        return (items.OrderByDescending(x => x.size).Select(x => x.item).ToList(), total);
    }

    static (long size, int count) Measure(string dir)
    {
        long size = 0; var count = 0;
        foreach (var f in Directory.EnumerateFiles(dir, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
        {
            try { size += new FileInfo(f).Length; count++; } catch { }
        }
        return (size, count);
    }
}
