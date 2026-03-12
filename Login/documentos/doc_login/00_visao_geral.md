# 00 - Visão Geral

## Objetivo do módulo
Entregar um **MVP local** de **Login + Auditoria** para garantir rastreabilidade de ações e reduzir o risco de sabotagem interna. O login do aplicativo é independente do login Windows (RDP/servidor), servindo **exclusivamente** para auditoria do sistema.

## Contexto do projeto
- Projeto: Automação desktop para escritório de contabilidade (dados sensíveis).
- Objetivo futuro do sistema: automatizar entrada de NFS-e (prestados e tomados), organizar arquivos em pasta compartilhada, operar o Domínio (no servidor/acesso remoto), baixar dados via navegador e comparar relatórios (Prefeitura vs Domínio).
- Restrição atual: desenvolvimento e testes **somente em máquinas locais**; **nenhuma instalação no servidor** da empresa no MVP.

## Escopo do MVP (Login + Auditoria)
Inclui:
- Tela de login com “lembrar e-mail”.
- Cadastro de usuário com status **PENDENTE**.
- Aprovação/rejeição local pelo administrador.
- Bloqueio temporário por tentativas inválidas (lockout).
- Auditoria detalhada de eventos críticos.

Fora do escopo do MVP:
- Integração com servidor, rede ou internet.
- Integrações com Domínio, prefeituras, pastas compartilhadas.
- Painel de tarefas, fila de execução e módulos de automação.

## Premissas e restrições
- Plataforma: **Windows**.
- UI: **Avalonia UI** (compatível com Windows e Linux).
- Runtime: **.NET 8**.
- Persistência: **SQLite local** (`.db`).
- Segurança: **PBKDF2 + salt**, sem armazenamento de senha em texto.

## Objetivos de qualidade
- **Segurança**: proteção de credenciais, bloqueio (lockout), logs consistentes.
- **Rastreabilidade**: trilha de auditoria confiável e legível.
- **Performance**: funcional em PCs com i5 antigo + HDD.
- **Usabilidade**: telas claras e fluxo direto.

## Público-alvo da documentação
- Dono/gestão: entender riscos, rastreabilidade e fluxo de aprovação.
- TI/segurança: validar práticas de segurança e armazenamento local.
- Desenvolvimento: guiar implementação do MVP.
