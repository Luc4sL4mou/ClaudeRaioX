namespace ClaudeRaioX;

public record Rec(string Tab, string Name, string Why, string Url);

// Lista curada (links conferidos em 2026-11). Some da tela quando o item já está instalado. Edite à vontade.
public static class Recommendations
{
    const string Skills = "https://github.com/anthropics/skills/tree/main/skills/";
    const string Plugins = "https://github.com/anthropics/claude-plugins-official/tree/main/";
    const string Volt = "https://github.com/VoltAgent/awesome-claude-code-subagents/blob/main/categories/";
    const string Install = " · instalar: /plugin install {0}@claude-plugins-official";

    public static readonly Rec[] All =
    {
        new("Skills", "webapp-testing", "testa apps web locais com Playwright", Skills + "webapp-testing"),
        new("Skills", "mcp-builder", "guia para criar os seus próprios servidores MCP", Skills + "mcp-builder"),
        new("Skills", "frontend-design", "interfaces web com visual caprichado, sem cara de template", Skills + "frontend-design"),
        new("Skills", "skill-creator", "cria e testa skills novas", Skills + "skill-creator"),
        new("Skills", "superpowers", "coleção de skills de método: TDD, depuração sistemática, planos", "https://github.com/obra/superpowers"),
        new("Skills", "awesome-claude-skills", "lista curada para descobrir mais skills", "https://github.com/travisvn/awesome-claude-skills"),

        new("Agentes", "csharp-developer", "especialista em C#/.NET (WinForms, WPF, ASP.NET)", Volt + "02-language-specialists/csharp-developer.md"),
        new("Agentes", "game-developer", "para projetos de jogos e mods", Volt + "07-specialized-domains/game-developer.md"),
        new("Agentes", "debugger", "investiga bugs difíceis sem poluir a conversa principal", Volt + "04-quality-security/debugger.md"),
        new("Agentes", "security-auditor", "revisão de segurança do código", Volt + "04-quality-security/security-auditor.md"),
        new("Agentes", "powershell-5.1-expert", "scripts PowerShell do Windows (o shell padrão aqui)", Volt + "02-language-specialists/powershell-5.1-expert.md"),
        new("Agentes", "awesome-claude-code-subagents", "coleção com mais de 100 agentes prontos", "https://github.com/VoltAgent/awesome-claude-code-subagents"),
        new("Agentes", "wshobson/agents", "outra coleção grande de agentes e fluxos", "https://github.com/wshobson/agents"),
        new("Agentes", "documentação de subagentes", "como criar os seus próprios agentes", "https://code.claude.com/docs/en/sub-agents"),

        new("Plugins", "csharp-lsp", "o Claude navega no código C# (definições, erros); requer o language server" + string.Format(Install, "csharp-lsp"), Plugins + "plugins/csharp-lsp"),
        new("Plugins", "lua-lsp", "o mesmo para Lua (mods de jogos, Neovim, scripts)" + string.Format(Install, "lua-lsp"), Plugins + "plugins/lua-lsp"),
        new("Plugins", "pyright-lsp", "o mesmo para Python" + string.Format(Install, "pyright-lsp"), Plugins + "plugins/pyright-lsp"),
        new("Plugins", "code-review", "revisão de código com vários revisores" + string.Format(Install, "code-review"), Plugins + "plugins/code-review"),
        new("Plugins", "commit-commands", "atalhos para commit, push e PR" + string.Format(Install, "commit-commands"), Plugins + "plugins/commit-commands"),
        new("Plugins", "security-guidance", "avisa sobre padrões inseguros enquanto edita" + string.Format(Install, "security-guidance"), Plugins + "plugins/security-guidance"),
        new("Plugins", "claude-md-management", "mantém os seus CLAUDE.md organizados" + string.Format(Install, "claude-md-management"), Plugins + "plugins/claude-md-management"),
        new("Plugins", "playwright", "controla um navegador para testar sites e apps web" + string.Format(Install, "playwright"), Plugins + "external_plugins/playwright"),
        new("Plugins", "context7", "documentação atualizada de bibliotecas: o mais perto de RAG pronto" + string.Format(Install, "context7"), Plugins + "external_plugins/context7"),
        new("Plugins", "claude-plugins-official", "marketplace oficial com todos os plugins", "https://github.com/anthropics/claude-plugins-official"),
    };

    public static IEnumerable<Rec> For(string tab, Snapshot s)
    {
        var installed = s.Tabs.GetValueOrDefault(tab) ?? [];
        return All.Where(r => r.Tab == tab && !installed.Any(i =>
            i.Name.Equals(r.Name, StringComparison.OrdinalIgnoreCase)
            || i.Name.EndsWith(":" + r.Name, StringComparison.OrdinalIgnoreCase)
            || i.Name.StartsWith(r.Name + "@", StringComparison.OrdinalIgnoreCase)));
    }
}
