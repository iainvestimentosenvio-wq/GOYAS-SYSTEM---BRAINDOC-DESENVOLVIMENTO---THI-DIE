# Changelog - Sistema de Login Protons v0.2

## Data: 2026-02-01

---

## Resumo das Alteracoes

Esta versao inclui melhorias significativas de seguranca, padronizacao visual e experiencia do usuario.

---

## Seguranca

### Lockout Exponencial
- **Antes**: Bloqueio de 30 segundos apos 5 tentativas falhas
- **Agora**: Bloqueio progressivo com backoff exponencial
  - 1o bloqueio: 15 minutos
  - 2o bloqueio: 30 minutos
  - 3o bloqueio em diante: 1 hora

### Novo Campo de Controle
- Adicionado campo `LockoutsConsecutivos` no modelo User
- Rastreia quantas vezes o usuario foi bloqueado
- Resetado para zero apos login bem-sucedido

### Indices de Performance
Novos indices no banco de dados para melhorar performance:
- `IX_Users_Email` - Acelera busca por email no login
- `IX_Users_LockoutAteUtc` - Acelera verificacao de bloqueios

---

## Interface Visual

### Cores Padronizadas
Recursos globais de cores adicionados em `App.axaml`:
- **ErrorBrush** (#FF4444) - Mensagens de erro (vermelho)
- **SuccessBrush** (#44BB44) - Mensagens de sucesso (verde)
- **InfoBrush** (#4CC3FF) - Informacoes (azul)
- **WarningBrush** (#FFB454) - Avisos (laranja)

### Indicadores de Carregamento
- ProgressBar indeterminado durante operacoes assincronas
- Texto "Autenticando..." / "Processando..." durante carregamento
- Feedback visual claro para o usuario

### Campo Empresa no Cadastro
- Campo "Empresa" adicionado no formulario de registro
- Agora usuarios podem informar sua empresa ao criar conta

---

## Confirmacao de Acoes

### Dialogo de Confirmacao
Novo dialogo `ConfirmDialog` para acoes criticas:
- Aparece antes de REJEITAR usuario
- Aparece antes de PROMOVER a administrador
- Mostra mensagem clara sobre a consequencia da acao
- Opcoes "Cancelar" e "Confirmar"

---

## Arquivos Modificados

### Core (Logica de Negocios)
- `Protons.Core/Login/modelos/User.cs` - Campo LockoutsConsecutivos
- `Protons.Core/Login/servicos/AuthService.cs` - Lockout exponencial

### Infrastructure (Banco de Dados)
- `Protons.Infrastructure/Login/banco_de_dados/SqliteDb.cs` - Indices + coluna
- `Protons.Infrastructure/Login/banco_de_dados/PostgresDb.cs` - Indices + coluna
- `Protons.Infrastructure/Login/repositorios/UserRepository.cs` - Mapeamento
- `Protons.Infrastructure/Login/repositorios/PostgresUserRepository.cs` - Mapeamento

### UI (Interface)
- `Protons.UI/App.axaml` - Recursos de cores globais
- `Protons.UI/Login/telas/LoginView.axaml` - ProgressBar + cores
- `Protons.UI/Login/telas/RegisterView.axaml` - Campo Empresa + cores
- `Protons.UI/Login/telas/AdminApprovalView.axaml` - ProgressBar + cores
- `Protons.UI/Login/telas/AdminApprovalView.axaml.cs` - Integracao dialogo
- `Protons.UI/Login/modelos_de_visao/AdminApprovalViewModel.cs` - Confirmacao

### Novos Arquivos
- `Protons.UI/Login/telas/ConfirmDialog.axaml` - Dialogo de confirmacao
- `Protons.UI/Login/telas/ConfirmDialog.axaml.cs` - Code-behind

---

## Compatibilidade

- .NET 8.0
- Avalonia UI 11.3.11
- SQLite (local) e PostgreSQL (servidor)
- Windows x64 e Linux x64

---

## Como Testar

1. **Lockout Exponencial**
   - Tente login com senha errada 5 vezes
   - Verifique mensagem mostrando tempo de bloqueio (15 minutos)
   - Aguarde expirar e repita para ver 30 minutos

2. **Cores de Mensagens**
   - Verifique que mensagens de erro/aviso usam cores consistentes

3. **Campo Empresa**
   - Acesse "Criar conta" e verifique campo Empresa

4. **Confirmacao**
   - Como admin, tente rejeitar um usuario
   - Verifique dialogo de confirmacao aparece

---

## Notas de Migracao

Se o banco de dados ja existe, a coluna `LockoutsConsecutivos` sera criada com valor padrao 0.
Os indices serao criados automaticamente se nao existirem.
