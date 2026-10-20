# Validação no Windows 11 antes da distribuição

Este roteiro exige uma máquina Windows 11 x64, preferencialmente descartável. Não foi executado no ambiente Linux de desenvolvimento. O smoke test automatizado cobre inicialização e carregamento, não toda a interação abaixo.

- [ ] Extrair o ZIP inteiro e abrir `NEXUS OPTIMIZER.exe` por duplo clique, como usuário padrão. Nenhum UAC na abertura.
- [ ] Percorrer nove páginas em escalas de 100%, 150% e 200%; testar redimensionamento e temas claro/escuro.
- [ ] Conferir RAM, espaço livre, plano e CPU com as ferramentas nativas. CPU varia entre amostras; a primeira não apresenta percentual inventado. Sensores não integrados dizem “Não disponível”.
- [ ] Verificar atualização dos gráficos, pausa em páginas inativas e pausa ao perder foco. Conferir CPU/RAM do próprio NEXUS no Gerenciador de Tarefas.
- [ ] Analisar, desmarcar itens e cancelar confirmação. Nada deve mudar. Testar lote aplicável e restaurá-lo em ordem inversa.
- [ ] Alternar entre planos existentes e desfazer pelo histórico. Em máquina com só um plano, opções inexistentes devem falhar com mensagem.
- [ ] Alterar o plano fora do aplicativo e tentar desfazer um ajuste: a mudança externa deve ser preservada com aviso de conflito.
- [ ] Testar aceleração do mouse com backup; restaurar e comparar a configuração original. Se já desativada, o comando deve ser idempotente.
- [ ] Desativar/ativar uma entrada Run descartável do usuário; conferir a cópia em DisabledStartup e a restauração. Entradas da máquina não devem ser alteradas pelo NEXUS.
- [ ] Salvar preferências, fechar e reabrir. Conferir tema, destaque, intervalo, notificações, jogo e opção de início com Windows. Testar a restauração da entrada própria pelo histórico.
- [ ] Analisar temporários descartáveis antigos/recentes. Apenas antigos entram na lista; arquivos alterados após análise e em uso são ignorados. Cancelamento mostra exclusões já concluídas.
- [ ] Tentar links/junções para pasta pessoal: a limpeza não deve percorrê-los. Não use documentos importantes para testar exclusões permanentes.
- [ ] Cancelar o esvaziamento da lixeira nas duas confirmações e conferir preservação. Testar limpeza apenas com itens descartáveis.
- [ ] Selecionar um processo de um aplicativo descartável. Conferir nome/PID na confirmação; solicitar fechamento, sem kill automático ou alteração de prioridade.
- [ ] Abrir ferramentas nativas; cancelar UAC de SFC/DISM e conferir feedback. Executá-las numa máquina de teste, acompanhar andamento nativo e conferir código de saída/reinicialização.
- [ ] Encerrar uma instância de teste entre registro e conclusão de uma alteração; reabrir, localizar o registro interrompido e restaurá-lo. Não fazer este teste no computador de produção.
- [ ] Conferir ausência de conexões externas em execução normal e revisar o relatório exportado antes de compartilhar.

Registre build do Windows, hardware, escala, ações, resultados e logs. Ausência de falhas no build e nos testes Core não substitui esta validação.
