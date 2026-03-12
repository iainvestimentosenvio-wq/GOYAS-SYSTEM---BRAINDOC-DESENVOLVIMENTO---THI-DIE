# Guia de Desenvolvimento Multiplataforma (Linux ↔ Windows)

## Objetivo
Permitir **desenvolvimento e testes no Linux**, garantindo **execução no Windows**, usando **Avalonia UI + .NET 8**.

## Stack oficial
- **C# + .NET 8**
- **Avalonia UI** (desktop cross‑platform)
- **SQLite** (local)

## Fluxo profissional recomendado
1) **Desenvolver no Linux** (ambiente principal)
2) **Testar no Linux** diariamente
3) **Validar no Windows** antes de demos/entregas

## Setup mínimo no Linux
- [ ] Instalar **.NET 8 SDK**
- [ ] Instalar **Avalonia templates** (para criar projeto)
- [ ] Editor: Cursor/VS Code

## Comandos recomendados (Linux)
> Executar somente quando o `dotnet` estiver instalado.

**Verificar SDK**:
```bash
dotnet --version
```

**Instalar templates do Avalonia**:
```bash
dotnet new install Avalonia.Templates
```

**Criar solução e projetos**:
```bash
dotnet new sln -n Protons
mkdir -p Login
dotnet new avalonia.app -n Protons.UI -o Protons.UI
dotnet new classlib -n Protons.Core -o Protons.Core
dotnet new classlib -n Protons.Infrastructure -o Protons.Infrastructure

dotnet sln Protons.sln add Protons.UI/Protons.UI.csproj
dotnet sln Protons.sln add Protons.Core/Protons.Core.csproj
dotnet sln Protons.sln add Protons.Infrastructure/Protons.Infrastructure.csproj

dotnet add Protons.UI/Protons.UI.csproj reference Protons.Core/Protons.Core.csproj
dotnet add Protons.Infrastructure/Protons.Infrastructure.csproj reference Protons.Core/Protons.Core.csproj
```

## Build e testes no Linux
- [ ] Rodar o app localmente (Avalonia)
- [ ] Executar testes unitários (quando existirem)
- [ ] Verificar logs e auditoria local

**Executar localmente**:
```bash
dotnet run --project Protons.UI/Protons.UI.csproj
```

## Publicação para Windows
- [ ] Gerar build para Windows (publicação .NET)
- [ ] Testar o executável no Windows
- [ ] Validar compatibilidade visual e performance

**Publicar para Windows (x64)**:
```bash
dotnet publish Protons.UI/Protons.UI.csproj -c Release -r win-x64 --self-contained false
```

## Estratégia de validação
- **Diária**: testes no Linux
- **Semanal**: validação no Windows (VM ou máquina real)
- **Antes de demo**: checklist completo + smoke test

## Checklist rápido (DoD multiplataforma)
- [ ] App roda no Linux sem erros
- [ ] Build para Windows gerado com sucesso
- [ ] App abre no Windows e navega até o login
- [ ] Performance aceitável em máquina padrão
- [ ] Logs de auditoria gerados corretamente

## Observações
- WPF e .NET Framework **não** funcionam no Linux.
- Avalonia + .NET 8 é a escolha correta para manter compatibilidade.
