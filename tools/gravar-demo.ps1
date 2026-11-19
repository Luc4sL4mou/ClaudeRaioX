# Grava um vídeo de demonstração do Claude Raio-X com dados 100% fictícios.
# Cria demo\home (uma pasta .claude falsa), abre o app com RAIOX_HOME apontando para ela,
# navega pelas abas via UI Automation e grava a janela com o ffmpeg.
# Uso: powershell -ExecutionPolicy Bypass -File tools\gravar-demo.ps1 [-Musica arquivo.mp3] [-Volume 0.3]   (saída: docs\media\demo.mp4)
param([string]$Musica, [double]$Volume = 0.3)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$demo = Join-Path $root 'demo'
$home2 = Join-Path $demo 'home'
$claude = Join-Path $home2 '.claude'
$out = Join-Path $root 'docs\media\demo.mp4'
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Put($rel, $text, $base = $claude) {
    $f = Join-Path $base $rel
    New-Item -ItemType Directory -Force (Split-Path $f) | Out-Null
    [IO.File]::WriteAllText($f, $text, $utf8)
}
function J($o) { $o | ConvertTo-Json -Depth 20 -Compress }

if (Test-Path $demo) { Remove-Item -Recurse -Force $demo }
$now = Get-Date
$ms = [DateTimeOffset]::new($now).ToUnixTimeMilliseconds()

# --- projetos fictícios ---
$projs = @('LojaVirtual', 'ApiPagamentos', 'BlogPessoal', 'AppFinancas') | ForEach-Object { Join-Path $demo "Projetos\$_" }
foreach ($p in $projs) { New-Item -ItemType Directory -Force $p | Out-Null }
Put 'CLAUDE.md' "# Loja Virtual`n`nNext.js + Postgres. Rodar npm test antes de commitar." $projs[0]
Put '.mcp.json' (J @{ mcpServers = @{ postgres = @{ command = 'npx'; args = @('-y', '@modelcontextprotocol/server-postgres', 'postgresql://localhost/loja') } } }) $projs[0]
Put '.claude\settings.json' (J @{ permissions = @{ allow = @('Bash(npm test:*)', 'Bash(npm run lint)'); deny = @() } }) $projs[0]
Put 'CLAUDE.md' "# API de Pagamentos`n`nGo 1.24. Nunca logar dados de cartão." $projs[1]
Put '.claude\agents\revisor-seguranca.md' "---`nname: revisor-seguranca`ndescription: Revisa mudanças em rotas de pagamento procurando vazamento de dados`nmodel: opus`n---`n" $projs[1]

$projNodes = [ordered]@{}
$i = 0
foreach ($p in $projs + @('D:\Antigo\ProjetoArquivado')) {
    $i++
    $projNodes[$p] = @{ lastCost = [math]::Round(0.8 + $i * 1.7, 2); lastTotalInputTokens = 120000 * $i; lastTotalOutputTokens = 18000 * $i; lastLinesAdded = 40 * $i; lastLinesRemoved = 12 * $i }
}
$projNodes[$projs[3]].mcpServers = @{ 'planilhas' = @{ type = 'http'; url = 'https://mcp.exemplo.com/planilhas' } }

# --- .claude.json ---
$skillUse = @{ 'deploy-site' = 42; 'revisar-pr' = 31; 'gerar-changelog' = 17; 'traduzir-ui' = 9; 'relatorio-semanal' = 6; 'code-review:review' = 23 }
$su = @{}; foreach ($k in $skillUse.Keys) { $su[$k] = @{ usageCount = $skillUse[$k]; lastUsedAt = $ms - (Get-Random -Max 5) * 86400000 } }
$claudeJson = [ordered]@{
    numStartups = 318; installMethod = 'native'; firstStartTime = '2026-03-02T14:10:00.000Z'
    oauthAccount = @{ displayName = 'Usuário Demo'; organizationName = 'Empresa Exemplo'; billingType = 'stripe_subscription'; emailAddress = 'demo@exemplo.com' }
    mcpServers = @{
        github = @{ command = 'npx'; args = @('-y', '@modelcontextprotocol/server-github'); env = @{ GITHUB_TOKEN = 'ghp_EXEMPLO_FICTICIO_123' } }
        memoria = @{ command = 'npx'; args = @('-y', '@modelcontextprotocol/server-memory') }
        'busca-docs' = @{ command = 'docs-search-mcp'; args = @('--porta', '7700') }
    }
    projects = $projNodes
    skillUsage = $su
    pluginUsage = @{ 'code-review@claude-plugins-official' = @{ usageCount = 23; lastUsedAt = $ms - 86400000 } }
    cachedUsageUtilization = @{
        fetchedAtMs = $ms - 12 * 60000
        utilization = @{
            limits = @(
                @{ kind = 'session'; percent = 34; resets_at = $now.AddHours(3).ToString('o') },
                @{ kind = 'weekly_all'; percent = 52; resets_at = $now.AddDays(4).ToString('o') },
                @{ kind = 'weekly_opus'; percent = 18; resets_at = $now.AddDays(4).ToString('o') })
            seven_day_breakdown = @{ rows = @(@{ display_name = 'Claude Code'; percent = 41 }, @{ display_name = 'claude.ai'; percent = 11 }) }
        }
    }
}
Put '.claude.json' (J $claudeJson) $home2

# --- configurações, memória, atualização ---
Put 'settings.json' (J @{
    model = 'opus'; theme = 'dark'; autoUpdatesChannel = 'latest'
    enabledPlugins = @{ 'code-review@claude-plugins-official' = $true; 'frontend-design@claude-plugins-official' = $false }
    permissions = @{ allow = @('Bash(git status)', 'Bash(git diff:*)', 'Bash(npm test:*)', 'Read(**)', 'WebSearch', 'Bash(ls:*)'); deny = @('Bash(rm -rf:*)', 'Read(.env)') }
    hooks = @{
        PostToolUse = @(@{ matcher = 'Edit|Write'; hooks = @(@{ type = 'command'; command = 'npx prettier --write "$CLAUDE_FILE_PATHS"' }) })
        Stop = @(@{ hooks = @(@{ type = 'command'; command = 'powershell -c [console]::beep(880,200)' }) })
    }
})
Put 'CLAUDE.md' "# Preferências gerais`n`n- Responder em português.`n- Commits pequenos e com mensagem clara.`n- Testes antes de abrir PR."
Put '.last-update-result.json' (J @{ version_to = '2.4.1'; timestamp = $now.AddDays(-2).ToString('o'); outcome = 'success' })

# --- skills, agentes, comandos do usuário ---
$skills = @{
    'deploy-site' = 'Publica o site estático na hospedagem e confere se a página abriu'
    'revisar-pr' = 'Revisa um pull request procurando bugs, testes faltando e nomes confusos'
    'gerar-changelog' = 'Monta o CHANGELOG a partir dos commits desde a última tag'
    'traduzir-ui' = 'Traduz os textos da interface mantendo as chaves e o tom'
    'relatorio-semanal' = 'Resume o que foi feito na semana a partir do git e das issues'
}
foreach ($k in $skills.Keys) { Put "skills\$k\SKILL.md" "---`nname: $k`ndescription: $($skills[$k])`n---`n`n# $k`n`nPassos da skill." }
Put 'skills\relatorio-semanal\coletar.py' "print('coletando commits')"
$agents = @{ 'arquiteto' = @('Planeja mudanças grandes antes de escrever código', 'opus'); 'testador' = @('Escreve e roda testes para o código alterado', 'sonnet'); 'documentador' = @('Atualiza README e comentários depois de uma mudança', 'haiku') }
foreach ($k in $agents.Keys) { Put "agents\$k.md" "---`nname: $k`ndescription: $($agents[$k][0])`nmodel: $($agents[$k][1])`n---`n" }
Put 'commands\resumo.md' "---`ndescription: Resume a conversa atual em 5 tópicos`n---`n"
Put 'commands\git\commit.md' "# Faz commit com mensagem no padrão Conventional Commits"
Put 'commands\git\pr.md' "# Abre um pull request com descrição e checklist"

# --- plugins ---
$plug = Join-Path $claude 'plugins\cache\claude-plugins-official\code-review\1.3.0'
Put '.claude-plugin\plugin.json' (J @{ name = 'code-review'; version = '1.3.0' }) $plug
Put 'skills\review\SKILL.md' "---`nname: review`ndescription: Revisão de código do diff atual com níveis de rigor`n---`n" $plug
Put 'agents\bug-hunter.md' "---`nname: bug-hunter`ndescription: Procura bugs de lógica no diff`nmodel: sonnet`n---`n" $plug
Put 'hooks\hooks.json' (J @{ hooks = @{ SessionStart = @(@{ hooks = @(@{ type = 'command'; command = 'echo revisor pronto' }) }) } }) $plug
$plug2 = Join-Path $claude 'plugins\cache\claude-plugins-official\frontend-design\0.9.2'
Put '.claude-plugin\plugin.json' (J @{ name = 'frontend-design' }) $plug2
Put 'plugins\installed_plugins.json' (J @{ plugins = @{
    'code-review@claude-plugins-official' = @(@{ installPath = $plug; version = '1.3.0'; installedAt = '2026-09-14T10:00:00Z' })
    'frontend-design@claude-plugins-official' = @(@{ installPath = $plug2; version = '0.9.2'; installedAt = '2026-10-01T10:00:00Z' })
} })

# --- histórico de sessões: 30 dias de uso fictício ---
$rnd = New-Object Random 7
$models = @('claude-opus-5-5', 'claude-sonnet-5-5', 'claude-haiku-5-5')
$tools = @('mcp__github__create_pull_request', 'mcp__memoria__search', 'mcp__claude_ai_Google_Drive__search', 'mcp__claude_ai_Notion__fetch', 'Bash', 'Edit')
$n = 0
for ($d = 29; $d -ge 0; $d--) {
    if ($rnd.NextDouble() -lt 0.2) { continue }  # alguns dias sem uso
    $day = $now.Date.AddDays(-$d).AddHours(10)
    foreach ($s in 1..($rnd.Next(1, 4))) {
        $p = $projs[$rnd.Next($projs.Count)]
        $slug = [regex]::Replace($p, '[^A-Za-z0-9]', '-')
        $lines = foreach ($m in 1..($rnd.Next(4, 12))) {
            $n++
            $model = $models[[math]::Min(2, [int][math]::Floor($rnd.NextDouble() * $rnd.NextDouble() * 3))]
            $tool = $tools[$rnd.Next($tools.Count)]
            J @{ timestamp = $day.AddMinutes($m * 3).ToUniversalTime().ToString('o'); type = 'assistant'
                message = @{ id = "msg_$n"; model = $model; role = 'assistant'; content = @(@{ type = 'tool_use'; name = $tool })
                    usage = @{ input_tokens = $rnd.Next(2000, 30000); output_tokens = $rnd.Next(500, 9000); cache_read_input_tokens = $rnd.Next(50000, 400000); cache_creation_input_tokens = $rnd.Next(5000, 60000) } } }
        }
        Put "projects\$slug\sessao-$d-$s.jsonl" (($lines -join "`n") + "`n")
    }
}
$slug0 = [regex]::Replace($projs[0], '[^A-Za-z0-9]', '-')
Put "projects\$slug0\memory\MEMORY.md" "- [Deploy](deploy.md) — o deploy roda pelo GitHub Actions"
Put "projects\$slug0\memory\deploy.md" "---`nname: deploy`ndescription: O deploy da loja roda pelo GitHub Actions na branch main`n---`n"
Put 'file-history\x.bin' ('x' * 300000)
Put 'shell-snapshots\s.sh' ('# snapshot' * 2000)
Write-Host "Dados fictícios em $home2"

# --- abre o app com os dados fictícios ---
$exe = Join-Path $root 'ClaudeRaioX.exe'
$temaFile = Join-Path $root 'tema.txt'
$temaAntes = if (Test-Path $temaFile) { Get-Content $temaFile -Raw } else { $null }
[IO.File]::WriteAllText($temaFile, 'Cyberpunk', $utf8)
$env:RAIOX_HOME = $home2
$app = Start-Process $exe -PassThru
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [Windows.Automation.AutomationElement]
$win = $null
for ($t = 0; $t -lt 40 -and -not $win; $t++) {
    Start-Sleep -Milliseconds 250
    $app.Refresh()
    if ($app.MainWindowHandle -ne 0) { $win = $A::FromHandle($app.MainWindowHandle) }
}
if (-not $win) { throw 'Janela do app não abriu' }
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class W { [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); }
'@
[W]::SetForegroundWindow($app.MainWindowHandle) | Out-Null
Start-Sleep 3  # espera a leitura terminar

function Find($type, $name) {
    $c = New-Object Windows.Automation.AndCondition(
        (New-Object Windows.Automation.PropertyCondition($A::ControlTypeProperty, $type)),
        (New-Object Windows.Automation.PropertyCondition($A::NameProperty, $name)))
    $win.FindFirst([Windows.Automation.TreeScope]::Descendants, $c)
}
function Tab($prefix, $wait = 2.5) {
    $all = $win.FindAll([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition($A::ControlTypeProperty, [Windows.Automation.ControlType]::TabItem)))
    $t = $all | Where-Object { $_.Current.Name -like "$prefix*" } | Select-Object -First 1
    $t.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $script:curTab = $t.Current.Name
    Start-Sleep -Milliseconds ($wait * 1000)
}
function Pick($index, $wait = 2) {
    # a lista certa é a que está dentro da página da aba ativa (as outras abas também expõem as suas)
    $page = Find ([Windows.Automation.ControlType]::Pane) $script:curTab
    $list = $page.FindFirst([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition($A::ControlTypeProperty, [Windows.Automation.ControlType]::Table)))
    $items = $list.FindAll([Windows.Automation.TreeScope]::Children, (New-Object Windows.Automation.PropertyCondition($A::ControlTypeProperty, [Windows.Automation.ControlType]::ListItem)))
    if ($items.Count -gt $index) { $items[$index].GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select() }
    Start-Sleep -Milliseconds ($wait * 1000)
}

# --- grava ---
$ffmpeg = (Get-Command ffmpeg).Source
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null
$raw = Join-Path $demo 'bruto.mkv'
$psi = New-Object Diagnostics.ProcessStartInfo $ffmpeg, "-y -loglevel error -f gdigrab -framerate 30 -draw_mouse 0 -i `"title=Claude Raio-X`" -c:v libx264 -preset ultrafast -qp 0 `"$raw`""
$psi.UseShellExecute = $false; $psi.RedirectStandardInput = $true; $psi.CreateNoWindow = $true
$rec = [Diagnostics.Process]::Start($psi)
try {
    Start-Sleep 4                      # Visão geral
    Tab 'Uso diário' 2; Pick 0 2.5
    Tab 'Skills'; Pick 1
    Tab 'Agentes'; Pick 0
    Tab 'Comandos' 2
    Tab 'MCP'; Pick 0
    Tab 'Plugins' 2
    Tab 'Hooks'; Pick 1
    Tab 'Memória'; Pick 0
    Tab 'Projetos' 2.5
    Tab 'Segurança'; Pick 0 2.5
    Tab 'Espaço' 2.5
    Tab 'Skills' 1
    $search = $win.FindAll([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition($A::ControlTypeProperty, [Windows.Automation.ControlType]::Edit))) | Where-Object { -not $_.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.IsReadOnly } | Select-Object -First 1
    foreach ($q in @('r', 're', 'rev', 'revi')) { $search.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($q); Start-Sleep -Milliseconds 300 }
    Start-Sleep 2
    $search.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('')
    Start-Sleep 1
    $themeBtn = $win.FindAll([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition($A::ControlTypeProperty, [Windows.Automation.ControlType]::Button))) | Where-Object { $_.Current.Name -like 'Tema:*' } | Select-Object -First 1
    $themeBtn.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep 3                      # tema Matrix: passeio por algumas abas
    Tab 'Uso diário' 2; Pick 2 2.5
    Tab 'Agentes'; Pick 1
    Tab 'MCP'; Pick 2
    Tab 'Segurança' 2.5
    Tab 'Visão geral' 4
}
finally {
    $rec.StandardInput.Write("q"); $rec.StandardInput.Close()  # "q" encerra o ffmpeg fechando o arquivo
    if (-not $rec.WaitForExit(15000)) { $rec.Kill() }
    Stop-Process -Id $app.Id -ErrorAction SilentlyContinue
    if ($null -ne $temaAntes) { [IO.File]::WriteAllText($temaFile, $temaAntes, $utf8) }
}

# H.264 compatível com qualquer player (dimensões pares, yuv420p)
if ($Musica) {
    # música de fundo cortada no fim do vídeo, com fade out de 2 s
    $dur = [double](& (Join-Path (Split-Path $ffmpeg) 'ffprobe.exe') -v error -show_entries format=duration -of csv=p=0 $raw)
    $fade = [string]::Format([Globalization.CultureInfo]::InvariantCulture, 'volume={0},afade=t=out:st={1}:d=2', $Volume, $dur - 2)
    & $ffmpeg -y -loglevel error -i $raw -i $Musica -vf 'scale=trunc(iw/2)*2:trunc(ih/2)*2,format=yuv420p' -c:v libx264 -crf 22 -preset slow -af $fade -map 0:v -map 1:a -c:a aac -b:a 192k -shortest -movflags +faststart $out
} else {
    & $ffmpeg -y -loglevel error -i $raw -vf 'scale=trunc(iw/2)*2:trunc(ih/2)*2,format=yuv420p' -c:v libx264 -crf 22 -preset slow -movflags +faststart $out
}
Write-Host "Vídeo: $out"
