# Checkpoint Técnico — QuickPocket v1.0.2
## Hotfix Multiplayer: OwnershipFixed + NPE + HUD Spam + Eligibilidade + HUD Messages
**Data:** 2026-05-28
**Status:** UNTESTED — aguardando validação em jogo (multiplayer, 2+ jogadores)
**Build:** 0 erros / 0 warnings (dotnet Release, 2026-05-28 20:47)

---

## 1. Motivação — Bugs confirmados em teste multiplayer real

Teste com 2 jogadores (host + player 2) revelou:

### P0-A — Player 2 não consegue usar o mod (OwnershipFixed)
```
[QuickPocket] Rejected: Item=Item Health Pack Small(Clone) Reason=OwnershipFixed
```
**Causa raiz:** No REPO multiplayer, todos os itens têm `PhotonView.OwnershipTransfer == Fixed`
para clientes não-host. O código rejeitava TODOS os itens do cliente com esse motivo.
O mod era completamente inutilizável para o player 2.

### P0-B — NullReferenceException em TryPocket no player 2
```
NullReferenceException
  at SemiFunc.PhysGrabberLocalGetGrabbedPhysGrabObject ()
  at QuickPocket.PocketManager.TryPocket ()
```
**Causa raiz:** `SemiFunc.PhysGrabberLocalGetGrabbedPhysGrabObject()` lança NPE internamente
quando PhysGrabber não está inicializado (ex: durante transição de lobby). A chamada não
estava envolvida em try/catch.

### P0-C — HUD spam (~300 logs) durante transição de fase
```
[QP:HUDState] HandleLevelChange — HUD suprimido, aguardando confirmacao de nivel.
[QP:HUDState] HandleLevelChange — HUD suprimido, aguardando confirmacao de nivel.
... (x300)
```
**Causa raiz:** `RunManager.ChangeLevel` é chamado múltiplas vezes por transição no REPO
(Arena Race: ~300x). Sem guarda de idempotência em `HandleLevelChange()`, a lógica de
drop + log disparava a cada chamada.

### P1-A — Objetos de jogador podem ser pocketados
```
[QuickPocket] Pocketado: Player Death Head(Clone) (3/4)
[QuickPocket] Pocketado: Player Tumble(Clone)
```
**Causa raiz:** `EligibilityChecker.Analyse()` não tinha verificação de objetos de jogador.

### P1-B — Objetos de cenário/especiais podem ser pocketados
- Phase Bridge, Doors, Hinges: partes do nível, quebram se removidas
- Scream Doll: comportamento especial que corrompe estado ao restaurar

### P1-C — Rejeições sem feedback visual (HUD silencioso)
Armas, drones, objetos de jogador e outros motivos de rejeição não exibiam mensagem no HUD.
Apenas `TooLarge` tinha mensagem. Player ficava confuso sem saber por que não funcionava.

---

## 2. Hotfix aplicado

### Parte A — P0: OwnershipFixed removido da lógica de rejeição

**Arquivo:** `src/Core.cs` → `TryPocket()`

**Antes:**
```csharp
if (!pv.IsMine)
{
    if (pv.OwnershipTransfer == OwnershipOption.Fixed)
    {
        // ← rejeitava TODOS os itens do cliente no REPO
        return;
    }
    ...
}
```

**Depois:**
```csharp
// Log diagnóstico de ownership
QuickPocketPlugin.Log.LogDebug(
    "[QuickPocket] MP actor=X isMaster=Y item=Z isMine=? owner=? ownershipTransfer=?");

// Solicitar ownership somente em modo Takeover
if (!pv.IsMine && pv.OwnershipTransfer == OwnershipOption.Takeover)
    pv.RequestOwnership();
// Fixed / Request: pocketar diretamente — item está nas mãos do jogador
```

**Filosofia:** O item está fisicamente nas mãos do jogador (PhysGrabber confirmado).
Isso é condição suficiente para pocketar. OwnershipTransfer só importa se quisermos
transferir ownership — não é necessário para esconder/mostrar o item.

### Parte B — P0: NullReferenceException protegida

**Arquivo:** `src/Core.cs` → `TryPocket()`

```csharp
PhysGrabObject pgoRaw;
try
{
    pgoRaw = SemiFunc.PhysGrabberLocalGetGrabbedPhysGrabObject();
}
catch (System.Exception ex)
{
    QuickPocketPlugin.Log.LogDebug(
        "[QuickPocket] PhysGrabberLocalGetGrabbedPhysGrabObject excecao: " + ex.GetType().Name);
    return;
}
```

### Parte C — P0: HandleLevelChange idempotente

**Arquivo:** `src/Core.cs` → `HandleLevelChange()`

```csharp
// Guard adicionado no início do método:
if (_hudSuppressedForTransition)
    return;  // já processado nesta transição
```

**Lógica:** `_hudSuppressedForTransition` é `false` apenas durante gameplay ativo.
Primeira chamada: `false` → executa e seta `true`. Chamadas subsequentes: `true` → retorna.
Limpo apenas em `Update()` quando `RunIsLevel()` confirma nível ativo.

### Parte D — P1: Bloqueio de objetos de jogador

**Arquivo:** `src/Eligibility.cs` → `Analyse()` — novo PASSO 4a

```csharp
// PlayerAvatar (via GetComponentInParent — cobre hierarquia completa)
if (go.GetComponentInParent<PlayerAvatar>() != null)
    return Finalise(info, false, "PlayerObjectBlocked");

// Ragdolls por nome (são root GameObjects clonados)
if (name.Contains("Player Tumble"))
    return Finalise(info, false, "PlayerBodyBlocked");

if (name.Contains("Player Death Head"))
    return Finalise(info, false, "PlayerDeathHeadBlocked");
```

### Parte E — P1: Denylist de objetos de cenário / itens especiais

**Arquivo:** `src/Eligibility.cs` → `Analyse()` — novo PASSO 4b

| Pattern | Motivo |
|---|---|
| `"Phase Bridge"` | `PhaseBridgeBlocked` |
| `"Door"` ou `"Hinge"` | `WorldPropBlocked` |
| `"Cart"` ou `"Truck"` ou `"Fridge"` | `WorldPropBlocked` |
| `"Scream Doll"` | `SpecialItemBlocked` |
| `"Upgrade"` | `UpgradeBlocked` |

### Parte F — P1: Mensagens de HUD para todos os motivos de rejeição

**Arquivo:** `src/Core.cs` — novo método `GetRejectHudMessage(string reason)` + chamada em `TryPocket()`

| Motivo | Mensagem |
|---|---|
| `PlayerObjectBlocked` | "Nao cabe gente viva no bolso." |
| `PlayerBodyBlocked` | "Esse ai ainda tem CPF." |
| `PlayerDeathHeadBlocked` | "Cabeca de jogador fica fora do bolso." |
| `PhaseBridgeBlocked` / `WorldPropBlocked` | "Isso e parte do nivel, nao um item." |
| `UpgradeBlocked` | "Upgrades ficam no lugar." |
| `SpecialItemBlocked` | "Esse item nao pode ser guardado." |
| `WeaponBlocked` / `MeleeBlocked` | "Armas nao cabem no bolso." |
| `DroneBlocked` | "Drones nao cabem no bolso." |
| `EnemyBlocked` | "Isso e um inimigo, nao um item." |
| `EnemyParentBlocked` | "Esse item esta preso a um inimigo." |
| `OrbBlocked` | "Orbs bloqueados nas configs." |
| `DuplicateBlocked` | "Ja tem um desse no bolso." |

---

## 3. O que NÃO foi alterado

| Componente | Status |
|---|---|
| `PocketOperations.RestoreSnapshot()` | Intocado |
| `PocketOperations.ApplyHide()` | Intocado |
| `PocketOperations.IsPocketStateConsistent()` | Intocado |
| `PocketOperations.SaveSnapshot()` | Intocado |
| Light snapshot (`ModifiedLights`) | Intocado |
| `TryFindSafeDropPosition()` | Intocado |
| `IsDropSphereClear()` | Intocado |
| Room Properties / Photon sync | Intocado |
| Configs (`REPOConfig`, todos os `ConfigEntry`) | Intocado |
| Frases TooLarge / HUD layout / fade | Intocado |
| GATE1–GATE6 (lógica de drop, sem alteração) | Intocado |
| `RepoCompat.cs` | Intocado |
| Cart-aware drop / contador por valor / VFX | Intocado |

---

## 4. Versão deployada

- Versão: **v1.0.2**
- Build timestamp: 2026-05-28 20:47
- `r2modman → REPO - Test → BepInEx\plugins\HiarlyScripter-QuickPocket\`
- ZIP irmão: `QuickPocket-v1.0.0-manual.zip` (60.4 KB, DLL atualizada)

---

## 5. Testes obrigatórios

### Teste A — Player 2 consegue pocketar (P0)
```
1. Host inicia sala, player 2 entra
2. Player 2 segura um item pequeno (ex: Health Pack)
3. Player 2 pressiona H
4. Log esperado: "[QuickPocket] Pocketado: Item Health Pack Small(Clone) (1/4)"
5. Log esperado NÃO deve aparecer: "Reason=OwnershipFixed"
6. HUD do player 2 deve mostrar "QuickPocket: 1/4"
7. Player 2 pressiona H novamente para retirar
8. Item deve reaparecer no chão
```

### Teste B — Sem NullReferenceException (P0)
```
Durante qualquer sessão multiplayer, verificar que o log do player 2
NÃO contém:
  NullReferenceException
  at SemiFunc.PhysGrabberLocalGetGrabbedPhysGrabObject
  at QuickPocket.PocketManager.TryPocket
```

### Teste C — HandleLevelChange uma única vez (P0)
```
1. Iniciar partida, passar por transição de fase (ex: Arena Race)
2. Log esperado: exatamente 1x "[QP:HUDState] HandleLevelChange — HUD suprimido..."
3. NÃO deve aparecer centenas de repetições da mesma linha
```

### Teste D — Bloqueio de objetos de jogador com HUD (P1)
```
1. Player 2 tenta segurar e pocketar Player Death Head(Clone) ou Player Tumble(Clone)
2. Log esperado: "Rejected: Item=Player Death Head(Clone) Reason=PlayerDeathHeadBlocked"
3. HUD esperado: "Cabeca de jogador fica fora do bolso."
4. Item NÃO deve sumir do mundo
```

### Teste E — Bloqueio de cenário com HUD (P1)
```
1. Tentar pocketar uma porta/dobradiça ou Phase Bridge
2. Log esperado: "Reason=WorldPropBlocked" ou "Reason=PhaseBridgeBlocked"
3. HUD esperado: "Isso e parte do nivel, nao um item."
```

### Teste F — Bloqueio de arma com HUD (P1)
```
1. Pegar arma (se AllowWeapons=false)
2. Pressionar H
3. HUD esperado: "Armas nao cabem no bolso."
```

### Teste G — Regressão: host ainda funciona normalmente
```
1. Host pocketa health packs, itens normais → continua funcionando
2. Host retira itens → continua funcionando
3. HUD do host correto
```

---

## 6. Critério de reprovação (STOP — corrigir antes de continuar)

| Critério | Descrição |
|---|---|
| OwnershipFixed ainda aparece | Log "Reason=OwnershipFixed" para player 2 segurando item |
| NullReferenceException | Qualquer NPE em TryPocket no log do player 2 |
| HandleLevelChange spam | Mais de 3 linhas de HandleLevelChange por transição |
| Player Death Head/Tumble pocketável | Item de jogador some do mundo |
| Porta/Phase Bridge pocketável | Objeto de cenário some do mundo |
| Host quebrou | Host não consegue mais pocketar itens normais |
| HUD não mostra mensagem | Rejeição silenciosa para categorias listadas na Parte F |
| Item some | Item não retorna ao bolso após drop falho |

---

## 7. Limitações conhecidas desta versão

- **Denylist por nome é heurística:** "Upgrade" bloqueia qualquer item com essa substring no nome. Se existir um item legítimo chamado "Upgrade Something" que deveria ser pocketável, precisará ser refinado.
- **Player 2 não é dono dos itens:** Drop sincronizado funciona via Room Properties, mas o item restaurado pode ter física ligeiramente diferente do estado original (limitação de v1.0.x, documentada).

---

## 8. Próxima fase (após validação)

Se todos os testes passarem:
- **Fase 5 — Polish:** revisar logs (remover debug verboso), atualizar DEVLOG.md, revisar todas as mensagens de HUD
- **Fase 6 — Package:** manifest.json, README.md, CHANGELOG.md, INSTALL.md, ícone

*Não publicar no Thunderstore sem "sim" explícito após validação completa.*
