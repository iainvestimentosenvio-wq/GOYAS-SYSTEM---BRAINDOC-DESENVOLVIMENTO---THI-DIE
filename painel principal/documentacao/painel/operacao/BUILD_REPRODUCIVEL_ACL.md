# Build Reproduzivel com ACL (Linux)

## Problema observado
Em ambientes compartilhados, arquivos em `obj/` podem ficar com owner diferente do usuario atual.
Nesses casos, o MSBuild/Avalonia falha ao atualizar timestamp com erros como:
- `MSB3374` em `*.csproj.CopyComplete`
- `UnauthorizedAccessException` em `Protons.UI/obj/.../Protons.UI.dll`

## Procedimento rapido (sem alterar codigo)
1. Remover somente artefatos de build que exigem update de timestamp:
```bash
find "/srv/DocumentosCompartilhados/PROJETO PROTONS/Login" -path "*/obj/*" -name "*.CopyComplete" -delete
```
2. Se persistir erro no `Protons.UI/obj/Debug/net8.0`, limpar apenas arquivos desse diretório:
```bash
find "/srv/DocumentosCompartilhados/PROJETO PROTONS/Login/Protons.UI/obj/Debug/net8.0" -type f -delete
find "/srv/DocumentosCompartilhados/PROJETO PROTONS/Login/Protons.UI/obj/Debug/net8.0" -depth -type d -empty -delete
```
3. Reexecutar build/test:
```bash
dotnet test "/srv/DocumentosCompartilhados/PROJETO PROTONS/Login/testes/Protons.Infrastructure.Tests/Protons.Infrastructure.Tests.csproj"
```

## Padrao operacional recomendado
- Limpar apenas `obj/`/`bin/` (nunca codigo-fonte).
- Evitar alternar usuarios diferentes no mesmo workspace de build.
- Em pipeline CI, sempre usar workspace limpo por execucao.
