using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Protons.Core.Clientes.Models;
using Protons.Core.Clientes.Validation;
using Protons.Core.Login.Validation;

namespace Protons.UI.Painel.ViewModels;

public sealed partial class PainelViewModel
{
    private const string ChaveGrupoSemGrupo = "__sem_grupo__";
    private const string NomeGrupoSemGrupo = "Clientes sem grupo";
    private static readonly TimeSpan CacheBuscaClientesTtl = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan[] BackoffBuscaClientes = [TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(350)];
    private static readonly Regex CodigoClienteRegex = new(
        "^[A-Z0-9][A-Z0-9\\-_./]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private CancellationTokenSource? _buscaClientesCts;
    private readonly Dictionary<string, ItemCacheBuscaClientes> _cacheBuscaClientes = new(StringComparer.Ordinal);
    private DateTimeOffset _cacheClientesSeletorExpiraEmUtc = DateTimeOffset.MinValue;
    private readonly Dictionary<string, bool> _estadoExpansaoGruposSeletor = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Cliente> _cacheClientesSeletor = new();
    private readonly Dictionary<int, Cliente> _mapaClientesPorId = new();
    private bool _atualizandoBuscaTextoInternamente;
    private bool _aplicandoMascaraDocumentoCadastro;
    private int? _clienteInativacaoPendenteId;

    [ObservableProperty] private bool _cadastroClientesAberto;
    [ObservableProperty] private bool _seletorClientesAberto;
    [ObservableProperty] private string _textoClienteSelecionadoNoCampo = string.Empty;
    [ObservableProperty] private string _clienteCodigoCadastro = string.Empty;
    [ObservableProperty] private string _clienteNomeCadastro = string.Empty;
    [ObservableProperty] private string? _clienteNomeFantasiaCadastro;
    [ObservableProperty] private string _clienteDocumentoCadastro = string.Empty;
    [ObservableProperty] private string _clienteDocumentoStatusCadastro = "Incompleto";
    [ObservableProperty] private string? _clienteEmailCadastro;
    [ObservableProperty] private string? _clienteTelefoneCadastro;
    [ObservableProperty] private GrupoEmpresarialPainel? _grupoEmpresarialSelecionadoCadastro;
    [ObservableProperty] private bool _cadastroGrupoAberto;
    [ObservableProperty] private string _grupoEmpresarialCadastroNome = string.Empty;
    [ObservableProperty] private string? _mensagemCadastroGrupo;
    [ObservableProperty] private string? _mensagemCadastroCliente;
    [ObservableProperty] private ClienteCadastroSeveridade _mensagemCadastroClienteSeveridade = ClienteCadastroSeveridade.Info;
    [ObservableProperty] private ClienteCadastroCampoErro _campoErroCadastro = ClienteCadastroCampoErro.Nenhum;
    [ObservableProperty] private string? _referenciaErroCadastro;
    [ObservableProperty] private bool _confirmacaoSalvarCadastroAberta;
    [ObservableProperty] private string _resumoConfirmacaoSalvarCadastro = string.Empty;
    [ObservableProperty] private bool _confirmacaoInativarClienteAberta;
    [ObservableProperty] private string _resumoConfirmacaoInativarCliente = string.Empty;
    [ObservableProperty] private bool _carregandoClientes;
    [ObservableProperty] private int _paginaClientesAtual = ConsultaClientesFiltro.PaginaMinima;
    [ObservableProperty] private int _tamanhoPaginaClientes = ConsultaClientesFiltro.TamanhoPadrao;
    [ObservableProperty] private int _totalClientesEncontrados;
    [ObservableProperty] private int _totalPaginasClientes = 1;
    [ObservableProperty] private string _resumoBuscaClientes = "Nenhum cliente carregado.";
    [ObservableProperty] private bool _carregandoContextoCliente;
    [ObservableProperty] private string _buscaListaClientesCadastro = string.Empty;
    [ObservableProperty] private ClienteResumoPainel? _clienteSelecionadoNaListaCadastro;
    [ObservableProperty] private bool _modoEdicaoClienteCadastro;
    [ObservableProperty] private int? _clienteEdicaoId;

    public string WatermarkBuscaPainel => CadastroClientesAberto
        ? "Buscar por grupo, razão social, fantasia, código ou documento..."
        : "Buscar cliente por grupo, fantasia, razão, código ou documento...";

    public bool ExibirBannerCadastroCliente => !string.IsNullOrWhiteSpace(MensagemCadastroCliente);
    public bool ExibirReferenciaErroCadastro => !string.IsNullOrWhiteSpace(ReferenciaErroCadastro);
    public string CorFundoMensagemCadastroCliente => MensagemCadastroClienteSeveridade switch
    {
        ClienteCadastroSeveridade.Warning => CorFundoWarning,
        ClienteCadastroSeveridade.Error => CorFundoError,
        _ => CorFundoInfo
    };

    public string CorBordaMensagemCadastroCliente => MensagemCadastroClienteSeveridade switch
    {
        ClienteCadastroSeveridade.Warning => CorBordaWarning,
        ClienteCadastroSeveridade.Error => CorBordaError,
        _ => CorBordaInfo
    };

    public string CorTextoMensagemCadastroCliente => MensagemCadastroClienteSeveridade switch
    {
        ClienteCadastroSeveridade.Warning => CorTextoWarning,
        ClienteCadastroSeveridade.Error => CorTextoError,
        _ => CorTextoInfo
    };

    public string CorBordaCodigoClienteCadastro => ObterCorBordaCampo(ClienteCadastroCampoErro.CodigoCliente);
    public string CorBordaNomeClienteCadastro => ObterCorBordaCampo(ClienteCadastroCampoErro.Nome);
    public string CorBordaDocumentoCadastro => ObterCorBordaCampo(ClienteCadastroCampoErro.Documento);
    public string CorBordaGrupoCadastro => ObterCorBordaCampo(ClienteCadastroCampoErro.Grupo);
    public string CorBordaEmailCadastro => ObterCorBordaCampo(ClienteCadastroCampoErro.Email);
    public string CorBordaTelefoneCadastro => ObterCorBordaCampo(ClienteCadastroCampoErro.Telefone);
    public string CorBordaNomeFantasiaCadastro => ObterCorBordaCampo(ClienteCadastroCampoErro.NomeFantasia);
    public bool TemClienteSelecionado => ClienteContextoId is > 0;
    public bool DocumentoCadastroSomenteLeitura => ModoEdicaoClienteCadastro;
    public bool ExibirAcoesEdicaoCliente => ModoEdicaoClienteCadastro && ClienteEdicaoId.HasValue;
    public bool ExibirConfirmacaoSalvarCadastro => ConfirmacaoSalvarCadastroAberta;
    public bool ExibirConfirmacaoInativarCliente => ConfirmacaoInativarClienteAberta;
    public string RotuloBotaoSalvarCliente => ModoEdicaoClienteCadastro ? "Salvar Alterações" : "Salvar Cliente";

    public ObservableCollection<ClienteResumoPainel> ClientesCadastrados { get; } = new();
    public ObservableCollection<SeletorClienteItemPainel> ItensSeletorClientes { get; } = new();
    public ObservableCollection<GrupoEmpresarialPainel> GruposEmpresariaisDisponiveis { get; } = new();

    partial void OnCadastroClientesAbertoChanged(bool value)
    {
        SalvarClienteCommand.NotifyCanExecuteChanged();
        SalvarGrupoEmpresarialCommand.NotifyCanExecuteChanged();
        ConfirmarSalvarClienteCommand.NotifyCanExecuteChanged();
        ConfirmarInativarClienteCommand.NotifyCanExecuteChanged();
        SolicitarInativarClienteCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(WatermarkBuscaPainel));

        if (value)
        {
            LimparMensagemCadastroCliente();
            ConfirmacaoSalvarCadastroAberta = false;
            ConfirmacaoInativarClienteAberta = false;
            _clienteInativacaoPendenteId = null;
            BuscaListaClientesCadastro = string.Empty;
            ResetarModoEdicaoCadastro();
            NavegarPara(DestinoNavegacaoPainel.CadastroClientes, "abrir_cadastro");
            DispararComSeguranca(CarregarGruposEmpresariaisAsync(), "grupos_empresariais_carga_falha");
            DispararComSeguranca(CarregarClientesPersistidosAsync(ConsultaClientesFiltro.PaginaMinima, atualizarTabela: true), "clientes_carga_cadastro_falha");
        }
        else
        {
            LimparMensagemCadastroCliente();
            ConfirmacaoSalvarCadastroAberta = false;
            ConfirmacaoInativarClienteAberta = false;
            _clienteInativacaoPendenteId = null;
            ResetarModoEdicaoCadastro();
            CadastroGrupoAberto = false;
            MensagemCadastroGrupo = null;
            DispararComSeguranca(CarregarClientesPersistidosAsync(ConsultaClientesFiltro.PaginaMinima, atualizarTabela: false), "clientes_carga_fechar_falha");
        }
    }

    partial void OnCadastroGrupoAbertoChanged(bool value)
    {
        SalvarGrupoEmpresarialCommand.NotifyCanExecuteChanged();
        if (!value)
            GrupoEmpresarialCadastroNome = string.Empty;
    }

    partial void OnCarregandoClientesChanged(bool value)
    {
        PaginaAnteriorClientesCommand.NotifyCanExecuteChanged();
        ProximaPaginaClientesCommand.NotifyCanExecuteChanged();
    }

    partial void OnPaginaClientesAtualChanged(int value)
    {
        PaginaAnteriorClientesCommand.NotifyCanExecuteChanged();
        ProximaPaginaClientesCommand.NotifyCanExecuteChanged();
    }

    partial void OnTotalPaginasClientesChanged(int value)
    {
        PaginaAnteriorClientesCommand.NotifyCanExecuteChanged();
        ProximaPaginaClientesCommand.NotifyCanExecuteChanged();
    }

    partial void OnBuscaTextoChanged(string value)
    {
        if (!_atualizandoBuscaTextoInternamente)
            SeletorClientesAberto = true;

        DispararComSeguranca(AgendarBuscaClientesAsync(CadastroClientesAberto), "busca_clientes_debounce_falha");
    }

    partial void OnMensagemCadastroClienteChanged(string? value)
    {
        OnPropertyChanged(nameof(ExibirBannerCadastroCliente));
    }

    partial void OnConfirmacaoSalvarCadastroAbertaChanged(bool value)
    {
        ConfirmarSalvarClienteCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ExibirConfirmacaoSalvarCadastro));
    }

    partial void OnConfirmacaoInativarClienteAbertaChanged(bool value)
    {
        ConfirmarInativarClienteCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ExibirConfirmacaoInativarCliente));
    }

    partial void OnMensagemCadastroClienteSeveridadeChanged(ClienteCadastroSeveridade value)
    {
        OnPropertyChanged(nameof(CorFundoMensagemCadastroCliente));
        OnPropertyChanged(nameof(CorBordaMensagemCadastroCliente));
        OnPropertyChanged(nameof(CorTextoMensagemCadastroCliente));
    }

    partial void OnCampoErroCadastroChanged(ClienteCadastroCampoErro value)
    {
        OnPropertyChanged(nameof(CorBordaCodigoClienteCadastro));
        OnPropertyChanged(nameof(CorBordaNomeClienteCadastro));
        OnPropertyChanged(nameof(CorBordaDocumentoCadastro));
        OnPropertyChanged(nameof(CorBordaGrupoCadastro));
        OnPropertyChanged(nameof(CorBordaEmailCadastro));
        OnPropertyChanged(nameof(CorBordaTelefoneCadastro));
        OnPropertyChanged(nameof(CorBordaNomeFantasiaCadastro));
    }

    partial void OnReferenciaErroCadastroChanged(string? value)
    {
        OnPropertyChanged(nameof(ExibirReferenciaErroCadastro));
    }

    partial void OnClienteContextoIdChanged(int? value)
    {
        OnPropertyChanged(nameof(TemClienteSelecionado));
        AtualizarDisponibilidadeEsteiras();
    }

    partial void OnBuscaListaClientesCadastroChanged(string value)
    {
        if (CadastroClientesAberto)
            DispararComSeguranca(AgendarBuscaClientesAsync(atualizarTabela: true), "busca_lista_clientes_debounce_falha");
    }

    partial void OnModoEdicaoClienteCadastroChanged(bool value)
    {
        OnPropertyChanged(nameof(DocumentoCadastroSomenteLeitura));
        OnPropertyChanged(nameof(ExibirAcoesEdicaoCliente));
        OnPropertyChanged(nameof(RotuloBotaoSalvarCliente));
        SolicitarInativarClienteCommand.NotifyCanExecuteChanged();
    }

    partial void OnClienteEdicaoIdChanged(int? value)
    {
        OnPropertyChanged(nameof(ExibirAcoesEdicaoCliente));
        SolicitarInativarClienteCommand.NotifyCanExecuteChanged();
    }

    partial void OnClienteDocumentoCadastroChanged(string value)
    {
        if (_aplicandoMascaraDocumentoCadastro)
            return;

        var analise = DocumentoClienteValidator.Analisar(value);
        var digitos = analise.DocumentoSomenteDigitos;
        var totalDigitosInformados = digitos.Length;
        if (digitos.Length > ClienteInputLimits.MaxDocumento)
            digitos = digitos[..ClienteInputLimits.MaxDocumento];

        AtualizarStatusDocumentoCadastro(analise, totalDigitosInformados);

        var mascarado = DocumentoClienteValidator.AplicarMascaraParcial(digitos);
        if (string.Equals(value, mascarado, StringComparison.Ordinal))
            return;

        _aplicandoMascaraDocumentoCadastro = true;
        try
        {
            ClienteDocumentoCadastro = mascarado;
        }
        finally
        {
            _aplicandoMascaraDocumentoCadastro = false;
        }
    }

    [RelayCommand]
    private void AbrirCadastroClientes()
    {
        RegistrarInteracaoPainel();
        NotificacoesAbertas = false;
        ListaTarefasLateralAberta = false;
        LimparMensagemCadastroCliente();
        MensagemCadastroGrupo = null;
        CadastroClientesAberto = true;
    }

    [RelayCommand]
    private void FecharCadastroClientes()
    {
        RegistrarInteracaoPainel();
        LimparMensagemCadastroCliente();
        MensagemCadastroGrupo = null;
        CadastroClientesAberto = false;
        CadastroGrupoAberto = false;
        SeletorClientesAberto = false;
        _buscaClientesCts?.Cancel();
        _buscaClientesCts = null;
    }

    [RelayCommand]
    private void AbrirCadastroGrupo()
    {
        RegistrarInteracaoPainel();
        MensagemCadastroGrupo = null;
        CadastroGrupoAberto = true;
    }

    [RelayCommand]
    private void FecharCadastroGrupo()
    {
        RegistrarInteracaoPainel();
        MensagemCadastroGrupo = null;
        CadastroGrupoAberto = false;
    }

    [RelayCommand]
    private void LimparGrupoSelecionado()
    {
        GrupoEmpresarialSelecionadoCadastro = null;
    }

    private bool PodeSalvarGrupoEmpresarial() => !IsBusy && CadastroClientesAberto && CadastroGrupoAberto;

    [RelayCommand(CanExecute = nameof(PodeSalvarGrupoEmpresarial))]
    private async Task SalvarGrupoEmpresarial()
    {
        RegistrarInteracaoPainel();

        IsBusy = true;
        try
        {
            var resultado = await Task.Run(() => _clienteService.CadastrarGrupo(new GrupoEmpresarialCadastroEntrada
            {
                Nome = GrupoEmpresarialCadastroNome,
                CriadoPorUserId = _userId
            }));

            MensagemCadastroGrupo = resultado.Mensagem;
            if (!resultado.Sucesso)
                return;

            await CarregarGruposEmpresariaisAsync();
            if (resultado.Grupo is not null)
            {
                GrupoEmpresarialSelecionadoCadastro = GruposEmpresariaisDisponiveis
                    .FirstOrDefault(g => g.Id == resultado.Grupo.Id);
            }

            CadastroGrupoAberto = false;
            GrupoEmpresarialCadastroNome = string.Empty;
        }
        catch (Exception ex)
        {
            MensagemCadastroGrupo = "Falha ao cadastrar grupo empresarial.";
            RegistrarErroPainel("grupo_empresarial_cadastro_falha", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool PodeSalvarCliente() => !IsBusy && CadastroClientesAberto;

    [RelayCommand(CanExecute = nameof(PodeSalvarCliente))]
    private void SalvarCliente()
    {
        RegistrarInteracaoPainel();
        SolicitarSalvarClienteInterno("botao_salvar");
    }

    public bool SolicitarSalvarClienteViaEnter()
    {
        RegistrarInteracaoPainel();
        return SolicitarSalvarClienteInterno("enter");
    }

    private bool SolicitarSalvarClienteInterno(string origem)
    {
        LimparMensagemCadastroCliente();
        if (!ValidarFormularioCadastroParaConfirmacao())
            return false;

        ResumoConfirmacaoSalvarCadastro = CriarResumoConfirmacaoSalvar();
        ConfirmacaoSalvarCadastroAberta = true;
        RegistrarEventoPainel(
            "cliente_cadastro_confirmacao_aberta",
            $"origem={origem} modo={(ModoEdicaoClienteCadastro ? "edicao" : "cadastro")}");
        return true;
    }

    private bool PodeConfirmarSalvarCliente() => !IsBusy && ConfirmacaoSalvarCadastroAberta;

    [RelayCommand(CanExecute = nameof(PodeConfirmarSalvarCliente))]
    private async Task ConfirmarSalvarCliente()
    {
        RegistrarInteracaoPainel();
        ConfirmacaoSalvarCadastroAberta = false;
        await PersistirCadastroClienteAsync();
    }

    [RelayCommand]
    private void CancelarSalvarCliente()
    {
        RegistrarInteracaoPainel();
        ConfirmacaoSalvarCadastroAberta = false;
    }

    private async Task PersistirCadastroClienteAsync()
    {
        IsBusy = true;
        LimparMensagemCadastroCliente();
        var emEdicao = ModoEdicaoClienteCadastro && ClienteEdicaoId.HasValue;

        var corr = GerarCorrelationId();
        var metricaNome = emEdicao ? "cliente_edicao" : "cliente_cadastro";
        var medicao = IniciarMetricaPainel(metricaNome, corr);
        var tamanhoDocumento = DocumentoClienteValidator.ExtrairSomenteDigitos(ClienteDocumentoCadastro).Length;
        RegistrarEventoPainel(
            emEdicao ? "cliente_edicao_inicio" : "cliente_cadastro_inicio",
            $"corr={corr} nome_len={ClienteNomeCadastro.Trim().Length} doc_len={tamanhoDocumento} modo={(emEdicao ? "edicao" : "cadastro")}");

        try
        {
            ResultadoCadastroCliente resultado;
            try
            {
                if (emEdicao && ClienteEdicaoId.HasValue)
                {
                    var entradaEdicao = new ClienteEdicaoEntrada
                    {
                        ClienteId = ClienteEdicaoId.Value,
                        CodigoCliente = ClienteCodigoCadastro.Trim().ToUpperInvariant(),
                        Nome = ClienteNomeCadastro.Trim(),
                        NomeFantasia = ClienteNomeFantasiaCadastro?.Trim(),
                        GrupoEmpresarialId = GrupoEmpresarialSelecionadoCadastro?.Id,
                        Email = ClienteEmailCadastro?.Trim(),
                        Telefone = ClienteTelefoneCadastro?.Trim(),
                        AtualizadoPorUserId = _userId
                    };

                    resultado = await Task.Run(() => _clienteService.Editar(entradaEdicao));
                }
                else
                {
                    var entrada = new ClienteCadastroEntrada
                    {
                        CodigoCliente = ClienteCodigoCadastro.Trim().ToUpperInvariant(),
                        Nome = ClienteNomeCadastro.Trim(),
                        NomeFantasia = ClienteNomeFantasiaCadastro?.Trim(),
                        Documento = ClienteDocumentoCadastro.Trim(),
                        GrupoEmpresarialId = GrupoEmpresarialSelecionadoCadastro?.Id,
                        Email = ClienteEmailCadastro?.Trim(),
                        Telefone = ClienteTelefoneCadastro?.Trim(),
                        CriadoPorUserId = _userId
                    };

                    resultado = await Task.Run(() => _clienteService.Cadastrar(entrada));
                }
            }
            catch (Exception ex)
            {
                var referenciaErro = GerarReferenciaErroCadastroUi();
                DefinirMensagemCadastroCliente(
                    "Não foi possível concluir o cadastro agora. Tente novamente.",
                    ClienteCadastroSeveridade.Error,
                    ClienteCadastroCampoErro.Nenhum,
                    referenciaErro);

                RegistrarErroPainel(emEdicao ? "cliente_edicao_falha" : "cliente_cadastro_falha", ex, $"corr={corr} ref={referenciaErro}");
                RegistrarMetricaPainel($"{metricaNome}_duracao_ms", medicao.ElapsedMs(), "ms", $"corr={corr} resultado=erro");
                return;
            }

            AplicarResultadoCadastroCliente(resultado);
            if (!resultado.Sucesso)
            {
                var codigoErro = resultado.CodigoErro?.ToString() ?? "nao_informado";
                var campoErro = resultado.CampoErro?.ToString() ?? "Nenhum";
                RegistrarEventoPainel(
                    emEdicao ? "cliente_edicao_negada" : "cliente_cadastro_negado",
                    $"corr={corr} codigo_erro={codigoErro} campo_erro={campoErro}");
                RegistrarMetricaPainel($"{metricaNome}_duracao_ms", medicao.ElapsedMs(), "ms", $"corr={corr} resultado=negado codigo_erro={codigoErro}");
                return;
            }

            var clienteIdResultado = resultado.Cliente?.Id ?? ClienteEdicaoId ?? 0;
            LimparFormularioCadastroCliente();
            LimparCacheBuscaClientes(emEdicao ? "edicao_sucesso" : "cadastro_sucesso");
            RegistrarEventoPainel(
                emEdicao ? "cliente_edicao_sucesso" : "cliente_cadastro_sucesso",
                $"corr={corr} cliente_id={clienteIdResultado}");

            var refreshOk = await CarregarClientesPersistidosAsync(ConsultaClientesFiltro.PaginaMinima, atualizarTabela: true);
            if (!refreshOk)
            {
                DefinirMensagemCadastroCliente(
                    "Cliente salvo, mas não foi possível atualizar a lista agora.",
                    ClienteCadastroSeveridade.Warning,
                    ClienteCadastroCampoErro.Nenhum);

                RegistrarEventoPainel("cliente_cadastro_persistido_refresh_falha", $"corr={corr} cliente_id={clienteIdResultado}");
                RegistrarMetricaPainel($"{metricaNome}_duracao_ms", medicao.ElapsedMs(), "ms", $"corr={corr} resultado=sucesso_refresh_falha");
            }
            else
            {
                RegistrarMetricaPainel($"{metricaNome}_duracao_ms", medicao.ElapsedMs(), "ms", $"corr={corr} resultado=sucesso");
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AbrirClienteLista(ClienteResumoPainel? clienteResumo)
    {
        if (clienteResumo is null)
            return;

        RegistrarInteracaoPainel();
        LimparMensagemCadastroCliente();
        ConfirmacaoSalvarCadastroAberta = false;

        try
        {
            if (GruposEmpresariaisDisponiveis.Count == 0)
                await CarregarGruposEmpresariaisAsync();

            var cliente = await Task.Run(() => _clienteService.ObterPorIdPorUsuario(_userId, clienteResumo.Id));
            if (cliente is null)
            {
                DefinirMensagemCadastroCliente(
                    "Cliente não encontrado para edição.",
                    ClienteCadastroSeveridade.Warning,
                    ClienteCadastroCampoErro.Nenhum);
                return;
            }

            ClienteSelecionadoNaListaCadastro = clienteResumo;
            ClienteEdicaoId = cliente.Id;
            ModoEdicaoClienteCadastro = true;
            ClienteCodigoCadastro = cliente.CodigoCliente;
            ClienteNomeCadastro = cliente.Nome;
            ClienteNomeFantasiaCadastro = cliente.NomeFantasia;
            ClienteEmailCadastro = cliente.Email;
            ClienteTelefoneCadastro = cliente.Telefone;
            GrupoEmpresarialSelecionadoCadastro = cliente.GrupoEmpresarialId.HasValue
                ? GruposEmpresariaisDisponiveis.FirstOrDefault(g => g.Id == cliente.GrupoEmpresarialId.Value)
                : null;

            var documentoFormatado = FormatarDocumento(cliente.TipoDocumento, cliente.Documento);
            _aplicandoMascaraDocumentoCadastro = true;
            try
            {
                ClienteDocumentoCadastro = documentoFormatado;
            }
            finally
            {
                _aplicandoMascaraDocumentoCadastro = false;
            }

            ClienteDocumentoStatusCadastro = cliente.TipoDocumento == TipoDocumentoCliente.CPF
                ? "CPF válido"
                : "CNPJ válido";

            DefinirMensagemCadastroCliente(
                "Cliente carregado para edição.",
                ClienteCadastroSeveridade.Info,
                ClienteCadastroCampoErro.Nenhum);
            RegistrarEventoPainel("cliente_edicao_carregada", $"cliente_id={cliente.Id}");
        }
        catch (Exception ex)
        {
            DefinirMensagemCadastroCliente(
                "Falha ao carregar o cliente para edição.",
                ClienteCadastroSeveridade.Error,
                ClienteCadastroCampoErro.Nenhum,
                GerarReferenciaErroCadastroUi());
            RegistrarErroPainel("cliente_edicao_carregar_falha", ex);
        }
    }

    private bool PodeSolicitarInativarCliente(ClienteResumoPainel? clienteResumo) =>
        !IsBusy && CadastroClientesAberto && ((clienteResumo?.Id ?? ClienteEdicaoId ?? 0) > 0);

    [RelayCommand(CanExecute = nameof(PodeSolicitarInativarCliente))]
    private void SolicitarInativarCliente(ClienteResumoPainel? clienteResumo = null)
    {
        var clienteId = clienteResumo?.Id ?? ClienteEdicaoId;
        if (!clienteId.HasValue || clienteId.Value <= 0)
            return;

        RegistrarInteracaoPainel();
        _clienteInativacaoPendenteId = clienteId;
        var nome = clienteResumo?.Nome ?? ClienteNomeCadastro.Trim();
        var codigo = clienteResumo?.CodigoCliente ?? ClienteCodigoCadastro.Trim();
        ResumoConfirmacaoInativarCliente = string.IsNullOrWhiteSpace(codigo)
            ? $"Tem certeza que deseja inativar o cliente {nome}?"
            : $"Tem certeza que deseja inativar o cliente {codigo} - {nome}?";
        ConfirmacaoInativarClienteAberta = true;
    }

    private bool PodeConfirmarInativarCliente() =>
        !IsBusy && ConfirmacaoInativarClienteAberta && _clienteInativacaoPendenteId.HasValue;

    [RelayCommand(CanExecute = nameof(PodeConfirmarInativarCliente))]
    private async Task ConfirmarInativarCliente()
    {
        if (!_clienteInativacaoPendenteId.HasValue)
            return;

        RegistrarInteracaoPainel();
        var clienteId = _clienteInativacaoPendenteId.Value;
        ConfirmacaoInativarClienteAberta = false;
        IsBusy = true;

        try
        {
            var inativado = await Task.Run(() => _clienteService.Inativar(clienteId, _userId));
            if (!inativado)
            {
                DefinirMensagemCadastroCliente(
                    "Não foi possível inativar o cliente agora.",
                    ClienteCadastroSeveridade.Error,
                    ClienteCadastroCampoErro.Nenhum,
                    GerarReferenciaErroCadastroUi());
                return;
            }

            if (ClienteContextoId == clienteId)
                LimparClienteSelecionado();

            if (ClienteEdicaoId == clienteId)
                LimparFormularioCadastroCliente();

            LimparCacheBuscaClientes("inativacao_sucesso");
            var refreshOk = await CarregarClientesPersistidosAsync(ConsultaClientesFiltro.PaginaMinima, atualizarTabela: true);
            if (!refreshOk)
            {
                DefinirMensagemCadastroCliente(
                    "Cliente inativado, mas não foi possível atualizar a lista agora.",
                    ClienteCadastroSeveridade.Warning,
                    ClienteCadastroCampoErro.Nenhum);
            }
            else
            {
                DefinirMensagemCadastroCliente(
                    "Cliente inativado com sucesso.",
                    ClienteCadastroSeveridade.Info,
                    ClienteCadastroCampoErro.Nenhum);
            }

            RegistrarEventoPainel("cliente_inativado", $"cliente_id={clienteId}");
        }
        catch (Exception ex)
        {
            DefinirMensagemCadastroCliente(
                "Falha ao inativar cliente. Tente novamente.",
                ClienteCadastroSeveridade.Error,
                ClienteCadastroCampoErro.Nenhum,
                GerarReferenciaErroCadastroUi());
            RegistrarErroPainel("cliente_inativar_falha", ex, $"cliente_id={clienteId}");
        }
        finally
        {
            _clienteInativacaoPendenteId = null;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelarInativarCliente()
    {
        RegistrarInteracaoPainel();
        ConfirmacaoInativarClienteAberta = false;
        _clienteInativacaoPendenteId = null;
    }

    [RelayCommand]
    private async Task AbrirSeletorClientes()
    {
        RegistrarInteracaoPainel();
        SeletorClientesAberto = true;
        try
        {
            await AtualizarItensSeletorClientesAsync(ObterTermoBuscaSeletor(BuscaTexto));
        }
        catch (Exception ex)
        {
            AtualizarItensSeletorVazio("Não foi possível carregar os clientes agora.");
            RegistrarErroPainel("seletor_clientes_abrir_falha", ex);
        }
    }

    [RelayCommand]
    private void FecharSeletorClientes()
    {
        SeletorClientesAberto = false;
    }

    [RelayCommand]
    private async Task AlternarGrupoSeletor(SeletorClienteItemPainel? item)
    {
        if (item is null || item.Tipo != SeletorClienteTipoItem.Grupo)
            return;

        RegistrarInteracaoPainel();

        var chaveGrupo = string.IsNullOrWhiteSpace(item.GrupoChave)
            ? ChaveGrupoSemGrupo
            : item.GrupoChave;

        var expandidoAtual = !_estadoExpansaoGruposSeletor.TryGetValue(chaveGrupo, out var valor) || valor;
        _estadoExpansaoGruposSeletor[chaveGrupo] = !expandidoAtual;

        try
        {
            await AtualizarItensSeletorClientesAsync(ObterTermoBuscaSeletor(BuscaTexto));
        }
        catch (Exception ex)
        {
            AtualizarItensSeletorVazio("Não foi possível atualizar a lista agora.");
            RegistrarErroPainel("seletor_clientes_atualizar_falha", ex);
        }
    }

    [RelayCommand]
    private async Task SelecionarClienteSeletor(SeletorClienteItemPainel? item)
    {
        if (item is null || item.Tipo != SeletorClienteTipoItem.Cliente || !item.ClienteId.HasValue)
            return;

        RegistrarInteracaoPainel();
        SeletorClientesAberto = false;
        await AtivarContextoClientePorIdAsync(item.ClienteId.Value, "seletor_unificado");
    }

    [RelayCommand]
    private void LimparClienteSelecionado()
    {
        RegistrarInteracaoPainel();
        LimparContextoClienteSelecionado("Selecione um cliente para abrir o contexto contábil.");
        TextoClienteSelecionadoNoCampo = string.Empty;
        AtualizarBuscaTextoInterno(string.Empty);
        SeletorClientesAberto = false;
        ItensSeletorClientes.Clear();
    }

    private bool PodeIrPaginaAnteriorClientes() => !CarregandoClientes && PaginaClientesAtual > 1;

    [RelayCommand(CanExecute = nameof(PodeIrPaginaAnteriorClientes))]
    private async Task PaginaAnteriorClientes()
    {
        RegistrarInteracaoPainel();
        var paginaAnterior = Math.Max(1, PaginaClientesAtual - 1);
        await CarregarClientesPersistidosAsync(paginaAnterior, atualizarTabela: true);
    }

    private bool PodeIrProximaPaginaClientes() => !CarregandoClientes && PaginaClientesAtual < TotalPaginasClientes;

    [RelayCommand(CanExecute = nameof(PodeIrProximaPaginaClientes))]
    private async Task ProximaPaginaClientes()
    {
        RegistrarInteracaoPainel();
        var proximaPagina = PaginaClientesAtual + 1;
        await CarregarClientesPersistidosAsync(proximaPagina, atualizarTabela: true);
    }

    private async Task CarregarGruposEmpresariaisAsync()
    {
        var grupos = await Task.Run(() => _clienteService.ListarGrupos());
        var grupoSelecionadoId = GrupoEmpresarialSelecionadoCadastro?.Id;

        GruposEmpresariaisDisponiveis.Clear();
        foreach (var grupo in grupos)
        {
            GruposEmpresariaisDisponiveis.Add(new GrupoEmpresarialPainel(grupo.Id, grupo.Nome));
        }

        if (grupoSelecionadoId.HasValue)
        {
            GrupoEmpresarialSelecionadoCadastro = GruposEmpresariaisDisponiveis
                .FirstOrDefault(g => g.Id == grupoSelecionadoId.Value);
        }
    }

    private void LimparFormularioCadastroCliente()
    {
        ClienteCodigoCadastro = string.Empty;
        ClienteNomeCadastro = string.Empty;
        ClienteNomeFantasiaCadastro = string.Empty;
        ClienteDocumentoCadastro = string.Empty;
        ClienteDocumentoStatusCadastro = "Incompleto";
        GrupoEmpresarialSelecionadoCadastro = null;
        ClienteEmailCadastro = string.Empty;
        ClienteTelefoneCadastro = string.Empty;
        ConfirmacaoSalvarCadastroAberta = false;
        ConfirmacaoInativarClienteAberta = false;
        _clienteInativacaoPendenteId = null;
        ResetarModoEdicaoCadastro();
    }

    private void ResetarModoEdicaoCadastro()
    {
        ModoEdicaoClienteCadastro = false;
        ClienteEdicaoId = null;
        ClienteSelecionadoNaListaCadastro = null;
    }

    private void AplicarResultadoCadastroCliente(ResultadoCadastroCliente resultado)
    {
        var severidade = resultado.Severidade;
        var campo = resultado.CampoErro ?? ClienteCadastroCampoErro.Nenhum;

        DefinirMensagemCadastroCliente(
            resultado.Mensagem,
            severidade,
            campo,
            resultado.ReferenciaErro);
    }

    private void DefinirMensagemCadastroCliente(
        string mensagem,
        ClienteCadastroSeveridade severidade,
        ClienteCadastroCampoErro campoErro,
        string? referenciaErro = null)
    {
        MensagemCadastroCliente = mensagem;
        MensagemCadastroClienteSeveridade = severidade;
        CampoErroCadastro = campoErro;
        ReferenciaErroCadastro = referenciaErro;
    }

    private void LimparMensagemCadastroCliente()
    {
        MensagemCadastroCliente = null;
        MensagemCadastroClienteSeveridade = ClienteCadastroSeveridade.Info;
        CampoErroCadastro = ClienteCadastroCampoErro.Nenhum;
        ReferenciaErroCadastro = null;
    }

    private async Task AgendarBuscaClientesAsync(bool atualizarTabela)
    {
        _buscaClientesCts?.Cancel();
        _buscaClientesCts?.Dispose();

        _buscaClientesCts = new CancellationTokenSource();
        var token = _buscaClientesCts.Token;

        try
        {
            await Task.Delay(220, token);
            await CarregarClientesPersistidosAsync(ConsultaClientesFiltro.PaginaMinima, token, atualizarTabela);
        }
        catch (OperationCanceledException)
        {
            // Cancelamento esperado quando o usuario continua digitando e reinicia o debounce.
        }
    }

    private async Task<bool> CarregarClientesPersistidosAsync(
        int pagina,
        CancellationToken cancellationToken = default,
        bool atualizarTabela = true)
    {
        var termoConsulta = atualizarTabela
            ? ObterTermoBuscaTabelaClientes()
            : ObterTermoBuscaSeletor(BuscaTexto);
        var filtro = ConsultaClientesFiltro.Criar(termoConsulta, pagina, TamanhoPaginaClientes);
        var corr = GerarCorrelationId();
        var metrica = IniciarMetricaPainel("clientes_busca", corr);
        var sucesso = false;
        AtualizarEstadoCarregamentoBusca(emProgresso: true, atualizarTabela);

        try
        {
            RegistrarEventoPainel(
                "clientes_busca",
                $"corr={corr} termo_len={filtro.Termo?.Length ?? 0} pagina={filtro.Pagina} tamanho={filtro.TamanhoPagina}");

            var resultado = await BuscarClientesComCacheERetryAsync(filtro, corr, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            if (atualizarTabela)
            {
                AtualizarColecoesDeClientes(resultado);
            }
            else
            {
                ResumoBuscaClientes = resultado.TotalItens == 0
                    ? "Nenhum cliente encontrado para o filtro atual."
                    : $"Página {resultado.PaginaAtual}/{resultado.TotalPaginas} - {resultado.TotalItens} cliente(s).";
            }

            await AtualizarItensSeletorClientesAsync(ObterTermoBuscaSeletor(BuscaTexto), cancellationToken);
            RegistrarMetricaPainel("clientes_busca_duracao_ms", metrica.ElapsedMs(), "ms", $"corr={corr} total={resultado.TotalItens}");
            sucesso = true;
        }
        catch (OperationCanceledException)
        {
            // Cancelamento esperado ao trocar pagina ou atualizar termo durante a busca.
        }
        catch (Exception ex)
        {
            if (string.IsNullOrWhiteSpace(MensagemCadastroCliente))
            {
                DefinirMensagemCadastroCliente(
                    "Falha ao carregar clientes. Você pode continuar no painel e tentar novamente.",
                    ClienteCadastroSeveridade.Warning,
                    ClienteCadastroCampoErro.Nenhum);
            }

            ResumoBuscaClientes = "Dados de clientes indisponíveis no momento. Modo seguro ativo.";
            RegistrarEventoPainel("clientes_busca_fallback", $"corr={corr}");
            RegistrarMetricaPainel("clientes_busca_duracao_ms", metrica.ElapsedMs(), "ms", $"corr={corr} resultado=falha");
            AtualizarItensSeletorVazio("Não foi possível carregar a lista de clientes.");

            RegistrarErroPainel("clientes_busca_falha", ex, $"corr={corr}");
        }
        finally
        {
            AtualizarEstadoCarregamentoBusca(emProgresso: false, atualizarTabela);
        }

        return sucesso;
    }

    private async Task<PaginacaoResultado<Cliente>> BuscarClientesComCacheERetryAsync(
        ConsultaClientesFiltro filtro,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var chaveCache = CriarChaveCacheBuscaClientes(filtro);
        if (TryObterCacheBuscaClientes(chaveCache, out var cache))
        {
            RegistrarEventoPainel("clientes_busca_cache_hit", $"corr={correlationId}");
            return cache;
        }

        Exception? ultimaFalha = null;
        for (var tentativa = 0; tentativa <= BackoffBuscaClientes.Length; tentativa++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var resultado = await Task.Run(
                    () => _clienteService.BuscarPorUsuario(_userId, filtro.Termo, filtro.Pagina, filtro.TamanhoPagina),
                    cancellationToken);

                SalvarCacheBuscaClientes(chaveCache, resultado);
                if (tentativa > 0)
                {
                    RegistrarEventoPainel("clientes_busca_retry_sucesso", $"corr={correlationId} tentativa={tentativa + 1}");
                }

                return resultado;
            }
            catch (Exception ex) when (tentativa < BackoffBuscaClientes.Length && EhFalhaTransitoriaPersistencia(ex))
            {
                ultimaFalha = ex;
                RegistrarEventoPainel("clientes_busca_retry", $"corr={correlationId} tentativa={tentativa + 1}");
                await Task.Delay(BackoffBuscaClientes[tentativa], cancellationToken);
            }
        }

        throw ultimaFalha ?? new InvalidOperationException("Falha ao buscar clientes.");
    }

    private void AtualizarColecoesDeClientes(PaginacaoResultado<Cliente> resultado)
    {
        ClientesCadastrados.Clear();
        foreach (var cliente in resultado.Itens)
        {
            ClientesCadastrados.Add(new ClienteResumoPainel(
                cliente.Id,
                cliente.CodigoCliente,
                cliente.Nome,
                cliente.NomeFantasia,
                cliente.GrupoEmpresarialNome,
                cliente.TipoDocumento.ToString(),
                FormatarDocumento(cliente.TipoDocumento, cliente.Documento),
                cliente.Email,
                cliente.Telefone));
        }

        if (ClienteEdicaoId.HasValue)
        {
            ClienteSelecionadoNaListaCadastro = ClientesCadastrados
                .FirstOrDefault(c => c.Id == ClienteEdicaoId.Value);
        }
        else if (ClienteSelecionadoNaListaCadastro is not null)
        {
            ClienteSelecionadoNaListaCadastro = ClientesCadastrados
                .FirstOrDefault(c => c.Id == ClienteSelecionadoNaListaCadastro.Id);
        }

        PaginaClientesAtual = resultado.PaginaAtual;
        TotalClientesEncontrados = resultado.TotalItens;
        TotalPaginasClientes = resultado.TotalPaginas;
        ResumoBuscaClientes = resultado.TotalItens == 0
            ? "Nenhum cliente encontrado para o filtro atual."
            : $"Página {resultado.PaginaAtual}/{resultado.TotalPaginas} - {resultado.TotalItens} cliente(s).";
    }

    private async Task AtivarContextoClientePorIdAsync(int clienteId, string origem)
    {
        var cliente = await ObterClienteParaContextoAsync(clienteId);
        if (cliente is null)
        {
            LimparContextoClienteSelecionado(
                "Cliente não encontrado. Atualize a busca e tente novamente.",
                "Cliente não encontrado.");
            TextoClienteSelecionadoNoCampo = string.Empty;
            return;
        }

        var contextoAnterior = ClienteContextoId;
        if (contextoAnterior != cliente.Id)
            EncerrarFluxosTarefaPorMudancaContexto();

        if (contextoAnterior.HasValue && contextoAnterior.Value != cliente.Id)
        {
            SalvarEstadoEsteirasCliente(contextoAnterior.Value);
        }

        ClienteContextoId = cliente.Id;
        ClienteContextoNome = cliente.Nome;
        ClienteContextoResumo = string.IsNullOrWhiteSpace(cliente.CodigoCliente)
            ? $"Contexto ativo: {cliente.Nome}."
            : $"Contexto ativo: {cliente.CodigoCliente} - {cliente.Nome}.";

        var textoSelecionado = CriarTextoCampoSelecionado(cliente);
        TextoClienteSelecionadoNoCampo = textoSelecionado;
        AtualizarBuscaTextoInterno(textoSelecionado);

        if (contextoAnterior != cliente.Id)
        {
            RestaurarEstadoEsteirasCliente(cliente.Id);
            LimparColecoesEsteira();
            MensagemTarefasCliente = null;
            ResumoTarefasCliente = "Carregando tarefas do cliente selecionado...";
            UltimaAtualizacaoTarefas = "Atualizando...";
            TimelineExpandida = false;
            CategoriaTimelineSelecionada = CategoriaTimelineEsteira.Futuras;
        }

        RegistrarEventoPainel("cliente_contexto_selecionado", $"origem={origem} cliente_id={clienteId}");
        await CarregarTarefasClienteAsync();
    }

    private async Task<Cliente?> ObterClienteParaContextoAsync(int clienteId)
    {
        if (clienteId <= 0)
            return null;

        if (_mapaClientesPorId.TryGetValue(clienteId, out var cache))
            return cache;

        var cliente = await Task.Run(() => _clienteService.ObterPorIdPorUsuario(_userId, clienteId));
        if (cliente is null)
            return null;

        RegistrarClienteNoMapa(cliente);
        return cliente;
    }

    private void RegistrarClienteNoMapa(Cliente cliente)
    {
        _mapaClientesPorId[cliente.Id] = cliente;
    }

    private async Task AtualizarItensSeletorClientesAsync(string? termo, CancellationToken cancellationToken = default)
    {
        var clientesBase = await ObterClientesBaseSeletorAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var itens = ConstruirItensSeletor(clientesBase, termo);
        AtualizarItensSeletor(itens);
    }

    private async Task<IReadOnlyList<Cliente>> ObterClientesBaseSeletorAsync(CancellationToken cancellationToken)
    {
        if (_cacheClientesSeletor.Count > 0 && _cacheClientesSeletorExpiraEmUtc >= DateTimeOffset.UtcNow)
            return _cacheClientesSeletor;

        var clientes = await Task.Run(() => _clienteService.ListarTodosPorUsuario(_userId), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        _cacheClientesSeletor.Clear();
        _cacheClientesSeletor.AddRange(clientes);
        _cacheClientesSeletorExpiraEmUtc = DateTimeOffset.UtcNow.Add(CacheBuscaClientesTtl);

        foreach (var cliente in clientes)
            RegistrarClienteNoMapa(cliente);

        return _cacheClientesSeletor;
    }

    private IReadOnlyList<SeletorClienteItemPainel> ConstruirItensSeletor(IReadOnlyList<Cliente> clientes, string? termo)
    {
        if (clientes.Count == 0)
            return [CriarItemSeletorVazio("Nenhum cliente cadastrado.")];

        var termoBusca = termo?.Trim() ?? string.Empty;
        var termoNormalizado = NormalizarTextoBusca(termoBusca);
        var termoDocumento = DocumentoClienteValidator.ExtrairSomenteDigitos(termoBusca);
        var termoDocumentoExato = termoDocumento.Length == 11 || termoDocumento.Length == 14;
        var quantidadePalavras = termoBusca.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;

        var grupos = clientes
            .Where(c => !string.IsNullOrWhiteSpace(c.GrupoEmpresarialNome))
            .GroupBy(c => c.GrupoEmpresarialNome!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new GrupoSeletor(g.Key, g.ToList()))
            .OrderBy(g => NormalizarTextoBusca(g.Nome), StringComparer.Ordinal)
            .ToList();

        var clientesSemGrupo = clientes
            .Where(c => string.IsNullOrWhiteSpace(c.GrupoEmpresarialNome))
            .OrderBy(c => NormalizarTextoBusca(ObterNomePrincipalClienteSemGrupo(c)), StringComparer.Ordinal)
            .ThenBy(c => c.Id)
            .ToList();

        if (string.IsNullOrWhiteSpace(termoBusca))
        {
            return ConstruirItensSemFiltro(grupos, clientesSemGrupo);
        }

        if (quantidadePalavras <= 1)
        {
            var gruposCorrespondentes = grupos
                .Select(g => new
                {
                    Grupo = g,
                    Pontuacao = PontuarCorrespondenciaGrupo(g.Nome, termoNormalizado)
                })
                .Where(x => x.Pontuacao >= 0)
                .OrderBy(x => x.Pontuacao)
                .ThenBy(x => NormalizarTextoBusca(x.Grupo.Nome), StringComparer.Ordinal)
                .ToList();

            if (gruposCorrespondentes.Count > 0)
            {
                var itensGrupos = new List<SeletorClienteItemPainel>();
                foreach (var correspondencia in gruposCorrespondentes)
                {
                    var chaveGrupo = CriarChaveGrupo(correspondencia.Grupo.Nome);
                    _estadoExpansaoGruposSeletor[chaveGrupo] = true;
                    var clientesDoGrupo = correspondencia.Grupo.Clientes
                        .OrderBy(c => NormalizarTextoBusca(ObterNomeOrdenacaoCliente(c)), StringComparer.Ordinal)
                        .ThenBy(c => c.Id)
                        .ToList();

                    itensGrupos.Add(CriarItemGrupo(correspondencia.Grupo.Nome, chaveGrupo, true, correspondencia.Pontuacao <= 1, clientesDoGrupo.Count));
                    foreach (var cliente in clientesDoGrupo)
                    {
                        itensGrupos.Add(CriarItemCliente(cliente, nivelIndentacao: 1, destacar: true, exibirSomenteFantasiaSemGrupo: false));
                    }
                }

                return itensGrupos;
            }
        }

        var correspondenciasClientes = clientes
            .Select(cliente => new
            {
                Cliente = cliente,
                Pontuacao = PontuarCorrespondenciaCliente(cliente, termoNormalizado, termoDocumento, termoDocumentoExato)
            })
            .Where(x => x.Pontuacao >= 0)
            .OrderBy(x => x.Pontuacao)
            .ThenBy(x => NormalizarTextoBusca(ObterNomeOrdenacaoCliente(x.Cliente)), StringComparer.Ordinal)
            .ThenBy(x => x.Cliente.Id)
            .ToList();

        if (correspondenciasClientes.Count == 0)
            return [CriarItemSeletorVazio("Nenhum cliente encontrado.")];

        return correspondenciasClientes
            .Select(x => CriarItemCliente(
                x.Cliente,
                nivelIndentacao: 0,
                destacar: true,
                exibirSomenteFantasiaSemGrupo: string.IsNullOrWhiteSpace(x.Cliente.GrupoEmpresarialNome)))
            .ToList();
    }

    private IReadOnlyList<SeletorClienteItemPainel> ConstruirItensSemFiltro(IReadOnlyList<GrupoSeletor> grupos, IReadOnlyList<Cliente> clientesSemGrupo)
    {
        var itens = new List<SeletorClienteItemPainel>(grupos.Count + clientesSemGrupo.Count + 4);

        foreach (var grupo in grupos)
        {
            var chaveGrupo = CriarChaveGrupo(grupo.Nome);
            var expandido = ObterEstadoExpandidoGrupo(chaveGrupo, expandidoPadrao: false);
            var clientesDoGrupo = grupo.Clientes
                .OrderBy(c => NormalizarTextoBusca(ObterNomeOrdenacaoCliente(c)), StringComparer.Ordinal)
                .ThenBy(c => c.Id)
                .ToList();

            itens.Add(CriarItemGrupo(grupo.Nome, chaveGrupo, expandido, destacado: false, clientesDoGrupo.Count));
            if (!expandido)
                continue;

            foreach (var cliente in clientesDoGrupo)
            {
                itens.Add(CriarItemCliente(cliente, nivelIndentacao: 1, destacar: false, exibirSomenteFantasiaSemGrupo: false));
            }
        }

        foreach (var cliente in clientesSemGrupo)
        {
            itens.Add(CriarItemCliente(cliente, nivelIndentacao: 0, destacar: false, exibirSomenteFantasiaSemGrupo: true));
        }

        if (itens.Count == 0)
            itens.Add(CriarItemSeletorVazio("Nenhum cliente encontrado."));

        return itens;
    }

    private static int PontuarCorrespondenciaGrupo(string nomeGrupo, string termoNormalizado)
    {
        if (string.IsNullOrWhiteSpace(termoNormalizado))
            return -1;

        var grupoNormalizado = NormalizarTextoBusca(nomeGrupo);
        if (grupoNormalizado.Equals(termoNormalizado, StringComparison.Ordinal))
            return 0;
        if (grupoNormalizado.StartsWith(termoNormalizado, StringComparison.Ordinal))
            return 1;
        if (grupoNormalizado.Contains(termoNormalizado, StringComparison.Ordinal))
            return 2;
        return -1;
    }

    private static int PontuarCorrespondenciaCliente(
        Cliente cliente,
        string termoNormalizado,
        string termoDocumento,
        bool termoDocumentoExato)
    {
        if (string.IsNullOrWhiteSpace(termoNormalizado))
            return 100;

        var codigo = NormalizarTextoBusca(cliente.CodigoCliente);
        var nome = NormalizarTextoBusca(cliente.Nome);
        var fantasia = NormalizarTextoBusca(cliente.NomeFantasia);
        var grupo = NormalizarTextoBusca(cliente.GrupoEmpresarialNome);
        var documento = DocumentoClienteValidator.ExtrairSomenteDigitos(cliente.Documento);

        if (!string.IsNullOrWhiteSpace(codigo))
        {
            if (codigo.Equals(termoNormalizado, StringComparison.Ordinal))
                return 0;
            if (codigo.StartsWith(termoNormalizado, StringComparison.Ordinal))
                return 1;
            if (codigo.Contains(termoNormalizado, StringComparison.Ordinal))
                return 2;
        }

        if (termoDocumentoExato && string.Equals(documento, termoDocumento, StringComparison.Ordinal))
            return 3;

        if (!string.IsNullOrWhiteSpace(fantasia))
        {
            if (fantasia.Equals(termoNormalizado, StringComparison.Ordinal))
                return 4;
            if (fantasia.StartsWith(termoNormalizado, StringComparison.Ordinal))
                return 5;
            if (fantasia.Contains(termoNormalizado, StringComparison.Ordinal))
                return 6;
        }

        if (!string.IsNullOrWhiteSpace(nome))
        {
            if (nome.Equals(termoNormalizado, StringComparison.Ordinal))
                return 7;
            if (nome.StartsWith(termoNormalizado, StringComparison.Ordinal))
                return 8;
            if (nome.Contains(termoNormalizado, StringComparison.Ordinal))
                return 9;
        }

        if (!string.IsNullOrWhiteSpace(grupo))
        {
            if (grupo.StartsWith(termoNormalizado, StringComparison.Ordinal))
                return 10;
            if (grupo.Contains(termoNormalizado, StringComparison.Ordinal))
                return 11;
        }

        return -1;
    }

    private static string CriarChaveGrupo(string nomeGrupo)
    {
        var nome = string.IsNullOrWhiteSpace(nomeGrupo) ? NomeGrupoSemGrupo : nomeGrupo.Trim();
        return $"grupo::{NormalizarTextoBusca(nome)}";
    }

    private bool ObterEstadoExpandidoGrupo(string chaveGrupo, bool expandidoPadrao)
    {
        if (_estadoExpansaoGruposSeletor.TryGetValue(chaveGrupo, out var expandido))
            return expandido;

        _estadoExpansaoGruposSeletor[chaveGrupo] = expandidoPadrao;
        return expandidoPadrao;
    }

    private static string ObterNomeOrdenacaoCliente(Cliente cliente)
    {
        if (!string.IsNullOrWhiteSpace(cliente.NomeFantasia))
            return cliente.NomeFantasia!;

        return cliente.Nome;
    }

    private static string ObterNomePrincipalClienteSemGrupo(Cliente cliente)
    {
        if (!string.IsNullOrWhiteSpace(cliente.NomeFantasia))
            return cliente.NomeFantasia!.Trim();

        return cliente.Nome.Trim();
    }

    private static string CriarTextoCampoSelecionado(Cliente cliente)
    {
        var nome = cliente.Nome.Trim();
        var fantasia = cliente.NomeFantasia?.Trim();
        if (string.IsNullOrWhiteSpace(fantasia) ||
            string.Equals(nome, fantasia, StringComparison.OrdinalIgnoreCase))
        {
            return nome;
        }

        return $"{nome} ({fantasia})";
    }

    private static SeletorClienteItemPainel CriarItemGrupo(
        string nomeGrupo,
        string chaveGrupo,
        bool expandido,
        bool destacado,
        int totalClientes)
    {
        var subtitulo = totalClientes == 1 ? "1 cliente" : $"{totalClientes} clientes";
        return new SeletorClienteItemPainel(
            SeletorClienteTipoItem.Grupo,
            null,
            nomeGrupo,
            nomeGrupo,
            subtitulo,
            0,
            expandido,
            chaveGrupo,
            destacado);
    }

    private static SeletorClienteItemPainel CriarItemCliente(
        Cliente cliente,
        int nivelIndentacao,
        bool destacar,
        bool exibirSomenteFantasiaSemGrupo)
    {
        var semGrupo = string.IsNullOrWhiteSpace(cliente.GrupoEmpresarialNome);
        var textoPrincipal = exibirSomenteFantasiaSemGrupo && semGrupo
            ? ObterNomePrincipalClienteSemGrupo(cliente)
            : cliente.Nome.Trim();

        var partesSubtitulo = new List<string>();
        if (!string.IsNullOrWhiteSpace(cliente.CodigoCliente))
            partesSubtitulo.Add($"Código: {cliente.CodigoCliente.Trim()}");

        if (semGrupo)
        {
            if (!string.IsNullOrWhiteSpace(cliente.NomeFantasia) &&
                !string.Equals(cliente.NomeFantasia.Trim(), cliente.Nome.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                partesSubtitulo.Add($"Razão: {cliente.Nome.Trim()}");
            }
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(cliente.NomeFantasia) &&
                !string.Equals(cliente.NomeFantasia.Trim(), cliente.Nome.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                partesSubtitulo.Add($"Fantasia: {cliente.NomeFantasia.Trim()}");
            }

            partesSubtitulo.Add($"Grupo: {cliente.GrupoEmpresarialNome!.Trim()}");
        }

        var subtitulo = partesSubtitulo.Count == 0
            ? null
            : string.Join(" | ", partesSubtitulo);

        return new SeletorClienteItemPainel(
            SeletorClienteTipoItem.Cliente,
            cliente.Id,
            cliente.GrupoEmpresarialNome,
            textoPrincipal,
            subtitulo,
            nivelIndentacao,
            false,
            semGrupo ? ChaveGrupoSemGrupo : CriarChaveGrupo(cliente.GrupoEmpresarialNome!),
            destacar);
    }

    private static SeletorClienteItemPainel CriarItemSeletorVazio(string mensagem)
    {
        return new SeletorClienteItemPainel(
            SeletorClienteTipoItem.Vazio,
            null,
            null,
            mensagem,
            null,
            0,
            false,
            string.Empty,
            false);
    }

    private void AtualizarItensSeletor(IReadOnlyList<SeletorClienteItemPainel> itens)
    {
        ItensSeletorClientes.Clear();
        foreach (var item in itens)
            ItensSeletorClientes.Add(item);
    }

    private void AtualizarItensSeletorVazio(string mensagem)
    {
        AtualizarItensSeletor([CriarItemSeletorVazio(mensagem)]);
    }

    private void AtualizarBuscaTextoInterno(string texto)
    {
        _atualizandoBuscaTextoInternamente = true;
        try
        {
            BuscaTexto = texto;
        }
        finally
        {
            _atualizandoBuscaTextoInternamente = false;
        }
    }

    private string ObterTermoBuscaTabelaClientes()
    {
        var termoLista = BuscaListaClientesCadastro?.Trim();
        if (!string.IsNullOrWhiteSpace(termoLista))
            return termoLista;

        return ObterTermoBuscaSeletor(BuscaTexto);
    }

    private string ObterTermoBuscaSeletor(string? termo)
    {
        var termoNormalizado = termo?.Trim() ?? string.Empty;
        var textoSelecionado = TextoClienteSelecionadoNoCampo?.Trim() ?? string.Empty;
        if (TemClienteSelecionado &&
            !string.IsNullOrWhiteSpace(textoSelecionado) &&
            string.Equals(termoNormalizado, textoSelecionado, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return termoNormalizado;
    }

    private void LimparContextoClienteSelecionado(string resumoContexto, string? resumoTarefas = null)
    {
        if (ClienteContextoId.HasValue)
            SalvarEstadoEsteirasCliente(ClienteContextoId.Value);

        EncerrarFluxosTarefaPorMudancaContexto();
        ClienteContextoId = null;
        ClienteContextoNome = "Nenhum cliente selecionado";
        ClienteContextoResumo = resumoContexto;
        LimparEstadoEsteiraParaNovoCliente();
        LimparEstadoTarefas(resumoTarefas ?? "Selecione um cliente para carregar tarefas.");
    }

    private static string NormalizarTextoBusca(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return string.Empty;

        var normalizado = valor.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalizado.Length);
        foreach (var c in normalizado)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToUpperInvariant(c));
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private void CarregarClientesPersistidos()
    {
        _ = CarregarClientesPersistidosAsync(ConsultaClientesFiltro.PaginaMinima, atualizarTabela: false);
    }

    private void AtualizarEstadoCarregamentoBusca(bool emProgresso, bool atualizarTabela)
    {
        if (atualizarTabela)
        {
            CarregandoClientes = emProgresso;
            return;
        }

        CarregandoContextoCliente = emProgresso;
    }

    private static bool EhFalhaTransitoriaPersistencia(Exception ex)
    {
        var nomeTipo = ex.GetType().Name;
        if (nomeTipo.Contains("Timeout", StringComparison.OrdinalIgnoreCase) ||
            nomeTipo.Contains("NpgsqlException", StringComparison.OrdinalIgnoreCase) ||
            nomeTipo.Contains("SqliteException", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var mensagem = ex.Message ?? string.Empty;
        return mensagem.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
               mensagem.Contains("tempor", StringComparison.OrdinalIgnoreCase) ||
               mensagem.Contains("locked", StringComparison.OrdinalIgnoreCase) ||
               mensagem.Contains("busy", StringComparison.OrdinalIgnoreCase);
    }

    private string CriarChaveCacheBuscaClientes(ConsultaClientesFiltro filtro)
    {
        return $"{filtro.Termo ?? "<vazio>"}|{filtro.Pagina}|{filtro.TamanhoPagina}";
    }

    private bool TryObterCacheBuscaClientes(string chave, out PaginacaoResultado<Cliente> resultado)
    {
        if (_cacheBuscaClientes.TryGetValue(chave, out var item) && item.ExpiraEmUtc >= DateTimeOffset.UtcNow)
        {
            resultado = item.Resultado;
            return true;
        }

        _cacheBuscaClientes.Remove(chave);
        resultado = default!;
        return false;
    }

    private void SalvarCacheBuscaClientes(string chave, PaginacaoResultado<Cliente> resultado)
    {
        _cacheBuscaClientes[chave] = new ItemCacheBuscaClientes(resultado, DateTimeOffset.UtcNow.Add(CacheBuscaClientesTtl));
    }

    private void LimparCacheBuscaClientes(string motivo)
    {
        _cacheBuscaClientes.Clear();
        _cacheClientesSeletor.Clear();
        _cacheClientesSeletorExpiraEmUtc = DateTimeOffset.MinValue;
        _mapaClientesPorId.Clear();
        RegistrarEventoPainel("clientes_cache_limpo", $"motivo={motivo}");
    }

    private static string GerarReferenciaErroCadastroUi()
    {
        return $"CLI-CAD-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
    }

    private string ObterCorBordaCampo(ClienteCadastroCampoErro campo)
    {
        return CampoErroCadastro == campo ? CorBordaCampoErro : CorBordaCampoNormal;
    }

    private static string FormatarDocumento(TipoDocumentoCliente tipo, string documento)
    {
        return DocumentoClienteValidator.FormatarDocumentoCompleto(tipo, documento);
    }

    public bool ValidarCampoCadastroParaNavegacao(string campoId)
    {
        var campo = (campoId ?? string.Empty).Trim().ToLowerInvariant();
        return campo switch
        {
            "codigo" => ValidarCodigoClienteCadastro(exibirMensagem: true),
            "nome" => ValidarNomeClienteCadastro(exibirMensagem: true),
            "fantasia" => ValidarNomeFantasiaCadastro(exibirMensagem: true),
            "documento" => ValidarDocumentoCadastro(exibirMensagem: true),
            "grupo" => ValidarGrupoCadastro(exibirMensagem: true),
            "email" => ValidarEmailCadastro(exibirMensagem: true),
            "telefone" => ValidarTelefoneCadastro(exibirMensagem: true),
            _ => true
        };
    }

    private bool ValidarFormularioCadastroParaConfirmacao()
    {
        return ValidarCodigoClienteCadastro(exibirMensagem: true) &&
               ValidarNomeClienteCadastro(exibirMensagem: true) &&
               ValidarNomeFantasiaCadastro(exibirMensagem: true) &&
               ValidarDocumentoCadastro(exibirMensagem: true) &&
               ValidarGrupoCadastro(exibirMensagem: true) &&
               ValidarEmailCadastro(exibirMensagem: true) &&
               ValidarTelefoneCadastro(exibirMensagem: true);
    }

    private bool ValidarCodigoClienteCadastro(bool exibirMensagem)
    {
        var codigo = (ClienteCodigoCadastro ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(codigo))
            return FalhaValidacao("Informe o código do cliente.", ClienteCadastroCampoErro.CodigoCliente, exibirMensagem);

        if (codigo.Length > ClienteInputLimits.MaxCodigoCliente)
            return FalhaValidacao("Código do cliente excede o limite permitido.", ClienteCadastroCampoErro.CodigoCliente, exibirMensagem);

        if (!CodigoClienteRegex.IsMatch(codigo))
        {
            return FalhaValidacao(
                "Código do cliente inválido. Use letras, números e os separadores -, _, ., /.",
                ClienteCadastroCampoErro.CodigoCliente,
                exibirMensagem);
        }

        ClienteCodigoCadastro = codigo;
        return true;
    }

    private bool ValidarNomeClienteCadastro(bool exibirMensagem)
    {
        var nome = (ClienteNomeCadastro ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(nome))
            return FalhaValidacao("Informe o nome do cliente.", ClienteCadastroCampoErro.Nome, exibirMensagem);

        if (nome.Length > ClienteInputLimits.MaxNome)
            return FalhaValidacao("Nome do cliente excede o limite permitido.", ClienteCadastroCampoErro.Nome, exibirMensagem);

        ClienteNomeCadastro = nome;
        return true;
    }

    private bool ValidarNomeFantasiaCadastro(bool exibirMensagem)
    {
        var fantasia = (ClienteNomeFantasiaCadastro ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(fantasia))
        {
            ClienteNomeFantasiaCadastro = string.Empty;
            return true;
        }

        if (fantasia.Length > ClienteInputLimits.MaxNomeFantasia)
            return FalhaValidacao("Nome fantasia excede o limite permitido.", ClienteCadastroCampoErro.NomeFantasia, exibirMensagem);

        ClienteNomeFantasiaCadastro = fantasia;
        return true;
    }

    private bool ValidarDocumentoCadastro(bool exibirMensagem)
    {
        var analise = DocumentoClienteValidator.Analisar(ClienteDocumentoCadastro);
        if (!analise.CaracteresPermitidos)
        {
            return FalhaValidacao(
                "Documento inválido. Use apenas números e os separadores ., -, /.",
                ClienteCadastroCampoErro.Documento,
                exibirMensagem);
        }

        var documentoSomenteDigitos = analise.DocumentoSomenteDigitos;
        if (string.IsNullOrWhiteSpace(documentoSomenteDigitos))
            return FalhaValidacao("Informe o CPF ou CNPJ do cliente.", ClienteCadastroCampoErro.Documento, exibirMensagem);

        if (documentoSomenteDigitos.Length != 11 && documentoSomenteDigitos.Length != 14)
        {
            return FalhaValidacao(
                "Documento inválido. Informe 11 dígitos para CPF ou 14 para CNPJ.",
                ClienteCadastroCampoErro.Documento,
                exibirMensagem);
        }

        if (analise.Status == StatusAnaliseDocumentoCliente.Valido)
            return true;

        if (analise.Status == StatusAnaliseDocumentoCliente.Invalido)
        {
            if (analise.TipoDocumento == TipoDocumentoCliente.CPF)
                return FalhaValidacao("CPF inválido.", ClienteCadastroCampoErro.Documento, exibirMensagem);
            if (analise.TipoDocumento == TipoDocumentoCliente.CNPJ)
                return FalhaValidacao("CNPJ inválido.", ClienteCadastroCampoErro.Documento, exibirMensagem);

            return FalhaValidacao("Documento inválido.", ClienteCadastroCampoErro.Documento, exibirMensagem);
        }

        return FalhaValidacao(
            "Documento inválido. Informe 11 dígitos para CPF ou 14 para CNPJ.",
            ClienteCadastroCampoErro.Documento,
            exibirMensagem);
    }

    private bool ValidarGrupoCadastro(bool exibirMensagem)
    {
        if (GrupoEmpresarialSelecionadoCadastro is null)
            return true;

        if (GrupoEmpresarialSelecionadoCadastro.Id <= 0)
            return FalhaValidacao("Grupo empresarial inválido.", ClienteCadastroCampoErro.Grupo, exibirMensagem);

        return true;
    }

    private bool ValidarEmailCadastro(bool exibirMensagem)
    {
        var email = (ClienteEmailCadastro ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(email))
        {
            ClienteEmailCadastro = string.Empty;
            return true;
        }

        if (email.Length > ClienteInputLimits.MaxEmail)
            return FalhaValidacao("E-mail do cliente excede o limite permitido.", ClienteCadastroCampoErro.Email, exibirMensagem);

        if (!EmailValidator.EhValido(email))
            return FalhaValidacao("E-mail do cliente inválido.", ClienteCadastroCampoErro.Email, exibirMensagem);

        ClienteEmailCadastro = email;
        return true;
    }

    private bool ValidarTelefoneCadastro(bool exibirMensagem)
    {
        var telefone = (ClienteTelefoneCadastro ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(telefone))
        {
            ClienteTelefoneCadastro = string.Empty;
            return true;
        }

        if (telefone.Length > ClienteInputLimits.MaxTelefone)
            return FalhaValidacao("Telefone do cliente excede o limite permitido.", ClienteCadastroCampoErro.Telefone, exibirMensagem);

        var somenteDigitos = new string(telefone.Where(char.IsDigit).ToArray());
        if (somenteDigitos.Length < 10 || somenteDigitos.Length > 11)
            return FalhaValidacao("Telefone inválido. Informe DDD + número (10 ou 11 dígitos).", ClienteCadastroCampoErro.Telefone, exibirMensagem);

        ClienteTelefoneCadastro = telefone;
        return true;
    }

    private bool FalhaValidacao(string mensagem, ClienteCadastroCampoErro campo, bool exibirMensagem)
    {
        if (exibirMensagem)
        {
            DefinirMensagemCadastroCliente(
                mensagem,
                ClienteCadastroSeveridade.Error,
                campo);
        }

        return false;
    }

    private string CriarResumoConfirmacaoSalvar()
    {
        var nome = string.IsNullOrWhiteSpace(ClienteNomeCadastro) ? "sem nome" : ClienteNomeCadastro.Trim();
        var codigo = string.IsNullOrWhiteSpace(ClienteCodigoCadastro) ? "sem código" : ClienteCodigoCadastro.Trim();
        var acao = ModoEdicaoClienteCadastro ? "salvar as alterações" : "cadastrar";

        if (ModoEdicaoClienteCadastro)
            return $"Confirma {acao} do cliente {codigo} - {nome}?";

        var documento = string.IsNullOrWhiteSpace(ClienteDocumentoCadastro) ? "sem documento" : ClienteDocumentoCadastro.Trim();
        return $"Confirma {acao} do cliente {codigo} - {nome}?\nDocumento: {documento}";
    }

    private sealed record GrupoSeletor(string Nome, List<Cliente> Clientes);
    private sealed record ItemCacheBuscaClientes(PaginacaoResultado<Cliente> Resultado, DateTimeOffset ExpiraEmUtc);

    private void AtualizarStatusDocumentoCadastro(AnaliseDocumentoCliente analise, int totalDigitosInformados)
    {
        if (!analise.CaracteresPermitidos)
        {
            ClienteDocumentoStatusCadastro = "Documento inválido";
            return;
        }

        if (analise.Status == StatusAnaliseDocumentoCliente.Valido)
        {
            ClienteDocumentoStatusCadastro = analise.TipoDocumento == TipoDocumentoCliente.CPF
                ? "CPF válido"
                : "CNPJ válido";
            return;
        }

        if (analise.Status == StatusAnaliseDocumentoCliente.Invalido)
        {
            ClienteDocumentoStatusCadastro = "Documento inválido";
            return;
        }

        ClienteDocumentoStatusCadastro = totalDigitosInformados > ClienteInputLimits.MaxDocumento
            ? "Documento inválido"
            : "Incompleto";
    }
}
