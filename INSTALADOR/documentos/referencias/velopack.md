# Análise Interna — Velopack (referência)

**Objetivo**
Documentar de forma objetiva o que o Velopack oferece, quais qualidades são úteis para o nosso instalador, e que evidências públicas existem sobre desempenho/robustez. Este documento **não** importa código do Velopack; serve apenas como base de decisão e inspiração de boas práticas.

## 1) O que é o Velopack (visão geral)
Velopack é um kit de empacotamento e atualização para apps desktop, com foco em facilitar distribuição e **auto‑update**. Ele oferece ferramentas de linha de comando e SDKs para integrar atualização automática no app.

## 2) Qualidades que podem inspirar nosso instalador
**Atualização incremental (delta updates)**
- Produz pacotes “delta” para reduzir tamanho de download e acelerar atualização.

**Fallback seguro**
- Quando um delta falha, o fluxo pode cair para um pacote completo.

**Canais e releases padronizados**
- Mantém releases organizados e com metadados, o que facilita auditoria e rollback.

**Assinatura de artefatos**
- Suporta assinatura (Windows) e notarização (macOS) como parte do processo.

## 3) Evidências públicas de performance/testes
**O que existe publicamente (qualitativo)**
- Documentação menciona **“speedy updates”** (atualizações rápidas) associadas a delta packages.
- O modo **BestSize** é mencionado como **mais lento**, porém comparável ao bsdiff, indicando trade‑off entre tamanho do delta e tempo de geração.
- A estratégia de delta escolhe o “melhor candidato” e mantém **um delta por release** (quando o full package também existe), reduzindo o trabalho de manutenção.

**O que NÃO foi encontrado**
- **Não há benchmarks numéricos oficiais** (ex.: % de economia média, tempo médio de update) publicados nos docs/repo.

## 4) Como replicar qualidades sem importar código
**Delta updates (conceito)**
- Estruturar releases com full + delta e manter manifestos de versão.
- Se o delta falhar, cair para full automaticamente.

**Canalização e metadados**
- Versionar artefatos e publicar manifestos simples (JSON), com hash e URL.
- Manter histórico de versões para rollback.

**Assinatura e integridade**
- Assinar MSI/EXE e registrar hashes SHA256 em cada release.

## 5) Recomendação prática
- **Não importar o Velopack no repo** (evita risco legal/manutenção).
- Criar pipeline de releases inspirado nas práticas acima.
- Medir nossa própria performance com testes internos (tamanho do pacote, tempo de instalação, tempo de update), já que não existem números oficiais públicos.

## 6) Referências (fontes oficiais)
- https://velopack.io/
- https://docs.velopack.io/
- https://docs.velopack.io/packaging/operating-systems/linux
- https://docs.velopack.io/packaging/channels
- https://docs.velopack.io/packaging/signing
- https://github.com/velopack/velopack
