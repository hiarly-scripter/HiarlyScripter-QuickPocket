# QuickPocket

**Português** | [English](#english)

Guarde itens pequenos no bolso com uma tecla (padrão **H**) e solte-os quando precisar. Tem HUD contador, mensagens com humor e suporte a multiplayer (o host manda no limite de slots).

## Recursos
- Guardar e soltar itens pequenos com `H` (configurável).
- HUD com a contagem do bolso (some sozinho ou fica fixo).
- Multiplayer sincronizado: os outros jogadores veem o item sumir e voltar, e o marcador do mapa/TAB também some.
- Solta tudo ao morrer e ao trocar de fase (configurável).
- Itens perigosos (Saw/Blade/Hazard) são bloqueados de propósito, por segurança.

## Configuração
Arquivo em `BepInEx/config` ou pelo REPOConfig: `PocketKey`, `MaxPocketSlots`, `MaxItemBoundsSize`, `PocketCooldown`, restrições (armas, corpo a corpo, orbes, drones), `UseHostLimit`, `HostMaxOverride`, `SyncPocketState`, HUD e comportamento (`DropOnDeath`, `DropOnLevelChange`).

## Multiplayer
Todos devem ter o mod na mesma versão. O host define o limite de slots.

## Compatibilidade
R.E.P.O. v0.5.1 (Unity 6). Se o jogo atualizar, o log do BepInEx mostra `RepoCompat OK` ou indica o que quebrou.

Código-fonte: https://github.com/hiarly-scripter/HiarlyScripter-QuickPocket

---

<a name="english"></a>
## English

Pocket small items with a key (default **H**) and drop them when needed. Includes a counter HUD, humorous messages and multiplayer support (the host controls the slot limit).

### Features
- Pocket and drop small items with `H` (configurable).
- HUD showing the pocket count (fades or stays visible).
- Synced multiplayer: other players see the item vanish and return, and the map/TAB marker is hidden too.
- Drops everything on death and on level change (configurable).
- Dangerous items (Saw/Blade/Hazard) are intentionally blocked for safety.

### Configuration
Config file in `BepInEx/config` or through REPOConfig: `PocketKey`, `MaxPocketSlots`, `MaxItemBoundsSize`, `PocketCooldown`, restrictions (weapons, melee, orbs, drones), `UseHostLimit`, `HostMaxOverride`, `SyncPocketState`, HUD and behavior (`DropOnDeath`, `DropOnLevelChange`).

### Multiplayer
Everyone should run the same mod version. The host sets the slot limit.

### Compatibility
R.E.P.O. v0.5.1 (Unity 6). After a game update, the BepInEx log shows `RepoCompat OK` or what broke.

Source: https://github.com/hiarly-scripter/HiarlyScripter-QuickPocket
