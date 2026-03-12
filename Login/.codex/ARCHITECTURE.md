# Arquitetura do Projeto (Codex)

## Objetivo
Padronizar a criação e manutenção de código seguindo **MVVM** e separação de camadas, mantendo o MVP local/offline.

## Camadas e responsabilidades
- **UI (Views/ViewModels)**
  - Tela, binding e validações visuais.
  - **Não** acessa banco diretamente.
- **Core (Domain/Services)**
  - Regras de negócio, validações, serviços (ex.: AuthService, AuditService).
- **Infraestrutura (Repositories)**
  - Persistência SQLite e acesso a dados.

## Regras de arquitetura
- **UI → Core → Infraestrutura** (fluxo único).
- **Sem dependências inversas** (Infraestrutura não depende de UI).
- **Serviços do Core** não devem conhecer Avalonia UI.

## Estrutura sugerida
- `Protons.UI/`
  - `Login/`
  - `Importacao/` (futuro)
  - `Dominio/` (futuro)
  - `Prefeitura/` (futuro)
- `Protons.Core/`
- `Protons.Infrastructure/`

## Checklist de conformidade
- [ ] ViewModels sem acesso direto ao SQLite
- [ ] Regras de negócio isoladas no Core
- [ ] Repositórios isolados na Infraestrutura
- [ ] Camadas comunicam apenas via interfaces/DTOs

## Como testar aderência
- [ ] Revisar imports/referências entre camadas
- [ ] Verificar se cada serviço tem doc espelhada
- [ ] Rodar testes unitários e de integração (quando existirem)
