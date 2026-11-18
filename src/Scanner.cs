using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ClaudeRaioX;

public record Item(string Name, string Origin, string Info, string Extra, string Path, string Detail);

public class Snapshot
{
    public string Overview = "";
    public Dictionary<string, List<Item>> Tabs = new();
    public List<(DateTime Day, double Cost)> Daily = new();
}

// Lê só arquivos locais do Claude Code; nunca escreve nada.
public static class Scanner
{
    public static readonly string[] TabNames =
        { "Uso diário", "Skills", "Agentes", "Comandos", "MCP", "Plugins", "Hooks", "Memória", "Projetos", "Configurações", "Segurança", "Espaço" };

    // Abas que já vêm na ordem certa (data, gravidade, tamanho)
    static readonly string[] Presorted = { "Uso diário", "Segurança", "Espaço" };
    static readonly Regex SecretKey = new("token|key|secret|pass|auth", RegexOptions.IgnoreCase);

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static Snapshot Scan(string home)
    {
        var claude = Path.Combine(home, ".claude");
        var json = ReadJson(Path.Combine(home, ".claude.json"));
        var settings = ReadJson(Path.Combine(claude, "settings.json"));
        var s = new Snapshot();
        foreach (var t in TabNames) s.Tabs[t] = new();
        void Add(string tab, Item it) => s.Tabs[tab].Add(it);

        var projectNodes = json?["projects"] as JsonObject ?? new JsonObject();
        var userClaude = Path.GetFullPath(claude).TrimEnd('\\');
        var projects = projectNodes.Select(p => p.Key)
            .Where(p => Directory.Exists(p) && !Path.GetFullPath(Path.Combine(p, ".claude")).TrimEnd('\\').Equals(userClaude, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Plugins
        var roots = new List<(string origin, string prefix, string dir)> { ("usuário", "", claude) };
        var installed = ReadJson(Path.Combine(claude, "plugins", "installed_plugins.json"))?["plugins"] as JsonObject;
        var enabled = settings?["enabledPlugins"] as JsonObject;
        var pluginUsage = json?["pluginUsage"] as JsonObject;
        foreach (var (key, arr) in installed ?? new JsonObject())
        {
            var e = (arr as JsonArray)?.FirstOrDefault();
            var path = Str(e?["installPath"]);
            var on = Bool(enabled?[key]);
            var name = key.Split('@')[0];
            if (on && Directory.Exists(path)) roots.Add(($"plugin {name}", name + ":", path));
            Add("Plugins", new(key, on ? "ativo" : "desativado", $"v{Str(e?["version"])} · instalado {Str(e?["installedAt"]).Split('T')[0]}",
                Uses(pluginUsage?[key]), path, e?.ToJsonString(Pretty) ?? ""));
        }
        foreach (var p in projects) roots.Add(($"projeto {Path.GetFileName(p.TrimEnd('/', '\\'))}", "", Path.Combine(p, ".claude")));

        // Skills, agentes e comandos têm o mesmo layout em usuário, plugin e projeto
        var skillUsage = json?["skillUsage"] as JsonObject;
        foreach (var (origin, prefix, dir) in roots)
        {
            foreach (var f in Files(Path.Combine(dir, "skills"), "SKILL.md", true))
            {
                var text = Read(f);
                var fm = Frontmatter(text);
                var name = prefix + Or(fm.GetValueOrDefault("name"), Path.GetFileName(Path.GetDirectoryName(f)!));
                var synced = f.Contains(@"\synced\");
                var skillUse = skillUsage?[name] ?? (synced ? skillUsage?["anthropic-skills:" + name] : null);
                Add("Skills", new(name, synced ? "claude.ai (sincronizada)" : origin, fm.GetValueOrDefault("description", ""), Uses(skillUse), f, text));
            }
            foreach (var f in Files(Path.Combine(dir, "agents"), "*.md", false))
            {
                var text = Read(f);
                var fm = Frontmatter(text);
                Add("Agentes", new(prefix + Or(fm.GetValueOrDefault("name"), Path.GetFileNameWithoutExtension(f)), origin,
                    fm.GetValueOrDefault("description", ""), fm.GetValueOrDefault("model", ""), f, text));
            }
            var cmdDir = Path.Combine(dir, "commands");
            foreach (var f in Files(cmdDir, "*.md", true))
            {
                var text = Read(f);
                var rel = Path.GetRelativePath(cmdDir, f)[..^3].Replace('\\', ':');
                Add("Comandos", new("/" + prefix + rel, origin, Or(Frontmatter(text).GetValueOrDefault("description"), FirstLine(text)), "", f, text));
            }
        }

        // MCP
        var claudeJsonPath = Path.Combine(home, ".claude.json");
        void AddMcp(JsonNode? servers, string origin, string path)
        {
            foreach (var (name, cfg) in servers as JsonObject ?? new JsonObject())
            {
                var cmd = Str(cfg?["command"]);
                var url = Str(cfg?["url"]);
                var type = Or(Str(cfg?["type"]), url != "" ? "http" : "stdio");
                var args = string.Join(' ', (cfg?["args"] as JsonArray ?? new JsonArray()).Select(Str));
                var info = cmd != "" ? $"{type}: {cmd} {args}" : $"{type}: {url}";
                var health = cmd == "" ? "" : OnPath(cmd) ? "comando ok" : "comando não encontrado";
                Add("MCP", new(name, origin, info, health, path, Mask(cfg)));
                foreach (var k in new[] { "env", "headers" })
                    foreach (var (envName, val) in cfg?[k] as JsonObject ?? new JsonObject())
                        if (SecretKey.IsMatch(envName) && Str(val) is { Length: > 0 } v && !v.Contains("${"))
                            Add("Segurança", new($"servidor MCP {name}", origin, $"segredo \"{envName}\" salvo em texto puro no arquivo; prefira ${{VARIAVEL}} de ambiente", "Média", path, Mask(cfg)));
            }
        }
        AddMcp(json?["mcpServers"], "usuário", claudeJsonPath);
        foreach (var (p, node) in projectNodes)
            AddMcp(node?["mcpServers"], $"projeto {Path.GetFileName(p.TrimEnd('/', '\\'))} (local)", claudeJsonPath);
        foreach (var p in projects)
        {
            var f = Path.Combine(p, ".mcp.json");
            AddMcp(ReadJson(f)?["mcpServers"], $"projeto {Path.GetFileName(p.TrimEnd('/', '\\'))} (.mcp.json)", f);
        }
        foreach (var (origin, _, dir) in roots.Where(r => r.origin.StartsWith("plugin")))
        {
            var f = Path.Combine(dir, ".mcp.json");
            var n = ReadJson(f);
            AddMcp(n?["mcpServers"] ?? n, origin, f);
        }

        // Hooks e configurações
        var settingsFiles = new List<(string origin, string file)>
        {
            ("usuário", Path.Combine(claude, "settings.json")),
            ("usuário", Path.Combine(claude, "settings.local.json")),
        };
        foreach (var p in projects)
        {
            var name = $"projeto {Path.GetFileName(p.TrimEnd('/', '\\'))}";
            settingsFiles.Add((name, Path.Combine(p, ".claude", "settings.json")));
            settingsFiles.Add((name, Path.Combine(p, ".claude", "settings.local.json")));
        }
        foreach (var (origin, _, dir) in roots.Where(r => r.origin.StartsWith("plugin")))
        {
            // plugin.json "hooks": caminho, lista de caminhos ou objeto inline; sem ele, hooks/hooks.json
            var manifestPath = Path.Combine(dir, ".claude-plugin", "plugin.json");
            var declared = ReadJson(manifestPath)?["hooks"];
            if (declared is JsonObject inline) { AddHooks(new JsonObject { ["hooks"] = inline.DeepClone() }, origin, manifestPath); continue; }
            var paths = declared is JsonArray a ? a.Select(Str).ToList() : declared != null ? [Str(declared)] : ["hooks/hooks.json"];
            foreach (var rel in paths)
            {
                var f = Path.GetFullPath(Path.Combine(dir, rel));
                AddHooks(ReadJson(f), origin, f);
            }
        }
        foreach (var (origin, file) in settingsFiles.Where(f => File.Exists(f.file)))
        {
            var n = ReadJson(file);
            AddHooks(n, origin, file);
            var perm = n?["permissions"];
            var allow = (perm?["allow"] as JsonArray)?.Count ?? 0;
            var deny = (perm?["deny"] as JsonArray)?.Count ?? 0;
            var keys = string.Join(", ", (n as JsonObject)?.Select(k => k.Key) ?? []);
            Add("Configurações", new(Path.GetFileName(file), origin, keys, $"permissões: {allow} allow / {deny} deny", file, Read(file)));
        }
        void AddHooks(JsonNode? root, string origin, string path)
        {
            foreach (var (evt, arr) in root?["hooks"] as JsonObject ?? new JsonObject())
            {
                var groups = arr as JsonArray ?? new JsonArray();
                var cmds = groups.SelectMany(g => (g?["hooks"] as JsonArray ?? new JsonArray()).Select(h => Or(Str(h?["command"]), Str(h?["type"]))));
                var matchers = groups.Select(g => Str(g?["matcher"])).Where(m => m != "");
                Add("Hooks", new(evt, origin, string.Join(" | ", cmds), string.Join(", ", matchers), path, arr?.ToJsonString(Pretty) ?? ""));
            }
        }

        // Memória: CLAUDE.md e pastas memory/
        void AddMem(string file, string origin)
        {
            if (!File.Exists(file)) return;
            var text = Read(file);
            Add("Memória", new(Path.GetFileName(file), origin, Or(Frontmatter(text).GetValueOrDefault("description"), FirstLine(text)),
                $"{new FileInfo(file).Length / 1024.0:F1} KB", file, text));
        }
        AddMem(Path.Combine(claude, "CLAUDE.md"), "global (usuário)");
        foreach (var p in projects)
        {
            var name = $"projeto {Path.GetFileName(p.TrimEnd('/', '\\'))}";
            foreach (var f in new[] { "CLAUDE.md", "CLAUDE.local.md", Path.Combine(".claude", "CLAUDE.md") }) AddMem(Path.Combine(p, f), name);
        }
        var projDir = Path.Combine(claude, "projects");
        foreach (var d in Dirs(projDir))
            foreach (var f in Files(Path.Combine(d, "memory"), "*.md", false)) AddMem(f, "auto-memória " + Path.GetFileName(d));

        // Projetos: custo/tokens da última sessão e nº de sessões gravadas
        double totalCost = 0;
        foreach (var (p, node) in projectNodes)
        {
            var cost = Num(node?["lastCost"]);
            totalCost += cost;
            var slugDir = Path.Combine(projDir, Regex.Replace(p, "[^A-Za-z0-9]", "-"));
            var sessions = Files(slugDir, "*.jsonl", false).ToList();
            var mb = sessions.Sum(f => new FileInfo(f).Length) / 1048576.0;
            Add("Projetos", new(p, Directory.Exists(p) ? "" : "pasta não existe",
                $"última sessão: US$ {cost:F2} · entrada {Num(node?["lastTotalInputTokens"]):N0} · saída {Num(node?["lastTotalOutputTokens"]):N0} · +{Num(node?["lastLinesAdded"])}/-{Num(node?["lastLinesRemoved"])} linhas",
                $"{sessions.Count} sessões ({mb:F1} MB)", Directory.Exists(slugDir) ? slugDir : p, node?.ToJsonString(Pretty) ?? ""));
        }

        foreach (var (_, list) in s.Tabs.Where(t => !Presorted.Contains(t.Key)))
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        var usage = Insights.ScanUsage(projDir);
        s.Tabs["Uso diário"] = usage.Days;
        s.Daily = usage.Daily;
        s.Tabs["Segurança"] = Insights.Security(s);
        var (disk, diskTotal) = Insights.Disk(claude);
        s.Tabs["Espaço"] = disk;
        s.Overview = Overview(s, json, settings, claude, totalCost, usage, diskTotal);
        return s;
    }

    static string Bar(double pct) => "[" + new string('█', (int)Math.Round(Math.Clamp(pct, 0, 100) / 5)).PadRight(20, '░') + "]";

    static string Overview(Snapshot s, JsonNode? json, JsonNode? settings, string claude, double totalCost, Insights.UsageResult usage, long diskTotal)
    {
        var sb = new StringBuilder();
        var acc = json?["oauthAccount"];
        sb.AppendLine("CONTA");
        sb.AppendLine($"  Nome: {Str(acc?["displayName"])}   Organização: {Str(acc?["organizationName"])}");
        sb.AppendLine($"  Cobrança: {Str(acc?["billingType"])}");
        sb.AppendLine();
        sb.AppendLine("CLAUDE CODE");
        sb.AppendLine($"  Modelo padrão: {Or(Str(settings?["model"]), "(padrão)")}   Tema: {Str(settings?["theme"])}   Atualizações: {Str(settings?["autoUpdatesChannel"])}");
        sb.AppendLine($"  Instalação: {Str(json?["installMethod"])}   Inicializações: {Num(json?["numStartups"])}   Primeiro uso: {Str(json?["firstStartTime"]).Split('T')[0]}");
        var upd = ReadJson(Path.Combine(claude, ".last-update-result.json"));
        sb.AppendLine($"  Versão: {Or(Str(upd?["version_to"]), "?")}   Última atualização: {Str(upd?["timestamp"]).Split('T')[0]} ({Str(upd?["outcome"])})");
        sb.AppendLine();

        var util = json?["cachedUsageUtilization"];
        var fetched = DateTimeOffset.FromUnixTimeMilliseconds((long)Num(util?["fetchedAtMs"])).LocalDateTime;
        var age = DateTime.Now - fetched;
        sb.AppendLine($"USO DO PLANO (cópia salva pelo Claude Code em {fetched:dd/MM HH:mm}, há {(age.TotalHours >= 1 ? $"{age.TotalHours:F0} h" : $"{age.TotalMinutes:F0} min")})");
        if (age.TotalHours >= 1) sb.AppendLine("  ⚠ Dado antigo: o valor ao vivo aparece no painel do Claude Code (Account & Usage).");
        var labels = new Dictionary<string, string> { ["session"] = "Sessão (5 h)", ["weekly_all"] = "Semana (tudo)", ["weekly_opus"] = "Semana (Opus)", ["weekly_sonnet"] = "Semana (Sonnet)" };
        foreach (var l in util?["utilization"]?["limits"] as JsonArray ?? new JsonArray())
        {
            var pct = Num(l?["percent"]);
            var reset = DateTime.TryParse(Str(l?["resets_at"]), out var r) ? r : (DateTime?)null;
            var label = labels.GetValueOrDefault(Str(l?["kind"]), Str(l?["kind"]));
            // depois do reinício o percentual salvo não vale mais: não mostrar como se fosse atual
            if (reset != null && reset < DateTime.Now) { sb.AppendLine($"  {label,-16} {Bar(0)}   ?%  reiniciou em {reset:dd/MM HH:mm}, depois dessa cópia"); continue; }
            sb.AppendLine($"  {label,-16} {Bar(pct)} {pct,3:F0}%{(reset == null ? "" : $"  reinicia {reset:dd/MM HH:mm}")}");
        }
        var rows = util?["utilization"]?["seven_day_breakdown"]?["rows"] as JsonArray;
        if (rows != null) sb.AppendLine("  Semana por produto: " + string.Join(" · ", rows.Select(x => $"{Str(x?["display_name"])} {Num(x?["percent"]):F0}%")));
        sb.AppendLine();

        sb.AppendLine("CONECTORES E MCP");
        sb.AppendLine("  Conectores do claude.ai vistos nas sessões (ficam na nuvem): " +
            (usage.Connectors.Count == 0 ? "nenhum" : string.Join(", ", usage.Connectors.OrderByDescending(c => c.Value).Select(c => $"{c.Key} ({c.Value} sessões)"))));
        foreach (var (srv, n) in usage.McpCalls.OrderByDescending(c => c.Value).Take(8)) sb.AppendLine($"  {n,5} chamadas  {srv}");
        sb.AppendLine();

        sb.AppendLine("RESUMO");
        foreach (var t in TabNames) sb.AppendLine($"  {t,-15} {s.Tabs[t].Count}");
        sb.AppendLine($"  Custo estimado nos últimos 30 dias (preço de API; no plano por assinatura você não paga isso): US$ {usage.Total30:F2}");
        sb.AppendLine($"  Espaço usado por {claude}: {diskTotal / 1048576.0:F1} MB (detalhes na aba Espaço)");
        var alerts = s.Tabs["Segurança"].GroupBy(a => a.Extra).Select(g => $"{g.Count()} {g.Key.ToLowerInvariant()}");
        sb.AppendLine($"  Alertas de segurança: {(s.Tabs["Segurança"].Count == 0 ? "nenhum" : string.Join(", ", alerts))}");
        sb.AppendLine();
        sb.AppendLine("SKILLS MAIS USADAS");
        var top = (json?["skillUsage"] as JsonObject ?? new JsonObject())
            .Select(kv => (kv.Key, n: Num(kv.Value?["usageCount"]))).OrderByDescending(x => x.n).Take(10);
        foreach (var (k, n) in top) sb.AppendLine($"  {n,4}×  {k}");
        sb.AppendLine();
        sb.AppendLine("E O RAG?");
        sb.AppendLine("  O Claude Code não tem RAG próprio. O que faz esse papel é a memória (CLAUDE.md e pastas memory/,");
        sb.AppendLine("  aba Memória) e servidores MCP que buscam em bases de conhecimento (aba MCP).");
        return sb.ToString();
    }

    public static Dictionary<string, string> Frontmatter(string text)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = text.Replace("\r", "").Split('\n');
        if (lines[0].Trim() != "---") return d;
        string? key = null;
        for (var i = 1; i < lines.Length; i++)
        {
            var l = lines[i];
            if (l.Trim() == "---") break;
            if (key != null && (l.StartsWith(' ') || l.StartsWith('\t'))) { d[key] = (d[key] + " " + l.Trim()).Trim(); continue; }
            var c = l.IndexOf(':');
            if (c <= 0) continue;
            key = l[..c].Trim();
            var v = l[(c + 1)..].Trim();
            d[key] = v is ">" or "|" or ">-" or "|-" ? "" : v.Trim('"', '\'');
        }
        return d;
    }

    // Esconde valores de env/headers (podem ter tokens)
    static string Mask(JsonNode? cfg)
    {
        if (cfg == null) return "";
        var copy = JsonNode.Parse(cfg.ToJsonString())!;
        foreach (var k in new[] { "env", "headers" })
            if (copy[k] is JsonObject o)
                foreach (var name in o.Select(x => x.Key).ToList()) o[name] = "••• (oculto)";
        return copy.ToJsonString(Pretty);
    }

    static bool OnPath(string cmd)
    {
        if (Path.IsPathRooted(cmd)) return File.Exists(cmd);
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);
        return dirs.Any(d => new[] { "", ".exe", ".cmd", ".bat" }.Any(ext => { try { return File.Exists(Path.Combine(d, cmd + ext)); } catch { return false; } }));
    }

    static string Uses(JsonNode? n) => n == null ? "" :
        $"{Num(n["usageCount"])}× · {DateTimeOffset.FromUnixTimeMilliseconds((long)Num(n["lastUsedAt"])).LocalDateTime:dd/MM/yyyy}";

    static JsonNode? ReadJson(string f)
    {
        try { return File.Exists(f) ? JsonNode.Parse(File.ReadAllText(f), documentOptions: new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) : null; }
        catch { return null; }
    }

    static string Read(string f) { try { return File.ReadAllText(f); } catch { return ""; } }
    static IEnumerable<string> Files(string dir, string pattern, bool recursive)
    {
        try { return Directory.Exists(dir) ? Directory.EnumerateFiles(dir, pattern, new EnumerationOptions { RecurseSubdirectories = recursive, IgnoreInaccessible = true }).ToList() : []; }
        catch { return []; }
    }
    static IEnumerable<string> Dirs(string dir) => Directory.Exists(dir) ? Directory.GetDirectories(dir) : [];
    static string Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : n is JsonValue ? n.ToJsonString() : "";
    static double Num(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) ? d : 0;
    static bool Bool(JsonNode? n) => n is JsonValue v && v.TryGetValue<bool>(out var b) && b;
    static string Or(string? a, string b) => string.IsNullOrWhiteSpace(a) ? b : a;
    static string FirstLine(string text) =>
        text.Replace("\r", "").Split('\n').Select(l => l.Trim().TrimStart('#', ' ')).FirstOrDefault(l => l != "" && l != "---") ?? "";
}
