using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Protons.Core.Login.Models;
using Protons.UI.Login.Models;

namespace Protons.UI.Painel.ViewModels;

public sealed partial class PainelViewModel
{
    [RelayCommand(CanExecute = nameof(PodeAbrirNotificacoes))]
    private void AbrirNotificacoes()
    {
        RegistrarInteracaoPainel();
        if (!TemNotificacoes)
            return;

        if (PendenciaAtual is null && Pendentes.Count > 0)
            PendenciaAtual = Pendentes[0];

        if (OrfaoAtual is null && OrfaosAdminExcluido.Count > 0)
            OrfaoAtual = OrfaosAdminExcluido[0];

        NotificacoesAbertas = true;
    }

    [RelayCommand]
    private void FecharNotificacoes()
    {
        RegistrarInteracaoPainel();
        NotificacoesAbertas = false;
    }

    [RelayCommand(CanExecute = nameof(PodeDecidirPendencia))]
    private async Task LiberarAcesso()
    {
        RegistrarInteracaoPainel();
        var atual = PendenciaAtual;
        if (atual is null)
        {
            return;
        }

        IsBusy = true;
        var corr = GerarCorrelationId();
        var medicao = IniciarMetricaPainel("notificacao_liberar", corr);
        try
        {
            RegistrarEventoPainel("notificacao_liberar_inicio", $"corr={corr} pendente_id={atual.Id}");
            await Task.Run(() => _authService.AprovarUsuario(atual.Id, _userId, "Aprovado pelo administrador"));
            Pendentes.Remove(atual);
            SelecionarProximaPendencia();
            MensagemPendencias = "Solicitação aprovada com sucesso.";
            RegistrarEventoPainel("notificacao_liberar_sucesso", $"corr={corr} pendente_id={atual.Id}");
            RegistrarMetricaPainel("notificacao_liberar_duracao_ms", medicao.ElapsedMs(), "ms", $"corr={corr} resultado=sucesso");
        }
        catch (Exception ex)
        {
            MensagemPendencias = "Falha ao aprovar solicitação. Tente novamente.";
            RegistrarErroPainel("notificacao_liberar_falha", ex, $"corr={corr} pendente_id={atual.Id}");
            RegistrarMetricaPainel("notificacao_liberar_duracao_ms", medicao.ElapsedMs(), "ms", $"corr={corr} resultado=falha");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(PodeDecidirPendencia))]
    private async Task NaoConheco()
    {
        RegistrarInteracaoPainel();
        var atual = PendenciaAtual;
        if (atual is null)
        {
            return;
        }

        IsBusy = true;
        var corr = GerarCorrelationId();
        var medicao = IniciarMetricaPainel("notificacao_rejeitar", corr);
        try
        {
            RegistrarEventoPainel("notificacao_rejeitar_inicio", $"corr={corr} pendente_id={atual.Id}");
            await Task.Run(() => _authService.RejeitarUsuario(atual.Id, _userId, "Rejeitado pelo administrador"));
            Pendentes.Remove(atual);
            SelecionarProximaPendencia();
            MensagemPendencias = "Solicitação rejeitada.";
            RegistrarEventoPainel("notificacao_rejeitar_sucesso", $"corr={corr} pendente_id={atual.Id}");
            RegistrarMetricaPainel("notificacao_rejeitar_duracao_ms", medicao.ElapsedMs(), "ms", $"corr={corr} resultado=sucesso");
        }
        catch (Exception ex)
        {
            MensagemPendencias = "Falha ao rejeitar solicitação. Tente novamente.";
            RegistrarErroPainel("notificacao_rejeitar_falha", ex, $"corr={corr} pendente_id={atual.Id}");
            RegistrarMetricaPainel("notificacao_rejeitar_duracao_ms", medicao.ElapsedMs(), "ms", $"corr={corr} resultado=falha");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(PodeDecidirPendencia))]
    private async Task LiberarComoAdministrador()
    {
        RegistrarInteracaoPainel();
        var atual = PendenciaAtual;
        if (atual is null)
        {
            return;
        }

        IsBusy = true;
        var corr = GerarCorrelationId();
        var medicao = IniciarMetricaPainel("notificacao_promover_admin", corr);
        try
        {
            RegistrarEventoPainel("notificacao_promover_admin_inicio", $"corr={corr} pendente_id={atual.Id}");
            await Task.Run(() => _authService.PromoverUsuarioAdmin(atual.Id, _userId, "Promovido no fluxo de aprovação"));
            Pendentes.Remove(atual);
            SelecionarProximaPendencia();
            MensagemPendencias = "Solicitação aprovada como administrador.";
            RegistrarEventoPainel("notificacao_promover_admin_sucesso", $"corr={corr} pendente_id={atual.Id}");
            RegistrarMetricaPainel("notificacao_promover_admin_duracao_ms", medicao.ElapsedMs(), "ms", $"corr={corr} resultado=sucesso");
        }
        catch (Exception ex)
        {
            MensagemPendencias = "Falha ao promover usuário para administrador. Tente novamente.";
            RegistrarErroPainel("notificacao_promover_admin_falha", ex, $"corr={corr} pendente_id={atual.Id}");
            RegistrarMetricaPainel("notificacao_promover_admin_duracao_ms", medicao.ElapsedMs(), "ms", $"corr={corr} resultado=falha");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    ///   Exclui suavemente um usuário (Admin ou Usuário comum).
    ///   Se o excluído era Admin com subordinados, eles são transferidos para o Supremo
    ///   e aparecem na seção "Sem Admin" do painel de notificações.
    /// </summary>
    [RelayCommand]
    private async Task ExcluirUsuario(UserSummary usuario)
    {
        RegistrarInteracaoPainel();
        if (usuario is null)
            return;

        IsBusy = true;
        var corr = GerarCorrelationId();
        var medicao = IniciarMetricaPainel("controle_acesso_excluir", corr);
        try
        {
            RegistrarEventoPainel("controle_acesso_excluir_inicio", $"corr={corr} usuario_id={usuario.Id}");

            var resultado = await Task.Run(() => _authService.ExcluirUsuario(
                new ExclusaoUsuarioEntrada(
                    UsuarioId: usuario.Id,
                    ExecutorId: _userId,
                    Motivo: "Excluído pelo administrador")));

            if (!resultado.Sucesso)
            {
                MensagemPendencias = resultado.Mensagem ?? "Não foi possível excluir o usuário.";
                return;
            }

            // Se o excluído era Admin com subordinados, carregar os órfãos no painel.
            if (resultado.UsuariosOrfaosTransferidos > 0)
            {
                await CarregarOrfaosDoAdminExcluidoAsync(usuario);
            }

            RegistrarEventoPainel("controle_acesso_excluir_sucesso",
                $"corr={corr} usuario_id={usuario.Id} orfaos={resultado.UsuariosOrfaosTransferidos}");
            RegistrarMetricaPainel("controle_acesso_excluir_duracao_ms", medicao.ElapsedMs(), "ms",
                $"corr={corr} resultado=sucesso");
        }
        catch (Exception ex)
        {
            MensagemPendencias = "Falha ao excluir usuário. Tente novamente.";
            RegistrarErroPainel("controle_acesso_excluir_falha", ex, $"corr={corr} usuario_id={usuario.Id}");
            RegistrarMetricaPainel("controle_acesso_excluir_duracao_ms", medicao.ElapsedMs(), "ms",
                $"corr={corr} resultado=falha");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    ///   Promove o usuário órfão atual a Administrador.
    ///   Ele permanece sob gestão do Supremo (como qualquer admin) e pode receber outros usuários.
    /// </summary>
    [RelayCommand(CanExecute = nameof(PodeDecidirOrfao))]
    private async Task PromoverOrfaoAAdmin()
    {
        RegistrarInteracaoPainel();
        var atual = OrfaoAtual;
        if (atual is null)
            return;

        IsBusy = true;
        var corr = GerarCorrelationId();
        var medicao = IniciarMetricaPainel("controle_acesso_promover_orfao", corr);
        try
        {
            RegistrarEventoPainel("controle_acesso_promover_orfao_inicio", $"corr={corr} orfao_id={atual.Id}");
            await Task.Run(() => _authService.PromoverUsuarioAdmin(
                atual.Id, _userId, "Promovido para assumir administração após exclusão de admin anterior"));

            OrfaosAdminExcluido.Remove(atual);
            SelecionarProximoOrfao();
            MensagemOrfaos = OrfaosAdminExcluido.Count > 0
                ? $"{atual.Nome} promovido a Administrador. Restam {OrfaosAdminExcluido.Count} usuário(s) sem admin."
                : $"{atual.Nome} promovido a Administrador. Todos os usuários possuem admin responsável.";

            RegistrarEventoPainel("controle_acesso_promover_orfao_sucesso", $"corr={corr} orfao_id={atual.Id}");
            RegistrarMetricaPainel("controle_acesso_promover_orfao_duracao_ms", medicao.ElapsedMs(), "ms",
                $"corr={corr} resultado=sucesso");
        }
        catch (Exception ex)
        {
            MensagemOrfaos = "Falha ao promover usuário. Tente novamente.";
            RegistrarErroPainel("controle_acesso_promover_orfao_falha", ex, $"corr={corr} orfao_id={atual.Id}");
            RegistrarMetricaPainel("controle_acesso_promover_orfao_duracao_ms", medicao.ElapsedMs(), "ms",
                $"corr={corr} resultado=falha");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    ///   Descarta o usuário órfão atual da lista de notificações.
    ///   O usuário continua ativo sob gestão direta do Supremo — nenhuma ação adicional é necessária.
    /// </summary>
    [RelayCommand(CanExecute = nameof(PodeDecidirOrfao))]
    private void ManterOrfaoComSupremo()
    {
        RegistrarInteracaoPainel();
        var atual = OrfaoAtual;
        if (atual is null)
            return;

        OrfaosAdminExcluido.Remove(atual);
        SelecionarProximoOrfao();
        MensagemOrfaos = OrfaosAdminExcluido.Count > 0
            ? $"{atual.Nome} ficará sob sua gestão. Restam {OrfaosAdminExcluido.Count} usuário(s) para revisar."
            : "Todos os usuários órfãos foram revisados.";

        RegistrarEventoPainel("controle_acesso_manter_orfao", $"orfao_id={atual.Id}");
    }

    /// <summary>
    ///   Carrega na coleção OrfaosAdminExcluido os subordinados do admin recém-excluído
    ///   que foram transferidos para o Supremo. Reabre o painel de notificações com mensagem explicativa.
    /// </summary>
    private async Task CarregarOrfaosDoAdminExcluidoAsync(UserSummary adminExcluido)
    {
        var corr = GerarCorrelationId();
        try
        {
            var subordinados = await Task.Run(() =>
                _authService.ListarSubordinados(_userId, UserRole.Supremo));

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // Filtra apenas os que eram subordinados do admin excluído
                // (agora têm ResponsavelAdminId = _userId, i.e., o Supremo).
                // Adiciona apenas os que ainda não estão na lista.
                var idsJaNaLista = new HashSet<int>();
                foreach (var o in OrfaosAdminExcluido) idsJaNaLista.Add(o.Id);

                int novosOrfaos = 0;
                foreach (var user in subordinados)
                {
                    if (!idsJaNaLista.Contains(user.Id))
                    {
                        OrfaosAdminExcluido.Add(new UserSummary
                        {
                            Id = user.Id,
                            Empresa = user.Empresa,
                            Nome = user.Nome,
                            Email = user.Email,
                            Cargo = user.Cargo
                        });
                        novosOrfaos++;
                    }
                }

                if (novosOrfaos > 0)
                {
                    SelecionarProximoOrfao();
                    MensagemOrfaos = novosOrfaos == 1
                        ? $"O admin {adminExcluido.Nome} foi excluído. 1 usuário ficou sem admin responsável e foi transferido para você. Deseja promovê-lo a Administrador?"
                        : $"O admin {adminExcluido.Nome} foi excluído. {novosOrfaos} usuários ficaram sem admin responsável e foram transferidos para você. Deseja promover um deles a Administrador?";

                    NotificacoesAbertas = true;
                }

                RegistrarEventoPainel("controle_acesso_orfaos_carregados",
                    $"corr={corr} admin_excluido={adminExcluido.Id} novos_orfaos={novosOrfaos}");
            });
        }
        catch (Exception ex)
        {
            RegistrarErroPainel("controle_acesso_orfaos_falha", ex, $"corr={corr}");
        }
    }

    private async Task CarregarPendenciasAsync()
    {
        var corr = GerarCorrelationId();
        var medicao = IniciarMetricaPainel("notificacao_carregar", corr);
        try
        {
            RegistrarEventoPainel("notificacao_carregar_inicio", $"corr={corr}");
            var pendentes = await Task.Run(() => _authService.ListarPendentes());

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Pendentes.Clear();
                foreach (var user in pendentes)
                {
                    Pendentes.Add(new UserSummary
                    {
                        Id = user.Id,
                        Empresa = user.Empresa,
                        Nome = user.Nome,
                        Email = user.Email,
                        Cargo = user.Cargo
                    });
                }

                MensagemPendencias = Pendentes.Count == 0 ? "Nenhuma pendência no momento." : null;
                SelecionarProximaPendencia();
                RegistrarEventoPainel("notificacao_carregar_sucesso", $"corr={corr} total={Pendentes.Count}");
                RegistrarMetricaPainel("notificacao_carregar_duracao_ms", medicao.ElapsedMs(), "ms", $"corr={corr} total={Pendentes.Count}");
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                MensagemPendencias = "Erro ao carregar pendências.";
            });
            RegistrarErroPainel("notificacao_carregar_falha", ex, $"corr={corr}");
            RegistrarMetricaPainel("notificacao_carregar_duracao_ms", medicao.ElapsedMs(), "ms", $"corr={corr} resultado=falha");
        }
    }
}
