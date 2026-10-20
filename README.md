# NEXUS OPTIMIZER

Aplicativo desktop em português para Windows 11 x64. MVP 0.1.0 em C# / .NET 10 LTS, WPF, WPF UI (Fluent) e CommunityToolkit.Mvvm. Sem telemetria, anúncios ou requisições externas na execução normal. Dependências são obtidas somente durante o desenvolvimento.

## Download

- [Baixar aplicativo portátil Windows x64 (ZIP, aproximadamente 69 MB)](https://github.com/AndreiTav/appotimizacao/raw/refs/heads/main/downloads/NEXUS-OPTIMIZER-0.1.0-win-x64.zip)
- [Baixar somente o código-fonte (ZIP)](https://github.com/AndreiTav/appotimizacao/raw/refs/heads/main/downloads/NEXUS-OPTIMIZER-0.1.0-source.zip)
- [SHA-256 do pacote portátil](downloads/NEXUS-OPTIMIZER-0.1.0-win-x64.zip.sha256)

Se o repositório for privado, entre no GitHub com uma conta que tenha acesso antes de baixar. Se o download direto não abrir, acesse a [pasta downloads](downloads) e use o botão de download do arquivo ZIP no GitHub. Extraia o pacote inteiro; o `.exe` isolado não funciona sem suas DLLs.

## Executar no Windows

1. Extraia **todo** o pacote `NEXUS-OPTIMIZER-0.1.0-win-x64.zip` para uma pasta fixa do usuário.
2. Abra **NEXUS OPTIMIZER.exe** por duplo clique. Não é necessário instalar .NET, Node, Python ou abrir um terminal.
3. Use **ANALISAR MEU PC**. Leia cada recomendação e confirme as alterações desejadas.

É uma distribuição portátil self-contained; mantenha DLLs e demais arquivos juntos. Não há instalador ou assinatura digital nesta versão. O manifesto inicia sem elevação (`asInvoker`). SFC e DISM usam o UAC apenas quando acionados.

## O que está implementado

| Módulo | Comportamento real |
| --- | --- |
| Dashboard | CPU via `GetSystemTimes`, RAM via `GlobalMemoryStatusEx`, CPU/frequência e primeiro adaptador de vídeo via WMI; espaço das unidades fixas, uptime, processos e plano ativo |
| Análise inteligente | Recomendações condicionais para plano Equilibrado, aceleração do ponteiro, inicialização e pouco espaço; atalho para Modo de Jogo; seleção padrão somente de baixo risco |
| Perfis gamer | Equilibrado; Gamer com plano escolhido pelo usuário; Alto desempenho somente se já existir. No MVP, os perfis modificam **apenas o plano de energia** |
| Mouse | Consulta e alteração pela API `SystemParametersInfo`, com backup exato dos três valores e verificação após a escrita |
| Jogo | Seleção manual de `.exe` com verificação de cabeçalho, persistência do caminho e acesso às preferências gráficas nativas; não executa nem altera o jogo |
| Limpeza | Pré-análise de temporários do usuário/Windows e miniaturas; somente arquivos sem alteração há mais de 24 horas. Exclusão apenas da lista analisada e das categorias selecionadas |
| Lixeira | Consulta nativa de tamanho/itens; confirmação expressa separada, seguida da confirmação do próprio Windows |
| Inicialização | Entradas Run do usuário podem ser movidas para `HKCU\Software\NexusOptimizer\DisabledStartup`; comandos e tipos originais são preservados. Entradas da máquina e pastas são somente leitura |
| Monitoramento | Histórico de até 60 amostras de CPU/RAM e até 12 processos por CPU/RAM. Pausa fora do Dashboard/Monitoramento e quando a janela perde o foco |
| Ferramentas | Gerenciador de Tarefas, energia, dispositivos, informações, discos, eventos, inicialização, Modo de Jogo, gráficos, DirectX, Proteção do Sistema, SFC e DISM |
| Histórico | Diário antes da escrita, leitura de confirmação, idempotência, reversão individual ou em ordem inversa, recuperação de operações interrompidas e rollback em falhas/cancelamento |
| Preferências | Claro/escuro, três destaques, intervalo de 1–30 s, notificações internas, entrada própria no logon, logs locais e exportação JSON |

Todas as alterações persistentes do NEXUS pedem confirmação e são registradas para reversão. No lote “Otimizar agora”, se uma etapa falhar, as etapas concluídas são revertidas em ordem inversa; falhas de reversão ficam no histórico. Uma configuração modificada externamente bloqueia a restauração comum. A recuperação de um registro interrompido exige confirmação e usa seu backup original.

Limpezas e ferramentas nativas são ações separadas: arquivos excluídos não têm reversão; SFC pode reparar arquivos do Windows e não é coberto pelo diário de ajustes. As ferramentas exibem seu andamento real em suas próprias janelas. Cancelar a espera de SFC/DISM no NEXUS não encerra a ferramenta administrativa. Código 3010 informa necessidade de reinicialização; outros códigos de falha são exibidos.

## Limites explícitos do MVP

- GPU (utilização, VRAM em uso, temperatura), temperatura da CPU e atividade de disco são **Não disponível**. Não há sensores simulados, drivers adicionais ou leitura de sensores não suportados.
- Não há medição de FPS, detecção automática de bibliotecas de jogos, encerramento forçado de processos, limpeza de caches de navegadores/shaders, gerenciamento de tarefas agendadas ou instalador.
- O pedido de fechar aplicativo envia `CloseMainWindow`, após confirmação de nome/PID e conferência de identidade, sessão e processo crítico. O aplicativo pode recusar o pedido.
- “Entrada presente” na inicialização indica presença em Run, não o estado de mecanismos como StartupApproved, políticas corporativas ou gerenciadores de terceiros. Para esses estados, use o gerenciamento nativo. O fabricante permanece “Não identificado” quando não foi resolvido de forma confiável.
- Proteção do Sistema é aberta na ferramenta nativa, que verifica suporte e permite criar pontos. Não há criação automática de pontos nem promessa de substituir backups específicos.
- Planos inexistentes não são criados; máquinas com Modern Standby podem oferecer somente Equilibrado. Falta de permissão é informada, sem elevação abrangente.
- A interface mantém confirmação obrigatória para ações sensíveis. Alterações do sistema não são executadas ao abrir páginas.

Esta entrega é um MVP compilável e testado no que o ambiente permite. A validação interativa no Windows 11, incluindo DPI, WMI em diferentes hardwares e UAC, continua necessária antes de uma distribuição de produção.

## Compilar e testar no Windows

Requisitos: Windows 11 x64, SDK .NET **10.0.402** (fixado em `global.json`; atualizações de patch aceitas) e PowerShell. Não é necessário Visual Studio; VS com suporte ao .NET 10/WPF é opcional.

```powershell
./scripts/build.ps1
```

O script restaura com lockfiles, compila, executa a suíte Core e a suíte nativa Windows, publica a distribuição portátil e abre um smoke test somente de leitura. O teste usa um diretório temporário separado, percorre nove páginas, consulta memória/planos e gera `artifacts/test-results/windows-ui-smoke.json`. Não aplica otimizações ou limpeza. Para compilar sem abrir a interface (ambiente sem desktop), use `-SkipSmokeTest`; isso não valida a inicialização gráfica.

Saídas:

```text
artifacts/win-x64/NEXUS OPTIMIZER.exe
artifacts/NEXUS-OPTIMIZER-0.1.0-win-x64.zip
artifacts/NEXUS-OPTIMIZER-0.1.0-win-x64.zip.sha256
artifacts/test-results/*.trx
```

## Desenvolvimento na nuvem / Linux

```bash
bash scripts/setup-cloud.sh
source scripts/env-cloud.sh
dotnet test tests/NexusOptimizer.Tests -c Release --logger trx
dotnet build NexusOptimizer.sln -c Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet publish src/NexusOptimizer.App -c Release -r win-x64 --self-contained true \
  --no-restore -p:PublishSingleFile=false -p:PublishTrimmed=false -m:1 -o artifacts/win-x64
```

O setup instala o SDK Linux x64 em `/workspace/.dotnet` e verifica SHA-512 antes de extrair. TLS, assinaturas de pacotes e auditoria NuGet não são desabilitados. Os comandos não requerem um novo worktree: tarefas de nuvem já são isoladas; use o checkout existente. Dependendo da sandbox, o .NET precisa de permissão para seus mutexes em `~/.dotnet` e os metadados NuGet em `~/.local/share/NuGet`; não é necessário alterar `HOME`.

Linux permite executar os testes Core e compilar/publicar para Windows. **Não executa WPF, testes nativos Windows nem UAC.** O workflow GitHub Actions fornecido cobre Linux e Windows Server 2025; está preparado, mas sua execução não é presumida. A validação final de Windows 11 segue o [roteiro manual](docs/VALIDACAO-WINDOWS.md).

## Estrutura e segurança

```text
src/NexusOptimizer.Core/      Modelos, persistência atômica, diário, limpeza e runner
src/NexusOptimizer.Windows/   P/Invoke, WMI, planos, mouse, registro e ferramentas
src/NexusOptimizer.App/       WPF/Fluent, MVVM, nove páginas e comandos assíncronos
tests/NexusOptimizer.Tests/   Testes multiplataforma com arquivos reais e falhas injetadas
tests/NexusOptimizer.Windows.Tests/  Integração com APIs e exclusão nativa no Windows
scripts/                     Setup, build, publicação e smoke test
```

Dados: `%LOCALAPPDATA%\NexusOptimizer\preferences.json`, `history.json` e `logs\`. Não apague o histórico antes de restaurar ajustes. Arquivo de histórico inválido bloqueia mutações, preservando a evidência. Preferências inválidas são copiadas com sufixo `.corrupt-*` antes de usar os padrões. Prefira manter uma cópia desse diretório antes de mover a instalação.

A limpeza recusa reparse points, junções, links e caminhos fora da regra; não percorre diretórios ligados. Antes de excluir no Windows, abre os ancestrais sem compartilhamento de exclusão, abre o arquivo por handle, verifica atributos/tamanho/data novamente e marca esse mesmo handle para exclusão. Arquivos protegidos, compartilhados, recentes, alterados ou sem permissão são ignorados com contagem e erros. O tamanho recuperado é o tamanho lógico dos arquivos removidos, não uma promessa de aumento idêntico de espaço físico.

Comandos da interface são IDs predefinidos, resolvidos para ferramentas do diretório do sistema. Argumentos usam `ArgumentList`; não há terminal com entrada arbitrária. Acesso ao registro fica restrito às entradas de usuário gerenciadas. Nenhuma ação desativa Defender, Windows Update, segurança, serviços essenciais, HPET ou BCD.

As bibliotecas externas mantêm suas próprias licenças; referências e versões estão nos projetos e em `packages.lock.json`.
