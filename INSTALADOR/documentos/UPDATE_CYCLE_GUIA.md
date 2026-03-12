# UPDATE CYCLE GUIA - Protons

DataUTC: 2026-02-15T10:53:00Z

## Ciclo Completo de Update

1. App verifica manifesto (HTTP GET) → `https://updates.protons.com.br/update-manifest.json`
2. Compara versão local (1.0.0) vs remota (1.1.0)
3. Se nova versão disponível: Download do instalador
4. Verifica hash SHA-256 antes de executar
5. Valida assinatura RSA com chave pública pinned
6. Executa instalador silencioso (`/silent`)
7. Preserva dados do usuário (`%APPDATA%\Protons`)

## Casos de Erro

- **Rede cai durante download**: Retry automático (3 tentativas)
- **Hash inválido**: Abortar update, alertar usuário
- **Instalação falha**: Rollback (MSI) ou cleanup manual (Inno)

## Status Atual

- ✅ Manifesto com assinatura: PASS
- ✅ Pinning de chave pública: PASS  
- ❌ Ciclo real HTTP: NÃO TESTADO
- ❌ Servidor HTTP real: NÃO IMPLEMENTADO
