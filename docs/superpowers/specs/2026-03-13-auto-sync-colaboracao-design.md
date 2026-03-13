# Design de Auto-Sync Colaborativo

Objetivo: estabilizar o auto-sync local para Git/GitHub e definir um fluxo de colaboracao com duas maquinas, cada uma usando branch propria, reduzindo conflitos.

Escopo:
- Corrigir o auto-sync Linux para nao commitar o proprio log nem outros artefatos locais.
- Manter push automatico apenas da branch pessoal da maquina.
- Permitir que cada desenvolvedor acompanhe o repositorio remoto com menos risco.
- Documentar o procedimento para configurar outra maquina.

Decisoes:
- Cada desenvolvedor usa uma branch pessoal.
- A branch compartilhada de integracao nao sera atualizada automaticamente pelo script local.
- O script local faz auto-commit e auto-push apenas da branch atual da maquina.
- Arquivos locais de controle do auto-sync ficam ignorados pelo Git.
- O script Linux passa a ter funcoes reutilizaveis para facilitar verificacao e manutencao.

Abordagens consideradas:
- Branch unica compartilhada: mais simples, mas com risco alto de conflito e historico ruidoso.
- Branch pessoal por maquina com integracao separada: recomendada por isolar o trabalho sem impedir sincronizacao via GitHub.

Resultado esperado:
- Esta maquina passa a sincronizar apenas a branch pessoal, sem loop de commits.
- O colega consegue configurar a maquina dele com a propria branch e o mesmo mecanismo.
