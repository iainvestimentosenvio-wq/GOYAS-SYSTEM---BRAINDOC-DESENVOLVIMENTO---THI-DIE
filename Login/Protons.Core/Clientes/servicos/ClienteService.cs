using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Protons.Core.Clientes.Exceptions;
using Protons.Core.Clientes.Models;
using Protons.Core.Clientes.Repositories;
using Protons.Core.Clientes.Security;
using Protons.Core.Clientes.Validation;
using Protons.Core.Login.Models;
using Protons.Core.Login.Repositories;
using Protons.Core.Login.Services;
using Protons.Core.Login.Validation;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Repositories;

namespace Protons.Core.Clientes.Services;

public sealed class ClienteService : IClienteService
{
    private static readonly Regex CodigoClienteRegex = new(
        "^[A-Z0-9][A-Z0-9\\-_./]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IClienteRepository _clientes;
    private readonly IAuditService? _audit;
    private readonly IUserRepository? _users;
    private readonly IClientePermissaoRepository? _clientePermissoes;
    private readonly IClienteDataProtector? _dataProtector;
    private readonly string _maquina;
    private readonly string _versaoApp;
    private readonly bool _usarHashChain;
    private readonly TimeProvider _timeProvider;

    public ClienteService(IClienteRepository clientes)
    {
        _clientes = clientes;
        _audit = null;
        _users = null;
        _clientePermissoes = null;
        _dataProtector = null;
        _maquina = string.Empty;
        _versaoApp = "0.0.0";
        _timeProvider = TimeProvider.System;
    }

    public ClienteService(
        IClienteRepository clientes,
        IAuditService audit,
        string maquina,
        string versaoApp,
        bool usarHashChain,
        IUserRepository? users = null,
        IClientePermissaoRepository? clientePermissoes = null,
        IClienteDataProtector? dataProtector = null,
        TimeProvider? timeProvider = null)
    {
        _clientes = clientes;
        _audit = audit;
        _users = users;
        _clientePermissoes = clientePermissoes;
        _dataProtector = dataProtector;
        _maquina = maquina ?? string.Empty;
        _versaoApp = string.IsNullOrWhiteSpace(versaoApp) ? "0.0.0" : versaoApp;
        _usarHashChain = usarHashChain;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ResultadoCadastroCliente Cadastrar(ClienteCadastroEntrada entrada)
    {
        try
        {
            var codigoCliente = NormalizarCodigoCliente(entrada.CodigoCliente);
            var nome = (entrada.Nome ?? string.Empty).Trim();
            var nomeFantasia = NormalizarNomeFantasia(entrada.NomeFantasia);
            var documentoEntrada = entrada.Documento;
            var documento = DocumentoClienteValidator.ExtrairSomenteDigitos(documentoEntrada);
            var email = NormalizarEmail(entrada.Email);
            var telefone = NormalizarTelefone(entrada.Telefone);

            if (entrada.CriadoPorUserId <= 0)
                return Falha("Usuário responsável inválido.", ClienteCadastroErroCodigo.UsuarioResponsavelInvalido, ClienteCadastroCampoErro.Nenhum);

            if (string.IsNullOrWhiteSpace(nome))
                return Falha("Informe o nome do cliente.", ClienteCadastroErroCodigo.NomeObrigatorio, ClienteCadastroCampoErro.Nome);

            if (string.IsNullOrWhiteSpace(codigoCliente))
                return Falha("Informe o código do cliente.", ClienteCadastroErroCodigo.CodigoObrigatorio, ClienteCadastroCampoErro.CodigoCliente);

            if (codigoCliente.Length > ClienteInputLimits.MaxCodigoCliente)
                return Falha("Código do cliente excede o limite permitido.", ClienteCadastroErroCodigo.CodigoInvalido, ClienteCadastroCampoErro.CodigoCliente);

            if (!CodigoClienteEhValido(codigoCliente))
                return Falha(
                    "Código do cliente inválido. Use letras, números e os separadores -, _, ., /.",
                    ClienteCadastroErroCodigo.CodigoInvalido,
                    ClienteCadastroCampoErro.CodigoCliente);

            if (nome.Length > ClienteInputLimits.MaxNome)
                return Falha("Nome do cliente excede o limite permitido.", ClienteCadastroErroCodigo.NomeInvalido, ClienteCadastroCampoErro.Nome);

            if (!string.IsNullOrWhiteSpace(nomeFantasia) && nomeFantasia.Length > ClienteInputLimits.MaxNomeFantasia)
                return Falha("Nome fantasia excede o limite permitido.", ClienteCadastroErroCodigo.NomeInvalido, ClienteCadastroCampoErro.Nome);

            if (!DocumentoClienteValidator.PossuiSomenteCaracteresPermitidos(documentoEntrada))
            {
                return Falha(
                    "Documento inválido. Use apenas números e os separadores ., -, /.",
                    ClienteCadastroErroCodigo.DocumentoCaracteresInvalidos,
                    ClienteCadastroCampoErro.Documento);
            }

            if (string.IsNullOrWhiteSpace(documento))
                return Falha("Informe o CPF ou CNPJ do cliente.", ClienteCadastroErroCodigo.DocumentoObrigatorio, ClienteCadastroCampoErro.Documento);

            if (documento.Length != 11 && documento.Length != 14)
            {
                return Falha(
                    "Documento inválido. Informe 11 dígitos para CPF ou 14 para CNPJ.",
                    ClienteCadastroErroCodigo.DocumentoTamanhoInvalido,
                    ClienteCadastroCampoErro.Documento);
            }

            var tipoDocumentoInferido = DocumentoClienteValidator.DetectarTipoPorDocumento(documento);
            if (!tipoDocumentoInferido.HasValue)
            {
                return Falha(
                    "Documento inválido. Informe 11 dígitos para CPF ou 14 para CNPJ.",
                    ClienteCadastroErroCodigo.DocumentoTamanhoInvalido,
                    ClienteCadastroCampoErro.Documento);
            }

            if (!DocumentoClienteValidator.EhValido(tipoDocumentoInferido.Value, documento))
            {
                return tipoDocumentoInferido.Value == TipoDocumentoCliente.CPF
                    ? Falha("CPF inválido.", ClienteCadastroErroCodigo.CpfInvalido, ClienteCadastroCampoErro.Documento)
                    : Falha("CNPJ inválido.", ClienteCadastroErroCodigo.CnpjInvalido, ClienteCadastroCampoErro.Documento);
            }

            if (!string.IsNullOrWhiteSpace(email))
            {
                if (email.Length > ClienteInputLimits.MaxEmail)
                    return Falha("E-mail do cliente excede o limite permitido.", ClienteCadastroErroCodigo.EmailInvalido, ClienteCadastroCampoErro.Email);

                if (!EmailValidator.EhValido(email))
                    return Falha("E-mail do cliente inválido.", ClienteCadastroErroCodigo.EmailInvalido, ClienteCadastroCampoErro.Email);
            }

            if (!string.IsNullOrWhiteSpace(telefone))
            {
                if (telefone.Length > ClienteInputLimits.MaxTelefone)
                    return Falha("Telefone do cliente excede o limite permitido.", ClienteCadastroErroCodigo.TelefoneInvalido, ClienteCadastroCampoErro.Telefone);

                var somenteDigitosTelefone = new string(telefone.Where(char.IsDigit).ToArray());
                if (somenteDigitosTelefone.Length is < 10 or > 11)
                    return Falha("Telefone inválido. Informe DDD + número (10 ou 11 dígitos).", ClienteCadastroErroCodigo.TelefoneInvalido, ClienteCadastroCampoErro.Telefone);
            }

            var clienteExistente = ExecutarComRetry(() => _clientes.GetByDocumento(documento));
            if (clienteExistente is not null)
            {
                return Falha(
                    "Já existe cliente ativo cadastrado com este documento.",
                    ClienteCadastroErroCodigo.DocumentoDuplicado,
                    ClienteCadastroCampoErro.Documento);
            }

            var clienteMesmoCodigo = ExecutarComRetry(() => _clientes.GetByCodigoCliente(codigoCliente));
            if (clienteMesmoCodigo is not null)
            {
                return Falha(
                    "Já existe cliente ativo cadastrado com este código.",
                    ClienteCadastroErroCodigo.CodigoDuplicado,
                    ClienteCadastroCampoErro.CodigoCliente);
            }

            GrupoEmpresarial? grupo = null;
            if (entrada.GrupoEmpresarialId.HasValue)
            {
                grupo = ExecutarComRetry(() => _clientes.GetGrupoById(entrada.GrupoEmpresarialId.Value));
                if (grupo is null || !grupo.Ativo)
                {
                    return Falha(
                        "Grupo empresarial inválido.",
                        ClienteCadastroErroCodigo.GrupoInvalido,
                        ClienteCadastroCampoErro.Grupo);
                }
            }

            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var cliente = new Cliente
            {
                CodigoCliente = codigoCliente,
                Nome = nome,
                NomeFantasia = nomeFantasia,
                TipoDocumento = tipoDocumentoInferido.Value,
                Documento = documento,
                GrupoEmpresarialId = grupo?.Id,
                GrupoEmpresarialNome = grupo?.Nome,
                Email = email,
                Telefone = telefone,
                Ativo = true,
                CriadoPorUserId = entrada.CriadoPorUserId,
                CriadoEmUtc = nowUtc,
                AtualizadoEmUtc = nowUtc
            };

            try
            {
                var id = ExecutarComRetry(() => _clientes.Create(cliente));
                cliente.Id = id;
            }
            catch (CodigoClienteDuplicadoException)
            {
                return Falha(
                    "Já existe cliente ativo cadastrado com este código.",
                    ClienteCadastroErroCodigo.CodigoDuplicado,
                    ClienteCadastroCampoErro.CodigoCliente);
            }
            catch (DocumentoClienteDuplicadoException)
            {
                return Falha(
                    "Já existe cliente ativo cadastrado com este documento.",
                    ClienteCadastroErroCodigo.DocumentoDuplicado,
                    ClienteCadastroCampoErro.Documento);
            }

            ConcederPermissaoInicialAoCriador(cliente.Id, entrada.CriadoPorUserId);

            try
            {
                RegistrarAuditoriaCliente(entrada.CriadoPorUserId, "CLIENTE_CRIADO", cliente, "OK");
            }
            catch
            {
                // Falha de auditoria não pode invalidar o cadastro já persistido.
            }

            return new ResultadoCadastroCliente
            {
                Sucesso = true,
                Mensagem = "Cliente cadastrado com sucesso.",
                Cliente = cliente,
                CampoErro = ClienteCadastroCampoErro.Nenhum,
                Severidade = ClienteCadastroSeveridade.Info
            };
        }
        catch
        {
            return Falha(
                "Não foi possível concluir o cadastro agora. Tente novamente.",
                ClienteCadastroErroCodigo.FalhaTecnica,
                ClienteCadastroCampoErro.Nenhum,
                ClienteCadastroSeveridade.Error,
                GerarReferenciaErroCadastro());
        }
    }

    public ResultadoCadastroCliente Editar(ClienteEdicaoEntrada entrada)
    {
        if (entrada.AtualizadoPorUserId <= 0)
            return Falha("Usuário responsável inválido.");
        if (entrada.ClienteId <= 0)
            return Falha("Cliente inválido.");

        var cliente = ExecutarComRetry(() => _clientes.GetById(entrada.ClienteId));
        if (cliente is null || !cliente.Ativo)
            return Falha("Cliente não encontrado.");

        var codigoCliente = NormalizarCodigoCliente(entrada.CodigoCliente);
        var nome = (entrada.Nome ?? string.Empty).Trim();
        var nomeFantasia = NormalizarNomeFantasia(entrada.NomeFantasia);

        if (string.IsNullOrWhiteSpace(codigoCliente))
            return Falha("Informe o código do cliente.");
        if (codigoCliente.Length > ClienteInputLimits.MaxCodigoCliente)
            return Falha("Código do cliente excede o limite permitido.");
        if (!CodigoClienteEhValido(codigoCliente))
            return Falha("Código do cliente inválido. Use letras, números e os separadores -, _, ., /.");

        var outroClienteMesmoCodigo = ExecutarComRetry(() => _clientes.GetByCodigoCliente(codigoCliente));
        if (outroClienteMesmoCodigo is not null && outroClienteMesmoCodigo.Id != cliente.Id)
            return Falha("Já existe cliente ativo cadastrado com este código.");

        if (string.IsNullOrWhiteSpace(nome))
            return Falha("Informe o nome do cliente.");
        if (nome.Length > ClienteInputLimits.MaxNome)
            return Falha("Nome do cliente excede o limite permitido.");
        if (!string.IsNullOrWhiteSpace(nomeFantasia) && nomeFantasia.Length > ClienteInputLimits.MaxNomeFantasia)
            return Falha("Nome fantasia excede o limite permitido.");

        var email = NormalizarEmail(entrada.Email);
        if (!string.IsNullOrWhiteSpace(email))
        {
            if (email.Length > ClienteInputLimits.MaxEmail)
                return Falha("E-mail do cliente excede o limite permitido.");
            if (!EmailValidator.EhValido(email))
                return Falha("E-mail do cliente inválido.");
        }

        var telefone = NormalizarTelefone(entrada.Telefone);
        if (!string.IsNullOrWhiteSpace(telefone) && telefone.Length > ClienteInputLimits.MaxTelefone)
            return Falha("Telefone do cliente excede o limite permitido.");

        GrupoEmpresarial? grupo = null;
        if (entrada.GrupoEmpresarialId.HasValue)
        {
            grupo = ExecutarComRetry(() => _clientes.GetGrupoById(entrada.GrupoEmpresarialId.Value));
            if (grupo is null || !grupo.Ativo)
                return Falha("Grupo empresarial inválido.");
        }

        cliente.CodigoCliente = codigoCliente;
        cliente.Nome = nome;
        cliente.NomeFantasia = nomeFantasia;
        cliente.GrupoEmpresarialId = grupo?.Id;
        cliente.GrupoEmpresarialNome = grupo?.Nome;
        cliente.Email = email;
        cliente.Telefone = telefone;
        cliente.AtualizadoEmUtc = _timeProvider.GetUtcNow().UtcDateTime;

        ExecutarComRetry<int>(() =>
        {
            _clientes.Update(cliente);
            return 0;
        });

        RegistrarAuditoriaCliente(entrada.AtualizadoPorUserId, "CLIENTE_EDITADO", cliente, "OK");

        return new ResultadoCadastroCliente
        {
            Sucesso = true,
            Mensagem = "Cliente atualizado com sucesso.",
            Cliente = cliente
        };
    }

    public ResultadoCadastroGrupo CadastrarGrupo(GrupoEmpresarialCadastroEntrada entrada)
    {
        var nome = NormalizarNomeGrupo(entrada.Nome);
        var nomeNormalizado = NormalizarNomeGrupoChave(nome);

        if (entrada.CriadoPorUserId <= 0)
            return FalhaGrupo("Usuário responsável inválido.");
        if (string.IsNullOrWhiteSpace(nome))
            return FalhaGrupo("Informe o nome do grupo empresarial.");
        if (nome.Length > ClienteInputLimits.MaxNomeGrupo)
            return FalhaGrupo("Nome do grupo empresarial excede o limite permitido.");

        var existente = ExecutarComRetry(() => _clientes.GetGrupoByNomeNormalizado(nomeNormalizado));
        if (existente is not null)
        {
            return new ResultadoCadastroGrupo
            {
                Sucesso = true,
                Mensagem = "Grupo empresarial já cadastrado. Registro existente reutilizado.",
                Grupo = existente
            };
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var grupo = new GrupoEmpresarial
        {
            Nome = nome,
            NomeNormalizado = nomeNormalizado,
            Ativo = true,
            CriadoPorUserId = entrada.CriadoPorUserId,
            CriadoEmUtc = nowUtc,
            AtualizadoEmUtc = nowUtc
        };

        try
        {
            var id = ExecutarComRetry(() => _clientes.CreateGrupo(grupo));
            grupo.Id = id;
        }
        catch (Exception ex)
        {
            var grupoDuplicado = ExecutarComRetry(() => _clientes.GetGrupoByNomeNormalizado(nomeNormalizado));
            if (grupoDuplicado is not null)
            {
                return new ResultadoCadastroGrupo
                {
                    Sucesso = true,
                    Mensagem = "Grupo empresarial já cadastrado. Registro existente reutilizado.",
                    Grupo = grupoDuplicado
                };
            }

            System.Diagnostics.Debug.WriteLine(
                $"[ClienteService] Falha ao cadastrar grupo empresarial: {ex.GetType().Name} - {ex.Message}");
            return FalhaGrupo("Falha ao cadastrar grupo empresarial.");
        }

        RegistrarAuditoriaGrupo(entrada.CriadoPorUserId, "GRUPO_EMPRESARIAL_CRIADO", grupo, "OK");

        return new ResultadoCadastroGrupo
        {
            Sucesso = true,
            Mensagem = "Grupo empresarial cadastrado com sucesso.",
            Grupo = grupo
        };
    }

    public bool Inativar(int clienteId, int atualizadoPorUserId)
    {
        if (clienteId <= 0 || atualizadoPorUserId <= 0)
            return false;

        var cliente = ExecutarComRetry(() => _clientes.GetById(clienteId));
        if (cliente is null || !cliente.Ativo)
            return false;

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        ExecutarComRetry<int>(() =>
        {
            _clientes.Inativar(clienteId, atualizadoPorUserId, nowUtc);
            return 0;
        });

        cliente.Ativo = false;
        cliente.AtualizadoEmUtc = nowUtc;

        RegistrarAuditoriaCliente(atualizadoPorUserId, "CLIENTE_INATIVADO", cliente, "OK");

        return true;
    }

    public IReadOnlyList<Cliente> ListarTodos()
    {
        return ExecutarComRetry(() => _clientes.ListarTodos());
    }

    public IReadOnlyList<GrupoEmpresarial> ListarGrupos()
    {
        return ExecutarComRetry(() => _clientes.ListarGrupos());
    }

    public Cliente? ObterPorId(int id)
    {
        if (id <= 0)
            return null;

        return ExecutarComRetry(() => _clientes.GetById(id));
    }

    public PaginacaoResultado<Cliente> Buscar(string? termo, int pagina, int tamanhoPagina)
    {
        var filtro = ConsultaClientesFiltro.Criar(termo, pagina, tamanhoPagina);
        var totalItens = ExecutarComRetry(() => _clientes.Contar(filtro.Termo));
        var totalPaginas = totalItens <= 0 ? 1 : (int)Math.Ceiling(totalItens / (double)filtro.TamanhoPagina);
        var paginaAjustada = Math.Min(filtro.Pagina, totalPaginas);

        var itens = ExecutarComRetry(() => _clientes.Buscar(filtro.Termo, paginaAjustada, filtro.TamanhoPagina));
        return new PaginacaoResultado<Cliente>
        {
            Itens = itens,
            PaginaAtual = paginaAjustada,
            TamanhoPagina = filtro.TamanhoPagina,
            TotalItens = totalItens,
            Termo = filtro.Termo
        };
    }

    public IReadOnlyList<Cliente> ListarTodosPorUsuario(int solicitanteUserId)
    {
        if (solicitanteUserId <= 0)
            return [];

        var solicitante = ObterUsuarioAtivo(solicitanteUserId);
        if (solicitante is null)
            return DependenciasRbacDisponiveis ? [] : ListarTodos();

        if (solicitante.Role is UserRole.Admin or UserRole.Supremo || !DependenciasRbacDisponiveis)
            return ListarTodos();

        var todos = ListarTodos();
        return todos
            .Where(cliente => PodeAcessarCliente(solicitante, cliente))
            .ToList();
    }

    public Cliente? ObterPorIdPorUsuario(int solicitanteUserId, int clienteId)
    {
        if (solicitanteUserId <= 0 || clienteId <= 0)
            return null;

        var solicitante = ObterUsuarioAtivo(solicitanteUserId);
        if (solicitante is null)
            return null;

        var cliente = ObterPorId(clienteId);
        if (cliente is null)
            return null;

        if (solicitante.Role is UserRole.Admin or UserRole.Supremo || !DependenciasRbacDisponiveis)
            return cliente;

        return PodeAcessarCliente(solicitante, cliente) ? cliente : null;
    }

    public PaginacaoResultado<Cliente> BuscarPorUsuario(int solicitanteUserId, string? termo, int pagina, int tamanhoPagina)
    {
        var filtro = ConsultaClientesFiltro.Criar(termo, pagina, tamanhoPagina);
        var solicitante = ObterUsuarioAtivo(solicitanteUserId);
        if (solicitante is null)
        {
            return new PaginacaoResultado<Cliente>
            {
                Itens = [],
                PaginaAtual = filtro.Pagina,
                TamanhoPagina = filtro.TamanhoPagina,
                TotalItens = 0,
                Termo = filtro.Termo
            };
        }

        if (solicitante.Role is UserRole.Admin or UserRole.Supremo || !DependenciasRbacDisponiveis)
            return Buscar(filtro.Termo, filtro.Pagina, filtro.TamanhoPagina);

        var permitidos = ListarTodosPorUsuario(solicitanteUserId);
        var filtrados = FiltrarPorTermoComRelevancia(permitidos, filtro.Termo).ToList();

        var totalItens = filtrados.Count;
        var totalPaginas = totalItens <= 0 ? 1 : (int)Math.Ceiling(totalItens / (double)filtro.TamanhoPagina);
        var paginaAjustada = Math.Min(filtro.Pagina, totalPaginas);
        var skip = (paginaAjustada - 1) * filtro.TamanhoPagina;

        var itens = filtrados
            .Skip(skip)
            .Take(filtro.TamanhoPagina)
            .ToList();

        return new PaginacaoResultado<Cliente>
        {
            Itens = itens,
            PaginaAtual = paginaAjustada,
            TamanhoPagina = filtro.TamanhoPagina,
            TotalItens = totalItens,
            Termo = filtro.Termo
        };
    }

    private bool DependenciasRbacDisponiveis => _users is not null && _clientePermissoes is not null;

    private User? ObterUsuarioAtivo(int userId)
    {
        if (userId <= 0)
            return null;

        if (_users is null)
        {
            return new User
            {
                Id = userId,
                Status = UserStatus.Ativo,
                Role = UserRole.Admin
            };
        }

        var user = ExecutarComRetry(() => _users.GetById(userId));
        if (user is null || user.Status != UserStatus.Ativo)
            return null;

        return user;
    }

    private bool PodeAcessarCliente(User solicitante, Cliente cliente)
    {
        if (solicitante.Role is UserRole.Admin or UserRole.Supremo)
            return true;

        if (cliente.CriadoPorUserId > 0 && cliente.CriadoPorUserId == solicitante.Id)
            return true;

        if (_clientePermissoes is null)
            return false;

        return ExecutarComRetry(() => _clientePermissoes.Obter(cliente.Id, solicitante.Id)) is not null;
    }

    private void ConcederPermissaoInicialAoCriador(int clienteId, int criadoPorUserId)
    {
        if (clienteId <= 0 || criadoPorUserId <= 0 || _clientePermissoes is null)
            return;

        try
        {
            ExecutarComRetry<int>(() =>
            {
                _clientePermissoes.DefinirPermissoes(
                    clienteId,
                    criadoPorUserId,
                    [
                        new PermissaoClienteEntrada
                        {
                            UserId = criadoPorUserId,
                            PodeEditar = true
                        }
                    ]);
                return 0;
            });
        }
        catch
        {
            // Falha de permissão não pode invalidar cadastro já persistido.
        }
    }

    private static IEnumerable<Cliente> FiltrarPorTermoComRelevancia(IReadOnlyList<Cliente> clientes, string? termo)
    {
        if (string.IsNullOrWhiteSpace(termo))
        {
            return clientes
                .OrderBy(c => c.Nome, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Id);
        }

        var termoNormalizado = termo.Trim();
        var termoUpper = termoNormalizado.ToUpperInvariant();
        var termoDocumento = DocumentoClienteValidator.ExtrairSomenteDigitos(termoNormalizado);
        if (termoDocumento.Length != 11 && termoDocumento.Length != 14)
            termoDocumento = string.Empty;

        return clientes
            .Select(cliente => new
            {
                Cliente = cliente,
                Relevancia = CalcularRelevancia(cliente, termoNormalizado, termoUpper, termoDocumento)
            })
            .Where(item => item.Relevancia < int.MaxValue)
            .OrderBy(item => item.Relevancia)
            .ThenBy(item => item.Cliente.Nome, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Cliente.Id)
            .Select(item => item.Cliente);
    }

    private static int CalcularRelevancia(Cliente cliente, string termo, string termoUpper, string termoDocumentoExato)
    {
        var codigo = cliente.CodigoCliente ?? string.Empty;
        var nome = cliente.Nome ?? string.Empty;
        var fantasia = cliente.NomeFantasia ?? string.Empty;
        var grupo = cliente.GrupoEmpresarialNome ?? string.Empty;
        var documento = cliente.Documento ?? string.Empty;

        if (string.Equals(codigo, termoUpper, StringComparison.OrdinalIgnoreCase)) return 0;
        if (!string.IsNullOrWhiteSpace(termoDocumentoExato) && string.Equals(documento, termoDocumentoExato, StringComparison.Ordinal)) return 1;
        if (string.Equals(nome, termo, StringComparison.OrdinalIgnoreCase)) return 2;
        if (string.Equals(fantasia, termo, StringComparison.OrdinalIgnoreCase)) return 3;
        if (string.Equals(grupo, termo, StringComparison.OrdinalIgnoreCase)) return 4;

        if (codigo.StartsWith(termoUpper, StringComparison.OrdinalIgnoreCase)) return 5;
        if (nome.StartsWith(termo, StringComparison.OrdinalIgnoreCase)) return 6;
        if (fantasia.StartsWith(termo, StringComparison.OrdinalIgnoreCase)) return 7;
        if (grupo.StartsWith(termo, StringComparison.OrdinalIgnoreCase)) return 8;

        if (codigo.Contains(termoUpper, StringComparison.OrdinalIgnoreCase)) return 20;
        if (nome.Contains(termo, StringComparison.OrdinalIgnoreCase)) return 21;
        if (fantasia.Contains(termo, StringComparison.OrdinalIgnoreCase)) return 22;
        if (grupo.Contains(termo, StringComparison.OrdinalIgnoreCase)) return 23;

        return int.MaxValue;
    }

    private void RegistrarAuditoriaCliente(int userId, string acao, Cliente? cliente, string resultado)
    {
        if (_audit is null)
            return;

        var detalhes = new
        {
            acao,
            clienteId = cliente?.Id ?? 0,
            codigoCliente = cliente?.CodigoCliente,
            tipoDocumento = cliente?.TipoDocumento.ToString(),
            documentoHashPrefix = ObterDocumentoHashPrefix(cliente?.Documento),
            resultado,
            timestamp = _timeProvider.GetUtcNow().UtcDateTime.ToString("o")
        };

        RegistrarAuditoria(userId, acao, resultado, detalhes);
    }

    private void RegistrarAuditoriaGrupo(int userId, string acao, GrupoEmpresarial? grupo, string resultado)
    {
        if (_audit is null)
            return;

        var detalhes = new
        {
            acao,
            grupoId = grupo?.Id ?? 0,
            nomeGrupo = grupo?.Nome,
            resultado,
            timestamp = _timeProvider.GetUtcNow().UtcDateTime.ToString("o")
        };

        RegistrarAuditoria(userId, acao, resultado, detalhes);
    }

    private void RegistrarAuditoria(int userId, string acao, string resultado, object detalhes)
    {
        if (_audit is null)
            return;

        _audit.Registrar(new AuditLogEntry
        {
            UserId = userId,
            Acao = acao,
            Resultado = resultado,
            Detalhes = JsonSerializer.Serialize(detalhes),
            Maquina = _maquina,
            VersaoApp = _versaoApp
        }, _usarHashChain);
    }

    private string? ObterDocumentoHashPrefix(string? documento)
    {
        var digits = DocumentoClienteValidator.ExtrairSomenteDigitos(documento);
        if (string.IsNullOrWhiteSpace(digits))
            return null;

        var hash = _dataProtector?.ComputeDocumentoHash(digits);
        if (string.IsNullOrWhiteSpace(hash))
        {
            var fallback = SHA256.HashData(Encoding.UTF8.GetBytes(digits));
            hash = Convert.ToBase64String(fallback);
        }

        return hash.Length <= 12 ? hash : hash[..12];
    }

    private static ResultadoCadastroCliente Falha(string mensagem)
    {
        return Falha(mensagem, ClienteCadastroErroCodigo.ValidacaoNegocio, ClienteCadastroCampoErro.Nenhum);
    }

    private static ResultadoCadastroCliente Falha(
        string mensagem,
        ClienteCadastroErroCodigo codigoErro,
        ClienteCadastroCampoErro campoErro,
        ClienteCadastroSeveridade severidade = ClienteCadastroSeveridade.Error,
        string? referenciaErro = null)
    {
        return new ResultadoCadastroCliente
        {
            Sucesso = false,
            Mensagem = mensagem,
            CodigoErro = codigoErro,
            CampoErro = campoErro,
            Severidade = severidade,
            ReferenciaErro = referenciaErro
        };
    }

    private static ResultadoCadastroGrupo FalhaGrupo(string mensagem)
    {
        return new ResultadoCadastroGrupo
        {
            Sucesso = false,
            Mensagem = mensagem
        };
    }

    private static string? NormalizarEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;

        return email.Trim().ToLowerInvariant();
    }

    private static string NormalizarCodigoCliente(string? codigoCliente)
    {
        if (string.IsNullOrWhiteSpace(codigoCliente))
            return string.Empty;

        return codigoCliente.Trim().ToUpperInvariant();
    }

    private static bool CodigoClienteEhValido(string codigoCliente)
    {
        if (string.IsNullOrWhiteSpace(codigoCliente))
            return false;

        return CodigoClienteRegex.IsMatch(codigoCliente);
    }

    private static string? NormalizarNomeFantasia(string? nomeFantasia)
    {
        if (string.IsNullOrWhiteSpace(nomeFantasia))
            return null;

        return nomeFantasia.Trim();
    }

    private static string NormalizarNomeGrupo(string? nomeGrupo)
    {
        if (string.IsNullOrWhiteSpace(nomeGrupo))
            return string.Empty;

        return nomeGrupo.Trim();
    }

    private static string NormalizarNomeGrupoChave(string? nomeGrupo)
    {
        var nome = NormalizarNomeGrupo(nomeGrupo);
        if (string.IsNullOrWhiteSpace(nome))
            return string.Empty;

        return string.Join(' ', nome
            .Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToUpperInvariant();
    }

    private static string? NormalizarTelefone(string? telefone)
    {
        if (string.IsNullOrWhiteSpace(telefone))
            return null;

        return telefone.Trim();
    }

    private static string GerarReferenciaErroCadastro()
    {
        var sufixo = Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
        return $"CLI-CAD-{sufixo}";
    }

    private T ExecutarComRetry<T>(Func<T> operacao)
    {
        Exception? ultimaFalha = null;
        const int maxTentativas = 3;
        for (var tentativa = 1; tentativa <= maxTentativas; tentativa++)
        {
            try
            {
                return operacao();
            }
            catch (DocumentoClienteDuplicadoException)
            {
                throw;
            }
            catch (CodigoClienteDuplicadoException)
            {
                throw;
            }
            catch (Exception ex) when (tentativa < maxTentativas && EhFalhaTransitoria(ex))
            {
                ultimaFalha = ex;
                Thread.Sleep(tentativa == 1 ? 120 : 280);
            }
        }

        throw ultimaFalha ?? new InvalidOperationException("Falha transitória não recuperada.");
    }

    private static bool EhFalhaTransitoria(Exception ex)
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
               mensagem.Contains("busy", StringComparison.OrdinalIgnoreCase) ||
               mensagem.Contains("deadlock", StringComparison.OrdinalIgnoreCase);
    }
}
