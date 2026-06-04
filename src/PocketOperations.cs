using System;
using System.Collections.Generic;
using UnityEngine;

namespace QuickPocket
{
    // =========================================================================
    // PocketOperations — snapshot, hide e restore de itens (somente local).
    // Fase 3: sem ownership, sem Room Properties, sem multiplayer sync.
    //
    // Principios:
    //   - Operacoes atomicas: snapshot completo + validacao ANTES de qualquer mutacao
    //   - Layer salva por objeto (hierarquia inteira), nao apenas root
    //   - Item permanece no parent/scene original enquanto pocketado (sem DontDestroyOnLoad)
    //     Motivo: DontDestroyOnLoad remove o item dos registros de LevelValuables do REPO,
    //     causando destruicao imediata ao restaurar (~0.2s). Manter no parent original
    //     preserva o lifecycle do REPO corretamente.
    //   - Rigidbody: alterar estado apenas, nunca destruir
    //   - IsPocketStateConsistent valida todos os campos relevantes
    // =========================================================================
    internal static class PocketOperations
    {
        // Layer 2 = "Ignore Raycast" (nativa Unity, sem layer customizada)
        private const int LAYER_IGNORE_RAYCAST = 2;

        // =====================================================================
        // SaveSnapshot — captura o estado atual de todos os componentes
        // relevantes. Retorna null se o objeto esta em estado invalido.
        // =====================================================================
        internal static ComponentSnapshot SaveSnapshot(PhysGrabObject pgo, int viewID, int actorNumber)
        {
            if (pgo == null || pgo.gameObject == null) return null;

            var go   = pgo.gameObject;
            var snap = new ComponentSnapshot
            {
                ViewID      = viewID,
                ActorNumber = actorNumber,
                ItemName    = go.name,
            };

            // --- Colliders: hierarquia completa, registrar somente os ativos ---
            var cols    = go.GetComponentsInChildren<Collider>(true);
            var modCols = new List<(Collider, bool)>(cols.Length);
            foreach (var col in cols)
                if (col != null && col.enabled)
                    modCols.Add((col, true));
            snap.ModifiedColliders = modCols.ToArray();

            // --- Renderers: hierarquia completa, registrar somente os ativos ---
            var rends    = go.GetComponentsInChildren<Renderer>(true);
            var modRends = new List<(Renderer, bool)>(rends.Length);
            foreach (var r in rends)
                if (r != null && r.enabled)
                    modRends.Add((r, true));
            snap.ModifiedRenderers = modRends.ToArray();

            // --- Layers: salvar por objeto em toda a hierarquia ---
            var transforms = go.GetComponentsInChildren<Transform>(true);
            var modLayers  = new (GameObject, int)[transforms.Length];
            for (int i = 0; i < transforms.Length; i++)
            {
                var child = transforms[i].gameObject;
                modLayers[i] = (child, child.layer);
            }
            snap.ModifiedLayers = modLayers;

            // --- Rigidbody: pgo.rb e PUBLIC (confirmado via reflexao) ---
            var rb = pgo.rb;
            if (rb != null)
            {
                snap.ModifiedRigidbody        = true;
                snap.OriginalKinematic        = rb.isKinematic;
                snap.OriginalGravity          = rb.useGravity;
                snap.OriginalDetectCollisions = rb.detectCollisions;
            }

            // --- Lights: hierarquia completa, capturar TODAS (inclusive desativadas) ---
            // CRÍTICO: capturar ALL luzes — não apenas as ativas no momento do pocket.
            //
            // Causa raiz do Bug C (luz residual da lanterna):
            //   Componentes como ValuableFlashlight desativam a luz enquanto o item está na
            //   mão do jogador. Ao pocketar (PhysGrabObject.enabled=false), o mesmo componente
            //   detecta a "liberação" e re-ativa a luz. Como ela não estava em ModifiedLights
            //   (estava off no snapshot), IsPocketStateConsistent não a detecta → enforcement
            //   não age → luz permanece acesa no chão durante TODO o pocket.
            //
            // Fix: capturar ALL Light components com seu estado original (on OU off).
            //   - Pocket: desativar TODAS, independente do estado original.
            //   - Restore: restaurar cada uma ao estado original (não forçar on).
            //   - Enforcement: detecta qualquer luz reacesa → ApplyHide → reapaga.
            var lights    = go.GetComponentsInChildren<Light>(true);
            var modLights = new List<(Light, bool)>(lights.Length);
            foreach (var lt in lights)
                if (lt != null)
                    modLights.Add((lt, lt.enabled));  // estado original: true OU false
            snap.ModifiedLights = modLights.ToArray();

            // --- Estado do PhysGrabObject MonoBehaviour ---
            // Necessario desabilitá-lo em ApplyHide para impedir que seu Update()
            // resete detectCollisions/layer/rendereres toda frame (luta contra hide).
            snap.PgoWasEnabled = pgo.enabled;

            // --- AudioSources: capturar ativos ou tocando ---
            var audios    = go.GetComponentsInChildren<AudioSource>(true);
            var modAudios = new List<(AudioSource, bool, bool)>(audios.Length);
            foreach (var src in audios)
                if (src != null && (src.enabled || src.isPlaying))
                    modAudios.Add((src, src.enabled, src.isPlaying));
            snap.ModifiedAudioSources = modAudios.ToArray();

            // --- ParticleSystems: capturar emitindo ---
            var particles    = go.GetComponentsInChildren<ParticleSystem>(true);
            var modParticles = new List<(ParticleSystem, bool)>(particles.Length);
            foreach (var ps in particles)
                if (ps != null && ps.isEmitting)
                    modParticles.Add((ps, true));
            snap.ModifiedParticleSystems = modParticles.ToArray();

            // --- TrailRenderers: capturar para Clear() no hide ---
            // Já desabilitados pelo loop de Renderers (TrailRenderer : Renderer),
            // mas o trail data persiste até Clear() ser chamado explicitamente.
            var trails    = go.GetComponentsInChildren<TrailRenderer>(true);
            var modTrails = new List<TrailRenderer>(trails.Length);
            foreach (var tr in trails)
                if (tr != null) modTrails.Add(tr);
            snap.ModifiedTrailRenderers = modTrails.ToArray();

            // --- MapValuable: encontrar o marker separado no Map system ---
            // MapValuable é um objeto na hierarquia do Map (não filho do item).
            // Identificado por MapValuable.target == ValuableObject do item.
            // Busca barata: só acontece no momento do pocket, não a cada frame.
            var valuableObj = go.GetComponent<ValuableObject>();
            if (valuableObj != null)
            {
                // FindObjectsOfType com includeInactive=true para capturar markers que o
                // próprio jogo já desativou (ex: item fora de alcance visual do Map system).
                foreach (var mv in UnityEngine.Object.FindObjectsOfType<MapValuable>(true))
                {
                    if (mv != null && mv.target == valuableObj)
                    {
                        snap.MapValuableMarker = mv;

                        // Cache do SpriteRenderer — usado para hide/restore sem SetActive.
                        // SetActive(false) mataria a coroutine Logic() e Start() não reinicia.
                        // Desabilitar apenas o SpriteRenderer mantém Logic() rodando.
                        var srField = HarmonyLib.AccessTools.Field(typeof(MapValuable), "spriteRenderer");
                        if (srField != null)
                            snap.MapValuableSpriteRenderer = srField.GetValue(mv) as SpriteRenderer;

                        QuickPocketPlugin.Log.LogDebug(
                            "[QP:MapMarker] item=" + snap.ItemName +
                            " method=MapValuable markerFound=True" +
                            " spriteRenderer=" + (snap.MapValuableSpriteRenderer != null ? "found" : "null"));
                        break;
                    }
                }
                if (snap.MapValuableMarker == null)
                    QuickPocketPlugin.Log.LogDebug(
                        "[QP:MapMarker] item=" + snap.ItemName + " markerFound=False");
            }

            // --- Behaviours de mapa/minimap/discovery + lógica visual de item ---
            // Capturar e desabilitar comportamentos que:
            //   a) Controlam indicadores de mapa/minimap (Map, Discover, Marker, etc.)
            //   b) Têm Update() que pode religar efeitos visuais (Boombox, Musical, Trap)
            //      Confirmado via reflection: ValuableBoombox tem light1/light2 e Update().
            //      MusicalValuableLogic tem Update(). Ambos podem re-ativar efeitos visuais.
            var allBehaviours = go.GetComponentsInChildren<Behaviour>(true);
            var modMap        = new List<(Behaviour, bool)>(4);
            foreach (var b in allBehaviours)
            {
                if (b == null || !b.enabled) continue;
                string tn = b.GetType().Name;
                if (tn.IndexOf("Map",       StringComparison.OrdinalIgnoreCase) >= 0 ||
                    tn.IndexOf("Minimap",   StringComparison.OrdinalIgnoreCase) >= 0 ||
                    tn.IndexOf("Radar",     StringComparison.OrdinalIgnoreCase) >= 0 ||
                    tn.IndexOf("Discover",  StringComparison.OrdinalIgnoreCase) >= 0 ||
                    tn.IndexOf("Blip",      StringComparison.OrdinalIgnoreCase) >= 0 ||
                    tn.IndexOf("Marker",    StringComparison.OrdinalIgnoreCase) >= 0 ||
                    tn.IndexOf("Tracker",   StringComparison.OrdinalIgnoreCase) >= 0 ||
                    tn.IndexOf("MapIcon",   StringComparison.OrdinalIgnoreCase) >= 0 ||
                    tn.IndexOf("ItemIcon",  StringComparison.OrdinalIgnoreCase) >= 0 ||
                    // Lógica visual de itens — podem religar luzes/efeitos em Update()
                    tn.IndexOf("Boombox",   StringComparison.OrdinalIgnoreCase) >= 0 ||
                    tn.IndexOf("Musical",   StringComparison.OrdinalIgnoreCase) >= 0 ||
                    tn.IndexOf("Glow",      StringComparison.OrdinalIgnoreCase) >= 0 ||
                    tn.IndexOf("Flash",     StringComparison.OrdinalIgnoreCase) >= 0 ||
                    tn.IndexOf("Blink",     StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    modMap.Add((b, true));
                }
            }
            snap.ModifiedMapBehaviours = modMap.ToArray();

            return snap;
        }

        // =====================================================================
        // ValidateSnapshot — verifica que o snapshot esta completo e coerente.
        // Deve ser chamado ANTES de ApplyHide. Retorna false se invalido.
        // =====================================================================
        internal static bool ValidateSnapshot(ComponentSnapshot snap)
        {
            if (snap == null)
            {
                QuickPocketPlugin.Log.LogInfo("[QuickPocket] Snapshot e nulo.");
                return false;
            }
            if (snap.ModifiedColliders == null)
            {
                QuickPocketPlugin.Log.LogInfo("[QuickPocket] Snapshot sem colliders: " + snap.ItemName);
                return false;
            }
            if (snap.ModifiedRenderers == null)
            {
                QuickPocketPlugin.Log.LogInfo("[QuickPocket] Snapshot sem renderers: " + snap.ItemName);
                return false;
            }
            if (snap.ModifiedLayers == null || snap.ModifiedLayers.Length == 0)
            {
                QuickPocketPlugin.Log.LogInfo("[QuickPocket] Snapshot sem layers: " + snap.ItemName);
                return false;
            }
            return true;
        }

        // =====================================================================
        // ApplyHide — esconde o item localmente.
        // ATOMICA: valida snapshot completo antes de qualquer mutacao.
        // O item permanece no parent/scene original — sem DontDestroyOnLoad.
        // Invisibilidade garantida por: PhysGrabObject desabilitado + kinematic
        // + colliders/renderers off + layer IgnoreRaycast.
        // =====================================================================
        internal static bool ApplyHide(GameObject go, ComponentSnapshot snap)
        {
            // Validacao atomica: so prosseguir se snapshot esta completo
            if (!ValidateSnapshot(snap))
            {
                QuickPocketPlugin.Log.LogInfo("[QuickPocket] ApplyHide cancelado: snapshot invalido.");
                return false;
            }

            if (go == null)
            {
                QuickPocketPlugin.Log.LogInfo("[QuickPocket] ApplyHide cancelado: GameObject nulo.");
                return false;
            }

            // --- A partir daqui: todas as mutacoes sao aplicadas ---

            // 1. Desabilitar PhysGrabObject MonoBehaviour PRIMEIRO.
            //    Motivo: PhysGrabObject.Update() reseta detectCollisions, layer e
            //    tenta aplicar velocidade ao rigidbody toda frame, lutando contra o hide.
            //    Desabilitar o componente para o Update() antes de qualquer mutacao.
            var pgoComp = go.GetComponent<PhysGrabObject>();
            if (pgoComp != null)
                pgoComp.enabled = false;

            // 2. Zerar fisica ANTES de setar kinematic (evita estado residual).
            //    Nota: se rb ja for kinematic, setar velocity gera warning Unity — ignorar.
            var rb = go.GetComponent<Rigidbody>();
            if (rb != null)
            {
                if (!rb.isKinematic)
                {
                    rb.velocity        = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                rb.isKinematic      = true;
                rb.useGravity       = false;
                rb.detectCollisions = false;
            }

            // 3. Desativar colliders registrados no snapshot
            foreach (var (col, _) in snap.ModifiedColliders)
                if (col != null) col.enabled = false;

            // 4. Desativar renderers registrados no snapshot
            foreach (var (rend, _) in snap.ModifiedRenderers)
                if (rend != null) rend.enabled = false;

            // 4b. Desativar luzes registradas no snapshot
            if (snap.ModifiedLights != null)
                foreach (var (lt, _) in snap.ModifiedLights)
                    if (lt != null) lt.enabled = false;

            // 4c. AudioSources — parar e desabilitar para suprimir som residual
            if (snap.ModifiedAudioSources != null)
                foreach (var (src, _, _) in snap.ModifiedAudioSources)
                    if (src != null) { src.Stop(); src.enabled = false; }

            // 4d. ParticleSystems — parar emissão e limpar partículas existentes
            if (snap.ModifiedParticleSystems != null)
                foreach (var (ps, _) in snap.ModifiedParticleSystems)
                    if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            // 4e. TrailRenderers — limpar dados do rastro (enabled=false já feito em 4)
            if (snap.ModifiedTrailRenderers != null)
                foreach (var tr in snap.ModifiedTrailRenderers)
                    if (tr != null) tr.Clear();

            // 4f. MapValuable — ocultar o ponto dourado no TAB/minimap nativo.
            // CRÍTICO: NÃO usar gameObject.SetActive(false) — isso mata a coroutine Logic().
            // Start() só roda uma vez; SetActive(true) chama OnEnable() mas não reinicia Logic().
            // Resultado: marker congelado na posição original após drop (Bug B confirmado via IL).
            // Solução: desabilitar apenas o SpriteRenderer — Logic() continua rastreando o item.
            if (snap.MapValuableSpriteRenderer != null)
            {
                snap.MapValuableSpriteRenderer.enabled = false;
                QuickPocketPlugin.Log.LogDebug(
                    "[QP:MapMarker] item=" + snap.ItemName + " spriteDisabled=True");
            }
            else if (snap.MapValuableMarker != null)
            {
                // Fallback: spriteRenderer não encontrado — usar SetActive como último recurso.
                // Neste caso Bug B pode persistir, mas é melhor que deixar marker visível.
                snap.MapValuableMarker.gameObject.SetActive(false);
                QuickPocketPlugin.Log.LogDebug(
                    "[QP:MapMarker] item=" + snap.ItemName + " disabled=True (fallback SetActive)");
            }

            // 4h. Behaviours de mapa/minimap/discovery/lógica visual — desabilitar
            int mapDisabled = 0;
            if (snap.ModifiedMapBehaviours != null)
            {
                foreach (var (comp, _) in snap.ModifiedMapBehaviours)
                    if (comp != null) { comp.enabled = false; mapDisabled++; }
            }

            // Log completo de hide — diagnóstico de componentes capturados
            QuickPocketPlugin.Log.LogDebug(
                "[QP:HideComponents] item=" + snap.ItemName +
                " lights=" + (snap.ModifiedLights?.Length ?? 0) +
                " audio=" + (snap.ModifiedAudioSources?.Length ?? 0) +
                " particles=" + (snap.ModifiedParticleSystems?.Length ?? 0) +
                " trails=" + (snap.ModifiedTrailRenderers?.Length ?? 0) +
                " behaviours=" + (snap.ModifiedMapBehaviours?.Length ?? 0) +
                " behavioursDisabled=" + mapDisabled);

            // 5. Setar layer Ignore Raycast por objeto (hierarquia completa)
            foreach (var (obj, _) in snap.ModifiedLayers)
                if (obj != null) obj.layer = LAYER_IGNORE_RAYCAST;

            // Nao reparentamos nem movemos o item — ele permanece no parent/scene original.
            // Motivo: DontDestroyOnLoad remove o item de LevelValuables, causando destruicao
            // ao restaurar. A invisibilidade e garantida por: PhysGrabObject desabilitado
            // (impede Update() de lutar contra o hide) + kinematic + colliders/renderers off
            // + layer IgnoreRaycast (ignorado por todos os raycasts e pelo PhysGrabber).

            return true;
        }

        // =====================================================================
        // RestoreSnapshot — restaura o item ao estado exato salvo no snapshot.
        // targetPosition: posicao no mundo onde o item vai aparecer.
        // =====================================================================
        internal static void RestoreSnapshot(GameObject go, ComponentSnapshot snap, Vector3 targetPosition)
        {
            if (go == null || snap == null) return;

            // Nao precisamos reparentar — o item nunca saiu do parent/scene original.

            // 1. Restaurar layer por objeto em toda a hierarquia
            foreach (var (obj, origLayer) in snap.ModifiedLayers)
                if (obj != null) obj.layer = origLayer;

            // 2. Restaurar colliders ao estado original exato
            foreach (var (col, origEnabled) in snap.ModifiedColliders)
                if (col != null) col.enabled = origEnabled;

            // 3. Restaurar renderers ao estado original exato
            foreach (var (rend, origEnabled) in snap.ModifiedRenderers)
                if (rend != null) rend.enabled = origEnabled;

            // 4. Restaurar luzes
            if (snap.ModifiedLights != null)
                foreach (var (lt, origEnabled) in snap.ModifiedLights)
                    if (lt != null) lt.enabled = origEnabled;

            // 4b. Restaurar AudioSources
            if (snap.ModifiedAudioSources != null)
                foreach (var (src, wasEnabled, wasPlaying) in snap.ModifiedAudioSources)
                    if (src != null)
                    {
                        src.enabled = wasEnabled;
                        // Replay apenas se era loop — one-shots nao precisam de replay forcado
                        if (wasEnabled && wasPlaying && src.loop) src.Play();
                    }

            // 4c. Restaurar ParticleSystems (somente os que estavam emitindo)
            if (snap.ModifiedParticleSystems != null)
                foreach (var (ps, _) in snap.ModifiedParticleSystems)
                    if (ps != null) ps.Play();

            // 4d. MapValuable: NÃO reativar aqui.
            // O marker será reativado APÓS o item ser posicionado (passo 8 abaixo).
            // Motivo: MapValuable.Logic() (coroutine) lê target.transform.position no Start().
            // Se reativado antes do item ser movido, o marker aponta para a posição antiga.

            // 4e. Restaurar Behaviours de mapa/minimap (fallback genérico)
            if (snap.ModifiedMapBehaviours != null)
                foreach (var (comp, wasEnabled) in snap.ModifiedMapBehaviours)
                    if (comp != null) comp.enabled = wasEnabled;

            // 5. Restaurar Rigidbody — CRITICO: rb.position deve ser setado ENQUANTO
            //    AINDA KINEMATIC, antes de isKinematic=false.
            //
            //    Motivo: ApplyHide seta rb.detectCollisions=false, o que "gela" a posicao
            //    interna do physics engine (rb.position) no ponto original do bolso.
            //    Setar apenas transform.position nao atualiza rb.position — quando
            //    isKinematic=false e chamado, o physics engine inicializa o corpo dinamico
            //    a partir do rb.position gelado, teleportando o item de volta a origem.
            //
            //    Sequencia correta:
            //      a) isKinematic=true  (ja esta — ApplyHide garantiu isso)
            //      b) transform.position = target  (move transform)
            //      c) rb.position = target          (sincroniza physics engine — quebra o gelo)
            //      d) isKinematic=false             (agora o body dinamico parte do target)
            var rb = go.GetComponent<Rigidbody>();
            if (rb != null && snap.ModifiedRigidbody)
            {
                // a) garantir kinematic (deveria ja estar, mas defensivo)
                rb.isKinematic = true;

                // b+c) sincronizar transform E physics engine para targetPosition
                go.transform.position = targetPosition;
                rb.position           = targetPosition;

                // d) liberar para fisica dinamica
                rb.isKinematic      = false;
                rb.useGravity       = true;
                rb.detectCollisions = true;
                rb.velocity         = Vector3.zero;
                rb.angularVelocity  = Vector3.zero;
            }
            else
            {
                // Sem Rigidbody: apenas posicionar via transform
                go.transform.position = targetPosition;
            }

            // 6. Reativar PhysGrabObject ULTIMO.
            //    PhysGrabObject.Update() pode sobrescrever posicao/velocidade no
            //    primeiro frame apos reativacao. Por isso forçamos posicao+velocity
            //    imediatamente apos reativar.
            var pgoComp = go.GetComponent<PhysGrabObject>();
            if (pgoComp != null)
                pgoComp.enabled = snap.PgoWasEnabled;

            // 7. Segundo set de posicao apos PhysGrabObject reativado.
            //    Garante que mesmo que PGO.OnEnable() ou Update() tenham rodado
            //    no mesmo frame, o item ainda parte de targetPosition.
            go.transform.position = targetPosition;
            if (rb != null)
            {
                rb.position        = targetPosition;
                rb.velocity        = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            // 8. Reativar SpriteRenderer do MapValuable — marker volta a ser visível.
            //    Logic() coroutine nunca foi interrompida (não usamos SetActive(false)),
            //    portanto o marker já está na posição correta (Logic() rastreia continuamente).
            //    Posição do item em targetPosition (steps 5-7) antes desta chamada — correto.
            if (snap.MapValuableSpriteRenderer != null)
            {
                snap.MapValuableSpriteRenderer.enabled = true;
                QuickPocketPlugin.Log.LogDebug(
                    "[QP:MapMarker] item=" + snap.ItemName +
                    " spriteRestored=True method=SpriteRenderer" +
                    " targetPos=" + (snap.MapValuableMarker != null && snap.MapValuableMarker.target != null
                        ? snap.MapValuableMarker.target.transform.position.ToString("F2")
                        : "n/a"));
            }
            else if (snap.MapValuableMarker != null && snap.MapValuableMarker.gameObject != null)
            {
                // Fallback: SpriteRenderer não cacheado — tentar SetActive como último recurso.
                // Bug B pode persistir neste caminho, mas evita marker invisível permanente.
                bool targetAlive = snap.MapValuableMarker.target != null;
                if (targetAlive)
                {
                    snap.MapValuableMarker.gameObject.SetActive(true);
                    QuickPocketPlugin.Log.LogDebug(
                        "[QP:MapMarker] item=" + snap.ItemName + " restore=True method=SetActive (fallback)");
                }
                else
                {
                    QuickPocketPlugin.Log.LogDebug(
                        "[QP:MapMarker] item=" + snap.ItemName + " restore=Skip reason=TargetDestroyed");
                }
            }
        }

        // =====================================================================
        // IsPocketStateConsistent — verifica se o item esta corretamente
        // escondido. Valida todos os campos relevantes.
        // Retorna false se qualquer condicao nao estiver satisfeita.
        // =====================================================================
        internal static bool IsPocketStateConsistent(GameObject go, ComponentSnapshot snap)
        {
            if (go == null || snap == null) return false;

            // Objeto ainda existe?
            if (go == null) return false;  // destruido

            // Layer do root
            if (go.layer != LAYER_IGNORE_RAYCAST) return false;

            // Layers de toda a hierarquia
            foreach (var (obj, _) in snap.ModifiedLayers)
            {
                if (obj == null) continue;
                if (obj.layer != LAYER_IGNORE_RAYCAST) return false;
            }

            // Renderers desativados
            foreach (var (rend, _) in snap.ModifiedRenderers)
            {
                if (rend == null) continue;
                if (rend.enabled) return false;
            }

            // Colliders desativados
            foreach (var (col, _) in snap.ModifiedColliders)
            {
                if (col == null) continue;
                if (col.enabled) return false;
            }

            // Luzes desativadas (itens com luz nao devem emitir luz enquanto pocketados)
            if (snap.ModifiedLights != null)
            {
                foreach (var (lt, _) in snap.ModifiedLights)
                {
                    if (lt == null) continue;
                    if (lt.enabled) return false;
                }
            }

            // Rigidbody: kinematic + detectCollisions desativado
            var rb = go.GetComponent<Rigidbody>();
            if (rb != null)
            {
                if (!rb.isKinematic)     return false;
                if (rb.detectCollisions) return false;
            }

            // AudioSources: desativados e parados
            if (snap.ModifiedAudioSources != null)
                foreach (var (src, _, _) in snap.ModifiedAudioSources)
                    if (src != null && (src.enabled || src.isPlaying)) return false;

            // Map behaviours: desativados
            if (snap.ModifiedMapBehaviours != null)
                foreach (var (comp, _) in snap.ModifiedMapBehaviours)
                    if (comp != null && comp.enabled) return false;

            return true;
        }
    }
}
