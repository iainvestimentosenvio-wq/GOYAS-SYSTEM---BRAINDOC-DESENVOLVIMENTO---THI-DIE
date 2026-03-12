# 07 - Plano de Testes

## Estratégia geral
- Priorizar testes de segurança e auditoria.
- Cobrir regras de negócio críticas (status, perfis, bloqueio/lockout).
- Garantir compatibilidade com ambiente offline.

## Testes unitários
**Objetivo**: validar regras isoladas da camada Core.

- Hashing:
  - PBKDF2 gera hash diferente para salts diferentes.
  - Senha válida retorna `true`.
  - Senha acima de 256 caracteres é rejeitada.
- Validação de CPF:
  - CPF válido passa.
  - CPF inválido falha.
- Validação de e-mail:
  - Formato inválido não permite cadastro/login.
- Limites de campos:
  - Nome/Empresa/Cargo acima do limite são rejeitados.
  - E-mail acima de 254 caracteres é rejeitado.
- Lockout:
  - Após 5 falhas, `LockoutAteUtc` é preenchido.
  - Antes de expirar, bloqueia login.
- Perfis:
  - Usuário comum não acessa modo Administrador.

## Testes de integração
**Objetivo**: validar persistência e fluxo completo no SQLite.

- Repositórios SQLite:
  - Inserção e consulta de usuário por e-mail.
  - Atualização de status e bloqueio (lockout).
- Fluxos completos:
  - Criar usuário → status PENDENTE.
  - Primeiro usuário → status ATIVO e Role=Admin.
  - Aprovar usuário → status ATIVO.
  - Promover usuário → Role=Admin.
  - Rejeitar usuário → status BLOQUEADO.
- Auditoria:
  - Cada ação crítica gera registro no `AuditLog`.
  - Cadeia de hash (se ativa) mantém integridade.

## Testes manuais
**UX e tema**:
- Verificar legibilidade no tema escuro.
- Checar mensagens de erro e estados visuais.

**Performance**:
- Login em até 1s em máquina antiga.
- Listas de auditoria com paginação/virtualização.

**Segurança**:
- Confirmar que não há senha em texto no banco.
- Validar que o último e-mail é o único dado persistido em `settings.json`.
- Verificar criação do `log_ops.jsonl` no diretório de dados do app.

## Critérios de aceite (Definição de Pronto - DoD)
- Login, cadastro e aprovação funcionando no fluxo offline.
- PBKDF2 + salt implementado e validado por testes.
- Lockout funcional com mensagens claras.
- AuditLog persistente e legível.
- UI responsiva em máquina alvo (i5 legado + HDD).
- Documentação atualizada e revisada.
