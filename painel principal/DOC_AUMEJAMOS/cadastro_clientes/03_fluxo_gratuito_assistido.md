# Fluxo Gratuito Assistido (Curto Prazo)

## Objetivo
Reduzir digitacao manual sem depender ainda de API paga oficial.

## Fluxo recomendado
1. Entrada por CPF/CNPJ.
2. Tentativa de pre-preenchimento por fonte gratuita quando disponivel e compativel.
3. Marcacao de cadastro como "pendente de conferencia".
4. Revisao humana obrigatoria.
5. Aprovacao manual com trilha de auditoria.

## Fallback
Quando a fonte gratuita estiver indisponivel, inconsistente ou sem cobertura:
- manter cadastro manual;
- exigir conferencia humana;
- nao liberar automatizacao critica ate aprovacao.

## Resultado esperado
Ganhar produtividade sem abrir mao da confiabilidade operacional.
