using System.Runtime.CompilerServices;

// Permite que o projeto de testes de infra acesse membros internos (AncorarPdfRegexCatalog,
// helpers internos de AncorarPdfSmartDetector, etc.) sem precisar torná-los públicos.
[assembly: InternalsVisibleTo("Protons.Infrastructure.Tests")]
