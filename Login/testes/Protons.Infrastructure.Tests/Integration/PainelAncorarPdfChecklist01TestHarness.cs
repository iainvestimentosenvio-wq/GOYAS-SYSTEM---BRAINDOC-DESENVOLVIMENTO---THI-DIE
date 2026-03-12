using System;
using System.Collections.Generic;
using System.IO;
using Moq;
using Protons.Core.Clientes.Models;
using Protons.Core.Clientes.Services;
using Protons.Core.Login.Models;
using Protons.Core.Login.Services;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Services;
using Protons.UI.Login.Services;
using Protons.UI.Login.ViewModels;
using Protons.UI.Painel.ViewModels;

namespace Protons.Infrastructure.Tests.Integration;

internal sealed class PainelAncorarPdfChecklist01TestHarness : IDisposable
{
    private readonly string _settingsPath;
    private readonly Dictionary<int, Cliente> _clientesConfigurados = new();

    public Mock<IAuthService> AuthService { get; } = new();
    public Mock<IAuditLogQueryService> AuditLogQueryService { get; } = new();
    public Mock<IClienteService> ClienteService { get; } = new();
    public Mock<ITarefaService> TarefaService { get; } = new();
    public Mock<IUserDirectoryService> UserDirectoryService { get; } = new();
    public Mock<IAncorarPdfConfiguracaoService> AncorarPdfConfiguracaoService { get; } = new();

    public PainelViewModel ViewModel { get; }

    public PainelAncorarPdfChecklist01TestHarness(UserRole role = UserRole.Usuario, TimeProvider? timeProvider = null)
    {
        _settingsPath = Path.Combine(Path.GetTempPath(), $"protons_checklist01_vm_{Guid.NewGuid():N}.json");
        ConfigurarDefaults();

        ViewModel = new PainelViewModel(
            AuthService.Object,
            new LocalSettings(_settingsPath),
            AuditLogQueryService.Object,
            ClienteService.Object,
            TarefaService.Object,
            UserDirectoryService.Object,
            AncorarPdfConfiguracaoService.Object,
            _ => { },
            userId: 77,
            email: "checklist01@protons.local",
            nome: "Checklist 01",
            role: role,
            timeProvider: timeProvider);
    }

    public void Dispose()
    {
        ViewModel.Dispose();
        if (File.Exists(_settingsPath))
            File.Delete(_settingsPath);
    }

    private void ConfigurarDefaults()
    {
        AuthService.Setup(x => x.ListarPendentes()).Returns(Array.Empty<User>());
        AuthService.Setup(x => x.RegistrarLogout(It.IsAny<int>(), It.IsAny<string>()));

        AuditLogQueryService
            .Setup(x => x.GetPage(It.IsAny<int>(), It.IsAny<int>()))
            .Returns(Array.Empty<AuditLogEntry>());

        ClienteService
            .Setup(x => x.ListarTodosPorUsuario(It.IsAny<int>()))
            .Returns(() => _clientesConfigurados.Values.OrderBy(x => x.Id).ToArray());
        ClienteService
            .Setup(x => x.ListarGrupos())
            .Returns(Array.Empty<GrupoEmpresarial>());
        ClienteService
            .Setup(x => x.ObterPorIdPorUsuario(It.IsAny<int>(), It.IsAny<int>()))
            .Returns((int _, int clienteId) =>
                _clientesConfigurados.TryGetValue(clienteId, out var cliente) ? cliente : null);
        ClienteService
            .Setup(x => x.BuscarPorUsuario(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns((int _, string? termo, int pagina, int tamanho) =>
            {
                var paginaNormalizada = pagina <= 0 ? 1 : pagina;
                var tamanhoNormalizado = tamanho <= 0 ? 20 : tamanho;
                var clientes = _clientesConfigurados.Values
                    .Where(c =>
                        string.IsNullOrWhiteSpace(termo)
                        || c.Nome.Contains(termo, StringComparison.OrdinalIgnoreCase)
                        || c.CodigoCliente.Contains(termo, StringComparison.OrdinalIgnoreCase)
                        || c.Documento.Contains(termo, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(c => c.Nome)
                    .ToArray();
                return new PaginacaoResultado<Cliente>
                {
                    Itens = clientes,
                    PaginaAtual = paginaNormalizada,
                    TamanhoPagina = tamanhoNormalizado,
                    TotalItens = clientes.Length,
                    Termo = termo
                };
            });

        TarefaService
            .Setup(x => x.BuscarHistoricoGlobal(It.IsAny<int>(), It.IsAny<int>()))
            .Returns(Array.Empty<Tarefa>());
        TarefaService
            .Setup(x => x.BuscarPorClienteIds(It.IsAny<IReadOnlyList<int>>(), It.IsAny<string?>(), It.IsAny<TarefaStatus?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<bool>(), It.IsAny<int>()))
            .Returns(Array.Empty<Tarefa>());
        TarefaService
            .Setup(x => x.Buscar(It.IsAny<TarefaFiltroConsulta>(), It.IsAny<int>()))
            .Returns(Array.Empty<Tarefa>());
        TarefaService
            .Setup(x => x.AlterarStatus(It.IsAny<int>(), It.IsAny<TarefaStatus>(), It.IsAny<int>()))
            .Returns((int tarefaId, TarefaStatus status, int userId) => new Tarefa
            {
                Id = tarefaId,
                ClienteId = 1,
                Titulo = $"Tarefa {tarefaId}",
                VencimentoUtc = DateTime.UtcNow.AddMinutes(10),
                ResponsavelUserId = userId,
                Status = status,
                Recorrencia = TarefaRecorrencia.Nenhuma,
                Ativa = true,
                CriadoPorUserId = userId,
                CriadoEmUtc = DateTime.UtcNow,
                AtualizadoEmUtc = DateTime.UtcNow
            });

        UserDirectoryService
            .Setup(x => x.ListarAtivos())
            .Returns(Array.Empty<UserDirectoryItem>());

        AncorarPdfConfiguracaoService
            .Setup(x => x.ObterPorTarefaId(It.IsAny<int>(), It.IsAny<int>()))
            .Returns((AncorarPdfConfiguracaoTarefa?)null);
        AncorarPdfConfiguracaoService
            .Setup(x => x.CriarOuAtualizar(It.IsAny<AncorarPdfSalvarEntrada>(), It.IsAny<int>()))
            .Returns((AncorarPdfSalvarEntrada entrada, int _) => new AncorarPdfConfiguracaoTarefa
            {
                TarefaId = entrada.TarefaId ?? 999,
                ClienteId = entrada.ClienteId,
                EsteiraId = entrada.EsteiraId,
                NomeTarefaPersonalizado = entrada.NomeTarefaPersonalizado,
                PastaMonitoradaPath = entrada.PastaMonitoradaPath,
                PdfModeloPath = entrada.PdfModeloPath,
                NomeReferenciaArquivo = entrada.NomeReferenciaArquivo,
                MonitorarSubpastas = entrada.MonitorarSubpastas,
                ValidacaoClienteAtiva = entrada.ValidacaoClienteAtiva,
                LimiarSimilaridadeNome = entrada.LimiarSimilaridadeNome,
                HighlightOpacity = entrada.HighlightOpacity,
                ModoSelecao = entrada.ModoSelecao,
                PdfModeloCrossCliente = entrada.PdfModeloCrossCliente,
                PdfModeloCrossClienteJustificativa = entrada.PdfModeloCrossClienteJustificativa,
                Recorrencia = entrada.Recorrencia,
                AgendamentoSegundo = entrada.AgendamentoSegundo,
                TimezoneId = string.IsNullOrWhiteSpace(entrada.TimezoneId) ? TimeZoneInfo.Local.Id : entrada.TimezoneId,
                PrioridadeExecucao = entrada.PrioridadeExecucao,
                DstHorarioInvalidoPolicy = entrada.DstHorarioInvalidoPolicy,
                DstHorarioAmbiguoPolicy = entrada.DstHorarioAmbiguoPolicy,
                TemplateAncoras = entrada.TemplateAncoras,
                OcrFallbackAtivo = entrada.OcrFallbackAtivo,
                OcrDpi = entrada.OcrDpi,
                OcrLang = entrada.OcrLang,
                ProgramadoPorUserId = entrada.ProgramadoPorUserId,
                ProgramadoPorNome = entrada.ProgramadoPorNome,
                ProgramadoEmUtc = DateTime.UtcNow,
                AtualizadoPorUserId = entrada.ProgramadoPorUserId,
                AtualizadoEmUtc = DateTime.UtcNow,
                VersaoTemplate = 1
            });
        AncorarPdfConfiguracaoService
            .Setup(x => x.ListarHistoricoTemplate(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(Array.Empty<AncorarPdfTemplateHistoricoItem>());
        AncorarPdfConfiguracaoService
            .Setup(x => x.ListarBacklogPendente(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(Array.Empty<AncorarPdfBacklogPendenteItem>());
        AncorarPdfConfiguracaoService
            .Setup(x => x.RegistrarDecisaoBacklog(It.IsAny<AncorarPdfBacklogDecisaoEntrada>(), It.IsAny<int>()))
            .Returns(true);
    }

    public void DefinirClientes(params Cliente[] clientes)
    {
        _clientesConfigurados.Clear();
        foreach (var cliente in clientes)
            _clientesConfigurados[cliente.Id] = cliente;
    }

    public async Task SelecionarClienteAsync(int clienteId, string? nome = null, string? codigo = null)
    {
        if (!_clientesConfigurados.TryGetValue(clienteId, out var cliente))
        {
            cliente = CriarCliente(clienteId, nome ?? $"Cliente {clienteId}", codigo);
            _clientesConfigurados[clienteId] = cliente;
        }

        var item = new SeletorClienteItemPainel(
            SeletorClienteTipoItem.Cliente,
            cliente.Id,
            cliente.GrupoEmpresarialNome,
            cliente.Nome,
            string.IsNullOrWhiteSpace(cliente.CodigoCliente) ? null : $"Código: {cliente.CodigoCliente}",
            0,
            false,
            string.Empty,
            false);

        await ViewModel.SelecionarClienteSeletorCommand.ExecuteAsync(item);
    }

    public void LimparClienteSelecionado()
    {
        ViewModel.LimparClienteSelecionadoCommand.Execute(null);
    }

    private static Cliente CriarCliente(int clienteId, string nome, string? codigo = null)
    {
        return new Cliente
        {
            Id = clienteId,
            CodigoCliente = string.IsNullOrWhiteSpace(codigo) ? $"CLI-{clienteId}" : codigo,
            Nome = nome,
            Documento = $"{clienteId:D11}",
            TipoDocumento = TipoDocumentoCliente.CPF,
            Ativo = true,
            CriadoPorUserId = 77,
            CriadoEmUtc = DateTime.UtcNow,
            AtualizadoEmUtc = DateTime.UtcNow
        };
    }

    public static Tarefa CriarTarefa(
        int id,
        int clienteId,
        string titulo,
        DateTime vencimentoUtc,
        TarefaStatus status = TarefaStatus.Agendada,
        int responsavelUserId = 77,
        int? esteiraId = 1)
    {
        return new Tarefa
        {
            Id = id,
            ClienteId = clienteId,
            Titulo = titulo,
            VencimentoUtc = vencimentoUtc,
            ResponsavelUserId = responsavelUserId,
            Status = status,
            Recorrencia = TarefaRecorrencia.Nenhuma,
            EsteiraId = esteiraId,
            Ativa = true,
            CriadoPorUserId = responsavelUserId,
            CriadoEmUtc = DateTime.UtcNow.AddMinutes(-20),
            AtualizadoEmUtc = DateTime.UtcNow.AddMinutes(-5),
            ConcluidaEmUtc = status == TarefaStatus.Concluida ? DateTime.UtcNow.AddMinutes(-1) : null
        };
    }
}
