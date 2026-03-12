using System.Collections.Concurrent;
using System.Text.Json;
using System.Text;
using Protons.Core.Login.Models;
using Protons.Core.Login.Repositories;
using Protons.Core.Login.Services;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Repositories;

namespace Protons.Core.Tarefas.Services;

public sealed class AncorarPdfConfiguracaoService : IAncorarPdfConfiguracaoService
{
    private const int MaxNomeTarefa = 200;
    private static readonly ConcurrentDictionary<string, ScopeLockState> SaveScopeLocks = new(StringComparer.Ordinal);

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly ITarefaService _tarefas;
    private readonly IUserRepository _users;
    private readonly IAncorarPdfConfiguracaoRepository _repositorio;
    private readonly IAncorarPdfPathPolicy _pathPolicy;
    private readonly TimeProvider _timeProvider;
    private readonly IAuditService? _audit;

    public AncorarPdfConfiguracaoService(
        ITarefaService tarefas,
        IUserRepository users,
        IAncorarPdfConfiguracaoRepository repositorio,
        TimeProvider? timeProvider = null)
        : this(tarefas, users, repositorio, pathPolicy: null, timeProvider, audit: null)
    {
    }

    public AncorarPdfConfiguracaoService(
        ITarefaService tarefas,
        IUserRepository users,
        IAncorarPdfConfiguracaoRepository repositorio,
        IAncorarPdfPathPolicy? pathPolicy,
        TimeProvider? timeProvider = null,
        IAuditService? audit = null)
    {
        _tarefas = tarefas;
        _users = users;
        _repositorio = repositorio;
        _pathPolicy = pathPolicy ?? new AncorarPdfAllowAllPathPolicy();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _audit = audit;
    }

    public AncorarPdfConfiguracaoTarefa? ObterPorTarefaId(int tarefaId, int solicitanteUserId)
    {
        if (tarefaId <= 0 || solicitanteUserId <= 0)
            return null;

        var tarefa = _tarefas.ObterPorId(tarefaId, solicitanteUserId);
        if (tarefa is null)
            return null;

        if (!FerramentaTarefaIds.EhAncorarPdf(tarefa.FerramentaId))
            return null;

        return _repositorio.ObterPorTarefaId(tarefaId);
    }

    public AncorarPdfConfiguracaoTarefa CriarOuAtualizar(AncorarPdfSalvarEntrada entrada, int solicitanteUserId)
    {
        using var lockReleaser = AcquireSaveScopeLock(entrada);
        try
        {
            ValidarEntrada(entrada);

            var solicitante = _users.GetById(solicitanteUserId);
            if (solicitante is null || solicitante.Status != UserStatus.Ativo)
                throw new InvalidOperationException("Solicitante inválido ou inativo.");

            if (entrada.ProgramadoPorUserId <= 0)
                throw new InvalidOperationException("ProgramadoPorUserId é obrigatório.");

            if (string.IsNullOrWhiteSpace(entrada.ProgramadoPorNome))
                throw new InvalidOperationException("ProgramadoPorNome é obrigatório.");

            ValidarPoliticasSolicitante(entrada, solicitante);
            ValidarPathPolicy(entrada.PastaMonitoradaPath, AncorarPdfPathTipo.PastaMonitorada, entrada.ClienteId);
            ValidarPathPolicy(entrada.PdfModeloPath, AncorarPdfPathTipo.PdfModelo, entrada.ClienteId);

            var tituloNormalizado = entrada.NomeTarefaPersonalizado.Trim();
            var clienteId = entrada.ClienteId;
            var esteiraId = entrada.EsteiraId;
            var tarefaIdIgnorar = entrada.TarefaId is > 0 ? entrada.TarefaId : null;

            // Fail fast para evitar criar/alterar tarefa e falhar somente depois na configuracao.
            if (_repositorio.ExisteNomeAtivoNoEscopo(clienteId, esteiraId, tituloNormalizado, tarefaIdIgnorar))
                throw new InvalidOperationException("Já existe tarefa ativa com o mesmo nome neste cliente/esteira.");

            Tarefa tarefa;
            var agoraUtc = _timeProvider.GetUtcNow().UtcDateTime;

            // Converte o horario local para UTC usando o timezone especifico da tarefa.
            // Isso corrige o bug de usar ToUniversalTime() que assume o timezone da maquina.
            var localComSegundos = AncorarPdfScheduleCalculator.AplicarSegundos(
                entrada.AgendamentoLocal, entrada.AgendamentoSegundo);

            var (vencimentoUtc, resolucaoDst) = AncorarPdfScheduleCalculator.ResolverLocalParaUtc(
                localComSegundos,
                entrada.TimezoneId,
                entrada.DstHorarioInvalidoPolicy,
                entrada.DstHorarioAmbiguoPolicy);

            if (vencimentoUtc is null)
                throw new InvalidOperationException(
                    "O horário configurado cai em uma transição de horário de verão inválida e a política é pular a ocorrência. " +
                    "Escolha um horário fora da janela de transição DST ou altere a política para avançar/recuar.");

            // Passa o UTC ja resolvido como VencimentoLocal com Kind=Utc.
            // TarefaService identifica Kind=Utc e usa o valor diretamente, sem nova conversao.
            var agendamentoParaServico = vencimentoUtc.Value;

            if (entrada.TarefaId.HasValue && entrada.TarefaId.Value > 0)
            {
                tarefa = _tarefas.ObterPorId(entrada.TarefaId.Value, solicitanteUserId)
                    ?? throw new InvalidOperationException("Tarefa não encontrada ou sem permissão de acesso.");

                if (!PodeEditarTarefaFutura(solicitante, tarefa, agoraUtc))
                    throw new InvalidOperationException("Somente Criador + Admin podem editar tarefa futura de ancorar_pdf.");

                _tarefas.Atualizar(new TarefaAtualizacaoEntrada
                {
                    TarefaId = tarefa.Id,
                    FerramentaId = FerramentaTarefaIds.AncorarPdfCanonico,
                    Titulo = tituloNormalizado,
                    VencimentoLocal = agendamentoParaServico,
                    Recorrencia = entrada.Recorrencia,
                    EsteiraId = esteiraId
                }, solicitanteUserId);

                tarefa = _tarefas.ObterPorId(tarefa.Id, solicitanteUserId)
                    ?? throw new InvalidOperationException("Falha ao recarregar tarefa após atualização.");
            }
            else
            {
                tarefa = _tarefas.Criar(new TarefaCadastroEntrada
                {
                    ClienteId = clienteId,
                    FerramentaId = FerramentaTarefaIds.AncorarPdfCanonico,
                    Titulo = tituloNormalizado,
                    VencimentoLocal = agendamentoParaServico,
                    ResponsavelUserId = solicitanteUserId,
                    Recorrencia = entrada.Recorrencia,
                    EsteiraId = esteiraId
                }, solicitanteUserId);
            }

            var atual = _repositorio.ObterPorTarefaId(tarefa.Id);
            var versaoTemplate = atual is null ? 1 : Math.Max(1, atual.VersaoTemplate + 1);
            var programadoPorUserId = atual?.ProgramadoPorUserId ?? entrada.ProgramadoPorUserId;
            var programadoPorNome = atual?.ProgramadoPorNome ?? entrada.ProgramadoPorNome.Trim();
            var programadoEmUtc = atual?.ProgramadoEmUtc ?? agoraUtc;

            var novaConfiguracao = new AncorarPdfConfiguracaoTarefa
            {
                TarefaId = tarefa.Id,
                ClienteId = clienteId,
                EsteiraId = esteiraId,
                NomeTarefaPersonalizado = tituloNormalizado,
                PastaMonitoradaPath = entrada.PastaMonitoradaPath.Trim(),
                PdfModeloPath = entrada.PdfModeloPath.Trim(),
                NomeReferenciaArquivo = entrada.NomeReferenciaArquivo.Trim(),
                MonitorarSubpastas = entrada.MonitorarSubpastas,
                ValidacaoClienteAtiva = entrada.ValidacaoClienteAtiva,
                LimiarSimilaridadeNome = entrada.LimiarSimilaridadeNome,
                HighlightOpacity = entrada.HighlightOpacity,
                ModoSelecao = entrada.ModoSelecao,
                PdfModeloCrossCliente = entrada.PdfModeloCrossCliente,
                PdfModeloCrossClienteJustificativa = NormalizarJustificativa(entrada.PdfModeloCrossClienteJustificativa),
                Recorrencia = entrada.Recorrencia,
                AgendamentoSegundo = entrada.AgendamentoSegundo,
                TimezoneId = NormalizarTimezoneId(entrada.TimezoneId),
                PrioridadeExecucao = Math.Clamp(entrada.PrioridadeExecucao, 1, 5),
                DstHorarioInvalidoPolicy = entrada.DstHorarioInvalidoPolicy,
                DstHorarioAmbiguoPolicy = entrada.DstHorarioAmbiguoPolicy,
                TemplateAncoras = entrada.TemplateAncoras.Select(CloneAncora).ToArray(),
                OcrFallbackAtivo = entrada.OcrFallbackAtivo,
                OcrDpi = NormalizarOcrDpi(entrada.OcrDpi),
                OcrLang = NormalizarOcrLang(entrada.OcrLang),
                ProgramadoPorUserId = programadoPorUserId,
                ProgramadoPorNome = programadoPorNome,
                ProgramadoEmUtc = programadoEmUtc,
                AtualizadoPorUserId = solicitanteUserId,
                AtualizadoEmUtc = agoraUtc,
                VersaoTemplate = versaoTemplate
            };

            AncorarPdfTemplateHistoricoItem? historico = null;
            if (atual is not null)
            {
                historico = new AncorarPdfTemplateHistoricoItem
                {
                    TarefaId = tarefa.Id,
                    Versao = versaoTemplate,
                    AntesJson = JsonSerializer.Serialize(atual, SerializerOptions),
                    DepoisJson = JsonSerializer.Serialize(novaConfiguracao, SerializerOptions),
                    AlteradoPorUserId = solicitanteUserId,
                    AlteradoPorNome = solicitante.Nome,
                    AlteradoEmUtc = agoraUtc
                };
            }

            _repositorio.Salvar(novaConfiguracao, historico);
            return novaConfiguracao;
        }
        catch (Exception ex) when (EhViolacaoNomeUnicoNoEscopo(ex))
        {
            throw new InvalidOperationException(
                "Já existe tarefa ativa com o mesmo nome neste cliente/esteira.",
                ex);
        }
        catch (Exception ex) when (EhConflitoVersaoTemplate(ex))
        {
            throw new InvalidOperationException(
                "A configuração foi alterada simultaneamente por outra sessão. Reabra a tarefa e tente salvar novamente.",
                ex);
        }
    }

    private static ScopeLockReleaser AcquireSaveScopeLock(AncorarPdfSalvarEntrada entrada)
    {
        var key = BuildSaveScopeKey(entrada);
        var state = SaveScopeLocks.AddOrUpdate(
            key,
            _ => ScopeLockState.Create(),
            (_, existing) =>
            {
                Interlocked.Increment(ref existing.RefCount);
                return existing;
            });

        state.Semaphore.Wait();
        return new ScopeLockReleaser(key, state);
    }

    private static string BuildSaveScopeKey(AncorarPdfSalvarEntrada entrada)
    {
        if (entrada.TarefaId is > 0)
            return $"task:{entrada.TarefaId.Value}";

        return $"scope:{entrada.ClienteId}:{entrada.EsteiraId}";
    }

    private static void ReleaseSaveScopeLock(string key, ScopeLockState state)
    {
        state.Semaphore.Release();
        if (Interlocked.Decrement(ref state.RefCount) != 0)
            return;

        if (!SaveScopeLocks.TryRemove(new KeyValuePair<string, ScopeLockState>(key, state)))
            return;

        state.Semaphore.Dispose();
    }

    public IReadOnlyList<AncorarPdfTemplateHistoricoItem> ListarHistoricoTemplate(int tarefaId, int solicitanteUserId, int limite)
    {
        if (tarefaId <= 0 || solicitanteUserId <= 0)
            return [];

        var tarefa = _tarefas.ObterPorId(tarefaId, solicitanteUserId);
        if (tarefa is null)
            return [];

        if (!FerramentaTarefaIds.EhAncorarPdf(tarefa.FerramentaId))
            return [];

        return _repositorio.ListarHistoricoTemplate(tarefaId, Math.Clamp(limite, 1, 200));
    }

    public IReadOnlyList<AncorarPdfBacklogPendenteItem> ListarBacklogPendente(int clienteId, int solicitanteUserId, int limite)
    {
        if (clienteId <= 0 || solicitanteUserId <= 0)
            return [];

        var pendentes = _repositorio.ListarBacklogPendente(clienteId, Math.Clamp(limite, 1, 500));
        if (pendentes.Count == 0)
            return [];

        // Reusa permissao da camada de tarefa para garantir segregacao por cliente/ownership.
        return pendentes
            .Where(item => _tarefas.ObterPorId(item.TarefaId, solicitanteUserId) is not null)
            .ToList();
    }

    public bool RegistrarDecisaoBacklog(AncorarPdfBacklogDecisaoEntrada entrada, int solicitanteUserId)
    {
        if (entrada.BacklogId <= 0 || solicitanteUserId <= 0)
            return false;

        var solicitante = _users.GetById(solicitanteUserId);
        if (solicitante is null || solicitante.Status != UserStatus.Ativo)
            throw new InvalidOperationException("Solicitante inválido ou inativo.");

        var backlog = _repositorio.ObterBacklogPendentePorId(entrada.BacklogId);
        if (backlog is null)
            return false;

        var tarefa = _tarefas.ObterPorId(backlog.TarefaId, solicitanteUserId);
        if (tarefa is null)
            throw new InvalidOperationException("Sem permissao para decidir backlog desta tarefa.");

        var statusFinal = entrada.ExecutarPendentes
            ? AncorarPdfBacklogStatus.Executar
            : AncorarPdfBacklogStatus.Ignorar;

        if (entrada.ExecutarPendentes)
        {
            _tarefas.AlterarStatus(backlog.TarefaId, TarefaStatus.EmAndamento, solicitanteUserId);
        }

        var decididoEmUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var atualizado = _repositorio.ResolverBacklogPendente(
            entrada.BacklogId,
            statusFinal,
            solicitanteUserId,
            solicitante.Nome,
            decididoEmUtc,
            entrada.Observacao);

        if (!atualizado)
            return false;

        _repositorio.RegistrarEventoScheduler(new AncorarPdfSchedulerEventoRegistro
        {
            TarefaId = backlog.TarefaId,
            ClienteId = backlog.ClienteId,
            TipoEvento = AncorarPdfErroCodigos.BacklogDecisao,
            Detalhes = $"backlog_id={entrada.BacklogId} usuario={solicitanteUserId} " +
                       $"decisao={(entrada.ExecutarPendentes ? "executar" : "ignorar")}",
            OcorreuEmUtc = decididoEmUtc,
            CorrelationId = Guid.NewGuid().ToString("N"),
            StatusAnterior = AncorarPdfErroCodigos.StatusAgendada,
            StatusNovo = entrada.ExecutarPendentes
                ? AncorarPdfErroCodigos.StatusEmAndamento
                : AncorarPdfErroCodigos.StatusAgendada,
            ErroCodigo = AncorarPdfErroCodigos.Nenhum,
            ExecutadaComAtraso = backlog.AtrasoSegundos > 0
        });

        _audit?.Registrar(new AuditLogEntry
        {
            UserId = solicitanteUserId,
            EmailSnapshot = solicitante.Email,
            Acao = "ANCORAR_PDF_BACKLOG_DECISAO",
            Resultado = "OK",
            Detalhes = JsonSerializer.Serialize(new
            {
                backlogId = entrada.BacklogId,
                clienteId = backlog.ClienteId,
                tarefaId = backlog.TarefaId,
                executarPendentes = entrada.ExecutarPendentes,
                observacao = entrada.Observacao
            }),
            Maquina = Environment.MachineName,
            VersaoApp = "0.0.0"
        }, usarHashChain: false);

        return true;
    }

    private static bool PodeEditarTarefaFutura(User solicitante, Tarefa tarefa, DateTime agoraUtc)
    {
        if (tarefa.VencimentoUtc <= agoraUtc)
            return false;

        if (solicitante.Role == UserRole.Admin)
            return true;

        return tarefa.CriadoPorUserId == solicitante.Id;
    }

    private static void ValidarEntrada(AncorarPdfSalvarEntrada entrada)
    {
        if (entrada.ClienteId <= 0)
            throw new InvalidOperationException("ClienteId inválido.");

        if (entrada.EsteiraId <= 0)
            throw new InvalidOperationException("EsteiraId inválido.");

        var nome = (entrada.NomeTarefaPersonalizado ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(nome))
            throw new InvalidOperationException("Nome da tarefa é obrigatório.");
        if (nome.Length > MaxNomeTarefa)
            throw new InvalidOperationException("Nome da tarefa excede o limite permitido.");

        if (!EhRecorrenciaPermitida(entrada.Recorrencia))
            throw new InvalidOperationException("Recorrência inválida para ancorar_pdf. Use Unica/Diaria/Semanal/Mensal.");

        if (entrada.AgendamentoSegundo < 0 || entrada.AgendamentoSegundo > 59)
            throw new InvalidOperationException("AgendamentoSegundo deve estar entre 0 e 59.");

        _ = NormalizarTimezoneId(entrada.TimezoneId);

        if (entrada.PrioridadeExecucao is < 1 or > 5)
            throw new InvalidOperationException("PrioridadeExecucao deve estar entre 1 e 5.");

        if (!Enum.IsDefined(typeof(AncorarPdfDstHorarioInvalidoPolicy), entrada.DstHorarioInvalidoPolicy))
            throw new InvalidOperationException("DstHorarioInvalidoPolicy inválida.");

        if (!Enum.IsDefined(typeof(AncorarPdfDstHorarioAmbiguoPolicy), entrada.DstHorarioAmbiguoPolicy))
            throw new InvalidOperationException("DstHorarioAmbiguoPolicy inválida.");

        if (entrada.LimiarSimilaridadeNome is < 0 or > 1)
            throw new InvalidOperationException("LimiarSimilaridadeNome deve estar no intervalo [0..1].");

        if (entrada.HighlightOpacity is < 0.30 or > 0.45)
            throw new InvalidOperationException("HighlightOpacity deve estar no intervalo [0.30..0.45].");

        if (!Enum.IsDefined(typeof(AncorarPdfModoSelecao), entrada.ModoSelecao))
            throw new InvalidOperationException("ModoSelecao inválido para ancorar_pdf.");

        if (string.IsNullOrWhiteSpace(entrada.PastaMonitoradaPath))
            throw new InvalidOperationException("Pasta monitorada é obrigatória.");

        if (string.IsNullOrWhiteSpace(entrada.PdfModeloPath))
            throw new InvalidOperationException("PDF modelo é obrigatório.");

        if (entrada.OcrDpi is < 150 or > 600)
            throw new InvalidOperationException("OcrDpi deve estar entre 150 e 600.");

        if (entrada.OcrFallbackAtivo && !EhOcrLangValido(entrada.OcrLang))
            throw new InvalidOperationException("OcrLang inválido. Use formato Tesseract, por exemplo: por+eng.");

        if (entrada.TemplateAncoras.Count > AncorarPdfPalettePolicy.MaximoAncoras)
            throw new InvalidOperationException($"Máximo de {AncorarPdfPalettePolicy.MaximoAncoras} âncoras por template.");

        var coresUsadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var chavesTecnicas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var ancora in entrada.TemplateAncoras)
        {
            if (ancora.Ordem < 0)
                throw new InvalidOperationException("Ordem da âncora não pode ser negativa.");

            if (ancora.Pagina <= 0)
                throw new InvalidOperationException("Página da âncora deve ser maior que zero.");

            if (!AncorarPdfPalettePolicy.EhCorPermitida(ancora.CorHex))
                throw new InvalidOperationException($"Cor inválida na âncora: {ancora.CorHex}.");

            if (!AncorarPdfPalettePolicy.TemContrasteMinimoSobreBranco(ancora.CorHex))
                throw new InvalidOperationException($"Cor sem contraste mínimo no overlay: {ancora.CorHex}.");

            if (!coresUsadas.Add(ancora.CorHex))
                throw new InvalidOperationException($"Cor duplicada no template: {ancora.CorHex}.");

            if (ancora.ModoAncora != AncorarPdfModoAncora.RegiaoFixa)
            {
                if (string.IsNullOrWhiteSpace(ancora.TextoAncora))
                    throw new InvalidOperationException("Texto âncora é obrigatório quando o modo é 'Texto à direita' ou 'Texto abaixo'.");
                ValidarCoordenadaRelativa(ancora.LarguraExtracaoRel, nameof(ancora.LarguraExtracaoRel));
                ValidarCoordenadaRelativa(ancora.AlturaExtracaoRel, nameof(ancora.AlturaExtracaoRel));
            }

            ValidarCoordenadaRelativa(ancora.XRel, nameof(ancora.XRel));
            ValidarCoordenadaRelativa(ancora.YRel, nameof(ancora.YRel));
            ValidarCoordenadaRelativa(ancora.LarguraRel, nameof(ancora.LarguraRel));
            ValidarCoordenadaRelativa(ancora.AlturaRel, nameof(ancora.AlturaRel));

            if (ancora.XRel + ancora.LarguraRel > 1 || ancora.YRel + ancora.AlturaRel > 1)
                throw new InvalidOperationException("A âncora ultrapassa os limites relativos da página.");

            if (string.IsNullOrWhiteSpace(ancora.Metadado.NomeExibido))
                throw new InvalidOperationException("Nome exibido da âncora é obrigatório.");

            var chaveTecnica = (ancora.Metadado.ChaveTecnica ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(chaveTecnica))
                throw new InvalidOperationException("Chave técnica da âncora é obrigatória.");

            if (!chavesTecnicas.Add(chaveTecnica))
                throw new InvalidOperationException($"Chave técnica duplicada no template: {chaveTecnica}.");
        }
    }

    private static bool EhRecorrenciaPermitida(TarefaRecorrencia recorrencia)
    {
        return recorrencia is TarefaRecorrencia.Nenhuma or
            TarefaRecorrencia.Diaria or
            TarefaRecorrencia.Semanal or
            TarefaRecorrencia.Mensal;
    }

    private static void ValidarPoliticasSolicitante(AncorarPdfSalvarEntrada entrada, User solicitante)
    {
        if (!entrada.PdfModeloCrossCliente)
            return;

        if (solicitante.Role != UserRole.Admin)
            throw new InvalidOperationException("PDF modelo cross-cliente permitido somente para Admin.");

        var justificativa = NormalizarJustificativa(entrada.PdfModeloCrossClienteJustificativa);
        if (string.IsNullOrWhiteSpace(justificativa) || justificativa.Length < 15)
            throw new InvalidOperationException("Justificativa de uso cross-cliente deve ter no mínimo 15 caracteres.");
    }

    private void ValidarPathPolicy(string caminho, AncorarPdfPathTipo tipo, int clienteId)
    {
        var resultado = _pathPolicy.Validar(caminho.Trim(), tipo, clienteId);
        if (!resultado.Permitido)
            throw new InvalidOperationException(resultado.MensagemErro ?? "Caminho não autorizado pela política de segurança.");
    }

    private static string? NormalizarJustificativa(string? justificativa)
    {
        if (string.IsNullOrWhiteSpace(justificativa))
            return null;

        return justificativa.Trim();
    }

    private static int NormalizarOcrDpi(int dpi)
    {
        return Math.Clamp(dpi, 150, 600);
    }

    private static string NormalizarOcrLang(string? ocrLang)
    {
        if (string.IsNullOrWhiteSpace(ocrLang))
            return "por+eng";

        var tokens = ocrLang
            .Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.ToLowerInvariant())
            .ToList();

        if (tokens.Count == 0)
            return "por+eng";

        var normalizados = new List<string>(tokens.Count);
        string? ultimo = null;
        foreach (var token in tokens)
        {
            if (string.IsNullOrWhiteSpace(token))
                continue;

            if (string.Equals(token, ultimo, StringComparison.Ordinal))
                continue;

            normalizados.Add(token);
            ultimo = token;
        }

        return normalizados.Count == 0 ? "por+eng" : string.Join('+', normalizados);
    }

    private static bool EhOcrLangValido(string? ocrLang)
    {
        if (string.IsNullOrWhiteSpace(ocrLang))
            return false;

        var valor = ocrLang.Trim();
        if (valor.StartsWith('+') || valor.EndsWith('+') || valor.Contains("++", StringComparison.Ordinal))
            return false;

        var tokens = valor.Split('+', StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
            return false;

        foreach (var token in tokens)
        {
            if (string.IsNullOrWhiteSpace(token))
                return false;

            foreach (var ch in token)
            {
                if (!(char.IsLetterOrDigit(ch) || ch == '_'))
                    return false;
            }
        }

        return true;
    }

    private static string NormalizarTimezoneId(string? timezoneId)
    {
        var tz = string.IsNullOrWhiteSpace(timezoneId)
            ? TimeZoneInfo.Local.Id
            : timezoneId.Trim();

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(tz);
            return tz;
        }
        catch (TimeZoneNotFoundException)
        {
            throw new InvalidOperationException("TimezoneId inválido para o sistema operacional.");
        }
        catch (InvalidTimeZoneException)
        {
            throw new InvalidOperationException("TimezoneId inválido para o sistema operacional.");
        }
    }

    private static void ValidarCoordenadaRelativa(double valor, string nomeCampo)
    {
        if (valor is < 0 or > 1)
            throw new InvalidOperationException($"{nomeCampo} deve estar no intervalo [0..1].");
    }

    private static AncorarPdfTemplateAncora CloneAncora(AncorarPdfTemplateAncora origem)
    {
        return origem with
        {
            Metadado = origem.Metadado with { }
        };
    }

    private static bool EhViolacaoNomeUnicoNoEscopo(Exception ex)
    {
        var detalhes = ColetarDetalhesExcecao(ex);

        return detalhes.Contains("UX_AncorarPdfConfig_Cliente_Esteira_NomeAtivo", StringComparison.OrdinalIgnoreCase)
               || detalhes.Contains("UNIQUE constraint failed: AncorarPdfConfiguracoesTarefa.ClienteId, AncorarPdfConfiguracoesTarefa.EsteiraId, AncorarPdfConfiguracoesTarefa.NomeTarefaPersonalizado", StringComparison.OrdinalIgnoreCase)
               || (detalhes.Contains("duplicate key value violates unique constraint", StringComparison.OrdinalIgnoreCase)
                   && detalhes.Contains("ancorarpdfconfiguracoestarefa", StringComparison.OrdinalIgnoreCase)
                   && detalhes.Contains("nometarefapersonalizado", StringComparison.OrdinalIgnoreCase));
    }

    private static bool EhConflitoVersaoTemplate(Exception ex)
    {
        var detalhes = ColetarDetalhesExcecao(ex);

        return detalhes.Contains("UX_AncorarPdfTemplateHistorico_Tarefa_Versao", StringComparison.OrdinalIgnoreCase)
               || detalhes.Contains("UNIQUE constraint failed: AncorarPdfTemplateHistorico.TarefaId, AncorarPdfTemplateHistorico.Versao", StringComparison.OrdinalIgnoreCase)
               || (detalhes.Contains("duplicate key value violates unique constraint", StringComparison.OrdinalIgnoreCase)
                   && detalhes.Contains("ancorarpdftemplatehistorico", StringComparison.OrdinalIgnoreCase)
                   && detalhes.Contains("versao", StringComparison.OrdinalIgnoreCase));
    }

    private static string ColetarDetalhesExcecao(Exception ex)
    {
        var sb = new StringBuilder(capacity: 512);
        for (var atual = ex; atual is not null; atual = atual.InnerException)
        {
            if (sb.Length > 0)
                sb.Append(" | ");

            sb.Append(atual.Message);
        }

        return sb.ToString();
    }

    private sealed class ScopeLockState
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int RefCount = 1;

        public static ScopeLockState Create() => new();
    }

    private readonly struct ScopeLockReleaser : IDisposable
    {
        private readonly string _key;
        private readonly ScopeLockState _state;

        public ScopeLockReleaser(string key, ScopeLockState state)
        {
            _key = key;
            _state = state;
        }

        public void Dispose()
        {
            ReleaseSaveScopeLock(_key, _state);
        }
    }
}
