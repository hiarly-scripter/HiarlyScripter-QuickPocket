# Checkpoint — Deploy & Version Integrity
## Verificação de integridade pós-deploy v1.0.5
**Data:** 2026-06-01
**Status:** VERIFICADO — deploy íntegro, todas as DLLs idênticas

---

## Por que o log mostrou v1.0.4

O LogOutput.log apresentado como evidência foi gerado **antes** do deploy de v1.0.5:
- v1.0.5 foi buildada e deployada em 31/mai às 15:20-15:21
- O log de "v1.0.4" veio de um teste anterior feito com REPO - Test (antes do deploy)
- v1.0.5 **nunca chegou a ser testada** — o log de teste era da sessão anterior

**Não houve falha de deploy para REPO - Test.** A DLL estava correta.

---

## Causa secundária descoberta

O perfil **Default** tinha uma DLL antiga (51.712 bytes, 26/mai/19:47, versão 1.0.0).  
Nunca foi atualizado nas rodadas anteriores — apenas REPO - Test era atualizado.  
→ **Corrigido:** Default agora tem a mesma DLL que REPO - Test.

---

## Verificação de versão no código

```
src/Core.cs linha 19:  [BepInPlugin("com.hiarlyscripter.quickpocket", "QuickPocket", "1.0.5")]
src/Core.cs linha 76:  Log.LogInfo("[QuickPocket] v1.0.5 inicializado. Aguardando jogador...")
```

Ambas as strings batem. Nenhuma constante de versão extra encontrada.

---

## Hashes SHA-256 — prova de integridade

| DLL | Bytes | Data | SHA-256 |
|---|---|---|---|
| Build | 66.560 | 31/mai 15:20 | `35692ffd96d90f13fac49a990cdd9ebb4b6d11f808907bbb81702a6f4b6da6e2` |
| REPO - Test | 66.560 | 31/mai 15:21 | `35692ffd96d90f13fac49a990cdd9ebb4b6d11f808907bbb81702a6f4b6da6e2` ✓ |
| Default (atualizado) | 66.560 | 01/jun 20:48 | `35692ffd96d90f13fac49a990cdd9ebb4b6d11f808907bbb81702a6f4b6da6e2` ✓ |

**STATUS: TODAS IDÊNTICAS.**

---

## Caminhos verificados

```
Build:
  C:\Users\Hiarly\.claude\PROJETOS\REPO\HiarlyScripter-QuickPocket\build\HiarlyScripter-QuickPocket.dll

REPO - Test (perfil de teste):
  C:\Users\Hiarly\AppData\Roaming\r2modmanPlus-local\REPO\profiles\REPO - Test\BepInEx\plugins\HiarlyScripter-QuickPocket\HiarlyScripter-QuickPocket.dll

Default (perfil atualizado nesta rodada):
  C:\Users\Hiarly\AppData\Roaming\r2modmanPlus-local\REPO\profiles\Default\BepInEx\plugins\HiarlyScripter-QuickPocket\HiarlyScripter-QuickPocket.dll
```

---

## DLLs duplicadas encontradas

Apenas 2 instâncias da DLL (um por perfil r2modman). Sem duplicatas dentro de um mesmo perfil. Sem cópias no diretório do jogo `E:\SteamLibrary\steamapps\common\REPO`.

---

## O que o próximo LogOutput DEVE mostrar

Independente do perfil usado (Default ou REPO - Test), o jogo deve mostrar:

```
[Info   :   BepInEx] Loading [QuickPocket 1.0.5]
[Info   :QuickPocket] [QuickPocket] v1.0.5 inicializado. Aguardando jogador...
```

Se ainda aparecer v1.0.4 ou outra versão antiga, o problema está no r2modman escolhendo o perfil errado ou cache de montagem do Windows.

---

## Deploy futuro — procedimento correto

A partir de agora, todo deploy deve atualizar **ambos os perfis**:

```bash
# REPO - Test (perfil de teste):
cp build/HiarlyScripter-QuickPocket.dll \
   "%APPDATA%\r2modmanPlus-local\REPO\profiles\REPO - Test\BepInEx\plugins\HiarlyScripter-QuickPocket\"

# Default (perfil secundário):
cp build/HiarlyScripter-QuickPocket.dll \
   "%APPDATA%\r2modmanPlus-local\REPO\profiles\Default\BepInEx\plugins\HiarlyScripter-QuickPocket\"
```

---

## Próxima etapa

Testar v1.0.5 com qualquer perfil (Default ou REPO - Test) — ambos têm a mesma DLL.

Funcionalidades para validar no próximo teste:
- HUD OFF no loading (singleton guard + LoadingUI)
- Marker TAB some ao pocketar item (MapValuable.SetActive)
- Áudio/luz param no pocket
- Player 2 consegue pocketar (sem OwnershipFixed)
