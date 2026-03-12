# Roteiro de Testes Manuais - MVP Login

## Objetivo
Validar fluxos críticos do MVP (cadastro, aprovação, login, lockout, persistência e performance).

## Ambiente
- Windows e/ou Linux
- Modo local (SQLite) ou servidor (Postgres), conforme configuração
- App iniciado normalmente

## Pré-requisitos
- App configurado com `appsettings.json` válido
- Diretório de dados acessível
- Se modo servidor: banco ativo e acessível

## Roteiro

### 1) Fluxo completo de cadastro
1. Abrir a tela de Cadastro.
2. Preencher Empresa, Nome, CPF válido, Cargo, E-mail e Senha válida.
3. Clicar em **Salvar**.
4. Verificar mensagem "Cadastro enviado para aprovação".
5. Confirmar navegação para a tela de Login.
6. Se for o primeiro usuário do sistema, validar que ele entra como Admin e fica ATIVO.

**Resultado esperado:** cadastro criado com status Pendente; mensagem exibida; navegação correta.

### 2) Aprovação por administrador
1. Entrar como Admin (login admin válido).
2. Abrir tela de Aprovações.
3. Selecionar o usuário pendente e clicar **Aprovar**.
4. Verificar mensagem "Usuário aprovado." e remoção da lista.

**Resultado esperado:** status do usuário passa para Ativo; log de auditoria criado.

### 3) Login após aprovação
1. Voltar à tela de Login.
2. Entrar com o usuário recém-aprovado.

**Resultado esperado:** login bem-sucedido; navegação para Home.

### 4) Lockout após falhas
1. Tentar logar com senha errada 5 vezes seguidas.
2. Na 5ª tentativa, verificar bloqueio.
3. Aguardar 30 segundos.
4. Tentar login novamente com senha correta.

**Resultado esperado:** bloqueio temporário ativado; após 30s, login volta a funcionar.

### 5) Mensagens de erro consistentes
1. Tentar login com e-mail inválido.
2. Tentar login com senha inválida.
3. Tentar login com usuário inexistente.

**Resultado esperado:** mensagens genéricas de credenciais inválidas (sem revelar detalhes).

### 6) Persistência do último e-mail
1. Marcar "Lembrar meu e-mail" e fazer login.
2. Fechar e abrir o app novamente.

**Resultado esperado:** e-mail preenchido automaticamente.

### 7) Diretório de dados correto
1. Verificar criação do banco e settings no diretório correto.
   - Windows: `%AppData%\Protons`
   - Linux: `XDG_DATA_HOME/Protons` (fallback `~/.local/share/Protons`)

**Resultado esperado:** arquivos criados no diretório correto.

### 8) Performance (tempo de abertura)
1. Em um PC mais antigo (HDD/i5), medir tempo de abertura do app.
2. Cronometrar até a tela de login aparecer.

**Resultado esperado:** login abre em menos de 3 segundos.

### 9) Logs de auditoria (paginação)
1. Entrar como Admin.
2. Abrir a tela **Ver Logs**.
3. Verificar carregamento da primeira página.
4. Navegar **Próxima** e **Anterior** e observar a atualização da lista.
5. Ao chegar ao fim, validar mensagem "Sem mais registros".

**Resultado esperado:** logs carregam sem travar e a paginação funciona.

## Registro
Para cada item, registrar:
- Data
- Ambiente (Windows/Linux, local/servidor)
- Resultado (OK/Erro)
- Observações
