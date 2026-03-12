# Nota: Multiplataforma para o Sistema Completo

## Objetivo
Deixar explícito que **todo o sistema**, e não apenas o login, seguirá o padrão **multiplataforma**.

## Diretriz oficial
- O projeto utiliza **Avalonia UI + .NET 8**.
- **Todo módulo futuro** (importação, domínio, prefeitura, etc.) será criado **na mesma base**.
- O aplicativo será publicado como **dois instaladores**:
  - Windows (EXE/MSI)
  - Linux (DEB/AppImage)

## Implicações
- **Um único código** para Windows e Linux.
- **Instalação completa** com atalhos, pasta de dados e dependências embutidas.
- Quando novos módulos forem adicionados, **eles já entram no mesmo instalador**.

## Conclusão
Estamos construindo o login agora, mas **todo o sistema seguirá o mesmo padrão** e será instalado corretamente em ambos os sistemas.
