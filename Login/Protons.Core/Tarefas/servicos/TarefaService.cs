using System.Text.Json;
using Protons.Core.Clientes.Repositories;
using Protons.Core.Login.Models;
using Protons.Core.Login.Repositories;
using Protons.Core.Login.Services;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Repositories;

namespace Protons.Core.Tarefas.Services;

public sealed class TarefaService : ITarefaService
{
    private const int MaxTitulo = 200;

    private readonly ITarefaRepository _tarefas;
    private readonly IClientePermissaoRepository _clientePermissoes;
    private readonly IUserRepository _users;
    private readonly IClienteRepository _clientes;
    private readonly IAuditService _audit;
    private readonly string _maquina;
    private readonly string _versaoApp;
    private readonly bool _usarHashChain;
    private readonly TimeProvider _timeProvider;

    public TarefaService(
        ITarefaRepository tarefas,
        IClientePermissaoRepository clientePermissoes,
        IUserRepository users,
        IClienteRepository clientes,
        IAuditService audit,
        string maquina,
        string versaoApp,
        bool usarHashChain,
        TimeProvider? timeProvider = null)
    {
        _tarefas = tarefas;
        _clientePermissoes = clientePermissoes;
        _users = users;
        _clientes = clientes;
        _audit = audit;
        _maquina = maquina ?? string.Empty;
        _versaoApp = string.IsNullOrWhiteSpace(versaoApp) ? "0.0.0" : versaoApp;
        _usarHashChain = usarHashChain;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Tarefa? ObterPorId(int tarefaId, int solicitanteUserId)
    {
        if (tarefaId <= 0 || solicitanteUserId <= 0)
            return null;

        var user = _users.GetById(solicitanteUserId);
        if (user is null || user.Status != UserStatus.Ativo)
            return null;

        var tarefa = _tarefas.GetById(tarefaId);
        if (tarefa is null)
            return null;

        return PodeVisualizarTarefa(user, tarefa) ? tarefa : null;
    }

    public IReadOnlyList<Tarefa> Buscar(TarefaFiltroConsulta filtro, int solicitanteUserId)
    {
        if (filtro.ClienteId <= 0 || solicitanteUserId <= 0)
            return [];

        var user = ObterUsuarioAtivoOuFalhar(solicitanteUserId);
        if (!PodeAcessarCliente(user, filtro.ClienteId))
            return [];

        var termo = string.IsNullOrWhiteSpace(filtro.Termo) ? null : filtro.Termo.Trim();
        var inicioUtc = ConverterLocalParaUtcOpcional(filtro.VencimentoInicioLocal);
        var fimUtc = ConverterLocalParaUtcOpcional(filtro.VencimentoFimLocal);
        var somenteProprias = filtro.SomenteMinhasTarefas || user.Role == UserRole.Usuario;
        int? responsavelFiltro = somenteProprias ? solicitanteUserId : null;

        var tarefas = _tarefas.Buscar(filtro.ClienteId, termo, filtro.Status, inicioUtc, fimUtc, responsavelFiltro);
        return FiltrarVisibilidade(user, tarefas);
    }

    public IReadOnlyList<Tarefa> BuscarPorClienteIds(
        IReadOnlyList<int> clienteIds,
        string? termo,
        TarefaStatus? status,
        DateTime? vencimentoInicioUtc,
        DateTime? vencimentoFimUtc,
        bool somenteMinhasTarefas,
        int solicitanteUserId)
    {
        if (clienteIds is null || clienteIds.Count == 0 || solicitanteUserId <= 0)
            return [];

        var user = ObterUsuarioAtivoOuFalhar(solicitanteUserId);
        var idsPermitidos = clienteIds
            .Where(id => id > 0 && PodeAcessarCliente(user, id))
            .Distinct()
            .ToList();

        if (idsPermitidos.Count == 0)
            return [];

        int? responsavelFiltro = somenteMinhasTarefas || user.Role == UserRole.Usuario ? solicitanteUserId : null;
        var tarefas = _tarefas.BuscarPorClienteIds(
            idsPermitidos,
            termo,
            status,
            vencimentoInicioUtc,
            vencimentoFimUtc,
            responsavelFiltro);
        return FiltrarVisibilidade(user, tarefas);
    }

    public IReadOnlyList<Tarefa> BuscarHistoricoGlobal(int solicitanteUserId, int limite)
    {
        if (solicitanteUserId <= 0)
            return [];

        var user = ObterUsuarioAtivoOuFalhar(solicitanteUserId);
        var limiteNormalizado = Math.Clamp(limite, 1, 500);
        var clientes = _clientes.ListarTodos();
        var clienteIds = clientes
            .Where(c => PodeAcessarCliente(user, c.Id))
            .Select(c => c.Id)
            .Distinct()
            .ToList();

        if (clienteIds.Count == 0)
            return [];

        var todas = _tarefas.BuscarPorClienteIds(
            clienteIds,
            termo: null,
            status: null,
            vencimentoInicioUtc: null,
            vencimentoFimUtc: null,
            responsavelUserId: null);

        return FiltrarVisibilidade(user, todas)
            .OrderByDescending(ObterDataHistoricoGlobal)
            .ThenByDescending(t => t.Id)
            .Take(limiteNormalizado)
            .ToList();
    }

    public Tarefa Criar(TarefaCadastroEntrada entrada, int solicitanteUserId)
    {
        var solicitante = ObterUsuarioAtivoOuFalhar(solicitanteUserId);
        ValidarClienteAtivo(entrada.ClienteId);

        var titulo = (entrada.Titulo ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(titulo))
            throw new InvalidOperationException("Informe o titulo da tarefa.");
        if (titulo.Length > MaxTitulo)
            throw new InvalidOperationException("Titulo da tarefa excede o limite permitido.");

        var responsavel = ObterUsuarioAtivoOuFalhar(entrada.ResponsavelUserId);
        ValidarPermissaoCadastro(solicitante, entrada.ClienteId, responsavel.Id);

        var vencimentoUtc = ConverterLocalParaUtcObrigatorio(entrada.VencimentoLocal);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var tarefa = new Tarefa
        {
            ClienteId = entrada.ClienteId,
            FerramentaId = FerramentaTarefaIds.Canonicalizar(entrada.FerramentaId),
            Titulo = titulo,
            VencimentoUtc = vencimentoUtc,
            ResponsavelUserId = responsavel.Id,
            Status = TarefaStatus.Agendada,
            Recorrencia = entrada.Recorrencia,
            DiaRecorrenciaMensal = entrada.Recorrencia == TarefaRecorrencia.Mensal ? entrada.VencimentoLocal.Day : null,
            EsteiraId = entrada.EsteiraId,
            HorarioRecorrencia = entrada.Recorrencia != TarefaRecorrencia.Nenhuma
                ? TimeOnly.FromDateTime(entrada.VencimentoLocal)
                : null,
            DiaSemanaRecorrencia = entrada.Recorrencia == TarefaRecorrencia.Semanal
                ? entrada.VencimentoLocal.DayOfWeek
                : null,
            Ativa = true,
            CriadoPorUserId = solicitante.Id,
            CriadoEmUtc = nowUtc,
            AtualizadoEmUtc = nowUtc
        };

        tarefa.Id = _tarefas.Create(tarefa);
        RegistrarAuditoria(
            solicitante.Id,
            solicitante.Email,
            "TAREFA_CRIADA",
            "OK",
            before: null,
            after: tarefa);

        return tarefa;
    }

    public Tarefa Atualizar(TarefaAtualizacaoEntrada entrada, int solicitanteUserId)
    {
        if (entrada.TarefaId <= 0)
            throw new InvalidOperationException("Tarefa invalida.");

        var solicitante = ObterUsuarioAtivoOuFalhar(solicitanteUserId);
        var tarefa = _tarefas.GetById(entrada.TarefaId) ?? throw new InvalidOperationException("Tarefa nao encontrada.");
        ValidarClienteAtivo(tarefa.ClienteId);

        if (!PodeEditarTarefa(solicitante, tarefa))
            throw new InvalidOperationException("Sem permissao para editar tarefa.");

        var before = Clonar(tarefa);

        if (entrada.FerramentaId is not null)
        {
            tarefa.FerramentaId = FerramentaTarefaIds.Canonicalizar(entrada.FerramentaId);
        }

        if (entrada.Titulo is not null)
        {
            var titulo = entrada.Titulo.Trim();
            if (string.IsNullOrWhiteSpace(titulo))
                throw new InvalidOperationException("Titulo da tarefa nao pode ficar vazio.");
            if (titulo.Length > MaxTitulo)
                throw new InvalidOperationException("Titulo da tarefa excede o limite permitido.");
            tarefa.Titulo = titulo;
        }

        if (entrada.VencimentoLocal.HasValue)
        {
            tarefa.VencimentoUtc = ConverterLocalParaUtcObrigatorio(entrada.VencimentoLocal.Value);
        }

        if (entrada.ResponsavelUserId.HasValue && entrada.ResponsavelUserId.Value != tarefa.ResponsavelUserId)
        {
            var novoResponsavel = ObterUsuarioAtivoOuFalhar(entrada.ResponsavelUserId.Value);

            if (solicitante.Role == UserRole.Supremo)
            {
                // Supremo possui escopo global.
            }
            else if (solicitante.Role == UserRole.Admin)
            {
                if (!UsuarioPertenceAoEscopoAdmin(solicitante.Id, novoResponsavel.Id))
                    throw new InvalidOperationException("Admin só pode atribuir tarefa para si ou para subordinados.");

                var permissao = _clientePermissoes.Obter(tarefa.ClienteId, novoResponsavel.Id);
                if (novoResponsavel.Id != solicitante.Id && permissao is null)
                    throw new InvalidOperationException("Responsavel selecionado nao possui permissao neste cliente.");
            }
            else if (solicitante.Id != novoResponsavel.Id)
            {
                throw new InvalidOperationException("Usuario comum so pode atribuir tarefa para si mesmo.");
            }

            tarefa.ResponsavelUserId = novoResponsavel.Id;
        }

        if (entrada.EsteiraId.HasValue)
        {
            tarefa.EsteiraId = entrada.EsteiraId.Value == 0 ? null : entrada.EsteiraId;
        }

        if (entrada.Recorrencia.HasValue)
        {
            tarefa.Recorrencia = entrada.Recorrencia.Value;
            tarefa.DiaRecorrenciaMensal = tarefa.Recorrencia == TarefaRecorrencia.Mensal
                ? tarefa.VencimentoUtc.ToLocalTime().Day
                : null;

            var vencLocal = tarefa.VencimentoUtc.ToLocalTime();
            tarefa.HorarioRecorrencia = tarefa.Recorrencia != TarefaRecorrencia.Nenhuma
                ? TimeOnly.FromDateTime(vencLocal)
                : null;
            tarefa.DiaSemanaRecorrencia = tarefa.Recorrencia == TarefaRecorrencia.Semanal
                ? vencLocal.DayOfWeek
                : null;
        }

        if (entrada.Status.HasValue)
        {
            tarefa.Status = entrada.Status.Value;
            tarefa.ConcluidaEmUtc = tarefa.Status == TarefaStatus.Concluida ? _timeProvider.GetUtcNow().UtcDateTime : null;
        }

        tarefa.AtualizadoEmUtc = _timeProvider.GetUtcNow().UtcDateTime;
        _tarefas.Update(tarefa);

        RegistrarAuditoria(
            solicitante.Id,
            solicitante.Email,
            "TAREFA_EDITADA",
            "OK",
            before,
            tarefa);

        var concluiuAgora = before.Status != TarefaStatus.Concluida && tarefa.Status == TarefaStatus.Concluida;
        if (concluiuAgora && tarefa.Recorrencia != TarefaRecorrencia.Nenhuma)
        {
            var recorrente = CriarProximaRecorrencia(tarefa, solicitante.Id);
            RegistrarAuditoria(
                solicitante.Id,
                solicitante.Email,
                "TAREFA_RECORRENCIA_GERADA",
                "OK",
                before: null,
                after: recorrente);
        }

        return tarefa;
    }

    public Tarefa AlterarStatus(int tarefaId, TarefaStatus status, int solicitanteUserId)
    {
        return Atualizar(new TarefaAtualizacaoEntrada
        {
            TarefaId = tarefaId,
            Status = status
        }, solicitanteUserId);
    }

    public IReadOnlyList<ClientePermissaoUsuario> ListarPermissoesCliente(int clienteId, int solicitanteUserId)
    {
        if (clienteId <= 0 || solicitanteUserId <= 0)
            return [];

        var solicitante = ObterUsuarioAtivoOuFalhar(solicitanteUserId);
        if (!PodeAcessarCliente(solicitante, clienteId))
            return [];

        return _clientePermissoes.ListarPorCliente(clienteId);
    }

    public void DefinirPermissoesCliente(int clienteId, IReadOnlyList<PermissaoClienteEntrada> permissoes, int adminUserId)
    {
        if (clienteId <= 0)
            throw new InvalidOperationException("Cliente invalido.");
        if (adminUserId <= 0)
            throw new InvalidOperationException("Usuario invalido.");

        var admin = ObterUsuarioAtivoOuFalhar(adminUserId);
        if (admin.Role is not (UserRole.Admin or UserRole.Supremo))
            throw new InvalidOperationException("Somente administrador pode gerenciar permissoes de cliente.");

        ValidarClienteAtivo(clienteId);

        var lista = (permissoes ?? [])
            .Where(p => p.UserId > 0)
            .GroupBy(p => p.UserId)
            .Select(g => new PermissaoClienteEntrada
            {
                UserId = g.Key,
                PodeEditar = g.Any(x => x.PodeEditar)
            })
            .ToList();

        foreach (var permissao in lista)
        {
            var user = ObterUsuarioAtivoOuFalhar(permissao.UserId);
            if (user.Role == UserRole.Admin)
                continue;
        }

        var before = _clientePermissoes.ListarPorCliente(clienteId);
        _clientePermissoes.DefinirPermissoes(clienteId, adminUserId, lista);
        var after = _clientePermissoes.ListarPorCliente(clienteId);

        RegistrarAuditoria(
            admin.Id,
            admin.Email,
            "CLIENTE_PERMISSOES_ATUALIZADAS",
            "OK",
            before,
            after);
    }

    private Tarefa CriarProximaRecorrencia(Tarefa tarefaConcluida, int solicitanteUserId)
    {
        var baseLocal = tarefaConcluida.VencimentoUtc.ToLocalTime();
        DateTime proximoVencimentoLocal;

        switch (tarefaConcluida.Recorrencia)
        {
            case TarefaRecorrencia.Horaria:
                proximoVencimentoLocal = baseLocal.AddHours(1);
                break;

            case TarefaRecorrencia.Diaria:
                proximoVencimentoLocal = baseLocal.AddDays(1);
                break;

            case TarefaRecorrencia.Semanal:
                proximoVencimentoLocal = baseLocal.AddDays(7);
                break;

            case TarefaRecorrencia.Mensal:
            default:
                var proximoMes = new DateTime(baseLocal.Year, baseLocal.Month, 1, baseLocal.Hour, baseLocal.Minute, baseLocal.Second, DateTimeKind.Local)
                    .AddMonths(1);
                var diaPreferencial = tarefaConcluida.DiaRecorrenciaMensal ?? baseLocal.Day;
                var ultimoDia = DateTime.DaysInMonth(proximoMes.Year, proximoMes.Month);
                var dia = Math.Min(Math.Max(1, diaPreferencial), ultimoDia);
                proximoVencimentoLocal = new DateTime(
                    proximoMes.Year,
                    proximoMes.Month,
                    dia,
                    baseLocal.Hour,
                    baseLocal.Minute,
                    baseLocal.Second,
                    DateTimeKind.Local);
                break;
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nova = new Tarefa
        {
            ClienteId = tarefaConcluida.ClienteId,
            FerramentaId = tarefaConcluida.FerramentaId,
            Titulo = tarefaConcluida.Titulo,
            VencimentoUtc = proximoVencimentoLocal.ToUniversalTime(),
            ResponsavelUserId = tarefaConcluida.ResponsavelUserId,
            Status = TarefaStatus.Agendada,
            Recorrencia = tarefaConcluida.Recorrencia,
            DiaRecorrenciaMensal = tarefaConcluida.DiaRecorrenciaMensal,
            EsteiraId = tarefaConcluida.EsteiraId,
            HorarioRecorrencia = tarefaConcluida.HorarioRecorrencia,
            DiaSemanaRecorrencia = tarefaConcluida.DiaSemanaRecorrencia,
            Ativa = true,
            CriadoPorUserId = solicitanteUserId,
            CriadoEmUtc = nowUtc,
            AtualizadoEmUtc = nowUtc
        };

        nova.Id = _tarefas.Create(nova);
        return nova;
    }

    private User ObterUsuarioAtivoOuFalhar(int userId)
    {
        var user = _users.GetById(userId);
        if (user is null || user.Status != UserStatus.Ativo)
            throw new InvalidOperationException("Usuario invalido ou inativo.");
        return user;
    }

    private void ValidarPermissaoCadastro(User solicitante, int clienteId, int responsavelUserId)
    {
        if (solicitante.Role == UserRole.Supremo)
            return;

        if (solicitante.Role == UserRole.Admin)
        {
            if (responsavelUserId == solicitante.Id)
                return;

            if (!UsuarioPertenceAoEscopoAdmin(solicitante.Id, responsavelUserId))
                throw new InvalidOperationException("Admin só pode criar tarefa para si ou para subordinados.");

            var permissaoResponsavel = _clientePermissoes.Obter(clienteId, responsavelUserId);
            if (permissaoResponsavel is null)
                throw new InvalidOperationException("Responsavel sem permissao para o cliente.");
            return;
        }

        var permissaoSolicitante = _clientePermissoes.Obter(clienteId, solicitante.Id);
        if (permissaoSolicitante is null || !permissaoSolicitante.PodeEditar)
            throw new InvalidOperationException("Sem permissao para criar tarefa neste cliente.");

        if (responsavelUserId != solicitante.Id)
            throw new InvalidOperationException("Usuario comum so pode criar tarefa para si mesmo.");
    }

    private bool PodeAcessarCliente(User user, int clienteId)
    {
        if (user.Role is UserRole.Admin or UserRole.Supremo)
            return true;

        return _clientePermissoes.Obter(clienteId, user.Id) is not null;
    }

    private bool PodeVisualizarTarefa(User user, Tarefa tarefa)
    {
        if (user.Role == UserRole.Supremo)
            return true;
        if (user.Role == UserRole.Admin)
            return TarefaPertenceAoEscopoAdmin(user.Id, tarefa);

        return tarefa.ResponsavelUserId == user.Id || tarefa.CriadoPorUserId == user.Id;
    }

    private bool PodeEditarTarefa(User user, Tarefa tarefa)
    {
        if (user.Role == UserRole.Supremo)
            return true;
        if (user.Role == UserRole.Admin)
            return TarefaPertenceAoEscopoAdmin(user.Id, tarefa);

        return tarefa.ResponsavelUserId == user.Id || tarefa.CriadoPorUserId == user.Id;
    }

    private IReadOnlyList<Tarefa> FiltrarVisibilidade(User user, IReadOnlyList<Tarefa> tarefas)
    {
        if (tarefas is null || tarefas.Count == 0)
            return tarefas ?? [];

        if (user.Role == UserRole.Supremo)
            return tarefas;

        if (user.Role == UserRole.Admin)
        {
            var escopo = ObterEscopoAdmin(user.Id);
            return tarefas
                .Where(t => escopo.Contains(t.ResponsavelUserId) || escopo.Contains(t.CriadoPorUserId))
                .ToList();
        }

        return tarefas
            .Where(t => t.ResponsavelUserId == user.Id || t.CriadoPorUserId == user.Id)
            .ToList();
    }

    private bool TarefaPertenceAoEscopoAdmin(int adminId, Tarefa tarefa)
    {
        var escopo = ObterEscopoAdmin(adminId);
        return escopo.Contains(tarefa.ResponsavelUserId) || escopo.Contains(tarefa.CriadoPorUserId);
    }

    private bool UsuarioPertenceAoEscopoAdmin(int adminId, int userId)
    {
        if (userId == adminId)
            return true;

        var escopo = ObterEscopoAdmin(adminId);
        return escopo.Contains(userId);
    }

    private HashSet<int> ObterEscopoAdmin(int adminId)
    {
        var escopo = new HashSet<int> { adminId };
        var subordinados = _users.ListarPorAdmin(adminId) ?? [];
        foreach (var subordinado in subordinados)
        {
            if (subordinado.Status == UserStatus.Ativo)
                escopo.Add(subordinado.Id);
        }

        return escopo;
    }

    private void ValidarClienteAtivo(int clienteId)
    {
        var cliente = _clientes.GetById(clienteId);
        if (cliente is null || !cliente.Ativo)
            throw new InvalidOperationException("Cliente inexistente ou inativo.");
    }

    private static DateTime ConverterLocalParaUtcObrigatorio(DateTime local)
    {
        var valor = local.Kind switch
        {
            DateTimeKind.Utc => local,
            DateTimeKind.Local => local.ToUniversalTime(),
            _ => DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime()
        };

        if (valor <= DateTime.UnixEpoch)
            throw new InvalidOperationException("Informe um vencimento valido.");

        return valor;
    }

    private static DateTime? ConverterLocalParaUtcOpcional(DateTime? local)
    {
        if (!local.HasValue)
            return null;

        return ConverterLocalParaUtcObrigatorio(local.Value);
    }

    private static DateTime ObterDataHistoricoGlobal(Tarefa tarefa)
    {
        return tarefa.Status == TarefaStatus.Concluida
            ? tarefa.ConcluidaEmUtc ?? tarefa.AtualizadoEmUtc
            : tarefa.AtualizadoEmUtc;
    }

    private void RegistrarAuditoria(int userId, string? email, string acao, string resultado, object? before, object? after)
    {
        var payload = JsonSerializer.Serialize(new
        {
            before,
            after
        });

        _audit.Registrar(new AuditLogEntry
        {
            UserId = userId,
            EmailSnapshot = email,
            Acao = acao,
            Resultado = resultado,
            Detalhes = payload,
            Maquina = _maquina,
            VersaoApp = _versaoApp
        }, _usarHashChain);
    }

    private static Tarefa Clonar(Tarefa tarefa)
    {
        return new Tarefa
        {
            Id = tarefa.Id,
            ClienteId = tarefa.ClienteId,
            FerramentaId = tarefa.FerramentaId,
            Titulo = tarefa.Titulo,
            VencimentoUtc = tarefa.VencimentoUtc,
            ResponsavelUserId = tarefa.ResponsavelUserId,
            Status = tarefa.Status,
            Recorrencia = tarefa.Recorrencia,
            DiaRecorrenciaMensal = tarefa.DiaRecorrenciaMensal,
            EsteiraId = tarefa.EsteiraId,
            HorarioRecorrencia = tarefa.HorarioRecorrencia,
            DiaSemanaRecorrencia = tarefa.DiaSemanaRecorrencia,
            Ativa = tarefa.Ativa,
            CriadoPorUserId = tarefa.CriadoPorUserId,
            CriadoEmUtc = tarefa.CriadoEmUtc,
            AtualizadoEmUtc = tarefa.AtualizadoEmUtc,
            ConcluidaEmUtc = tarefa.ConcluidaEmUtc
        };
    }
}
