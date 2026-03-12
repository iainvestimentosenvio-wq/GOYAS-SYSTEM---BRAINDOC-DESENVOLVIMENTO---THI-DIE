namespace Protons.Core.Tarefas.Models;

// Tipos de variável detectados automaticamente por smart click.
public enum SmartTipoVariavel
{
    Cnpj,
    Cpf,
    Email,
    Cep,
    Telefone,
    DataBr,
    DataExtenso,
    Uf,
    MoedaBrl,
    Inteiro,
    Texto
}

// Entrada para detecção: coordenadas relativas do clique no PDF renderizado.
// XRel e YRel estão no espaço normalizado [0,1] top-left, mesma origem que BboxRelativo.
public sealed record SmartDeteccaoEntrada(double XRel, double YRel, int Pagina = 1);

// Resultado da detecção automática de variável.
// Quando Encontrado=false, apenas MotivoFalha é relevante; os demais campos têm valores padrão.
public sealed record SmartDeteccaoResultado(
    bool Encontrado,
    string TextoBruto,
    SmartTipoVariavel Tipo,
    string? RegraNormalizacao,
    string TextoNormalizado,
    string? LabelInferido,
    string NomeSugerido,
    string ChaveTecnicaSugerida,
    int Pagina,
    BboxRelativo Bbox,
    double Confianca,
    string? MotivoFalha = null)
{
    // Cria resultado de falha (Encontrado=false) com bbox vazia.
    public static SmartDeteccaoResultado NaoEncontrado(string motivo) =>
        new(
            Encontrado: false,
            TextoBruto: string.Empty,
            Tipo: SmartTipoVariavel.Texto,
            RegraNormalizacao: null,
            TextoNormalizado: string.Empty,
            LabelInferido: null,
            NomeSugerido: string.Empty,
            ChaveTecnicaSugerida: string.Empty,
            Pagina: 1,
            Bbox: new BboxRelativo(0, 0, 0, 0),
            Confianca: 0,
            MotivoFalha: motivo);
}
