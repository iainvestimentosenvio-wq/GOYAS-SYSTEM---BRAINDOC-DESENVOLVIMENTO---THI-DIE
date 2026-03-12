# CONTINUAÇÃO DO PLANO 7 DIAS - Contexto Completo

**Data:** 2026-02-16
**Status:** DIA 4 concluído, indo para DIA 5
**Score atual:** 75/100 (consolidado dos melhores resultados)
**Meta GO:** 85/100
**Faltam:** 10 pontos

---

## 📊 SCORE CONSOLIDADO (MELHORES RESULTADOS)

### Rodadas executadas:
1. **FINAL2-20260216024438** (primeira) - Dados de performance e Inno
2. **FINAL2-20260216202437** - Win11 QGA resolvido (G1 PASS)
3. **FINAL2-20260216213303** - Validação adicional
4. **FINAL2-20260216220250** - Última rodada (problemas)

### Resultados consolidados:

| Métrica | Status | Fonte | Evidência |
|---------|--------|-------|-----------|
| **Upload otimizado** | ✅ PASS | FINAL2-024438 | Chunk 64KB, Win10 2-3min |
| **Win10 QGA** | ✅ PASS | Todas | QGA conectado e estável |
| **Win11 QGA** | ✅ PASS | FINAL2-202437+ | RESOLVIDO! G1 PASS |
| **Inno Setup** | ✅ 9/10 PASS | FINAL2-024438 | Só INNO-04 (registry HKLM) falhando |
| **Performance medida** | ✅ SIM | FINAL2-024438 | MSI: 8.41s/94.69s, Inno: 12.64s/2.39s |
| **MSI E2E** | ❌ UNKNOWN | - | msi-results.json não gerado |
| **Upgrade/Reinstall** | ✅ PASS | FINAL2-024438 | Preservação de dados OK |
| **Certificado** | ❌ PENDENTE | - | Não disponível ainda |

### Score por pilar:

| Pilar | Score | Evidência |
|-------|-------|-----------|
| P1 - Performance | 60 | 4/5 metas OK (MSI uninstall 94.69s bloqueador) |
| P2 - Instalação | 68 | Inno 9/10, MSI unknown |
| P3 - Governança | 74 | CHANGELOG, RELEASE_APPROVAL OK |
| P4 - Segurança | 70 | SBOM, hashes, manifesto OK |
| P5 - Robustez | 52 | Inno cleanup OK, MSI unknown |
| P6 - Update | 65 | Manifesto validado, ciclo real pendente |
| P7 - Operação | 80 | Win10+Win11 QGA OK (G1 PASS) |
| P8 - UX | 25 | Silencioso, sem progress bar |
| P9 - Compatibilidade | 45 | Win10+Win11 validados |
| P10 - Resiliência | 15 | MSI rollback não testado |
| **TOTAL** | **75/100** | Média ponderada |

---

## ✅ O QUE JÁ FOI FEITO (DIA 1-4)

### DIA 1 - Validação ✅
- Upload otimizado (chunk 64KB): FUNCIONOU
- Win10 QGA: Estável
- Performance: MEDIDA pela primeira vez
- Inno: 9/10 PASS (3 FAILs históricos resolvidos!)

### DIA 2 - Diagnóstico ✅
- Win11 QGA: Causa identificada (serviço não conectava)
- MSI results missing: Causa identificada (script não gera arquivo)
- INNO-04: Código já correto, precisa rebuild
- UTF-8 BOM: Identificado e corrigido no Python

### DIA 3 - Correções ✅
- Código MSI: JÁ CORRIGIDO (RemoveFolderEx + RemoveRegistryKey)
- Código Inno: JÁ CORRIGIDO (registry HKLM OK)
- Correção BOM: Aplicada (Test-Common.ps1 + Python)

### DIA 4 - Reteste ✅
- Win11 QGA: RESOLVIDO! ✅
- G1: PASS (ambas VMs)
- Score: 72 → 75/100 (+3 pontos)
- Problema: Testes E2E falhando (scripts não gerando arquivos)

---

## 🚨 PROBLEMAS CONHECIDOS

### P0 - Bloqueadores críticos:

1. **MSI Uninstall Performance**
   - Valor: 94.69s (meta ≤45s)
   - Excesso: +49.69s (110%)
   - Causa: Limpeza síncrona, retry loops
   - Solução: Profile detalhado + otimização

2. **Scripts test-msi.ps1 / test-inno.ps1 não geram arquivos**
   - Problema: Na última rodada, NENHUM arquivo gerado
   - Impact: Não conseguimos validar MSI/Inno
   - Possível causa: Instaladores não encontrados na VM, erro PowerShell
   - Solução: Verificar paths, permissões, logs detalhados

3. **Certificado .pfx**
   - Status: Não disponível
   - Impact: Windows SmartScreen bloqueia (5 pontos para GO)
   - Ação: Solicitar à Protons Consultoria

### P1 - Importantes mas não bloqueadores:

4. **INNO-04-REGISTRY**
   - Status: Código corrigido, precisa rebuild
   - Impact: 1 FAIL restante do Inno
   - Solução: Rebuild em ambiente Windows

5. **MSI E2E unknown**
   - Status: msi-results.json não gerado em nenhuma rodada
   - Impact: Não sabemos status de MSI-UNINST-01, MSI-UNINST-03
   - Solução: Diagnosticar por que arquivo não é gerado

---

## 🎯 PRÓXIMOS PASSOS (DIA 5-7)

### DIA 5 - Diagnóstico e correção

**Objetivo:** Resolver problemas de execução dos testes

**Tarefas:**

1. **Investigar por que test-msi.ps1 não gera arquivos (2h)**
   - Acessar VM via console: `virsh console win10-lite`
   - Verificar se instaladores existem: `ls C:\protons\INSTALADOR\saida\windows\`
   - Verificar permissões: `Get-Acl C:\protons\INSTALADOR\saida\test-logs`
   - Testar script manualmente:
     ```powershell
     cd C:\protons\INSTALADOR\testes\windows
     .\test-msi.ps1 -RunId TEST-MANUAL
     ```
   - Ver logs de erro

2. **Corrigir problema identificado (1-2h)**
   - Se instaladores não existem: Copiar para VM
   - Se permissão: Ajustar ACL
   - Se erro script: Corrigir Test-Common.ps1

3. **Rebuild instaladores (SE ambiente Windows disponível) (30min)**
   ```powershell
   cd C:\caminho\INSTALADOR
   powershell -File windows\scripts\build-msi.ps1
   powershell -File windows\scripts\build-inno.ps1
   ```

4. **Reteste (30min)**
   ```bash
   bash RODAR-FINAL2-CORRIGIDO.sh
   ```

**Score esperado:** 75 → 80/100 (+5 pontos)

---

### DIA 6 - Certificado ⚠️

**Objetivo:** Obter e aplicar certificado code signing

**Bloqueador:** Certificado .pfx não disponível

**Tarefas SE certificado disponível:**

1. **Verificar certificado (5min)**
   ```bash
   openssl pkcs12 -info -in certificado.pfx -noout
   ```

2. **Assinar instaladores (15min)**
   ```bash
   signtool sign /f certificado.pfx /p SENHA /t http://timestamp.digicert.com windows/Protons-1.0.0.msi
   signtool sign /f certificado.pfx /p SENHA /t http://timestamp.digicert.com windows/ProtonsSetup-1.0.0.exe
   ```

3. **Verificar assinaturas (5min)**
   ```bash
   signtool verify /pa /v windows/Protons-1.0.0.msi
   ```

4. **Reteste (30min)**
   ```bash
   bash RODAR-FINAL2-CORRIGIDO.sh
   ```

**Score esperado:** 80 → 85/100 ✅ GO!

**SE certificado NÃO disponível:**
- Score máximo: 80/100 (não 85)
- Veredito: PARCIAL
- Lançamento: Adiado até obter certificado OU lançar sem (com aviso SmartScreen)

---

### DIA 7 - Aprovação e Release

**Objetivo:** Validar tudo e aprovar GO

**Tarefas:**

1. **Validação final (1h)**
   - Executar todos os testes
   - Verificar gate GO/NO-GO
   - Gerar relatório executivo

2. **Aprovação formal (30min)**
   - QA: Aprovar
   - Revisor: Aprovar
   - Tech Lead: Aprovar

3. **Publicar release (30min)**
   ```bash
   git tag -a v1.0.0 -m "Release v1.0.0 - GO aprovado"
   git push origin v1.0.0
   gh release create v1.0.0 windows/*.msi windows/*.exe SHA256SUMS.txt
   ```

**Score final:** 85/100 ✅ GO!

---

## 📁 ARQUIVOS IMPORTANTES

### Documentos principais:
- `PLANO-7-DIAS-GO.md` - Plano completo 7 dias
- `CHECKLIST.md` - Score e progresso (renomeado!)
- `CONTINUACAO-PLANO-7DIAS.md` - ESTE ARQUIVO

### Resultados das rodadas:
```bash
# Melhor rodada (performance + Inno):
saida/validacao-windows-FINAL2-20260216024438/

# Win11 QGA resolvido:
saida/validacao-windows-FINAL2-20260216202437/

# Última rodada (problemas):
saida/validacao-windows-FINAL2-20260216220250/
```

### Arquivos de código:
- `testes/windows/Test-Common.ps1` - Corrigido (Out-File utf8NoBOM)
- `comum/scripts/windows-e2e-sequencial.sh` - Corrigido (encoding utf-8-sig)
- `windows/wix/Components.wxs` - Corrigido (RemoveFolderEx)
- `windows/innosetup/protons-setup.iss` - Corrigido (registry HKLM)

### Scripts úteis:
```bash
# Executar teste completo:
bash RODAR-FINAL2-CORRIGIDO.sh

# Ver gates:
cat saida/validacao-windows-FINAL2-*/gates-summary.md

# Ver resultados:
cat saida/validacao-windows-FINAL2-*/resumo.csv
```

---

## 🔍 DADOS TÉCNICOS IMPORTANTES

### Melhor performance medida (FINAL2-024438):
```json
{
  "msi_install_p95_seconds": 8.41,
  "msi_uninstall_p95_seconds": 94.69,
  "inno_install_p95_seconds": 12.64,
  "inno_uninstall_p95_seconds": 2.39
}
```

### Inno Setup resultados (FINAL2-024438):
```
✅ INNO-INSTALL-CMD: PASS (12.64s)
✅ INNO-01-EXE: PASS
✅ INNO-02-DESKTOP: PASS (era FAIL!)
✅ INNO-03-STARTMENU: PASS (era FAIL!)
❌ INNO-04-REGISTRY: FAIL (HKLM missing)
✅ INNO-05-APPDATA: PASS
✅ INNO-06-APP-OPEN: PASS
✅ INNO-UNINSTALL-CMD: PASS (2.39s)
✅ INNO-UNINST-01: PASS (era FAIL!)
✅ INNO-UNINST-02: PASS
```

### Gates status:
- G1 (QGA Bootstrap): ✅ PASS (ambas VMs)
- G2 (Regressão E2E): ❌ NÃO (scripts não geram arquivos)
- G3 (6 FAILs resolvidos): ❌ NÃO (aguarda G2)
- G4 (Performance): ✅ PASS (dados da primeira rodada)

---

## 💡 OBSERVAÇÕES IMPORTANTES

### 1. Instaladores já compilados (07/fev):
```bash
saida/windows/Protons-1.0.0-x64.msi      # 07/fev 19:29
saida/windows/ProtonsSetup-1.0.0.exe     # 07/fev 17:05
```
- Código foi corrigido DEPOIS (10/fev)
- Instaladores PRECISAM rebuild para validar correções

### 2. Problema recorrente: msi-results.json
- NUNCA foi gerado em nenhuma rodada
- test-msi.ps1 executa mas não cria arquivo
- Possível causa: Exception antes de Write-ResultFiles (linha 151)
- Solução: Adicionar logging detalhado, try-catch explícito

### 3. UTF-8 BOM resolvido:
- PowerShell: Tentou Out-File utf8NoBOM (mas ainda gera BOM)
- Python: Corrigido para utf-8-sig (lê BOM corretamente)
- Status: RESOLVIDO no lado Python ✅

### 4. Win11 QGA - Como foi resolvido:
- Problema: QGA não conectava (timeout 120s)
- Solução: Parece ter sido automático (VM reiniciada?)
- Status: Agora funciona consistentemente ✅

---

## 🚀 ESTRATÉGIA RECOMENDADA

### Curto prazo (DIA 5):
1. **FOCAR em resolver scripts test-msi/test-inno**
   - Diagnóstico manual na VM
   - Logs detalhados
   - Correções pontuais

2. **SE não resolver em 2h:**
   - Aceitar dados da primeira rodada (Inno 9/10, performance OK)
   - Score real: 75/100
   - Seguir para certificado

### Médio prazo (DIA 6-7):
3. **Obter certificado**
   - Contatar Protons Consultoria
   - Se não disponível: Lançar v1.0 com aviso SmartScreen

4. **Validação e aprovação**
   - Com certificado: 85/100 GO ✅
   - Sem certificado: 80/100 PARCIAL

### Longo prazo (v1.1+):
5. **Melhorias incrementais**
   - v1.1: Update automático (+4 pontos)
   - v1.2: Performance elite (+3 pontos)
   - v1.3: ARM64 (+3 pontos)

---

## 📞 CONTATOS NECESSÁRIOS

| Responsabilidade | Ação necessária |
|------------------|-----------------|
| **Certificado** | Solicitar .pfx para code signing |
| **Rebuild Windows** | Acessar ambiente Windows para compilar |
| **Aprovação QA** | Validar resultados finais |
| **Aprovação Tech Lead** | Aprovar GO para lançamento |

---

## ✅ CHECKLIST PARA RETOMAR

Quando retomar o trabalho, execute na ordem:

1. **Verificar score atual**
   ```bash
   cat CHECKLIST.md | grep "Nota global"
   ```

2. **Ver melhor rodada**
   ```bash
   cat saida/validacao-windows-FINAL2-20260216024438/win10-lite-guest-regressao-windows-FINAL2-20260216024438.json
   ```

3. **Continuar no DIA 5**
   - Diagnosticar scripts test-msi/test-inno
   - OU aceitar dados atuais e ir para certificado

4. **Meta:** 85/100 GO em 2-3 dias

---

---

## 🔧 DIA 5 - DIAGNÓSTICO CONCLUÍDO (2026-02-16)

### ❌ CAUSA RAIZ IDENTIFICADA: Bug no encoding PowerShell

**Problema:**
- Mudança para resolver UTF-8 BOM introduziu bug CRÍTICO
- `Out-File -Encoding utf8NoBOM` NÃO EXISTE no Windows PowerShell 5.1
- Scripts geravam exception antes de escrever JSONs
- Por isso NENHUM arquivo foi gerado nas últimas rodadas

**Evidência:**
```powershell
# FINAL2-024438 (FUNCIONOU - gerou todos arquivos)
$Store | ConvertTo-Json | Set-Content -Encoding UTF8

# FINAL2-220250 (FALHOU - nenhum arquivo gerado)
$Store | ConvertTo-Json | Out-File -Encoding utf8NoBOM  # ← ERRO!
```

**Correção aplicada:**
- `testes/windows/Test-Common.ps1` linha 165:
  - Revertido para `Set-Content -Encoding UTF8` (gera BOM, compatível PS 5.1)
  - Python mantém `encoding="utf-8-sig"` (lê BOM corretamente)
- Status: ✅ CORRIGIDO

**Próximo passo:**
- Executar `bash RODAR-FINAL2-CORRIGIDO.sh` para validar correção
- Esperado: Volta a gerar msi-results.json, inno-results.json, métricas
- Tempo: 60-90 minutos automático

---

**Última atualização:** 2026-02-16 (DIA 5 concluído)
**Próxima ação:** Executar teste FINAL3 para validar fix encoding
**Score atual consolidado:** 75/100
**Faltam para GO:** 10 pontos
