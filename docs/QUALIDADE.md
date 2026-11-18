# Plano de Qualidade: Claude Raio-X

App desktop WinForms (.NET 10), **somente leitura**: mostra o que o Claude Code tem instalado na máquina lendo `~/.claude` e `~/.claude.json`. Não é web, então não usa Cypress: a lógica fica em `src/Scanner.cs` (sem UI) e é testada pelo `--selftest`.

## Fluxos críticos e critérios de aceite

```gherkin
# language: pt
Funcionalidade: Inventário do Claude Code

  Cenário: Listar skills com descrição e uso
    Dado uma skill em ".claude/skills/minha/SKILL.md" com descrição em várias linhas
    E "skillUsage" com 3 usos para "minha" no ".claude.json"
    Quando o app lê a pasta do usuário
    Então a aba Skills mostra "minha" com a descrição em uma linha e "3×"

  Cenário: Não expor segredos de MCP
    Dado um servidor MCP com a variável de ambiente "TOKEN" = "segredo123"
    Quando abro o servidor na aba MCP
    Então o detalhe mostra "••• (oculto)" e não mostra "segredo123"

  Cenário: Agentes, comandos, hooks e memória
    Dado um agente, um comando em subpasta, um hook "Stop" e um CLAUDE.md global
    Quando o app lê a pasta do usuário
    Então cada um aparece na sua aba com nome, origem e resumo

  Cenário: Pasta ausente ou arquivo corrompido
    Dado que ".claude.json" não existe ou é JSON inválido
    Quando o app lê a pasta do usuário
    Então o app abre sem erro e as abas ficam vazias

  Cenário: Custo por dia sem contar em dobro
    Dado uma resposta do "claude-opus-5-5" com 1.000.000 tokens de entrada repetida em 2 linhas do histórico
    Quando o app lê o histórico
    Então o dia mostra "US$ 4,00" (contada uma vez) e o gráfico de 30 dias inclui esse valor

  Cenário: Alertas de segurança
    Dado um MCP com "TOKEN" em texto puro e uma skill que traz "run.py"
    Então a aba Segurança mostra um alerta "Média" para o MCP e "Baixa" para a skill, sem exibir o segredo

  Cenário: Recomendações
    Dado que o plugin "code-review" já está instalado
    Então ele não aparece entre os recomendados, e cada recomendado abre o link com um clique

  Cenário: Exportar relatório
    Quando clico em "Exportar relatório" e escolho onde salvar
    Então um HTML com todas as abas é salvo e aberto no navegador, sem segredos

  Cenário: Filtrar
    Quando digito "pentest" no filtro
    Então todas as abas mostram só os itens que contêm "pentest" e o contador na aba é atualizado
```

## Definição de pronto
- `dotnet build` sem avisos novos; `ClaudeRaioX.exe --selftest` retorna 0.
- Nunca escreve em `~/.claude`; valores de `env`/`headers` sempre mascarados.
- Leitura fora da thread da interface; janela não congela.
- Navegável por teclado (Tab, setas, F5, Ctrl+F); segue tema claro/escuro do Windows.
- Exe da raiz e atalho atualizados.

## Metas
- Scanner: cada fonte de dados nova ganha uma checagem no `--selftest`.
- Funções ≤ ~50 linhas, exceto `Scanner.Scan` (sequência linear de fontes; dividir se passar de ~150).
- Abrir e ler tudo em < 5 s na máquina do usuário (medido: ~3–4 s com 742 MB de histórico).
- Links de recomendação conferidos (HTTP 200) sempre que a lista mudar.

## Pirâmide
Muitos: checagens do `--selftest` (lógica pura sobre pasta falsa). Poucos: teste manual da janela (abrir, filtrar, duplo clique) antes de cada release.

## Portões
Local, antes do commit: `dotnet build -c Release` + `--selftest`. Sem CI (projeto local).
