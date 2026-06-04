# DEVLOG — QuickPocket v1.0.0

## Decisões Técnicas

### Por que NÃO usar DontDestroyOnLoad para itens pocketados (decisão v1.0.0)
Abordagem inicial: mover item para um container `DontDestroyOnLoad` + Y=-9999 para esconder.
**Problema descoberto em testes**: `DontDestroyOnLoad` move o objeto para uma cena Unity separada, removendo-o das listas `LevelValuables` do REPO (`tiny`, `small`, `medium`, `big`, `wide`, `tall`, `veryTall`). Quando restaurado para a cena principal, o REPO detecta o item como orphan e destrói em ~0.2s.

**Solução implementada**: item permanece no parent e cena originais durante todo o tempo pocketado. Invisibilidade garantida por: `PhysGrabObject.enabled = false` (impede Update() do jogo de lutar contra o hide) + `Rigidbody` kinematic + `detectCollisions=false` + colliders/renderers off + layer `IgnoreRaycast`. Sem reparenting, sem movimento de posição.

### DLL final sem obfuscação
O package de release (Fase 6) usa a DLL de build Release limpa, sem nenhuma etapa de obfuscação. Razões: o jogo roda em ambiente BepInEx onde obfuscação causa problemas de interoperabilidade com Harmony patches; a comunidade de modding espera DLLs inspecionáveis; e não há lógica proprietária que justifique obfuscação.

### Por que `Ignore Raycast` (layer 2) foi escolhida para itens pocketados
Itens pocketados precisam ser invisíveis e intocáveis para o sistema de grab do REPO. A layer `Ignore Raycast` é nativa do Unity (layer 2) e está excluída de todos os raycasts por padrão — incluindo o `PhysGrabber`. Não requer layer customizada, não quebra outros mods que leem layers por nome. A posição em Y=-9999 é proteção extra contra mods de terceiros que possam ignorar raycasts.

### Por que o limite do host fica travado na sessão
Room Custom Properties não têm mecanismo de "atualização ao vivo" que force re-sincronização de clientes já conectados de forma confiável. Travar o limite no momento de entrada da sala garante consistência: todos os jogadores usam o mesmo valor durante a sessão inteira. Mudanças no `.cfg` durante a sessão requerem reinício da sala.

### Por que drones são bloqueados por padrão (`AllowDrones=false`)
Drones em REPO são implementados via `ItemDrone` com lógica de IA e PathFinding. Pocketar e restaurar o estado de um drone exigiria pausar/restaurar o NavMeshAgent e IA, o que é complexo e propenso a bugs. Bloqueio por padrão elimina esse risco. Usuários avançados podem habilitar via config sob sua responsabilidade.

### Por que armas são bloqueadas por padrão (`AllowWeapons=false`)
Armas têm `ItemAttributes.itemType == itemType.gun` (ou melee/launcher). Em gameplay, esconder uma arma no bolso quebraria a fantasia de que o personagem está desarmado — além de possível exploração de gameplay. Bloqueio por padrão é a escolha conservadora correta.

### Por que não há persistência de itens entre fases em v1.0.0
Items em REPO são destruídos e recriados entre fases junto com a cena. Persistir o ViewID de um item entre fases não tem sentido porque o objeto não existirá mais. Na troca de fase, os itens pocketados são dropados (se `DropOnLevelChange=true`) ou simplesmente descartados do estado (o item será destruído pela engine de qualquer forma).

### Riscos conhecidos de ownership
- `OwnershipTransfer.Fixed` → rejeitar com log informativo. Sem fallback.
- `OwnershipTransfer.Request` → aguardar callback. Timeout de 2s → rejeitar.
- `OwnershipTransfer.Takeover` → `RequestOwnership()` imediato → operar após callback.
- Item sem `PhotonView` → singleplayer ou item local → operar diretamente.

### Limitações de late join e snapshot
O late-joiner cria um snapshot baseado no estado atual do item (não no estado original de quando foi pocketado). Isso é aceitável para v1.0.0 porque os componentes que o QuickPocket modifica (Renderer, Collider, Rigidbody, layer) têm estados padrão conhecidos. A única divergência possível é se um outro mod tiver modificado esses componentes antes do late join — caso raro e documentado.

### Por que `itemType` enum (nativo do jogo) é preferido sobre heurística de nome
A reflexão confirmou que `ItemAttributes.itemType` expõe `itemType.gun`, `itemType.drone`, `itemType.orb`, etc. Usar o enum nativo é mais robusto do que `item.name.Contains("Gun")` — funciona independente do nome do prefab.

### Por que `PhysGrabObject.itemWidthX/Y/Z` é usado em vez de bounds calculados
A reflexão confirmou que `PhysGrabObject` tem campos `itemWidthX`, `itemHeightY`, `itemLengthZ` (floats). Esses valores são mais confiáveis que bounds calculados em runtime (que dependem de colliders ativos). São usados como verificação primária de tamanho.

---

## APIs Confirmadas via Reflexão .NET

### PhysGrabber (Assembly-CSharp)
- `grabbed` — bool: se está segurando algo
- `grabbedPhysGrabObject` — PhysGrabObject: objeto sendo segurado

### PhysGrabObject (Assembly-CSharp)
- `photonView` — PhotonView
- `itemAttributes` — ItemAttributes
- `itemWidthX`, `itemHeightY`, `itemLengthZ` — float: dimensões do item
- `isGun`, `isMelee`, `isEnemy`, `isValuable` — bool: flags de classificação

### ItemAttributes (Assembly-CSharp)
- `itemType` — enum `itemType`: classificação nativa do jogo
- `itemType.gun`, `.melee`, `.drone`, `.orb`, `.tracker`, `.tool`, `.healthPack`, `.item_upgrade`, `.player_upgrade`, `.cart`, `.pocket_cart`, `.power_crystal`, `.vehicle`, `.launcher`, `.grenade`, `.mine`

### PlayerAvatar (Assembly-CSharp)
- `photonView` — PhotonView
- `playerHealth` — PlayerHealth
- `physGrabber` — PhysGrabber

### PlayerHealth (Assembly-CSharp)
- `Death()` — método para patch

### RunManager (Assembly-CSharp)
- `ChangeLevel()` — método para patch de troca de fase

### MainMenuOpen (Assembly-CSharp)
- `Start()` — método para patch de reset ao menu

### SemiFunc (Assembly-CSharp)
- `IsMultiplayer()` — bool
- `IsMasterClientOrSingleplayer()` — bool
- `PhysGrabberLocalIsGrabbing()` — bool
- `PhysGrabberLocalGetGrabbedPhysGrabObject()` — PhysGrabObject
- `PlayerAvatarLocal()` — PlayerAvatar

### OwnershipOption (PhotonUnityNetworking.dll)
- Valores confirmados: `Fixed`, `Takeover`, `Request`

---

## Achados da Fase 2 (reflexão adicional)

### `itemType` é enum privado aninhado em `SemiFunc`
`SemiFunc+itemType` — `IsPublic: false`. Não pode ser referenciado como tipo em assembly externo.
Solução: comparar o valor retornado por `_fItemType.GetValue(attrs)` como `int`.
Valores confirmados: `drone=0`, `orb=1`, `grenade=6`, `melee=7`, `gun=9`, `mine=11`, `launcher=15`.

### Campos de `PhysGrabObject` são private/internal
- `isGun`, `isMelee`, `isEnemy` → INTERNAL
- `itemWidthX`, `itemHeightY`, `itemLengthZ` → PRIVATE
- Todos acessados via `AccessTools.Field()` (FieldInfo cacheado no carregamento do tipo).

### `PhysGrabObject.colliders` é `List<Transform>`, não `List<Collider>`
Inutilizável para cálculo de bounds de física. Fallback de bounds usa `GetComponentsInChildren<Collider>()`.

### `PhysGrabObject.rb` é PUBLIC
Acesso direto a `pgo.rb` em C# — sem reflexão. Usado na Fase 3 para snapshot do Rigidbody.

### `PhysGrabObject.photonView` não é PUBLIC
Acesso via `pgo.gameObject.GetComponent<PhotonView>()` — sempre disponível como componente Unity.

### `ItemAttributes.itemType` é PRIVATE
Acessado via `AccessTools.Field(typeof(ItemAttributes), "itemType").GetValue(attrs)` com cast para `int`.

---

### RepoCompat — camada de compatibilidade centralizada

Todos os `AccessTools.Field()` e `AccessTools.Method()` do mod vivem exclusivamente em `RepoCompat`. `EligibilityChecker` usa os acessores tipados de `RepoCompat` (`GetIsGun`, `GetWidthX`, etc.) — sem `FieldInfo` local espalhado.

`RepoCompat.Initialize()` roda em `QuickPocketPlugin.Awake()` antes de `PatchAll()`. Se qualquer campo crítico estiver ausente, `IsCompatible = false`, `PatchAll()` é pulado e o mod desabilita-se com log estruturado:
```
[QuickPocket] Compatibility failed: missing PhysGrabObject.itemWidthX
```
Métodos de patch são verificados como advisory: falha gera warning mas não desabilita o mod.

### Ownership em TryPocket

- `IsMine == true` → operar diretamente
- `OwnershipTransfer == Fixed` → rejeitar
- `OwnershipTransfer == Request` → rejeitar (callback assíncrono fora do escopo v1.0.0)
- `OwnershipTransfer == Takeover` → `RequestOwnership()` imediato e prosseguir
- Sem PhotonView → rejeitar (sem sync = sem uso seguro)

### Snapshot: ownership exclusivo do cliente que pocketou

Somente o cliente que executou `TryPocket` cria um `ComponentSnapshot` real. Outros clientes usam `HideRemoteItem` (sem snapshot). `RestoreWithSafeDefaults` é o fallback para itens sem snapshot. Nunca chamar `SaveSnapshot` em um item já escondido.

### _remoteHides e _pendingLateJoin

`_remoteHides: Dictionary<int, List<int>>` — actorNumber → ViewIDs ocultos por aquele jogador. Mantido via diff em `ProcessRemotePocketUpdate`.

`_pendingLateJoin: Dictionary<int, float>` — ViewID → tempo de expiração. Itens ainda não instanciados no late join são retentados a cada frame; desiste após `LATE_JOIN_TIMEOUT = 10s`.

### Separação ClearLocalPocket vs ClearAllState

- `ClearLocalPocket(publishToRoom)` — limpa bolso local; mantém estado remoto e limite. Usado em morte e mudança de fase.
- `ClearAllState()` — limpa tudo, reseta `_lateJoinProcessed`. Usado em `OnLeftRoom` e menu principal.

### ProcessLateJoin em Start() e OnJoinedRoom()

Flag `_lateJoinProcessed` evita dupla execução. Cobre tanto o caso em que PocketManager é criado antes quanto depois de entrar na sala.

### OnPlayerLeftRoom — somente master restaura

Apenas o master chama `RestoreWithSafeDefaults` para itens do jogador que saiu e limpa `QP_{actorNumber}` da sala.

---

### REPOConfig — limitação confirmada de sliders sem label (2026-05-26)

**Evidência coletada via análise binária dos DLLs:**
- `MenuLib.dll` contém: `labelTMP`, `labelTemplate`, `CreateREPOSlider`, `CreateREPOLabel`, `GetLabelSize`
- `REPOConfig.dll` contém: `labelTMP`, `CreateREPOSlider`, `CreateREPOToggle`, `CreateREPOLabel`, `TMP_Text`, `get_text`

**Conclusão técnica**: O campo `labelTMP` existe nos dois assemblies. REPOConfig claramente usa `CreateREPOLabel` — porém apenas para toggles. Para sliders (`CreateREPOSlider`), o texto do config entry key não é passado para o `labelTMP` do componente. Confirmado em runtime: toggles exibem label, sliders não.

**Workaround possível?** Não — sem modificar MenuLib ou REPOConfig. Não há API pública exposta que permita injetar texto em sliders existentes após a criação. A limitação é interna ao `MenuLib.REPOSlider`.

**Ação recomendada**: abrir issue no repositório do nickklmao (MenuLib/REPOConfig) com a evidência acima.

---

## Backlog — Fase 5+ (não implementar antes de validação multiplayer)

| Item | Prioridade | Notas |
|---|---|---|
| **AutoGrab real** | Alta | Quando Mouse1 pressionado no unpocket, item deve ser automaticamente grabado. Requer investigação das APIs: `PhysGrabber.TryGrab`, `PhysGrabObject.playerGrabbing`, `PlayerAvatar.physGrabber`. Bloquear Mouse1 é o fallback atual. |
| **Limpeza de logs verbose** | Alta | Remover coroutine `MonitorRestoredObject` (fase diagnóstico), logs de "Enforce SP check", logs de `FindPocketedObject SP`, logs de `Restore t=0`. Manter apenas logs de Info significativos. |
| **Nome limpo no HUD** | Média | Exibir `Valuable Arctic Scale` em vez de `Valuable Arctic Scale(Clone)`. Adicionar helper `CleanName(s)` que remove `(Clone)` e trim. |
| **Contador por valor** | Baixa | Exibir valor total dos itens pocketados no HUD. Requer acesso ao campo de valor do `Valuable` via reflexão. |
| **Estatísticas da run** | Baixa | Items guardados/retirados por run. Persistência em arquivo ou Room Properties. |
| **Efeitos visuais opcionais** | Baixa | Partículas ou flash na câmera ao pocketar/retirar. Config `ShowVFX`. |
| **HideRemoteItem sem Y=-9999** | Baixa | Aplicar o mesmo fix (sem mover Y) para itens de outros jogadores. Requer investigar se REPO tem cleanup por Y para remote items. |
| **REPOConfig slider labels** | Externa | Aguardar fix do nickklmao (MenuLib/REPOConfig). Não tem workaround implementável do lado do QuickPocket. |

---

## Histórico de Fases

- **2026-05-20** — Fase 1: Estrutura criada, configs vinculadas, PocketManager skeleton, Room Properties, HUD "syncing..." / "0/N", patches placeholder. Build e teste pendentes.
- **2026-05-21** — Fase 2: EligibilityChecker com cache por InstanceID, IsSmallItem com maior dimensão, ComponentSnapshot (dados apenas), AccessTools para campos private/internal, itemType como int. Build 0 erros 0 warnings.
- **2026-05-21** — Fase 3: PocketOperations completo (SaveSnapshot, ValidateSnapshot, ApplyHide atômico, RestoreSnapshot, IsPocketStateConsistent), layer por objeto em hierarquia, Y incremental por slot, detectCollisions, TryPocket/TryUnpocket/EnforcePocketState em PocketManager. Build 0 erros 0 warnings.
- **2026-05-25** — Fase 4: RepoCompat centraliza todos os AccessTools com validação de compatibilidade. Multiplayer sync completo: PublishPocketedItems, OnRoomPropertiesUpdate (diff), OnPlayerLeftRoom (master cleanup), IMatchmakingCallbacks (OnJoinedRoom/OnLeftRoom). Late join com retry. Ownership check (Fixed/Request rejeitados, Takeover aceito). HandleDeath/HandleLevelChange/HandleMainMenu com patches reais. Build 0 erros 0 warnings.
- **2026-05-26** — Fix crítico: removido DontDestroyOnLoad. Item permanece no parent/scene original enquanto pocketado. Eliminada destruição em ~0.2s causada por remoção das listas LevelValuables do REPO. Build 0 erros 0 warnings.
- **2026-05-26** — Polish: HUD sempre visível (contador 0/N permanente), mensagens de ação temporárias com fade (Guardado/Retirado/Bolso cheio/Aguarde/Mouse1), 105 frases engraçadas para TooLarge em 3 níveis de ratio, shadow double-alpha corrigido, DEVLOG atualizado com backlog. Build 0 erros 0 warnings.
