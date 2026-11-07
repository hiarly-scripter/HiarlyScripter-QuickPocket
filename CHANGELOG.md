# Changelog

## 1.0.12 — 2026-11-06 (Português)

Compatível com R.E.P.O. v0.5.1 (build de 06/11/2026, Unity 6000.3.17).

- Primeira versão preparada para publicação.
- Atualização para o Unity 6: `Rigidbody.velocity` → `linearVelocity` e `FindObjectsOfType` → `FindObjectsByType` (0 warnings).
- `RepoCompat` agora também valida `MapValuable.spriteRenderer`, `LoadingUI` e `SemiFunc.RunIsLevel`, apontando no log o que mudar após um update do jogo.
- Corrigida a versão do plugin: o BepInEx ignorava a versão `1.0.12-test`; agora é `1.0.12`.
- Mantido o que foi aprovado na 1.0.12-test: marcador do mapa/TAB oculto de forma simétrica entre host e jogador 2, sem colisão ou dano invisível, sem fantasma no drop. Itens Saw/Blade/Hazard continuam bloqueados por segurança.

## 1.0.12 — 2026-11-06 (English)

Compatible with R.E.P.O. v0.5.1 (2026-11-06 build, Unity 6000.3.17).

- First version prepared for publication.
- Unity 6 update: `Rigidbody.velocity` → `linearVelocity` and `FindObjectsOfType` → `FindObjectsByType` (0 warnings).
- `RepoCompat` now also validates `MapValuable.spriteRenderer`, `LoadingUI` and `SemiFunc.RunIsLevel`, logging exactly what to fix after a game update.
- Fixed the plugin version: BepInEx ignored `1.0.12-test`; it is now `1.0.12`.
- Kept everything approved in 1.0.12-test: map/TAB marker hidden symmetrically for host and player 2, no invisible collision or damage, no ghost on drop. Saw/Blade/Hazard items remain blocked for safety.
