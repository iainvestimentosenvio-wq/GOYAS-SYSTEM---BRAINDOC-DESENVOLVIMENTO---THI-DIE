# 01 - Requisitos

## Requisitos funcionais (RF)
- **RF-01**: Permitir login com e-mail e senha.
- **RF-02**: Permitir cadastro de usuário com campos obrigatórios: Empresa, Nome, CPF, Cargo, E-mail e Senha.
- **RF-03**: Registrar usuário recém-cadastrado com status **PENDENTE**.
- **RF-04**: Permitir aprovação/rejeição de usuários pelo administrador **na mesma máquina** (offline).
- **RF-05**: Aplicar bloqueio temporário após falhas consecutivas (ex.: 5 tentativas → 30 segundos).
- **RF-06**: Permitir “Lembrar meu e-mail” (armazenar apenas o último e-mail localmente).
- **RF-07**: Exigir perfil **Administrador (Admin)** para “Entrar como Administrador”.
- **RF-08**: Registrar auditoria para eventos críticos (login com sucesso/falha, cadastro, aprovação/rejeição, bloqueio/lockout, logout).
- **RF-09**: Exibir mensagens de erro claras e não ambíguas (sem revelar informações sensíveis).
- **RF-10**: Permitir promover usuário a **Admin** (apenas por Admin).

## Requisitos não funcionais (RNF)
- **RNF-01 (Segurança)**: Nunca armazenar senha em texto; usar **PBKDF2 + salt**.
- **RNF-02 (Disponibilidade)**: Operar **offline** (sem servidor e sem internet).
- **RNF-03 (Performance)**: Responder em até 1s em PCs com i5 antigo + HDD (fluxo de login e navegação básica).
- **RNF-04 (Compatibilidade)**: Windows e Linux, Avalonia UI, .NET 8.
- **RNF-05 (Persistência)**: SQLite local em `%AppData%\Protons` (Windows) e `XDG_DATA_HOME/Protons` (Linux, com fallback `~/.local/share/Protons`).
- **RNF-06 (Observabilidade)**: log de auditoria persistente e consultável.
- **RNF-07 (Privacidade)**: Evitar registrar dados sensíveis em logs (ex.: senha, CPF completo).
- **RNF-08 (Manutenibilidade)**: Separação de camadas (UI/Core/Infraestrutura) e padrão MVVM.

## Regras de negócio (RB)
- **RB-01**: Usuário com status **PENDENTE** não pode acessar o sistema.
- **RB-02**: Usuário **BLOQUEADO** não pode autenticar até expirar o bloqueio (lockout).
- **RB-03**: Aprovação é ação administrativa e deve gerar auditoria com motivo.
- **RB-04**: Rejeição altera status para **REJEITADO** ou **BLOQUEADO** (definir como **BLOQUEADO** no MVP).
- **RB-05**: Login de administrador exige perfil `Admin`.
- **RB-06**: Primeiro usuário criado é **Admin** e entra como **ATIVO**.

## Requisitos de interface (UI)
- Layout moderno e discreto (Avalonia UI, tema escuro).
- Campo de e-mail com validação visual.
- Mensagens de erro padronizadas.
- Navegação clara entre Login, Cadastro e Administrador.

## Requisitos de auditoria
- Campos mínimos: `timestamp`, `userId/email`, `acao`, `resultado`, `detalhes` (sem dados sensíveis), `maquina`, `versaoApp`.
- Persistir logs localmente no SQLite.
- (Opcional) Encadeamento de hash para detectar adulterações.
