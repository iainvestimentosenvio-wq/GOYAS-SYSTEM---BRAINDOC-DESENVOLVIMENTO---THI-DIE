# UX ERROR MAP INSTALADOR

## Erros prioritarios

1. Falha em rollback MSI (failpoint de teste)
- Mensagem: "Falha simulada para validar rollback. Nenhuma alteracao permanente deve permanecer."
- Acao: verificar logs e rodar `test-msi-rollback.ps1`.

2. Falha em cleanup Inno
- Mensagem: "A instalacao foi interrompida. O sistema iniciou limpeza de seguranca."
- Acao: rodar `test-inno-rollback.ps1` e `test-transactional-install.ps1`.

3. Cancelamento no meio
- Mensagem: "Instalacao cancelada pelo usuario. Limpando arquivos temporarios."
- Acao: validar `test-cancel-install.ps1`.

4. Defender bloqueando execucao
- Mensagem: "O ambiente de seguranca pode ter bloqueado parte da instalacao."
- Acao: validar `test-defender-compatibility.ps1`.

5. Falha de recuperacao apos interrupcao
- Mensagem: "Instalacao interrompida inesperadamente. Tentando recuperar."
- Acao: rodar `test-power-failure-recovery.ps1`.

