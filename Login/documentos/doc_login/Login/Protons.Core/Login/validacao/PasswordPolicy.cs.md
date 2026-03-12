# PasswordPolicy.cs

## Arquivo
`Login/Protons.Core/Login/validacao/PasswordPolicy.cs`

## Objetivo
Centralizar a politica de senha do sistema com rollout seguro:
- Regra forte para cadastro/troca.
- Compatibilidade para login de senhas legadas.

## Regras atuais
- `MinLength = 12`
- `MaxLength = 256`
- `EhValida(string?)`:
  - exige minimo de 12 caracteres;
  - exige pelo menos 3 de 4 grupos: maiuscula, minuscula, digito, especial.
- `EhValidaParaLogin(string?)`:
  - aceita senha nao vazia;
  - valida somente limite maximo (compatibilidade de login legado).
- `ObterRequisitos()`:
  - retorna mensagem unica para UI/servicos com os requisitos oficiais.

## Pontos de atencao
- Nao usar `EhValida` no login, para evitar bloquear usuarios antigos.
- Usar `ObterRequisitos()` para manter mensagem de erro sincronizada com a regra real.

## Como testar
- `PasswordPolicyTests` em `Login/testes/Protons.Core.Tests/Validation/PasswordPolicyTests.cs`.
