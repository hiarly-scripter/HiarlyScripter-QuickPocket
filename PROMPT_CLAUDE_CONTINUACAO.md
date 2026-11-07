# Prompt de continuação — QuickPocket

**Mod:** QuickPocket. Guarda itens pequenos no "bolso" e os solta depois, com HUD contador, mensagens com humor, sincronização multiplayer por Room Properties e request/ACK, bloqueio de itens perigosos (Saw/Blade/Hazard) e supressão de marcador no mapa/TAB.
**Pasta:** `G:\IA\.claude\PROJETOS\REPO\HiarlyScripter-QuickPocket`

## Estado encontrado
- Versão 1.0.12-test (`QuickPocket.csproj`, net48, assembly `HiarlyScripter-QuickPocket`). Testada em multiplayer local em 10/06/2026 e APROVADA para o cenário reportado (marcador simétrico host e player 2, sem colisão ou dano invisível, sem fantasma no drop). NÃO foi publicada.
- Estrutura própria: o csproj fica na raiz e o código em `src/` (`Core.cs` 157 KB, `Eligibility.cs`, `Messages.cs`, `PocketOperations.cs`, `RepoCompat.cs`).
- `RepoCompat.cs` centraliza os AccessTools e valida os campos críticos. O log mostra "RepoCompat OK" quando tudo bate. Isso é o melhor detector de quebra do update, então comece por ele.
- O log de hoje no perfil `REPO - Test` mostra "RepoCompat OK" e "v1.0.12 carregado". Confirme isso após o update, em partida de verdade.
- Sem commit: mudanças em `QuickPocket.csproj`, `src/Core.cs`, `src/PocketOperations.cs` e as pastas `media/` e `package/` (só tem o ícone). Não tem manifest, README nem thunderstore.toml: falta montar o pacote de publicação.
- Não tem remote do GitHub configurado (verifique). Branch citada nos checkpoints: `codex/unified-hide-restore-v1.0.11`. Parte do trabalho recente foi feita com o Codex.
- Histórico em `CHECKPOINTS/` e evidência em `EVIDENCE/` (logs grandes de host/player 2 da v1.0.10). Leia primeiro o checkpoint `2026-06-10_codex_v1.0.12_multiplayer-tested.md`.
- Backlog (DEVLOG): AutoGrab real, limpeza de logs verbose, nome limpo no HUD (sem "(Clone)"), contador por valor, estatísticas da run, VFX opcional, `HideRemoteItem` sem Y=-9999. Limitação externa: labels de slider do REPOConfig/MenuLib.

## Pontos de maior risco no update
Layers, colliders e Rigidbody dos itens, `PhysGrabObject`, `LevelValuables`, os Room Properties do Photon, ownership, o marcador do mapa (`MapValuable`) e o sistema de HUD/loading. É o mod mais invasivo da coleção.

## Primeira tarefa
Siga o procedimento do update. Não implemente o backlog antes de a compatibilidade estar validada em solo e em multiplayer. Depois, ajude a decidir o caminho até a primeira publicação (pacote, README, ícone, banner em `media/`).
## Contexto geral (vale para todos os meus mods de R.E.P.O.)

- Sou Hiarly (autor "HiarlyScripter"). Este mod foi criado com a sua ajuda em outra conta; esta pasta é o projeto dele. A partir de agora este chat é o chat DEDICADO deste mod: manutenção, updates, features e publicação.
- Jogo: R.E.P.O. (Steam) em `E:\SteamLibrary\steamapps\common\REPO`. DLLs de referência: `...\REPO_Data\Managed\` (Assembly-CSharp.dll etc.). BepInEx 5.4.23.5 + Harmony, via r2modman: `C:\Users\Hiarly\AppData\Roaming\r2modmanPlus-local\REPO\profiles\`. Perfis: `Default` (uso normal) e `REPO - Test` (SÓ testo lá).
- O jogo ATUALIZOU em 06/11/2026 (Assembly-CSharp.dll modificado 06/11/2026 20:55). Todo o código foi feito/testado contra a build antiga (23250495, v0.4.3.2; checkpoints antigos citam Unity 2022.3.67). O log de hoje do BepInEx mostra "Unity v6000.3.17": confirme se o jogo mudou de Unity e o que isso afeta (netstandard2.1/net48, módulos Unity, referências).
- Meta imediata: deixar este mod compatível com o novo update, SEM regredir o que já foi aprovado.

## Procedimento obrigatório para o update (nesta ordem)

1. Só leitura primeiro: leia README/DEVLOG/CHANGELOG/checkpoints desta pasta e `src/`. Resuma o estado real (versão, o que está aprovado, o que está pendente) e aponte documentos desatualizados ou contraditórios.
2. Compile contra a nova Assembly-CSharp.dll e liste erros e warnings.
3. Verifique CADA API/campo/método do jogo que o mod usa (patches Harmony, reflexão, AccessTools, campos privados) contra a build nova. Use decompilação (ilspycmd/dnSpy se existir, ou reflexão .NET) e diga o que mudou. Mudanças silenciosas em reflexão e em patches Harmony são o risco principal.
4. Leia o `LogOutput.log` do perfil `REPO - Test` procurando MissingMethod/MissingField/NullReference/erros do mod.
5. Proponha o plano de correção ANTES de editar. Depois corrija o mínimo necessário, com logs claros.
6. Build com 0 erros/0 warnings, copie a DLL para o perfil `REPO - Test` e me diga exatamente o roteiro de teste in-game (solo e multiplayer, quando o mod tiver sincronização). Marque o resultado como UNTESTED até eu confirmar.
7. Só depois do meu "aprovado": bump de versão (manifest, csproj, thunderstore.toml, README, CHANGELOG PT-BR primeiro e EN abaixo, DEVLOG), ZIP e checkpoint.

## Regras

- Thunderstore, Nexus e Discord: só publique quando eu pedir, mostrando antes o que vai enviar.

## Fluxo Git/GitHub (autorizado por mim neste projeto)

Para TODA alteração concluída (código, docs ou assets), faça sem eu precisar pedir de novo:
1. Atualize o `CHANGELOG.md` (PT-BR primeiro, EN abaixo, com data e a build do jogo de compatibilidade) e o `DEVLOG.md`. Se a versão mudou, alinhe manifest, csproj e thunderstore.toml.
2. `git status` e `git diff`: revise o que entra. Nunca inclua tokens, logs grandes, DLLs de referência do jogo nem ZIPs temporários. Adicione os arquivos por nome, não com `git add -A` às cegas.
3. Commit com mensagem clara em português, no padrão `tipo: descrição` (feat, fix, docs, chore, release), terminando com a linha de co-autoria exigida pelo ambiente.
4. `git push` para o repositório do GitHub do projeto (conta HiarlyScripter), na branch atual. Para uma versão nova, crie a tag `vX.Y.Z` e a release do GitHub com o ZIP.
5. Código ainda UNTESTED no jogo: commite e envie para uma branch (por exemplo `update/novo-patch-2026-11`) e só faça merge na principal depois do meu "aprovado". Nunca use `push --force`.
6. ATENÇÃO: este projeto não tem remote do GitHub. Antes do primeiro push, proponha criar o repositório `HiarlyScripter/HiarlyScripter-QuickPocket` (privado ou público, eu decido) e configurar o remote sem token na URL. Os commits locais já podem acontecer.
7. Se o push falhar (autenticação, remote ausente, conflito), pare e me explique. Não improvise credenciais.
- Não apague ZIPs, `releases/` nem checkpoints antigos; eles são o histórico.
- Código 100% original; não copie código de mods de terceiros.
- SEGURANÇA: o `git remote` dos repositórios pode conter um token do GitHub (`ghp_...`) em texto puro na URL. Não imprima o token. Me lembre de revogá-lo e de trocar o remote por uma URL sem credencial.
- Skills globais úteis: `/repo-mod`, `/huntsmanloot` (só HuntsmanLoot), `/nexus-mod-publisher`, `/discord-mod-sync`.
- Responda em português do Brasil. No fim de cada etapa, escreva/atualize um checkpoint curto em `CHECKPOINTS/` ou no DEVLOG.