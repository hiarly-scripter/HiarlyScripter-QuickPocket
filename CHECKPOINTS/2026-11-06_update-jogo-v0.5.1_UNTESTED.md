# QuickPocket — compatibilidade com o update do jogo de 06/11/2026 (UNTESTED)

Data: 2026-11-06
Branch: `update/novo-patch-2026-11` (base: 1.0.12-test aprovada em 10/06/2026)
Jogo: R.E.P.O. v0.5.1, Unity 6000.3.17f1, Assembly-CSharp.dll de 06/11/2026 20:55.

## Estado

- UNTESTED em partida. Nada publicado. Sem remote do GitHub ainda.
- Versão mantida em 1.0.12-test (sem bump até o "aprovado").
- O trabalho não testado de 22/06 (`DebugMode`, `LogDbg`, SetActive/AudioSource) foi descartado
  e guardado como patch fora do repositório (notas da sessão), não faz parte deste update.

## Verificação contra a build nova

Todos os campos e métodos usados por reflexão e Harmony existem: `PhysGrabObject` (isGun, isMelee,
isEnemy, itemWidthX/HeightY/LengthZ), `ItemAttributes.itemType`, `MapValuable.spriteRenderer`,
`LoadingUI` (levelAnimationCompleted, StartLoading, LevelAnimationComplete), `PlayerAvatar.Awake`,
`PlayerHealth.Death`, `MainMenuOpen.Start`, `SemiFunc.RunIsLevel`, `RunManager.ChangeLevel(bool,bool,ChangeLevelType)`.
Log do perfil `REPO - Test`: "RepoCompat OK" e plugin carregado, sem MissingMethod/MissingField.

## Mudanças

- `Rigidbody.velocity` → `linearVelocity` (6 pontos; API obsoleta no Unity 6).
- `FindObjectsOfType` → `FindObjectsByType(..., None)` (3 pontos; ordem não é usada).
- `RepoCompat`: validação advisory extra de `MapValuable.spriteRenderer`, `LoadingUI`
  (campo + 2 métodos) e `SemiFunc.RunIsLevel`.
- Build: 0 erros, 0 warnings. DLL SHA-256 `97B25961...0B7A`, implantada em `REPO - Test`
  (backup `.bak-pre-update-20261106` na mesma pasta).

## Roteiro de teste pendente

Solo: carregar o jogo e checar "RepoCompat OK" sem "Compat warning"; pocket e drop de itens pequenos;
HUD contador na troca de fase e no loading; marcador do mapa/TAB some ao guardar e não volta no ponto antigo ao soltar;
sem colisão/dano invisível; itens Saw/Blade/Hazard continuam bloqueados; drop ao morrer e ao trocar de nível.
Multiplayer (host + player 2): pocket/drop nos dois sentidos (request/ACK), marcador oculto simétrico,
sem fantasma no drop, sem spam de áudio.
