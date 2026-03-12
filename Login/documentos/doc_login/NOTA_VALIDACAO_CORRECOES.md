# Nota de validação – correções (PR)

## 1. PasswordHasher.Verify – null/Base64 inválido
- **Código:** Validação de `password`, `hash`, `salt`, `iterations`; `TryFromBase64String` para evitar exceção em Base64 inválido; retorno `false` sem lançar.
- **Testes:** Novos testes em `PasswordHasherTests`: `Verify_ShouldReturnFalse_WhenPasswordIsNull`, `WhenHashIsNull`, `WhenSaltIsNull`, `Verify_ShouldReturnFalse_WhenHashOrSaltInvalidBase64`, `Verify_ShouldReturnFalse_WhenIterationsZeroOrNegative`.
- **Evidência:** `dotnet test testes/Protons.Core.Tests/Protons.Core.Tests.csproj -c Release` — todos passando.

## 2. LocalSettings.LoadLastEmail – JSON inválido
- **Código:** `try/catch (JsonException)`; JSON vazio retorna `null`.
- **Validação manual:** Criar arquivo de settings vazio ou com `{ invalid }` e chamar `LoadLastEmail()` — deve retornar `null` sem exceção.

## 3. UserRepository – parsing de datas com cultura
- **Código:** `DateTime.Parse(..., CultureInfo.InvariantCulture)` em `Map` e `TryReadDate`.
- **Validação:** Rodar app com cultura pt-BR (ou outra); criar usuário e reler — datas em ISO devem ser lidas corretamente. Testes de integração com DB preenchido em formato ISO também validam.

## 4. AuditService – hash chain e concorrência
- **Código:** `lock (HashChainLock)` ao redor de GetLastHash + cálculo + Insert quando `usarHashChain = true`.
- **Validação manual:** Teste de carga com 2+ threads chamando `Registrar(..., usarHashChain: true)` e conferir que cada registro tem `PrevHash` igual ao `Hash` do anterior (chain consistente).

## 5. CriarConta – validar Empresa/Nome/Cargo
- **Código:** Verificação `string.IsNullOrWhiteSpace` para Empresa, Nome e Cargo; retorno "Preencha Empresa, Nome e Cargo." antes de persistir.
- **Requisito de negócio:** Decisão produto: campos obrigatórios. Se no futuro forem opcionais, remover esta validação.
- **Testes:** `CriarConta_ShouldReturnFailure_WhenEmpresaNomeOuCargoVazios`; testes de sucesso atualizados com Empresa/Cargo preenchidos.

## 6. ViewLocator – resolução por assembly
- **Código:** Uso de `param.GetType().Assembly.GetType(viewName)` em vez de `Type.GetType(name)`.
- **Validação:** Abrir app e navegar por Login, Cadastro, Admin, Home — todas as views devem carregar. Em publicação trimmed/AOT, revalidar se necessário.

## 7. UserRepository.Create – ExecuteScalar nulo
- **Código:** Checagem `result is null || result == DBNull.Value`; lança `InvalidOperationException` com mensagem clara.
- **Validação:** Em cenário de falha de constraint (ex.: e-mail duplicado), o INSERT falha e a exceção do provider sobe; em falhas que retornem null no scalar, a exceção controlada é lançada.

## 8. RegisterViewModel – fluxo pós-cadastro
- **Código:** Após `CriarConta` com sucesso, chama `_navigate(new LoginViewModel(...))`.
- **Requisito de negócio:** Decisão produto: redirecionar para a tela de login após cadastro enviado para aprovação.
- **Validação manual:** Cadastrar com sucesso e confirmar que a tela volta para Login com mensagem de sucesso.

## 9. SqliteDb – caminho do DB
- **Código:** No construtor, `DbPath = Path.IsPathRooted(dbPath) ? dbPath : Path.GetFullPath(dbPath)`.
- **Validação:** Verificar que o DB é criado no caminho esperado:  
  - Windows: `%AppData%\Protons\Login\database\protons.db`  
  - Linux: `XDG_DATA_HOME/Protons/Login/database/protons.db` (fallback `~/.local/share/Protons/...`)  
  O `App.axaml.cs` já usa caminho absoluto; com path relativo, passa a ser resolvido para o diretório atual de forma explícita.

## 10. AdminApprovalViewModel – seleção após remover
- **Código:** Após `Pendentes.Remove(Selecionado)` em Aprovar e Rejeitar, `Selecionado = null`.
- **Validação manual:** Aprovar ou rejeitar um item e confirmar que a seleção visual não fica presa ao item removido.

---

## Riscos / Assunções / Gaps

- **ViewLocator (linha 22):** A resolução por assembly pressupõe View e ViewModel no **mesmo assembly**. Se View ou ViewModel forem movidos para outro projeto (assembly), o locator deixa de encontrar o tipo e volta a exibir "Not Found". Solução futura: mapeamento explícito (dicionário ViewModel→View) ou resolução pelo assembly da View.
- **AuditService – hash chain:** Mitigado com `AuditChain` (tabela de controle) e atualização otimista em transação. A cadeia fica consistente mesmo com múltiplas instâncias usando o mesmo DB. Para manter isso em produção, usar DB centralizado (ex.: servidor) e evitar arquivo SQLite em pasta compartilhada.
- **Evidência de testes:** Testes automatizados do Core podem ser executados localmente com `dotnet test testes/Protons.Core.Tests/Protons.Core.Tests.csproj -c Release`.
