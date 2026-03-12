# 🪟 GUIA COMPLETO: Passos Manuais nas VMs Windows

**Data:** 2026-02-15
**Contexto:** Preparar Win10-lite e Win11-lite para testes automáticos
**Objetivo:** Eliminar bloqueios manuais para atingir GO (85/100)

---

## 📋 CHECKLIST RÁPIDO

Use esta seção para marcar o que já foi feito:

### Win10-lite:
- [x] QGA instalado (QEMU Guest Agent)
- [x] Driver VirtIO Serial instalado
- [ ] PowerShell ExecutionPolicy configurado
- [ ] Windows Defender/SmartScreen exceção criada
- [ ] Pasta `C:\Windows\Temp` verificada/criada
- [ ] Teste de upload HTTP (se necessário)

### Win11-lite:
- [x] QGA instalado (QEMU Guest Agent)
- [x] Driver VirtIO Serial instalado
- [ ] PowerShell ExecutionPolicy configurado
- [ ] Windows Defender/SmartScreen exceção criada
- [ ] Pasta `C:\Windows\Temp` verificada/criada
- [ ] Teste de upload HTTP (se necessário)

---

## 🎯 PASSOS MANUAIS (WIN10-LITE E WIN11-LITE)

Execute estes passos em CADA VM (Win10 e Win11).

---

### PASSO 1: Conectar na VM via Console

**No Linux (terminal):**

```bash
# Para Win10:
virt-viewer win10-lite &

# Para Win11:
virt-viewer win11-lite &
```

**Ou via VNC:**
```bash
# Descobrir porta VNC:
virsh vncdisplay win10-lite
# Exemplo de saída: :0 (significa porta 5900)

# Conectar via VNC client:
vncviewer localhost:5900
```

**✅ VALIDAÇÃO:**
- [ ] Janela da VM abriu
- [ ] Desktop do Windows visível
- [ ] Mouse e teclado funcionam

---

### PASSO 2: Abrir PowerShell como Administrador

**Na VM Windows:**

1. Clique no botão **Start** (Windows)
2. Digite: `PowerShell`
3. Aparecerá **Windows PowerShell** nos resultados
4. Clique com **botão direito** sobre ele
5. Selecione **"Run as Administrator"**
6. Clique **"Yes"** na janela UAC (se aparecer)

**✅ VALIDAÇÃO (tire print desta janela):**
- [ ] Janela PowerShell abriu
- [ ] Título diz "Administrator: Windows PowerShell"
- [ ] Prompt mostra: `PS C:\Windows\System32>`

---

### PASSO 3: Configurar PowerShell ExecutionPolicy

**No PowerShell (como Admin):**

```powershell
# Verificar politica atual
Get-ExecutionPolicy -Scope CurrentUser

# Se retornar "Restricted" ou "Undefined", configure:
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser -Force

# Validar
Get-ExecutionPolicy -Scope CurrentUser
# Deve retornar: RemoteSigned
```

**✅ VALIDAÇÃO (tire print):**
- [ ] Comando executou sem erro
- [ ] Política agora é "RemoteSigned"

**Screenshot esperado:**
```
PS C:\Windows\System32> Get-ExecutionPolicy -Scope CurrentUser
RemoteSigned
```

---

### PASSO 4: Verificar/Criar Pasta de Testes

**No PowerShell (como Admin):**

```powershell
# Verificar se pasta existe
Test-Path "C:\Windows\Temp"

# Se retornar False, criar:
New-Item -Path "C:\Windows\Temp" -ItemType Directory -Force

# Verificar permissoes (deve permitir escrita)
$acl = Get-Acl "C:\Windows\Temp"
$acl.Access | Format-Table IdentityReference, FileSystemRights
```

**✅ VALIDAÇÃO (tire print):**
- [ ] Pasta existe (Test-Path retorna True)
- [ ] Permissões incluem "FullControl" ou "Modify" para SYSTEM/Administrator

---

### PASSO 5: Configurar Exceção Windows Defender

**No PowerShell (como Admin):**

```powershell
# Adicionar excecao para pasta de testes
Add-MpPreference -ExclusionPath "C:\Windows\Temp"

# Adicionar excecao para processos PowerShell
Add-MpPreference -ExclusionProcess "powershell.exe"

# Validar excecoes
Get-MpPreference | Select-Object ExclusionPath, ExclusionProcess
```

**✅ VALIDAÇÃO (tire print):**
- [ ] Comando executou sem erro
- [ ] Lista de exceções inclui `C:\Windows\Temp`
- [ ] Lista de exceções inclui `powershell.exe`

**Screenshot esperado:**
```
ExclusionPath           ExclusionProcess
-------------           ----------------
{C:\Windows\Temp, ...}  {powershell.exe, ...}
```

---

### PASSO 6: Desabilitar UAC Temporariamente (OPCIONAL)

**⚠️ IMPORTANTE:** Só faça isso se os testes continuarem falhando.

**No PowerShell (como Admin):**

```powershell
# Desabilitar UAC (requer reinicializacao)
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" -Name "EnableLUA" -Value 0

# Verificar
Get-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" -Name "EnableLUA"
```

**Depois execute:**
```powershell
# Reiniciar VM
Restart-Computer -Force
```

**✅ VALIDAÇÃO (após reiniciar):**
- [ ] VM reiniciou
- [ ] UAC não aparece mais ao rodar programas

**⚠️ LEMBRAR:** Reabilitar UAC depois dos testes!
```powershell
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" -Name "EnableLUA" -Value 1
Restart-Computer -Force
```

---

### PASSO 7: Teste Manual de Upload via HTTP (SE NECESSÁRIO)

**⚠️ Só faça isso se o upload via QGA continuar falhando!**

**Pré-requisito:** Linux deve estar rodando servidor HTTP:
```bash
# No Linux (terminal separado):
cd "saida/http-bundle-server"
python3 -m http.server 8888
```

**Descobrir IP do host Linux:**
```bash
# No Linux:
ip -4 addr show | grep -oP '(?<=inet\s)\d+(\.\d+){3}' | grep -v '127.0.0.1' | head -1
# Exemplo: 192.168.122.1
```

**Na VM Windows (PowerShell como Admin):**

```powershell
# Substituir IP_DO_LINUX pelo IP obtido acima
$url = "http://IP_DO_LINUX:8888/protons-autonoma-bundle.zip"
$dest = "C:\Windows\Temp\protons-autonoma-bundle.zip"

# Testar conectividade
Test-NetConnection -ComputerName IP_DO_LINUX -Port 8888

# Se conectividade OK, fazer download
Invoke-WebRequest -Uri $url -OutFile $dest -Verbose

# Verificar download
if (Test-Path $dest) {
    $size = (Get-Item $dest).Length / 1MB
    Write-Host "✓ Bundle baixado com sucesso! Tamanho: $size MB" -ForegroundColor Green
} else {
    Write-Host "✗ ERRO: Bundle não foi baixado!" -ForegroundColor Red
}
```

**✅ VALIDAÇÃO (tire print):**
- [ ] Test-NetConnection retorna `TcpTestSucceeded: True`
- [ ] Download completou sem erro
- [ ] Arquivo `C:\Windows\Temp\protons-autonoma-bundle.zip` existe
- [ ] Tamanho ~81 MB

**Screenshot esperado:**
```
✓ Bundle baixado com sucesso! Tamanho: 80.5 MB
```

---

### PASSO 8: Descompactar e Rodar Teste Manual (VALIDAÇÃO FINAL)

**No PowerShell (como Admin):**

```powershell
# Ir para pasta Temp
cd C:\Windows\Temp

# Descompactar bundle
Expand-Archive -Path "protons-autonoma-bundle.zip" -DestinationPath "protons-test" -Force

# Verificar conteudo
Get-ChildItem "protons-test"

# Rodar regressao (teste rapido)
cd protons-test

# Verificar se run-regressao.ps1 existe
if (Test-Path ".\run-regressao.ps1") {
    Write-Host "✓ Script encontrado!" -ForegroundColor Green

    # Rodar (isso demora ~5-10 minutos)
    .\run-regressao.ps1

} else {
    Write-Host "✗ ERRO: Script run-regressao.ps1 nao encontrado!" -ForegroundColor Red
    Get-ChildItem  # Listar o que tem na pasta
}
```

**✅ VALIDAÇÃO (tire print):**
- [ ] Descompactação concluída sem erro
- [ ] Pasta `protons-test` criada
- [ ] Script `run-regressao.ps1` existe
- [ ] Regressão executou (pode demorar 5-10min)
- [ ] Arquivos JSON gerados (ex: `regressao-windows-*.json`)

---

### PASSO 9: Verificar Resultados (IMPORTANTE!)

**No PowerShell (na pasta protons-test):**

```powershell
# Listar arquivos JSON gerados
Get-ChildItem -Filter "*.json"

# Ver resumo de um dos JSONs
Get-Content "regressao-windows-*.json" | ConvertFrom-Json | Format-List

# Verificar se tem FAILs
$json = Get-Content "regressao-windows-*.json" | ConvertFrom-Json
$fails = $json.PSObject.Properties | Where-Object { $_.Value -like "*FAIL*" }
if ($fails.Count -gt 0) {
    Write-Host "⚠️ Encontrados $($fails.Count) FAILs:" -ForegroundColor Yellow
    $fails | Format-Table Name, Value
} else {
    Write-Host "✓ Nenhum FAIL encontrado!" -ForegroundColor Green
}
```

**✅ VALIDAÇÃO (tire print dos JSONs):**
- [ ] Arquivos JSON existem
- [ ] Conteúdo é válido (não vazio)
- [ ] Número de FAILs identificado

**Prints importantes para enviar ao Claude:**
1. Lista de arquivos JSON gerados
2. Conteúdo de pelo menos 1 arquivo JSON
3. Lista de FAILs (se houver)

---

### PASSO 10: Criar Snapshot "Testes Prontos"

**⚠️ IMPORTANTE:** Faça isso DEPOIS de validar tudo acima!

**Desligar a VM (PowerShell):**
```powershell
# Na VM Windows:
Stop-Computer -Force
```

**Criar snapshot (Linux):**
```bash
# Para Win10:
virsh snapshot-create-as win10-lite \
  "testes-prontos" \
  --description "QGA + PowerShell + Defender configurados para testes"

# Para Win11:
virsh snapshot-create-as win11-lite \
  "testes-prontos" \
  --description "QGA + PowerShell + Defender configurados para testes"

# Verificar snapshots
virsh snapshot-list win10-lite
virsh snapshot-list win11-lite
```

**✅ VALIDAÇÃO:**
- [ ] Snapshot criado com sucesso
- [ ] Nome aparece na lista de snapshots

---

## 🐛 TROUBLESHOOTING

### Problema 1: "execution of scripts is disabled on this system"

**Solução:**
```powershell
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser -Force
```

### Problema 2: "Access to the path is denied"

**Solução:**
```powershell
# Rodar PowerShell como Administrador (Passo 2)
# Verificar permissoes da pasta:
icacls "C:\Windows\Temp"
```

### Problema 3: Windows Defender bloqueia script

**Solução:**
```powershell
Add-MpPreference -ExclusionPath "C:\Windows\Temp"
Add-MpPreference -ExclusionProcess "powershell.exe"
```

### Problema 4: Download HTTP falha (Test-NetConnection False)

**Possíveis causas:**
1. Firewall do Windows bloqueando
2. Servidor HTTP no Linux não está rodando
3. IP incorreto

**Solução:**
```powershell
# Desabilitar firewall temporariamente
Set-NetFirewallProfile -Profile Domain,Public,Private -Enabled False

# Testar novamente
Test-NetConnection -ComputerName IP_DO_LINUX -Port 8888

# Reabilitar firewall depois
Set-NetFirewallProfile -Profile Domain,Public,Private -Enabled True
```

### Problema 5: QGA desconecta durante upload

**Sintoma:** `Guest agent not available for now`

**Solução:**
```bash
# No Linux, reiniciar servico QGA na VM:
virsh shutdown win10-lite
sleep 10
virsh start win10-lite

# Aguardar boot (~60s)
sleep 60

# Testar QGA
virsh qemu-agent-command win10-lite '{"execute":"guest-ping"}'
```

---

## 📸 PRINTS IMPORTANTES PARA ENVIAR AO CLAUDE

Tire screenshots/prints das seguintes telas e envie ao Claude.ai:

### Print 1: PowerShell ExecutionPolicy
```
PS C:\Windows\System32> Get-ExecutionPolicy -Scope CurrentUser
RemoteSigned
```

### Print 2: Pasta C:\Windows\Temp existe
```
PS C:\Windows\System32> Test-Path "C:\Windows\Temp"
True
```

### Print 3: Exceções do Defender
```
PS C:\Windows\System32> Get-MpPreference | Select ExclusionPath, ExclusionProcess

ExclusionPath           ExclusionProcess
-------------           ----------------
{C:\Windows\Temp, ...}  {powershell.exe, ...}
```

### Print 4: Download HTTP (se usado)
```
✓ Bundle baixado com sucesso! Tamanho: 80.5 MB
```

### Print 5: Arquivos JSON gerados
```
PS C:\Windows\Temp\protons-test> Get-ChildItem -Filter "*.json"

Mode         LastWriteTime    Length Name
----         -------------    ------ ----
-a----  2/15/2026  3:45 PM   12345  regressao-windows-msi.json
-a----  2/15/2026  3:50 PM   12345  regressao-windows-inno.json
```

### Print 6: QGA respondendo (Linux)
```bash
$ virsh qemu-agent-command win10-lite '{"execute":"guest-ping"}'
{"return":{}}
```

---

## ✅ VALIDAÇÃO FINAL

**Antes de sair das VMs, confirme:**

- [ ] QGA respondendo: `{"return":{}}`
- [ ] PowerShell ExecutionPolicy: `RemoteSigned`
- [ ] Pasta `C:\Windows\Temp` existe e tem permissões
- [ ] Defender configurado com exceções
- [ ] Teste manual de regressão executou (se possível)
- [ ] Snapshot "testes-prontos" criado

---

## 🚀 PRÓXIMOS PASSOS (AUTOMÁTICO)

Depois de completar TODOS os passos acima nas duas VMs:

```bash
# No Linux:
cd "<raiz-do-repo>/INSTALADOR"

# Rodar rodada FINAL2 com upload otimizado:
bash run-windows-base-gate.sh
```

**Duração esperada:** 60-90 minutos (100% automático)

**Resultado esperado:**
- Upload de bundle: PASS (~2-3 min por VM)
- Regressão E2E: PASS
- Score global: sobe de 55.2 para ~65-70/100

---

## 📞 SUPORTE

Se encontrar problemas:

1. Tire prints das mensagens de erro
2. Cole no chat do Claude.ai: "Estou tendo este erro no passo X do GUIA-MANUAL-PASSOS-WINDOWS.md"
3. Envie os prints
4. Claude vai diagnosticar e ajudar

**Arquivos de log importantes (Linux):**
- `saida/validacao-windows-*/resumo.csv`
- `saida/validacao-windows-*/run_regressao_*_bundle_upload.log`
- `saida/validacao-windows-*/gates-summary.md`
- `saida/status/latest-status.json` (fonte oficial)

---

**Última atualização:** 2026-02-15
**Versão do guia:** 1.0
**Testado em:** Win10-lite, Win11-lite (QEMU/KVM)
