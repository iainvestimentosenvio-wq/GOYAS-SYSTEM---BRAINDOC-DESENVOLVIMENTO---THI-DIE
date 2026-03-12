# AuthService.cs

## Arquivo
`Login/Protons.Core/Login/servicos/AuthService.cs`

## Objetivo
Implementar autenticacao, cadastro de contas e aprovacao administrativa de usuarios.

## Regras de senha aplicadas
- `Autenticar(...)` usa `PasswordPolicy.EhValidaParaLogin(...)`.
  - Motivo: permitir login de senhas legadas durante transicao.
- `CriarConta(...)` usa `PasswordPolicy.EhValida(...)` (regra forte).
- Em senha invalida no cadastro, a mensagem vem de `PasswordPolicy.ObterRequisitos()`.

## Fluxos de aprovacao
- `AprovarUsuario(...)`: ativa usuario comum.
- `RejeitarUsuario(...)`: bloqueia usuario.
- `PromoverUsuarioAdmin(...)`: ativa e promove para admin.

## Auditoria
- Todas as acoes relevantes gravam entrada de auditoria (`LOGIN_*`, `CRIAR_CONTA`, `APROVAR_USUARIO`, `REJEITAR_USUARIO`, `PROMOVER_ADMIN`, `LOGOUT`).

## Pontos de atencao
- Primeiro usuario continua como admin inicial.
- Demais usuarios seguem fluxo pendente para aprovacao.

## Como testar
- `Login/testes/Protons.Core.Tests/Services/AuthServiceTests.cs`.
