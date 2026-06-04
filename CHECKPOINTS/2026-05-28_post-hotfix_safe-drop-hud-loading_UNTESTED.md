# Checkpoint Técnico — QuickPocket v1.0.0
## Post-Hotfix: SafeDrop + HUD Loading
**Data:** 2026-05-28
**Status:** UNTESTED — aguardando validação em jogo

---

## 1. Estado atual do projeto

O mod está compilado e deployado no perfil `REPO - Test` do r2modman.
Build: **0 erros / 0 warnings** (dotnet Release, 2026-05-28 18:28).

### Arquivos do projeto
```
src/
  Core.cs              — plugin + PocketManager + patches (ALTERADO neste hotfix)
  Eligibility.cs       — EligibilityChecker + ComponentSnapshot (não alterado)
  PocketOperations.cs  — SaveSnapshot / ApplyHide / RestoreSnapshot (não alterado)
  RepoCompat.cs        — AccessTools centralizado (não alterado)
build/
  HiarlyScripter-QuickPocket.dll — última build
```

### Versão deployada
- `r2modman → REPO - Test → BepInEx\plugins\HiarlyScripter-QuickPocket\`
- DLL timestamp: 2026-05-28 18:28:05

---

## 2. Hotfix aplicado nesta sessão

### A) HUD aparecendo no loading/transição de fase

**Causa raiz confirmada:**
`HandleLevelChange()` chamava apenas `ClearLocalPocket()`, que **não** setava `_inGameplay = false`.
Como o PlayerAvatar persiste via DontDestroyOnLoad entre fases, `PocketManager.Awake()` não
re-dispara após troca de fase → `_inGameplay` permanecia `true` durante o loading screen inteiro.
A guarda `PlayerAvatarLocal() != null` também era insuficiente pois o avatar persiste.

**Mudanças aplicadas:**

| Mudança | Arquivo | Detalhe |
|---|---|---|
| `using System.Reflection;` | `src/Core.cs` | Import para `MethodInfo` |
| Campo `_isTransitioningOrLoading` | `src/Core.cs` | `bool`, default `false`. `true` somente durante HandleLevelChange. |
| Campo `_levelTransitionTime` | `src/Core.cs` | `float`, `-1f` = sem transição ativa. `Time.time` ao entrar em HandleLevelChange. |
| Constante `LEVEL_TRANSITION_SETTLE_SECS = 8f` | `src/Core.cs` | Espera mínima antes de tentar re-habilitar HUD. |
| Campos estáticos `_runIsLevelMethod` / `_runIsLevelChecked` | `src/Core.cs` | Cache lazy para reflexão de `SemiFunc.RunIsLevel()`. |
| Campo `_lastHudHiddenReason` | `src/Core.cs` | Log anti-spam: só loga na mudança de estado. |
| `PocketManager.Awake()` | `src/Core.cs` | Seta `_isTransitioningOrLoading = false` e `_levelTransitionTime = -1f` ao lado de `_inGameplay = true`. |
| `HandleLevelChange()` — início | `src/Core.cs` | **Novo:** seta `_inGameplay=false`, `_isTransitioningOrLoading=true`, `_levelTransitionTime=Time.time` **antes** de qualquer limpeza. |
| `ClearAllState()` | `src/Core.cs` | Adicionados: `_isTransitioningOrLoading=false`, `_levelTransitionTime=-1f`, `_lastHudHiddenReason=null`. |
| `Update()` — recovery block | `src/Core.cs` | Após `LEVEL_TRANSITION_SETTLE_SECS` (8s), verifica `TrySemiRunIsLevel()` + avatar → re-habilita HUD. Retry a cada 2s se ainda não em nível. Só executa quando `_isTransitioningOrLoading=true`. |
| `ShouldRenderHud()` reescrito | `src/Core.cs` | Quádrupla guarda + log anti-spam: `_isTransitioningOrLoading` → `_inGameplay` → `PlayerAvatarLocal()` → `TrySemiRunIsLevel()`. |
| Novo `TrySemiRunIsLevel()` | `src/Core.cs` | Reflexão lazy para `SemiFunc.RunIsLevel()`. Se disponível: gate preciso (HUD off durante loading/shop). Se ausente: retorna `true` e o timer de 8s é o fallback. |

**Lógica de estados do HUD:**

```
Estado inicial:
  _inGameplay = false, _isTransitioningOrLoading = false
  → ShouldRenderHud = false (reason=NotInGameplay)

Primeiro Awake() de gameplay:
  _inGameplay = true, _isTransitioningOrLoading = false
  → ShouldRenderHud = true (se RunIsLevel() ok)

HandleLevelChange() chamado:
  _inGameplay = false, _isTransitioningOrLoading = true, _levelTransitionTime = T
  → ShouldRenderHud = false (reason=LoadingOrTransition)

Após 8s em Update() (com RunIsLevel=true e avatar ok):
  _inGameplay = true, _isTransitioningOrLoading = false
  → ShouldRenderHud = true

OU: avatar recriado → Awake() dispara:
  _inGameplay = true, _isTransitioningOrLoading = false
  → ShouldRenderHud = true

ClearAllState() (menu / OnLeftRoom):
  _inGameplay = false, _isTransitioningOrLoading = false, _levelTransitionTime = -1f
  → ShouldRenderHud = false (reason=NotInGameplay)
  → Update() recovery NÃO executa (_isTransitioningOrLoading = false)
```

---

### B) SafeDrop — GATE4 SphereFull falso em área aberta

**Causa raiz confirmada:**
`Physics.CheckSphere` é cego — detectava os colliders do PlayerAvatar local (hitbox,
PhysGrabber area, etc.) como obstáculos. Com `DROP_SEARCH_RADIUS=1.2m` e
`DROP_SPHERE_R=0.28m`, dependendo do tamanho dos colliders do avatar, o check bloqueava
todos os 12 ângulos mesmo em área completamente livre.

**Mudanças aplicadas:**

| Mudança | Arquivo | Detalhe |
|---|---|---|
| Buffer estático `_overlapBuffer` | `src/Core.cs` | `Collider[32]` estático — sem alocação por candidato em GATE4. |
| GATE4 — substituição | `src/Core.cs` | `Physics.CheckSphere` → `IsDropSphereClear()` com filtragem. |
| Novo `IsDropSphereClear()` | `src/Core.cs` | `OverlapSphereNonAlloc` + filtra colliders do PlayerAvatar local (`IsChildOf`). Loga nome+layer do primeiro bloqueador real. |

**Assinatura do novo helper:**
```csharp
private static bool IsDropSphereClear(
    Vector3 center, float radius, int mask,
    PlayerAvatar player, out string blockerName)
```

---

### C) GATE1 NoFloor — log detalhado + origem elevada

**Mudanças aplicadas:**

| Mudança | Arquivo | Detalhe |
|---|---|---|
| `horizPt.y` | `src/Core.cs` | `playerPos.y + 0.5f` → `playerPos.y + 0.8f`. Mais margem acima de geometria rente ao chão. |
| GATE1 FAIL log | `src/Core.cs` | Adicionados `origin=` e `castDist=` para diagnóstico de NoFloor. |
| GATE1 OK log | `src/Core.cs` | Novo log com `floor=`, `normal.y=`, `layer=`. |

---

## 3. Arquivo alterado

- **`src/Core.cs`** — único arquivo modificado

---

## 4. O que NÃO foi alterado

| Componente | Status |
|---|---|
| `PocketOperations.RestoreSnapshot()` | Intocado |
| `PocketOperations.ApplyHide()` | Intocado |
| `PocketOperations.IsPocketStateConsistent()` | Intocado |
| Light snapshot (`ModifiedLights`) | Intocado |
| Multiplayer / Room Properties / Photon sync | Intocado |
| Configs (`REPOConfig`, todos os `ConfigEntry`) | Intocado |
| Frases TooLarge / HUD layout / fade | Intocado |
| AutoGrab | Não implementado (backlog futuro) |
| Cart-aware drop | Não implementado (backlog futuro, registrado separadamente) |
| `Eligibility.cs` | Intocado |
| `RepoCompat.cs` | Intocado |
| GATE2, GATE1b, GATE3, GATE5, GATE6 | Intocados |

---

## 5. Bugs que motivaram este hotfix

### Bug A — HUD aparecia no loading screen
- **Reprodução:** Entrar em gameplay → trocar de fase → HUD visível durante loading entre fases.
- **Causa:** `HandleLevelChange()` não setava `_inGameplay = false`. PlayerAvatar persiste
  via DontDestroyOnLoad → `Awake()` não re-dispara → `_inGameplay` permanecia `true`.
- **Tentativa anterior:** Flag `_inGameplay` adicionada mas só cobrindo window pós-`OnLeftRoom`,
  não a window pós-`HandleLevelChange`. Fix insuficiente.

### Bug B — "Sem espaço para soltar aqui" em área aberta
- **Reprodução:** Segurar item + pressionar H em corredor/sala aberta → mensagem de bloqueio.
- **Causa primária:** `Physics.CheckSphere` em GATE4 detectava colliders do PlayerAvatar local
  como obstáculos, bloqueando todos os 12 ângulos de busca.
- **Causa secundária:** `horizPt.y = playerPos.y + 0.5f` poderia originar raycast GATE1 dentro
  de geometria rente ao chão em alguns ângulos.

---

## 6. Próximos testes obrigatórios

### Teste A — HUD no loading
```
1. Entrar em sala (singleplayer ou multiplayer)
2. Carregar fase normalmente
3. VERIFICAR: HUD "QuickPocket: 0/4" deve aparecer
4. Log esperado: "[QP:HUD] visible."
5. Esperar fim da fase / trocar de fase (DropOnLevelChange=true)
6. VERIFICAR: HUD NÃO deve aparecer durante loading
7. Log esperado: "[QuickPocket] HandleLevelChange: HUD desabilitado..."
8.              "[QP:HUD] hidden reason=LoadingOrTransition"
9. Nova fase carregada
10. VERIFICAR: HUD reaparece (após ~8s se avatar persiste, imediato se recriado)
11. Log esperado: "[QuickPocket] HUD re-ativado apos transicao de fase." OU
                  "[QuickPocket] PocketManager.Awake — gameplay ativo, HUD habilitado."
```

### Teste B — HUD no menu principal
```
1. Pressionar ESC / voltar ao menu durante run
2. VERIFICAR: HUD não aparece no menu
3. Log esperado: "[QuickPocket] Estado interno completamente limpo..."
4.              "[QP:HUD] hidden reason=NotInGameplay"
```

### Teste C — Drop em área aberta
```
1. Pegar item pequeno (cristal, moeda, etc.)
2. Pressionar H — item guardado
3. Pressionar H novamente — drop deve ocorrer
4. VERIFICAR: drop executado sem "Sem espaço"
5. Se bloqueio: verificar log GATE4 com "blocker=" mostrando obstáculo real
   (não deve ser o PlayerAvatar)
6. Log esperado ao passar: "[QP:SafeDrop] ang=0 gate=GATE1 result=OK floor=..."
   "[QP:SafeDrop] ang=0 result=OK pos=..."
```

### Teste D — Drop em prateleira/lugar alto
```
1. Subir em prateleira ou plataforma elevada
2. Tentar drop (H com item)
3. Se superfície válida existe: drop ocorre na prateleira
4. Se sem superfície (borda, plataforma muito alta): "Sem espaço" correto
5. VERIFICAR: item nunca some — permanece no bolso se drop falhar
6. Log de GATE2 esperado em falha de andar: "reason=FloorDelta=X.XX"
```

### Teste E — Drop falho
```
1. Ficar em canto encurralado ou dentro de geometria
2. Tentar drop
3. VERIFICAR: "Drop falhou. Tente em outro lugar." ou "Sem espaço"
4. VERIFICAR: contador não muda, item permanece no bolso
5. VERIFICAR: nenhum NullReferenceException no log
```

### Teste F — Log de diagnóstico GATE1/GATE4
```
Reproduzir qualquer bloqueio de drop e verificar que os logs mostram:
  [QP:SafeDrop] ang=X gate=GATE1 result=FAIL reason=NoFloor origin=(X,Y,Z) castDist=4.0
  [QP:SafeDrop] ang=X gate=GATE4 result=FAIL reason=SphereFull blocker=NomeObjeto (layer:LayerName)
Blocker não deve ser "PlayerAvatar", "LocalPlayer" ou componentes do avatar local.
```

---

## 7. Critério de reprovação (STOP — corrigir antes de continuar)

| Critério | Descrição |
|---|---|
| HUD no loading | HUD aparece durante tela de carregamento entre fases |
| "Sem espaço" em área aberta | Bloqueio sem blocker real no log (blocker = PlayerAvatar = bug) |
| Item some | Item não retorna ao bolso após drop falho nem aparece no mundo |
| Item sai do bolso em drop falho | Contador decrementa mesmo com drop falhando |
| NullReferenceException | Qualquer exceção nula no log BepInEx |
| HUD não reaparece nunca | Após troca de fase, HUD nunca volta (timer travado ou RunIsLevel bloqueando) |

---

## 8. Limitações conhecidas (documentadas, não são bugs)

### L1 — Fallback sem `SemiFunc.RunIsLevel()`
Se `SemiFunc.RunIsLevel()` não existir no REPO atual (log: `"nao encontrado — usando timer"`),
a re-habilitação do HUD após loading depende do timer de 8s + `PlayerAvatarLocal() != null`.
Em máquinas muito lentas (loading > 8s), o HUD pode reaparecer prematuramente durante loading.
Verificar no log na primeira execução se `RunIsLevel()` foi encontrado.

### L2 — Prateleiras altas (GATE2 delta > 1.5m)
Drop para o chão abaixo de prateleiras altas (> 1.5m de diferença) é bloqueado por GATE2.
Isso é comportamento CORRETO — o item não deve pular para um andar muito diferente.
Item permanece no bolso nesses casos.

### L3 — Cart-aware drop (backlog v1.1)
TryFindSafeDropPosition não detecta a superfície de suporte de carrinhos.
Item seria dropado no chão abaixo do carrinho, não sobre o carrinho.
Feature registrada como backlog futuro.

---

## 9. Constantes SafeDrop atuais (não alteradas neste hotfix)

```csharp
DROP_SEARCH_RADIUS   = 1.2f   // distância horizontal de busca (m)
DROP_SPHERE_R        = 0.28f  // raio da esfera de verificação
DROP_MAX_FLOOR_DELTA = 1.5f   // max diferença de andar aceita (m)
DROP_ITEM_HEIGHT     = 0.2f   // altura acima do chão ao spawnar
DROP_CEILING_MIN     = 0.5f   // espaço mínimo entre item e teto
DROP_GROUND_CAST     = 4.0f   // alcance do raycast de chão
LEVEL_TRANSITION_SETTLE_SECS = 8f  // NOVO: espera mínima antes de re-habilitar HUD
```

---

## 10. Próxima fase recomendada (após validação deste hotfix)

Se todos os testes passarem:
- **Fase 5 — Polish:** revisar logs (remover debug verboso desnecessário), atualizar DEVLOG.md
  com decisões técnicas desta sessão.
- **Fase 6 — Package:** manifest.json, README.md, CHANGELOG.md, INSTALL.md, ícone SVG.

Se algum teste falhar: hotfix cirúrgico adicional antes de continuar.

---

*Gerado automaticamente pelo arquiteto sênior pós-execução. Não publicar no Thunderstore
sem "sim" explícito do usuário após validação completa.*
