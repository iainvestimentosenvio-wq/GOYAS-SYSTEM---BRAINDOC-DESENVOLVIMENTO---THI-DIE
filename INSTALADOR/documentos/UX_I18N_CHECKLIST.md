# UX I18N CHECKLIST (PT-BR ONLY)

## Escopo

- Instalador Inno com experiencia 100% em portugues.
- Idioma ingles removido deste ciclo UX.
- Tema dinamico light/dark habilitado no wizard.
- MSI permanece fora do escopo de traducao visual avancada.

## Checklist tecnico

- [x] `[Languages]` contem apenas `ptbr`.
- [x] Nao existe `Name: "en"` no `.iss`.
- [x] Nao existem chaves `en.*` em `[CustomMessages]`.
- [x] `ShowLanguageDialog=no` definido no `[Setup]`.
- [x] `WizardStyle` com perfil dinamico (`modern dynamic hidebevels excludelightcontrols`).
- [x] `WizardBackImageFile` light configurado.
- [x] `WizardBackImageFileDynamicDark` configurado.
- [x] `WizardSmallImageFile` light configurado.
- [x] `WizardSmallImageFileDynamicDark` configurado.
- [x] `WizardBackColorDynamicDark` configurado.
- [x] `ProgressStageLabel` em portugues.
- [x] `EtaLabel` em portugues.
- [x] `EtaCalculating` em portugues.
- [x] `CancelConfirmMessage` em portugues.
- [x] Mensagens de failpoint/cancel-test em portugues.
- [ ] Validado visualmente em Win10.
- [ ] Validado visualmente em Win11.

## Observacao operacional

- Sempre que houver alteracao de copy visual no instalador, executar:
  1. `bash testes/windows/test-installer-contract-static.sh`
  2. Smoke Win10 com `-EnableUxSuite`
  3. `bash RODAR-UX-P10-GATE.sh` (fechamento)
