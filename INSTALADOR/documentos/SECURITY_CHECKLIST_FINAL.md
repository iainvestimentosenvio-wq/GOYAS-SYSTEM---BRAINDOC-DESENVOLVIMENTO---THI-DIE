# SECURITY CHECKLIST FINAL

DataUTC: 2026-02-15T10:40:33Z
RunId: 20260215T104033Z
Politica: 100% conservadora (somente evidência executada).

| Controle | Status | Evidência |
| --- | --- | --- |
| Termos proibidos | PASS | `saida/validacao-max-score-20260215T104033Z/01-validate-prohibited-terms.log` |
| SBOM válido | PASS | `saida/validacao-max-score-20260215T104033Z/05-validate-sbom.log` |
| Update manifest estrito | PASS | `saida/validacao-max-score-20260215T104033Z/06-validate-update-manifest-strict.log` |
| Testes de manifesto | PASS | `saida/validacao-max-score-20260215T104033Z/07-test-update-manifest.log` |
| Hashes de release | PASS | `saida/validacao-max-score-20260215T104033Z/22-test-release-hashes.log` |
| Pinning e assinatura no update | PASS | `saida/validacao-max-score-20260215T104033Z/24-test-update-manifest-pinning.log` |
| Certificado corporativo disponível | SKIP/BLOQUEADO | `saida/validacao-max-score-20260215T104033Z/23-test-code-signing-cert.log` |
| GO/NO-GO automatizado | PASS (decisão NO-GO coerente) | `saida/validacao-max-score-20260215T104033Z/98-test-go-nogo-gate.log` |

## Bloqueios de segurança remanescentes

1. Certificado `.pfx` corporativo indisponível.
2. Assinatura Authenticode final de MSI/EXE pendente.
3. SmartScreen/Defender com assinatura final pendente de validação Windows.
