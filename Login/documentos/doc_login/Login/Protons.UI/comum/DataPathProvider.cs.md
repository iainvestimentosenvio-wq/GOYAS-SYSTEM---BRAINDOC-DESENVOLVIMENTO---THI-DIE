# DataPathProvider.cs

## Objetivo
Utilitario Data Path Provider compartilhado na UI.

## Responsabilidades
- Funcionalidades reutilizaveis da camada UI.
- Suporte a logging/configuracao local.

## Dependencias
- Namespace: Protons.UI.Common
- Tipo: class DataPathProvider

## Principais metodos
- GetAppDataPath()
- GetModuleDataPath()
- EnsureAppDataPathExists()
- EnsureModuleDataPathExists()

## Fluxo principal
- Executar as responsabilidades principais conforme a camada do modulo.

## Pontos de atencao
- Validar entradas e estados esperados.
- Manter compatibilidade com as regras de negocio atuais.

## Como testar
- Validar em runtime (startup e operacao normal).
