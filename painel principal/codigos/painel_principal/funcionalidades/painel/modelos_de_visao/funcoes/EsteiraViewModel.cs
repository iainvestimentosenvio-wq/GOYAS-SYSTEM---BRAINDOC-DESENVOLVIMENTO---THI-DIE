using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Protons.UI.Painel.ViewModels;

public sealed partial class EsteiraViewModel : ObservableObject
{
    [ObservableProperty] private int _id;
    [ObservableProperty] private string _nome = string.Empty;
    [ObservableProperty] private string _nomeSalvo = string.Empty;
    [ObservableProperty] private string _nomeEmEdicao = string.Empty;
    [ObservableProperty] private bool _nomePersonalizadoSalvo;
    [ObservableProperty] private int _ordem;
    [ObservableProperty] private double _nivelZoom = 0.5;
    [ObservableProperty] private DateTime _centroTemporal = DateTime.UtcNow;

    public ObservableCollection<TarefaReguaItem> Tarefas { get; } = new();
}
