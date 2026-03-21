# ADR-002 — Preview de PDF: Docnet (PDFium) vs Ghostscript

- **Status:** DECISÃO REGISTRADA
- **Data:** 2026-03
- **Contexto:** Renderização do PDF no editor de âncoras (Ancorar PDF)

---

## 1. Contexto

O editor de âncoras precisa exibir um preview do PDF para o usuário definir regiões de extração. Em Linux, o WebView com PDF.js não está disponível; usa-se fallback via bitmap renderizado.

## 2. Decisão

**Cadeia de renderers:** Docnet (PDFium) como primário, Ghostscript como fallback opcional.

- **Docnet.Core:** PDFium embutido no NuGet, zero dependência externa. Suporta DPI até 1200.
- **Ghostscript:** Processo `gs` externo. Melhor antialiasing em PDFs com fontes Type1/CFF.

**Configuração:** `AncorarPdf.Preview.UseGhostscriptFallback` ou `ANCORA_PDF_PREVIEW_USE_GHOSTSCRIPT=false` desativa o fallback (apenas Docnet).

## 3. Consequências

- Linux sempre usa canvas (bitmap); WebView desativada.
- Áreas transparentes no PDF: fundo branco aplicado antes de copiar pixels para evitar exibição escura.
- Cache de até 20 páginas PNG em memória por sessão.
