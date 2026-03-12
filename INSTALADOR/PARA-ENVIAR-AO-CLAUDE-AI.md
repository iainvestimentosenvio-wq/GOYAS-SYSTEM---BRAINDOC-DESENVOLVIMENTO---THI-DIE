# 📧 MENSAGEM PARA ENVIAR AO CLAUDE.AI (Se precisar ajuda manual)

---

## COPY/PASTE ESTA MENSAGEM NO CLAUDE.AI:

```
Olá Claude!

Estou configurando VMs Windows (Win10 e Win11) para rodar testes automatizados
do instalador Protons. O objetivo é eliminar passos manuais e atingir 100% de
automação rodando do terminal Linux.

CONTEXTO:
- Projeto: Instalador Protons (MSI + Inno Setup)
- VMs: win10-lite e win11-lite (QEMU/KVM no Linux)
- Problema: Upload de bundle (81MB) via QEMU Guest Agent está dando timeout
- Solução tentada: Aumentar chunk size de 3KB → 64KB
- Guia seguindo: GUIA-MANUAL-PASSOS-WINDOWS.md

ONDE ESTOU:
- Passo X do guia (substituir X pelo número do passo)
- VM: [Win10-lite OU Win11-lite]

PROBLEMA ENCONTRADO:
[Descreva o problema ou cole a mensagem de erro aqui]

PRINTS/SCREENSHOTS:
[Anexar prints conforme solicitado no guia]

PERGUNTA:
[O que você precisa saber ou qual erro precisa resolver]

Pode me ajudar a diagnosticar e resolver este problema?
```

---

## PRINTS IMPORTANTES PARA ANEXAR:

### Print 1: PowerShell como Administrador
**Quando:** Passo 2 do guia
**O que mostrar:** Título da janela dizendo "Administrator: Windows PowerShell"

### Print 2: ExecutionPolicy Configurado
**Quando:** Passo 3 do guia
**Comando executado:**
```powershell
Get-ExecutionPolicy -Scope CurrentUser
```
**Resultado esperado:** `RemoteSigned`

### Print 3: Pasta C:\Windows\Temp Verificada
**Quando:** Passo 4 do guia
**Comando executado:**
```powershell
Test-Path "C:\Windows\Temp"
```
**Resultado esperado:** `True`

### Print 4: Exceções do Windows Defender
**Quando:** Passo 5 do guia
**Comando executado:**
```powershell
Get-MpPreference | Select-Object ExclusionPath, ExclusionProcess
```
**Resultado esperado:** Lista incluindo `C:\Windows\Temp` e `powershell.exe`

### Print 5: Teste de Conectividade HTTP (se aplicável)
**Quando:** Passo 7 do guia
**Comando executado:**
```powershell
Test-NetConnection -ComputerName IP_DO_LINUX -Port 8888
```
**Resultado esperado:** `TcpTestSucceeded: True`

### Print 6: Download Bundle HTTP (se aplicável)
**Quando:** Passo 7 do guia
**Comando executado:**
```powershell
Invoke-WebRequest -Uri $url -OutFile $dest -Verbose
```
**Resultado esperado:** `✓ Bundle baixado com sucesso! Tamanho: 80.5 MB`

### Print 7: Arquivos JSON Gerados (validação final)
**Quando:** Passo 9 do guia
**Comando executado:**
```powershell
Get-ChildItem -Filter "*.json"
```
**Resultado esperado:** Lista de arquivos `.json` (regressao-windows-*.json)

---

## INFORMAÇÕES ADICIONAIS ÚTEIS:

### Arquivos de Log (Linux) para referência:

```bash
# Resumo da última rodada
cat /srv/DocumentosCompartilhados/PROJETO\ PROTONS/INSTALADOR/saida/validacao-windows-FINAL-*/resumo.csv

# Log de upload que falhou (Win10)
cat /srv/DocumentosCompartilhados/PROJETO\ PROTONS/INSTALADOR/saida/validacao-windows-FINAL-*/run_regressao_win10-lite_bundle_upload.log

# Log de upload que falhou (Win11)
cat /srv/DocumentosCompartilhados/PROJETO\ PROTONS/INSTALADOR/saida/validacao-windows-FINAL-*/run_regressao_win11-lite_bundle_upload.log

# Gates summary
cat /srv/DocumentosCompartilhados/PROJETO\ PROTONS/INSTALADOR/saida/validacao-windows-FINAL-*/gates-summary.md
```

Se o Claude.ai pedir, cole o conteúdo destes arquivos também.

---

## PERGUNTAS FREQUENTES PARA CLAUDE.AI:

### 1. "ExecutionPolicy não muda, continua 'Restricted'"

**Print necessário:**
```powershell
PS C:\Windows\System32> Get-ExecutionPolicy -List

        Scope ExecutionPolicy
        ----- ---------------
MachinePolicy       Undefined
   UserPolicy       Undefined
      Process       Undefined
  CurrentUser      Restricted  <-- Este deve mudar para RemoteSigned
 LocalMachine       Undefined
```

**Solução esperada:** Claude vai sugerir tentar `-Scope LocalMachine` ou verificar GPO.

---

### 2. "Pasta C:\Windows\Temp não tem permissão de escrita"

**Print necessário:**
```powershell
PS C:\Windows\System32> $acl = Get-Acl "C:\Windows\Temp"
PS C:\Windows\System32> $acl.Access | Format-Table

IdentityReference      FileSystemRights
-----------------      ----------------
NT AUTHORITY\SYSTEM    FullControl
BUILTIN\Administrators FullControl
```

**Solução esperada:** Claude vai sugerir adicionar permissões ou usar outra pasta.

---

### 3. "Windows Defender continua bloqueando script"

**Print necessário:**
```powershell
PS C:\Windows\System32> Get-MpThreat

# Se retornar algo, significa que Defender detectou ameaça
```

**Solução esperada:** Claude vai sugerir adicionar exceção específica ou desabilitar real-time protection temporariamente.

---

### 4. "Download HTTP falha com erro de conexão"

**Print necessário:**
```powershell
PS C:\Windows\Temp> Test-NetConnection -ComputerName 192.168.122.1 -Port 8888

ComputerName     : 192.168.122.1
RemoteAddress    : 192.168.122.1
RemotePort       : 8888
InterfaceAlias   : Ethernet
SourceAddress    : 192.168.122.XXX
TcpTestSucceeded : False  <-- Problema aqui!
```

**Solução esperada:** Claude vai sugerir verificar firewall do Windows ou servidor HTTP no Linux.

---

### 5. "run-regressao.ps1 não executa, retorna erro"

**Print necessário:**
```powershell
PS C:\Windows\Temp\protons-test> .\run-regressao.ps1

# Cole a mensagem de erro COMPLETA que aparecer
```

**Solução esperada:** Claude vai diagnosticar o erro específico (pode ser ExecutionPolicy, permissões, dependências faltando, etc.)

---

## TEMPLATE DE MENSAGEM POR PASSO:

### Se falhar no PASSO 3 (ExecutionPolicy):

```
Claude, estou no Passo 3 do guia (Configurar PowerShell ExecutionPolicy).

Executei:
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser -Force

Mas quando verifico com:
Get-ExecutionPolicy -Scope CurrentUser

Ainda retorna: Restricted

[ANEXAR PRINT DO POWERSHELL]

O que pode estar bloqueando? Como resolver?
```

---

### Se falhar no PASSO 5 (Windows Defender):

```
Claude, estou no Passo 5 do guia (Configurar exceção Windows Defender).

Executei:
Add-MpPreference -ExclusionPath "C:\Windows\Temp"

Mas quando tento rodar scripts, o Defender continua bloqueando.

[ANEXAR PRINT DO ERRO DO DEFENDER ou de Get-MpPreference]

Como posso verificar se a exceção foi aplicada? Preciso desabilitar o Defender?
```

---

### Se falhar no PASSO 7 (Download HTTP):

```
Claude, estou no Passo 7 do guia (Teste de upload via HTTP).

No Linux, o servidor HTTP está rodando:
python3 -m http.server 8888

No Windows, tento:
Test-NetConnection -ComputerName 192.168.122.1 -Port 8888

Retorna: TcpTestSucceeded: False

[ANEXAR PRINT DO TEST-NETCONNECTION]

Firewall está bloqueando? Como liberar porta 8888?
```

---

### Se falhar no PASSO 8 (Regressão não executa):

```
Claude, estou no Passo 8 do guia (Rodar teste manual).

Bundle foi descompactado com sucesso em C:\Windows\Temp\protons-test

Mas quando tento rodar:
.\run-regressao.ps1

Recebo este erro:
[COLE O ERRO AQUI]

[ANEXAR PRINT DO ERRO]

O que está faltando? Preciso instalar alguma dependência?
```

---

## 🎯 DICA FINAL:

**Quanto mais detalhes você fornecer ao Claude.ai, melhor ele pode ajudar:**

✅ **BOM:**
- Passo específico do guia
- Comando executado
- Erro completo
- Print da tela

❌ **RUIM:**
- "Não está funcionando"
- "Dá erro"
- Sem especificar qual passo
- Sem prints

---

## 📞 SUPORTE ADICIONAL:

Se Claude.ai não conseguir resolver imediatamente, peça:

1. **Comando de diagnóstico:**
   ```
   Claude, que comando posso rodar para diagnosticar melhor este problema?
   ```

2. **Solução alternativa:**
   ```
   Claude, existe alguma forma alternativa de fazer isso sem [X]?
   ```

3. **Validação passo a passo:**
   ```
   Claude, pode me guiar passo a passo para resolver? Vou enviando prints conforme avanço.
   ```

---

**Última atualização:** 2026-02-15
**Guia relacionado:** GUIA-MANUAL-PASSOS-WINDOWS.md
**Para uso com:** Claude.ai (https://claude.ai)
