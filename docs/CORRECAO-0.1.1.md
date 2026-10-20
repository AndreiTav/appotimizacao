# Correção 0.1.1

A versão 0.1.0 apresentava uma falha de interface ao criar as barras de ocupação das unidades: `ProgressBar.Value` herdava o comportamento de associação bidirecional, mas `DriveSnapshot.UsedPercent` é uma propriedade calculada somente de leitura. A mensagem de exceção mostrada pelo usuário corresponde a essa incompatibilidade.

O tratamento global abria um `MessageBox` para cada exceção. As caixas modais reentravam no processamento da interface e permitiam a repetição das falhas, formando uma pilha de janelas. Esse era um defeito do aplicativo; a mensagem não é um mecanismo de acesso remoto.

Alterações:

- Associações de progresso explicitamente `OneWay`; a interface apenas lê os indicadores.
- Nenhum `MessageBox` no tratamento global de falhas da interface.
- Uma única tela de recuperação substitui a árvore visual defeituosa e solicita cancelamento das tarefas. Uma segunda falha após a recuperação solicita encerramento, em vez de repetir notificações.
- Registros de backup, logs e preferências existentes são preservados. A atualização não aplica otimizações automaticamente.
- Regressões de reentrância e repetição de erros, teste de configuração XAML e teste Windows com o mecanismo real de associação WPF.

A versão 0.1.0 deve ser substituída pela 0.1.1. Feche a versão antiga pelo Gerenciador de Tarefas se as caixas impedirem seu fechamento. Extraia o novo pacote inteiro em outra pasta e execute o novo `.exe`; não apague `%LOCALAPPDATA%\NexusOptimizer`, pois esse diretório contém o histórico para restauração.
