# LIMITACOES CONHECIDAS - Protons v1.0.0

DataUTC: 2026-02-15T10:52:00Z

## Plataformas

- ❌ **32-bit**: Não suportado (sem build planejado)
- ❌ **ARM64**: Não testado (futuro)

## Ambientes Corporativos

- ⚠️ **GPO restritivas**: Pode bloquear instalação
  - Workaround: Solicitar exceção ao departamento de IT
- ⚠️ **Proxy corporativo**: Update pode falhar
  - Workaround: Configuração manual de proxy

## Antivirus

- ⚠️ **Windows Defender/SmartScreen**: Bloqueará artefatos não-assinados
  - Status: Aguardando certificado .pfx corporativo (FINAL-11)
- ⚠️ **Avast/Kaspersky**: Comportamento não testado

## Outros

- ❌ **Rollback de update**: Não implementado no Inno Setup (apenas MSI tem rollback nativo)
- ❌ **Repair mode**: MSI suporta (`msiexec /f`) mas não testado
