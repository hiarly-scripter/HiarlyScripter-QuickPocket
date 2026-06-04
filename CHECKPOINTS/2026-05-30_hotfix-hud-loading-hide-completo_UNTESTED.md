# Checkpoint — QuickPocket v1.0.4
## Hotfix: HUD Loading Definitivo + Hide Completo (Áudio/Partícula/Trail/Mapa)
**Data:** 2026-05-30
**Status:** UNTESTED — aguardando validação em jogo
**Build:** 0 erros / 0 warnings

---

## Diagnóstico — causas confirmadas

### A — HUD no loading
**Causa 1 (crítica):** `TrySemiRunIsLevel()` tinha `return true` como fallback quando `SemiFunc.RunIsLevel()` não era encontrado por reflexão → HUD liberado imediatamente no primeiro frame após Awake, independente de estar em loading.

**Causa 2:** `catch { return true; }` — qualquer exceção na reflexão também liberava HUD.

**Causa 3:** Nenhum kill-switch por troca de cena (SceneManager não subscrito).

**Causa 4:** Sem stability gate — um único frame com `RunIsLevel()=true` durante overlay de loading já liberava o HUD.

### B — Minimap marker
`Behaviour` de mapa/discovery não estava sendo capturado nem desativado no hide.

### C — Áudio/partícula/trail residuais
`AudioSource`, `ParticleSystem`, `TrailRenderer` não capturados no snapshot — sons e efeitos continuavam ativos na posição original enquanto item estava pocketado.

---

## Fixes aplicados

### Fix A — HUD fail-closed triplo

**1. TrySemiRunIsLevel() fail-closed (Core.cs):**
```
ANTES: if (_runIsLevelMethod == null) return true;   // liberava HUD!
ANTES: catch { return true; }                        // liberava HUD!

DEPOIS: if (_runIsLevelMethod == null) return false; // fail-closed
DEPOIS: catch { return false; }                      // fail-closed
```

**2. SceneManager kill-switch (Core.cs):**
- `OnEnable()`: `SceneManager.sceneLoaded += OnSceneLoaded`
- `OnDisable()`: `SceneManager.sceneLoaded -= OnSceneLoaded`
- `OnSceneLoaded()`: seta `_hudSuppressedForTransition=true`, `_hudGameplayReady=false`, reseta streak
- Log: `[QP:HUDState] allowed=false reason=SceneLoaded scene=X`

**3. Stability gate (Core.cs — Update):**
- `_runIsLevelTrueStreak`: contador de frames consecutivos com `RunIsLevel()=true`
- HUD só liberado após `_HUD_STABLE_FRAMES = 5` frames consecutivos
- Streak resetado quando `RunIsLevel()=false` ou `OnSceneLoaded()`
- Log: `[QP:HUDState] allowed=true reason=GameplayConfirmed streak=5`
- Detecta saída de gameplay: `[QP:HUDState] allowed=false reason=RunIsLevelFalse`

**4. ClearAllState + HandleLevelChange:** também resetam streak

### Fix B — Minimap/TAB marker (Eligibility.cs + PocketOperations.cs)

**ComponentSnapshot — novo campo:**
```csharp
public (Behaviour comp, bool wasEnabled)[] ModifiedMapBehaviours;
```

**SaveSnapshot:** captura Behaviours com keywords no nome do tipo:
`Map`, `Minimap`, `Radar`, `Discover`, `Blip`, `Marker`, `Tracker`, `MapIcon`, `ItemIcon`

**ApplyHide:** desativa cada behaviour encontrado
```
[QP:HideComponents] item=X mapComponents=N disabled=N
```

**RestoreSnapshot:** restaura estado original de cada behaviour

**IsPocketStateConsistent:** se algum map behaviour estiver enabled → inconsistente → re-aplica hide

### Fix C — Áudio/Partícula/Trail residuais (Eligibility.cs + PocketOperations.cs)

**ComponentSnapshot — novos campos:**
```csharp
public (AudioSource src, bool wasEnabled, bool wasPlaying)[] ModifiedAudioSources;
public (ParticleSystem ps, bool wasEmitting)[] ModifiedParticleSystems;
public TrailRenderer[] ModifiedTrailRenderers;
```

**SaveSnapshot:**
- AudioSource: captura os que estão enabled OU isPlaying
- ParticleSystem: captura os que isEmitting
- TrailRenderer: captura todos (para Clear())

**ApplyHide:**
- `AudioSource.Stop() + .enabled = false`
- `ParticleSystem.Stop(true, StopEmittingAndClear)`
- `TrailRenderer.Clear()` (dados do rastro)

**RestoreSnapshot:**
- AudioSource: restaura enabled; replay somente se `loop=true` (evita replay de one-shots)
- ParticleSystem: `Play()` se era emitindo
- Map behaviours: restaura enabled

**IsPocketStateConsistent:** verifica AudioSource enabled/isPlaying e map behaviours

### Fix D — HideRemoteItem (Core.cs)

Estendido para cobrir também:
- `AudioSource.Stop() + .enabled = false`
- `ParticleSystem.Stop(true, StopEmittingAndClear)`
- `TrailRenderer.Clear()`
- `Light.enabled = false` (que já havia na operação de hide, reforçado explicitamente)

### Fix E — QuickPocket.csproj

Adicionadas referências:
- `UnityEngine.AudioModule.dll`
- `UnityEngine.ParticleSystemModule.dll`

---

## O que NÃO foi alterado

| Componente | Status |
|---|---|
| `PocketOperations.RestoreSnapshot()` | Expandido (novos campos apenas) |
| `PocketOperations.ApplyHide()` | Expandido (novos campos apenas) |
| Lógica SafeDrop / GATE1-GATE6 | Intocado |
| Room Properties / sync | Intocado |
| Cart-aware drop | Intocado |
| Contador por valor / VFX | Intocado |
| Acentuação das frases | Intocado |
| Frases TooLarge / HUD layout | Intocado |
| EligibilityChecker.Analyse() | Intocado |
| Patches Harmony | Intocados |

---

## Checklist de teste

| # | Teste | Critério de aprovação |
|---|---|---|
| 1 | Menu principal | HUD completamente ausente |
| 2 | Loading inicial (cena 0 → jogo) | HUD ausente durante toda tela preta |
| 3 | Loading entre fases | HUD ausente durante transição |
| 4 | Gameplay ativo | HUD aparece após ~5 frames |
| 5 | Pocketar item com marker no TAB | Marker some do TAB/minimap |
| 6 | Dropar item | Marker volta ao TAB/minimap |
| 7 | Pocketar caixa de som | Som para na posição original |
| 8 | Dropar caixa de som | Som volta (se era loop) |
| 9 | Pocketar item com luz | Luz some |
| 10 | Dropar item com luz | Luz volta |
| 11 | Outro jogador não ouve som de item pocketado | Sem áudio residual |
| 12 | Sem NullReferenceException | Log limpo |
| 13 | Sem log spam por frame | Log muda apenas quando estado muda |

**Logs esperados no loading:**
```
[QP:HUDState] allowed=false reason=SceneLoaded scene=X
```
**Log esperado ao entrar em gameplay:**
```
[QP:HUDState] allowed=true reason=GameplayConfirmed streak=5
```

---

## Critérios de reprovação (STOP)

| | Critério |
|---|---|
| ❌ | HUD aparece uma única vez durante loading ou tela preta |
| ❌ | Marker do item permanece no TAB enquanto pocketado |
| ❌ | Som/partícula/luz permanece no local original após pocket |
| ❌ | Item dropado fica atravessável ou sem colisão |
| ❌ | NullReferenceException no log |
| ❌ | Host não consegue pocketar item normal (regressão) |

---

*Não publicar no Thunderstore sem "sim" explícito após validação completa.*
