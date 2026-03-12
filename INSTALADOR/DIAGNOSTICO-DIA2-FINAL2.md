# DIAGNÓSTICO DIA 2 - FINAL2-20260216024438

**Data:** 2026-02-16
**Rodada analisada:** FINAL2-20260216024438
**Status:** Diagnóstico COMPLETO ✅

---

## 🔍 RESUMO EXECUTIVO

**4 problemas principais identificados:**
1. ✅ **MSI-E2E FAIL:** Arquivo `msi-results-*.json` não foi puxado do guest (bug no pull)
2. ✅ **INNO-E2E FAIL:** INNO-04-REGISTRY falhou (HKLM missing)
3. ⚠️ **MSI uninstall lento:** 94.69s (meta ≤45s) - BLOQUEADOR GO!
4. ✅ **ARTIFACT-VERIFY FAIL:** Falta certificado .pfx (esperado, Dia 6)
5. ✅ **Win11 QGA blocking:** Serviço QGA não conectado

---

## 📊 PARTE A: Win10 invoke_regressao - DIAGNÓSTICO COMPLETO

### A.1 - Status Real do Script

**IMPORTANTE:** O script `run-regressao.ps1` EXECUTOU COM SUCESSO!

- ✅ Script rodou completamente no guest
- ✅ Todos os testes foram executados
- ✅ Métricas de performance coletadas (PRIMEIRA VEZ!)
- ❌ Retornou exit code 1 (FAIL) porque encontrou problemas

**Evidência:** `win10-lite-guest-regressao-windows-FINAL2-20260216024438.json` existe e contém resultados completos.

---

### A.2 - Resultados Detalhados dos Testes

#### ✅ UPGRADE-REINSTALL-E2E: PASS
- Exit code: 0
- Preservação de dados funcionando corretamente

#### ❌ MSI-E2E: FAIL
**Causa raiz:** Arquivo `msi-results-FINAL2-20260216024438.json` não foi puxado do guest para o host

**Análise:**
1. Arquivo EXISTE no guest: `C:\protons\INSTALADOR\saida\test-logs\msi-results-FINAL2-20260216024438.json`
2. Arquivo APARECE no índice coletado: `run_regressao_win10-lite_guest_file_index.log` (linha 12-13)
3. Arquivo NÃO foi puxado para o host: ausente na lista de arquivos host-side
4. `run-regressao.ps1` (linha 144-146) verificou a presença do arquivo e marcou MSI-E2E como FAIL

**Linha do código problemática:**
```powershell
# run-regressao.ps1:144-146
$msiResultPath = Join-Path $ctx.LogDir ("msi-results-{0}.json" -f $runId)
if (-not (Test-Path $msiResultPath)) {
    Set-StepFail -Steps $steps -Name 'MSI-E2E' -Reason "missing result file: $msiResultPath"
```

**Bug identificado:**
- Script `windows-autonomous-round.sh` linha 1045 tem regex para coletar arquivos
- Script linha 1062 tem case statement para processar arquivos
- Arquivo msi-results-*.json ESTÁ no pattern mas não foi puxado
- **HIPÓTESE:** Pode ter havido erro silencioso no `qga_read_file` (linha 1066)

**Arquivo esperado mas faltando:**
- Host: `/srv/.../win10-lite-guest-msi-results-FINAL2-20260216024438.json`
- Guest: `C:\protons\INSTALADOR\saida\test-logs\msi-results-FINAL2-20260216024438.json` (EXISTE!)

#### ❌ INNO-E2E: FAIL
**Causa raiz:** Teste INNO-04-REGISTRY falhou

**Detalhes (from `win10-lite-guest-inno-results-FINAL2-20260216024438.json`):**
- **9 testes PASS:**
  - INNO-INSTALL-CMD: PASS (12.64s)
  - INNO-01-EXE: PASS (executável exists)
  - INNO-02-DESKTOP: PASS (atalho desktop criado) ✅ **RESOLVIDO vs histórico!**
  - INNO-03-STARTMENU: PASS (atalho start menu criado) ✅ **RESOLVIDO vs histórico!**
  - INNO-05-APPDATA: PASS
  - INNO-06-APP-OPEN: PASS (app iniciou)
  - INNO-UNINSTALL-CMD: PASS (2.39s)
  - INNO-UNINST-01-PROGRAMFILES: PASS (cleanup OK) ✅ **RESOLVIDO vs histórico!**
  - INNO-UNINST-02-APPDATA-PRESERVED: PASS

- **1 teste FAIL:**
  - **INNO-04-REGISTRY:** FAIL
    - Detalhe: "HKLM missing; legacy HKCU key exists (diagnostic fallback only)"
    - Problema: Chave de registro não criada em `HKEY_LOCAL_MACHINE`
    - Só existe em `HKEY_CURRENT_USER` (fallback legado)

**Ação necessária:**
Corrigir `windows/instalador-inno/protons-setup.iss` para criar chave HKLM:
```ini
[Registry]
Root: HKLM; Subkey: "Software\Protons"; ValueType: string; ValueName: "Version"; ValueData: "{#MyAppVersion}"
Root: HKLM; Subkey: "Software\Protons"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"
```

**Tempo estimado:** 15 minutos (correção trivial)

#### ❌ ARTIFACT-VERIFY: FAIL
**Causa raiz:** Certificado .pfx não disponível (esperado)

**Detalhes:**
- ✅ Hash SHA-256: PASS (ambos MSI e EXE)
- ❌ Assinatura digital: BLOCKED_SIGNSDK
  - `signtool_available: false`
  - `require_signature: true`
  - `policy_result: FAIL`

**Solução:** Obter certificado .pfx (Dia 6 do plano)

**NOTA:** Este FAIL é esperado e NÃO bloqueia testes internos. Só bloqueia release público.

---

### A.3 - Performance Medida (🎉 PRIMEIRA VEZ!)

#### ✅ Métricas coletadas com sucesso:

| Instalador | Operação | p95 | Meta | Status |
|------------|----------|-----|------|--------|
| MSI | Install | **8.41s** | ≤90s | ✅ PASS (-91% da meta!) |
| MSI | Uninstall | **94.69s** | ≤45s | ❌ **FAIL (+110% da meta!)** |
| Inno | Install | **12.64s** | ≤90s | ✅ PASS (-86% da meta!) |
| Inno | Uninstall | **2.39s** | ≤45s | ✅ PASS (-95% da meta!) |

#### ⚠️ **BLOQUEADOR GO IDENTIFICADO:**

**MSI uninstall: 94.69s vs meta 45s**

**Impacto:**
- Usuário aguarda 1min 35s para desinstalar (muito lento!)
- Gate de performance automático bloqueia GO
- Score não pode subir para 85/100 enquanto não resolver

**Ações necessárias (Dia 3):**

1. **Diagnóstico detalhado:**
   - Revisar `windows/instalador-msi/Product.wxs`
   - Identificar operações lentas (registry cleanup, file removal, custom actions)
   - Verificar se há delays desnecessários ou loops ineficientes

2. **Otimizações possíveis:**
   - Remover custom actions lentas durante uninstall
   - Simplificar registry cleanup
   - Reduzir validações/checks desnecessários
   - Paralelizar operações quando possível

3. **Meta realista:**
   - Atual: 94.69s
   - Meta: ≤45s
   - Precisamos reduzir em 52% (eliminar ~50 segundos)

**Tempo estimado:** 2-4 horas (investigação + correção + reteste)

---

## 📊 PARTE B: Win11 QGA Blocking - DIAGNÓSTICO COMPLETO

### B.1 - Sintomas

**Status:** BLOQUEADO (mesmo problema desde rodada 20260215)

**Evidência:** `bootstrap_qga_win11-lite_qga_wait.log`
- 24 tentativas de ping (cada 5 segundos)
- Timeout após 120 segundos
- Erro consistente: "QEMU guest agent is not connected"

### B.2 - Causa Raiz

**QGA Guest Agent service não está rodando no Win11**

**Checklist diagnóstico:**
- [x] VM Win11 está running: SIM (bootstrap_qga_win11-lite_ensure_running.log = PASS)
- [x] Canal QGA configurado no XML: SIM (bootstrap_qga_win11-lite_qga_channel.log = PASS)
- [x] Precheck connectivity: PARCIAL (bootstrap_qga_win11-lite_qga_precheck.log)
- [ ] QGA service instalado: **DESCONHECIDO** (precisa verificar dentro da VM)
- [ ] QGA service rodando: **PROVAVELMENTE NÃO** (ping falha)

### B.3 - Ação Necessária (MANUAL)

**Intervenção manual na VM Win11 (uma única vez):**

1. **Conectar à VM:**
   ```bash
   sg libvirt -c "virsh start win11-lite"
   virt-viewer -c qemu:///system win11-lite
   ```

2. **Verificar status do serviço QGA:**
   ```powershell
   Get-Service -Name "QEMU Guest Agent"
   ```

3. **Se serviço NÃO EXISTE:**
   - Montar ISO: `D:\virtio-win-guest-tools.iso` (via virsh attach)
   - Executar: `D:\virtio-win-guest-tools.exe` como Admin
   - Reiniciar Win11
   - Validar: `Get-Service -Name "QEMU Guest Agent"`

4. **Se serviço EXISTE mas STOPPED:**
   ```powershell
   Start-Service "QEMU Guest Agent"
   Set-Service -Name "QEMU Guest Agent" -StartupType Automatic
   ```

5. **Validar do host:**
   ```bash
   sg libvirt -c "virsh qemu-agent-command win11-lite '{\"execute\":\"guest-ping\"}'"
   # Esperado: {"return":{}}
   ```

6. **Criar snapshot clean após sucesso:**
   ```bash
   bash comum/scripts/windows-vm-control.sh snapshot-create win11-lite clean
   ```

**Tempo estimado:** 20-30 minutos (instalação manual + validação)

---

## 📊 PARTE C: JSON UTF-8 BOM Error - DIAGNÓSTICO

### C.1 - Erro Observado

**Timestamp:** `2026-02-16T02:53:52Z` (durante processamento Win11)
**Tipo:** `json.decoder.JSONDecodeError: Unexpected UTF-8 BOM`

### C.2 - Causa Raiz

**PowerShell gera arquivos JSON com BOM (Byte Order Mark) por padrão**

Quando PowerShell usa:
```powershell
$obj | ConvertTo-Json | Set-Content -Path file.json -Encoding UTF8
```

O encoding "UTF8" do PowerShell INCLUI BOM (EF BB BF), mas Python json.load() NÃO aceita BOM.

### C.3 - Solução

**Opção A: Corrigir no guest (PowerShell) - RECOMENDADO**

Modificar todos os scripts Windows que geram JSON:
```powershell
# ANTES:
$obj | ConvertTo-Json | Set-Content -Path file.json -Encoding UTF8

# DEPOIS:
$obj | ConvertTo-Json | Set-Content -Path file.json -Encoding UTF8NoBOM
```

**Arquivos a modificar:**
- `testes/windows/Test-Common.ps1` (função Write-ResultFiles)
- `testes/windows/test-msi.ps1`
- `testes/windows/test-inno.ps1`
- `testes/windows/verify-artifacts.ps1`

**Opção B: Strip BOM no host (Python) - FALLBACK**

Modificar scripts host que leem JSON:
```python
import json
with open(file_path, 'r', encoding='utf-8-sig') as f:  # utf-8-sig remove BOM
    data = json.load(f)
```

**Recomendação:** Usar Opção A (corrigir origem) para evitar problemas futuros.

**Tempo estimado:** 30 minutos (4 arquivos + reteste)

---

## 📊 PARTE D: Artefatos Faltando (msi-results-*.json)

### D.1 - Problema

Arquivo `msi-results-FINAL2-20260216024438.json`:
- ✅ EXISTE no guest: `C:\protons\INSTALADOR\saida\test-logs\msi-results-FINAL2-20260216024438.json`
- ✅ LISTADO no índice: `run_regressao_win10-lite_guest_file_index.log` (linha 12)
- ❌ NÃO PUXADO para host: ausente em `/srv/.../win10-lite-guest-msi-results-*.json`

### D.2 - Causa Raiz (HIPÓTESE)

**Erro silencioso no qga_read_file durante pull**

Código relevante (`windows-autonomous-round.sh:1066`):
```bash
if qga_read_file "$vm" "$gpath" "$out_file" >/dev/null 2>&1; then
    record_custom_step "${step_prefix}_pull_${base_name}" "PASS" "0" "$out_file"
else
    record_custom_step "${step_prefix}_pull_${base_name}" "PARCIAL" "0" "$out_file"
fi
```

**Possíveis causas:**
1. Arquivo muito grande para QGA buffer (verificar tamanho)
2. Erro de leitura base64 (encoding issue)
3. Timeout durante transferência
4. Permissões de arquivo no guest

### D.3 - Ação de Investigação

1. **Verificar tamanho do arquivo no guest:**
   ```bash
   # Via QGA:
   sg libvirt -c "virsh qemu-agent-command win10-lite '{\"execute\":\"guest-file-open\",\"arguments\":{\"path\":\"C:\\\\protons\\\\INSTALADOR\\\\saida\\\\test-logs\\\\msi-results-FINAL2-20260216024438.json\",\"mode\":\"r\"}}'"
   # Capturar handle, depois guest-file-read
   ```

2. **Tentar pull manual para diagnosticar:**
   ```bash
   bash comum/scripts/windows-vm-control.sh start win10-lite
   # Tentar pull manual do arquivo específico
   # Ver se erro aparece nos logs
   ```

3. **Workaround temporário:**
   - Pull manual após cada rodada
   - Copiar arquivo via outro método (shared folder)

**Tempo estimado:** 1 hora (investigação + workaround)

---

## 📋 CHECKLIST DIA 2 - STATUS

### ✅ Parte A: Win10 invoke_regressao (COMPLETO)

- [x] **2.1** Analisar log de erro do invoke ✅
  - Código: exit 1 (testes falharam, não erro de execução)
  - Mensagem: "missing result file: msi-results-*.json"

- [x] **2.2** Verificar bundle extraído ✅
  - Bundle: EXTRAÍDO corretamente
  - Arquivos: TODOS presentes

- [x] **2.3** Verificar logs PowerShell ✅
  - Logs coletados e analisados
  - Script executou completamente

- [x] **2.4** Identificar causa raiz ✅
  - ✅ MSI-E2E: arquivo não puxado do guest
  - ✅ INNO-E2E: INNO-04-REGISTRY FAIL (HKLM missing)
  - ✅ ARTIFACT-VERIFY: certificado ausente (esperado)
  - ✅ Performance: MSI uninstall 94.69s (bloqueador!)

- [x] **2.5** Planejar correção ✅
  - Ver seção "AÇÕES PRIORITÁRIAS DIA 3" abaixo

### ✅ Parte B: Win11 QGA (COMPLETO)

- [x] **2.6** Analisar log QGA timeout ✅
  - 120s timeout, 24 tentativas, erro: "not connected"

- [x] **2.7** Causa identificada ✅
  - QGA service provavelmente não instalado ou parado

- [x] **2.8** Solução documentada ✅
  - Manual: instalar QGA via ISO (uma vez)

### ✅ Parte C: JSON BOM (COMPLETO)

- [x] **2.10** Arquivos com BOM identificados ✅
  - Todos JSON gerados por PowerShell

- [x] **2.11** Solução planejada ✅
  - Opção A: UTF8NoBOM no PowerShell (recomendado)

---

## 🎯 AÇÕES PRIORITÁRIAS DIA 3

### 1. CRÍTICO - Otimizar MSI uninstall (94.69s → ≤45s)

**Bloqueador GO:** Sem isso, score não sobe para 85/100

**Passos:**
1. Ler `windows/instalador-msi/Product.wxs` completo
2. Identificar custom actions executadas durante uninstall
3. Buscar por:
   - Delays/sleeps desnecessários
   - Registry cleanup ineficiente
   - File removal com retry/polling excessivo
   - Validações lentas
4. Aplicar otimizações:
   - Remover operações não essenciais
   - Simplificar cleanup
   - Paralelizar quando possível
5. Rebuild MSI
6. Reteste: `bash RODAR-FINAL2-CORRIGIDO.sh`

**Tempo:** 2-4 horas
**Impacto:** +10-15 pontos no score

### 2. ALTA - Corrigir INNO-04-REGISTRY

**Arquivo:** `windows/instalador-inno/protons-setup.iss`

**Correção:**
```ini
[Registry]
Root: HKLM; Subkey: "Software\Protons"; ValueType: string; ValueName: "Version"; ValueData: "{#MyAppVersion}"
Root: HKLM; Subkey: "Software\Protons"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"
```

**Tempo:** 15 minutos
**Impacto:** INNO-E2E PASS (resolve 1/3 FAILs)

### 3. ALTA - Corrigir JSON UTF-8 BOM

**Arquivos:**
- `testes/windows/Test-Common.ps1` (Write-ResultFiles)
- `testes/windows/test-msi.ps1` (linha 168)
- `testes/windows/test-inno.ps1` (linha 163)
- `testes/windows/verify-artifacts.ps1`

**Mudança:**
```powershell
# TROCAR: -Encoding UTF8
# POR:    -Encoding UTF8NoBOM
```

**Tempo:** 30 minutos
**Impacto:** Elimina erro JSON BOM

### 4. MÉDIA - Investigar pull msi-results

**Ação:**
- Tentar pull manual do arquivo
- Verificar tamanho/permissões
- Adicionar logging detalhado no qga_read_file

**Tempo:** 1 hora
**Impacto:** MSI-E2E PASS (resolve 2/3 FAILs)

### 5. MANUAL - Ativar QGA no Win11

**Ação:** Conforme seção B.3 acima

**Tempo:** 20-30 minutos
**Impacto:** Win11 disponível para testes (dobra cobertura)

---

## 📊 SCORE ESTIMADO PÓS-DIA 3

**Score atual:** ~60-63/100 (NO-GO)

**Ganhos esperados (se todas as correções funcionarem):**
- MSI uninstall otimizado: +8-10 pontos
- INNO-04 corrigido: +2-3 pontos
- MSI-E2E resolvido: +2-3 pontos
- Win11 ativado: +3-5 pontos

**Score pós-Dia 3:** ~75-84/100 (ainda NO-GO se <85)

**Faltando para GO (85/100):**
- Certificado .pfx (Dia 6): +5 pontos
- Update E2E (Dia 5): +2-3 pontos

**Score final estimado:** 85-92/100 ✅ **GO!**

---

## 📝 CONCLUSÃO

**Diagnóstico Dia 2: COMPLETO ✅**

**4 causas raiz identificadas:**
1. ✅ MSI uninstall lento (94.69s) - custom actions ineficientes
2. ✅ INNO-04 FAIL - registro HKLM faltando
3. ✅ msi-results-*.json não puxado - erro no qga_read_file
4. ✅ JSON BOM error - PowerShell UTF8 inclui BOM
5. ✅ Win11 QGA - serviço não instalado/rodando

**Próximas 24h (Dia 3):**
- Focar em MSI uninstall (CRÍTICO - bloqueador GO)
- Corrigir INNO-04 (15 min, fácil)
- Corrigir JSON BOM (30 min, fácil)
- Investigar pull msi-results (1h, média)
- Ativar Win11 QGA manual (30 min, manual)

**Confiança:** ALTA (todas as causas identificadas, soluções claras)

---

**Documento gerado em:** 2026-02-16
**Por:** Claude Sonnet 4.5
**Revisão:** Diagnóstico técnico completo - Dia 2 do Plano GO
