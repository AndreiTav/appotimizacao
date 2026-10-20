# Pacotes do NEXUS OPTIMIZER 0.1.1

- **NEXUS-OPTIMIZER-0.1.1-win-x64.zip**: aplicativo portátil com .NET incluído. Extraia tudo e abra `NEXUS OPTIMIZER.exe` no Windows 11 x64. Mantenha todas as DLLs e arquivos da pasta.
- **NEXUS-OPTIMIZER-0.1.1-win-x64.zip.sha256**: checksum SHA-256 do pacote portátil.
- **NEXUS-OPTIMIZER-0.1.1-source.zip**: código-fonte com projetos, lockfiles, testes, scripts e documentação.

Os pacotes foram compilados em Linux para Windows x64. A compilação Release terminou sem avisos e os 44 testes Core passaram. A [verificação automatizada](https://github.com/AndreiTav/appotimizacao/actions/runs/39356258615) em Windows Server 2025 também passou pelos 44 testes Core, 6 testes nativos/WPF e abertura das nove páginas. UAC, DPI e diferentes hardwares ainda exigem a validação manual no Windows 11. Consulte o README principal e `docs/VALIDACAO-WINDOWS.md`; esta é uma entrega MVP, sem instalador ou assinatura digital.

No GitHub, abra o ZIP e use o botão **Download raw file** (ícone de download). Um repositório privado requer login de uma conta autorizada.

A versão 0.1.0 foi retirada desta pasta por uma falha de associação de dados que causava repetição de caixas de erro ao abrir. Consulte `docs/CORRECAO-0.1.1.md`. Preserve o diretório local de histórico e backups ao trocar de versão.
