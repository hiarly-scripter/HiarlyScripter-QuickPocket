# Checkpoint Técnico Completo — QuickPocket v1.0.5
## Estado pré-compact — HUD loading / MapMarker posição / Glow residual
**Data:** 2026-05-31
**Propósito:** Handoff completo para próxima sessão após /compact
**Status:** Em teste — bugs ativos confirmados, próxima rodada definida

---

## 1. Estado atual do projeto

### Identificação
- **Mod:** HiarlyScripter-QuickPocket
- **GUID:** `com.hiarlyscripter.quickpocket`
- **Versão compilada:** `1.0.5`
- **Raiz:** `C:\Users\Hiarly\.claude\PROJETOS\REPO\HiarlyScripter-QuickPocket\`
- **Jogo:** R.E.P.O. (Unity 2022.3.67, Photon PUN, BepInEx 5.4.23.5, net48)

### Arquivos principais

| Arquivo | Linhas | Responsabilidade |
|---|---|---|
| `src/Core.cs` | 1988 | Plugin bootstrap, PocketManager MonoBehaviour, patches Harmony |
| `src/PocketOperations.cs` | 484 | SaveSnapshot, ApplyHide, RestoreSnapshot, IsPocketStateConsistent |
| `src/Eligibility.cs` | 384 | EligibilityInfo, ComponentSnapshot, EligibilityChecker.Analyse() |
| `src/Messages.cs` | — | Frases engraçadas TooLarge (não alterado) |
| `src/RepoCompat.cs` | — | Wrappers de reflexão para campos privados do REPO (não alterado) |
| `QuickPocket.csproj` | — | net48, refs Unity + BepInEx + Photon + AudioModule + ParticleModule |

### Build e deploy

| DLL | Bytes | Data/hora | SHA-256 |
|---|---|---|---|
| `build/HiarlyScripter-QuickPocket.dll` | 66.560 | 31/mai 15:20 | `35692ffd96d90f13fac49a990cdd9ebb4b6d11f808907bbb81702a6f4b6da6e2` |
| `profiles/REPO - Test/.../HiarlyScripter-QuickPocket.dll` | 66.560 | 31/mai 15:21 | **IDÊNTICA** ✓ |
| `profiles/Default/.../HiarlyScripter-QuickPocket.dll` | 66.560 | 01/jun 20:48 | **IDÊNTICA** ✓ |

**Ambos os perfis (Default e REPO - Test) contêm a mesma DLL v1.0.5.**

O log BepInEx esperado ao iniciar qualquer perfil:
```
[Info   :   BepInEx] Loading [QuickPocket 1.0.5]
[Info   :QuickPocket] [QuickPocket] v1.0.5 inicializado. Aguardando jogador...
```

---

## 2. Histórico técnico resumido

| Funcionalidade | Status |
|---|---|
| Pocket/unpocket básico (local + SP) | ✅ Funciona |
| SafeDrop com GATE1-6 | ✅ Funciona — 12 ângulos, SphereCast horizontal, filtro RoomVolume |
| Agachamento corrigido (forward da câmera) | ✅ Funciona |
| OwnershipFixed client desbloqueado | ✅ Não mais rejeita cliente por Fixed — pocketa direto |
| NPE em PhysGrabberLocalIsGrabbing | ✅ Protegido por try/catch global em HandlePocketKey |
| NPE em PhysGrabberLocalGetGrabbedPhysGrabObject | ✅ Protegido por try/catch em TryPocket |
| HandleLevelChange idempotente (~300 calls) | ✅ Guard de idempotência |
| Player objects / Player Tumble / Player Death Head | ✅ Bloqueados — PASSO 4a |
| Phase Bridge / Door / Hinge / Fridge / Cart | ✅ Bloqueados — PASSO 4b (case-insensitive + root check) |
| Scream Doll / Screen Doll | ✅ Bloqueados — ScreamDollBlocked |
| Deployable | ✅ Bloqueado — DeployableBlocked |
| Upgrade | ✅ Bloqueado — UpgradeBlocked |
| HUD mensagens para todas as rejeições | ✅ GetRejectHudMessage cobre todos os reasons |
| Light no snapshot/hide/restore | ✅ ModifiedLights |
| AudioSource no snapshot/hide/restore | ✅ ModifiedAudioSources (v1.0.4+) |
| ParticleSystem no snapshot/hide/restore | ✅ ModifiedParticleSystems (v1.0.4+) |
| TrailRenderer.Clear() no hide | ✅ ModifiedTrailRenderers (v1.0.4+) |
| MapValuable marker TAB some ao pocketar | ✅ Funciona (v1.0.5) |
| MapValuable marker restaurado no local correto | ❌ **BUG ATIVO** — marker volta para posição original |
| HUD off durante loading | ❌ **BUG ATIVO** — aparece ~2s antes de sumir |
| Glow/brilho visual ao redor do item | ❌ **BUG ATIVO** — componente não capturado |
| Multiplayer P2 — teste real completo | ⚠️ Não testado com v1.0.5 (teste anterior era v1.0.4) |
| Acentuação das frases HUD | 🔜 P2 polish — não tratar agora |

---

## 3. Bugs ativos confirmados em v1.0.5

### Bug A — HUD aparece no loading (~2 segundos)

**Observado:** HUD do QuickPocket aparece na tela preta de loading por ~2 segundos e depois some.

**O que o log mostra:**
```
[QP:HUDState] allowed=false reason=LevelChangeStart
[QP:HUDState] allowed=true reason=GameplayConfirmed streak=5
```
O log mostra que RunIsLevel() fica `true` em streak de 5 frames antes da animação de loading terminar visualmente. O HUD é liberado quando `LoadingUI.levelAnimationCompleted` ainda pode ser `false`, mas RunIsLevel ficou `true` rápido demais.

**Hipóteses restantes (a confirmar):**
1. `LoadingUI.levelAnimationCompleted` fica `true` cedo demais (antes da tela preta sumir visualmente)
2. O singleton guard `if (this != Instance) return;` funcionou para PocketManager stale, mas há um PocketManager que não é stale ainda ficou com `_hudGameplayReady=true`
3. O streak de 5 frames é insuficiente se RunIsLevel() vira `true` muito antes do fim visual

---

### Bug B — MapValuable marker volta para posição ORIGINAL no drop

**Observado:** Ao pocketar, o ponto dourado do TAB some corretamente. Ao dropar em outro local, o ponto volta para a **posição original** onde o item foi descoberto, não onde foi dropado.

**Causa provável:** `MapValuable.SetActive(false)` desativa o marker e `SetActive(true)` o reativa. O objeto `MapValuable` tem um campo de posição cacheado ou um Update que ainda aponta para a posição original do item quando foi registrado no mapa.

**O que já sabemos via reflection:**
```
MapValuable campos:
  ValuableObject target       — aponta para o ValuableObject do item
  SpriteRenderer spriteRenderer
  Sprite spriteSmall / spriteBig
```

`MapValuable` provável possui Update() que lê `target.transform.position` e atualiza sua própria posição. Se o item foi movido enquanto o marker estava desativado, o marker não seguiu.

**Sub-bug:** Se o item quebra/é destruído enquanto pocketado, o marker fica como fantasma na posição original indefinidamente.

---

### Bug C — Glow/brilho visual ao redor da caixa de som permanece no local original

**Observado:** Ao pocketar caixa de som (ValuableBoombox):
- Áudio para ✅ (AudioSource capturado)
- Brilho/pulso visual ao redor do objeto **permanece no local original** ❌

**Causa provável:** O brilho da caixa de som não é uma `Light` nem um `ParticleSystem` — é provavelmente:
- Um efeito de shader (Renderer emission) — Renderer está sendo desativado, mas o shader emission pode ser separado
- Um objeto filho com Renderer próprio que tem nome incomum e não foi capturado
- Um componente específico como `ValuableBoombox` com update próprio que mantém efeito visual
- Uma `MusicalValuableLogic` (classe encontrada via reflection) que controla comportamento especial

**O que já foi capturado:** Renderer, Light, AudioSource, ParticleSystem — mas o glow específico da boombox não some.

---

## 4. Diagnóstico técnico atual do código

### HUD — fluxo completo (Core.cs)

**Flags (campos de instância):**
```csharp
private bool _hasLocalPocketManager      = false;
private bool _hudGameplayReady           = false;
private bool _hudSuppressedForTransition = true;   // fail-closed
private bool _cachedRunIsLevel           = false;
private bool _cachedIsLoading            = true;   // fail-closed
private int  _runIsLevelTrueStreak       = 0;
private const int _HUD_STABLE_FRAMES     = 5;
```

**Campos estáticos (reflexão cached):**
```csharp
private static MethodInfo _runIsLevelMethod       = null;
private static bool       _runIsLevelChecked      = false;
private static FieldInfo  _loadingUIAnimCompleted = null;
```

**Singleton guard (NOVO em v1.0.5):**
- `Update()`: primeira linha `if (this != Instance) return;`
- `OnGUI()`: primeira linha `if (this != Instance) return;`
- `OnSceneLoaded()`: primeira linha `if (this != Instance) return;`
- `Awake()`: seta `Instance = this`, loga se havia stale

**ShouldRenderHud() — cadeia de checks:**
```csharp
if (!QuickPocketPlugin.ShowPocketHUD.Value) return false;
if (!_hasLocalPocketManager)                return false;
if (_hudSuppressedForTransition)            return false;
if (!_hudGameplayReady)                     return false;
if (!_cachedRunIsLevel)                     return false;
if (_cachedIsLoading)                       return false;   // LoadingUI kill-switch
if (SemiFunc.PlayerAvatarLocal() == null)   return false;
return true;
```

**IsLoadingActive() — verifica LoadingUI.instance:**
```csharp
// LoadingUI.levelAnimationCompleted é campo PRIVADO → AccessTools (cached)
var lui = LoadingUI.instance;
if (lui == null) return false;
bool animDone = (bool)_loadingUIAnimCompleted.GetValue(lui);
return !animDone;  // false = loading em andamento
```

**TrySemiRunIsLevel() — fail-closed:**
- Fallback `false` quando método não encontrado (corrigido na v1.0.4)
- `catch { return false; }` — fail-closed

**Quando HUD é supprimido:**
- `Awake()`: seta `_hudSuppressedForTransition = true`
- `HandleLevelChange()`: seta `_hudSuppressedForTransition = true`
- `ClearAllState()`: seta `_hudSuppressedForTransition = true`
- `OnSceneLoaded()`: seta `_hudSuppressedForTransition = true`
- Update: quando `RunIsLevel()` retorna false após ter sido true

**Quando HUD é liberado:**
- Update: streak de 5 frames com `RunIsLevel() == true` E `_hudSuppressedForTransition == true`

---

### ComponentSnapshot — campos completos (Eligibility.cs)

```csharp
public int    ViewID;
public int    ActorNumber;
public string ItemName;

// Capturado + desativado/restaurado:
public (Collider col,   bool originalEnabled)[]   ModifiedColliders;
public (Renderer rend,  bool originalEnabled)[]   ModifiedRenderers;
public (GameObject go,  int originalLayer)[]      ModifiedLayers;
public bool   ModifiedRigidbody;
public bool   OriginalKinematic;
public bool   OriginalGravity;
public bool   OriginalDetectCollisions;
public bool   PgoWasEnabled;
public (Light lt, bool originalEnabled)[]         ModifiedLights;
public (AudioSource src, bool wasEnabled, bool wasPlaying)[] ModifiedAudioSources;
public (ParticleSystem ps, bool wasEmitting)[]    ModifiedParticleSystems;
public TrailRenderer[]                            ModifiedTrailRenderers;  // só Clear()
public (Behaviour comp, bool wasEnabled)[]        ModifiedMapBehaviours;   // fallback por nome
public MapValuable                                MapValuableMarker;       // objeto separado no Map
```

**O que NÃO está capturado:**
- Glow/brilho de shader emission (não é componente independente)
- Componentes específicos de `MusicalValuableLogic` ou `ValuableBoombox`
- Efeitos de ValuableDiscover que podem ficar ativos separadamente

---

### EligibilityChecker.Analyse() — ordem dos passos (Eligibility.cs)

```
PASSO 1 — Inimigo ativo (EnemyBlocked)
PASSO 2 — Classificação: IsWeapon/IsMelee/IsDrone/IsOrb via flags + ItemTypeIds
PASSO 3 — Regras de config (WeaponBlocked/MeleeBlocked/DroneBlocked/OrbBlocked)
PASSO 4 — Hierarquia inimigo (EnemyParentBlocked)
PASSO 4a — Objetos de jogador (PlayerObjectBlocked/PlayerBodyBlocked/PlayerDeathHeadBlocked)
           [GetComponentInParent<PlayerAvatar> + case-insensitive + rootName]
PASSO 4b — Cenário/especiais (PhaseBridgeBlocked/WorldPropBlocked/ScreamDollBlocked/
           UpgradeBlocked/DeployableBlocked)
           [case-insensitive + nameClean + rootName]
PASSO 5 — Tamanho (TooLarge) — fallback collider → renderer → localScale
PASSO 6 — Aprovado
```

---

### Patches Harmony registrados (Core.cs)

| Patch | Tipo | Classe alvo | Método |
|---|---|---|---|
| `AddPocketManagerPatch` | Postfix | `PlayerAvatar` | `Awake` |
| `PublishHostLimitPatch` | Postfix | `PlayerAvatar` | `Awake` |
| `DeathDropPatch` | Postfix | `PlayerHealth` | `Death` |
| `MainMenuResetPatch` | Postfix | `MainMenuOpen` | `Start` |
| `LevelChangePatch` | Postfix | `RunManager` | `ChangeLevel` |

---

### Room Properties (Photon sync)

```
QP_{actorNumber}  = int[] ViewIDs pocketados pelo jogador
QP_HostMaxSlots   = int: limite do host
QP_UseHostLimit   = bool: se clientes usam limite do host
```

---

### Classes REPO confirmadas via reflection (Assembly-CSharp.dll)

Relevantes para próxima rodada:

| Classe | Relevância |
|---|---|
| `LoadingUI` | Singleton da tela de loading. `levelAnimationCompleted` (bool, privado). Métodos: `StartLoading()`, `StopLoading()`, `LevelAnimationStart()`, `LevelAnimationComplete()` |
| `MapValuable` | Marker do item no TAB. Campos: `target` (ValuableObject), `spriteRenderer`, `spriteSmall`, `spriteBig`. Provavelmente tem Update() que sincroniza posição com `target.transform.position` |
| `MapObject` | Provavelmente base de MapValuable. Campos: `parent` (Transform). Métodos: `Hide()`, `Show()` |
| `ValuableObject` | Campos: `discovered`, `discoveredReminder`, `physGrabObject`. Método: `Discover(State)` |
| `ValuableDirector` | Singleton que gerencia spawn de valuables. `instance` (static) |
| `Map` | Singleton do TAB map. `instance` (static). Método: `AddValuable(ValuableObject)` |
| `RunManager` | Singleton. `instance` (static). `waitToChangeScene` (bool). `skipLoadingUI` (bool) |
| `SemiFunc` | Utilitários globais. `RunIsLevel()`, `RunIsShop()`, `RunIsLobby()`, `RunIsLobbyMenu()`, `MenuLevel()` |
| `MusicalValuableLogic` | Lógica da caixa de som. Provavelmente controla o glow/brilho (a investigar) |
| `ValuableBoombox` | Classe específica da boombox (a investigar campos/métodos) |

---

## 5. Restrições críticas para próxima rodada

| Restrição | Status |
|---|---|
| Não mexer em SafeDrop | ❌ Proibido |
| Não mexer em Room Properties / multiplayer sync | ❌ Proibido (exceto hide remoto se necessário) |
| Não mexer em carrinho/cart-aware drop | ❌ Proibido |
| Não mexer em REPOConfig | ❌ Proibido |
| Não mexer em contador por valor | ❌ Proibido |
| Não tratar acentuação/pontuação | ❌ Proibido (P2 polish futuro) |
| Não publicar no Thunderstore | ❌ Proibido |
| Não fazer git push | ❌ Proibido |
| Não aceitar HUD aparecendo no loading | ❌ Critério de reprovação |
| Não aceitar marker TAB voltando para posição antiga | ❌ Critério de reprovação |
| Não aceitar glow/brilho permanecendo na origem | ❌ Critério de reprovação |

---

## 6. Próxima tarefa após compact — HOTFIX BLOQUEADOR FINAL

### Objetivo: corrigir definitivamente 3 bugs

#### A) HUD loading definitivo

**Investigar:**
- Usar `SemiFunc.MenuLevel()` (retorna true quando em menu/loading) como kill-switch adicional
- Verificar se `LoadingUI.levelAnimationCompleted` é o campo correto ou se existe outro
- Considerar patchar `LoadingUI.LevelAnimationComplete()` [HarmonyPostfix] para garantir que o HUD só é liberado depois do método nativo ser chamado
- Considerar patchar `LoadingUI.StartLoading()` [HarmonyPostfix] para garantir suppression imediato

**Abordagem recomendada:**
```csharp
// Patch LoadingUI.StartLoading() — mais confiável que polling de campo
[HarmonyPatch(typeof(LoadingUI), "StartLoading")]
static void Postfix() => PocketManager.Instance?.SuppressHudForLoading();

// Patch LoadingUI.LevelAnimationComplete() — loading visual concluído
[HarmonyPatch(typeof(LoadingUI), "LevelAnimationComplete")]
static void Postfix() => PocketManager.Instance?.OnLoadingAnimationComplete();
```

Isso substitui a necessidade de ler o campo privado por reflexão toda frame.

#### B) MapValuable marker — posição correta no restore

**Investigar:**
- Inspecionar `MapValuable` para encontrar campos de posição cacheada
- Verificar se `MapObject.Hide()` / `Show()` são os métodos corretos a usar (em vez de `SetActive`)
- Verificar se `MapValuable` tem um `Update()` que sincroniza com `target.transform.position`
- Se sim: ao fazer `SetActive(true)` no restore, o Update() deve recalcular posição no próximo frame → o marcador deve aparecer no local correto se `target` ainda existe

**Sub-bug (item quebrado):** se `target` foi destruído (item quebrou), `MapValuable.target == null` → Update() pode crashar. Verificar se há null-check nativo.

#### C) Glow/brilho da caixa de som

**Investigar via reflection:**
```powershell
# Inspecionar MusicalValuableLogic e ValuableBoombox
$asm.GetType("MusicalValuableLogic").GetFields(...)
$asm.GetType("ValuableBoombox").GetFields(...)
```

**Abordagem:**
- Verificar se `MusicalValuableLogic` tem campo de efeito visual (Light, ParticleSystem, Animator)
- Verificar se o glow é um `Light` em objeto separado não filho do item (como o MapValuable)
- Verificar se o glow está sendo controlado por `ValuableObject.discovered` ou similar
- Solução mais segura: desabilitar `MusicalValuableLogic` (ou componente equivalente) no snapshot

---

## 7. Critérios de aprovação da próxima rodada

| # | Critério |
|---|---|
| 1 | `[BepInEx] Loading [QuickPocket 1.0.X]` onde X > 5 |
| 2 | Menu: HUD completamente ausente |
| 3 | Loading inicial (tela preta): HUD ausente durante TODO o período |
| 4 | Loading entre fases: HUD ausente durante transição |
| 5 | Gameplay real: HUD aparece (apenas em gameplay) |
| 6 | Item pocketado: marker some do TAB |
| 7 | Item dropado em outro local: marker aparece **no local real do drop** |
| 8 | Item que quebrou enquanto pocketado: marker não fica como fantasma |
| 9 | Caixa de som pocketada: áudio E brilho/glow param na posição original |
| 10 | Drop restaura item sem atravessar (colliderEnabled=True no log) |
| 11 | Sem NullReferenceException no log |
| 12 | Sem log spam por frame |
| 13 | Host consegue pocketar item normal (regressão) |
| 14 | Player 2 consegue pocketar (OwnershipFixed não reaparece) |

---

## 8. Arquivos não modificados nesta série de rodadas

| Arquivo | Status |
|---|---|
| `src/Messages.cs` | Intocado — frases TooLarge engraçadas |
| `src/RepoCompat.cs` | Intocado — wrappers de reflexão para campos privados |
| `DEVLOG.md` | Não criado/atualizado (pendente Fase 5) |
| `manifest.json`, `README.md`, `CHANGELOG.md` | Não criados (Fase 6) |

---

## 9. Instrução para próxima sessão

**Antes de qualquer edição:**
1. Ler `src/Core.cs` completo (especialmente Awake, Update, ShouldRenderHud, IsLoadingActive)
2. Ler `src/PocketOperations.cs` completo
3. Inspecionar `MusicalValuableLogic` e `MapValuable` via reflection para entender glow e posição
4. Verificar campos de `MapObject` — se tem `Hide()`/`Show()` que poderia ser mais correto que `SetActive`
5. Só então implementar

**Não aceitar conclusão se:**
- HUD aparecer uma única vez no loading
- Marker TAB voltar para posição antiga após drop
- Glow/brilho da boombox continuar visível após pocket
