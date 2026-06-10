using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace QuickPocket
{
    internal enum HideMode
    {
        Local,
        HostHosted,
        OwnerAck,
        Remote,
        LateJoin,
        Cleanup
    }

    internal enum RestoreMode
    {
        Local,
        HostHosted,
        OwnerAck,
        Remote,
        SafeDefaults,
        Cleanup
    }

    // =========================================================================
    // PocketOperations - snapshot + unified hide/restore state.
    //
    // The pocketed state is intentionally local to every client. Photon does not
    // synchronize component.enabled, AudioSource state, MapValuable sprite state,
    // or arbitrary damage/audio behaviours, so every path must call this layer.
    // =========================================================================
    internal static class PocketOperations
    {
        private const int LAYER_IGNORE_RAYCAST = 2;

        private static FieldInfo _mapValuableSpriteRendererField;
        private static bool _mapValuableSpriteRendererChecked;

        internal static ComponentSnapshot SaveSnapshot(PhysGrabObject pgo, int viewID, int actorNumber)
        {
            if (pgo == null || pgo.gameObject == null) return null;

            var go = pgo.gameObject;
            var snap = new ComponentSnapshot
            {
                ViewID = viewID,
                ActorNumber = actorNumber,
                ItemName = go.name,
            };

            snap.ModifiedColliders = BuildColliderSnapshot(go);
            snap.ModifiedRenderers = BuildRendererSnapshot(go);

            var transforms = go.GetComponentsInChildren<Transform>(true);
            var layers = new (GameObject, int)[transforms.Length];
            for (int i = 0; i < transforms.Length; i++)
            {
                var child = transforms[i].gameObject;
                layers[i] = (child, child.layer);
            }
            snap.ModifiedLayers = layers;

            var rb = pgo.rb != null ? pgo.rb : go.GetComponent<Rigidbody>();
            if (rb != null)
            {
                snap.ModifiedRigidbody = true;
                snap.OriginalKinematic = rb.isKinematic;
                snap.OriginalGravity = rb.useGravity;
                snap.OriginalDetectCollisions = rb.detectCollisions;
            }

            snap.PgoWasEnabled = pgo.enabled;
            snap.ModifiedLights = BuildLightSnapshot(go);
            snap.ModifiedAudioSources = BuildAudioSnapshot(go);
            snap.ModifiedParticleSystems = BuildParticleSnapshot(go);
            snap.ModifiedTrailRenderers = go.GetComponentsInChildren<TrailRenderer>(true);
            snap.ModifiedLineRenderers = go.GetComponentsInChildren<LineRenderer>(true);
            snap.ModifiedMapBehaviours = BuildSuppressedBehaviourSnapshot(go);

            FindMapMarker(go, out snap.MapValuableMarker, out snap.MapValuableSpriteRenderer);
            QuickPocketPlugin.Log.LogDebug(
                "[QP:MapMarker] item=" + snap.ItemName +
                " markerFound=" + (snap.MapValuableMarker != null) +
                " spriteRenderer=" + (snap.MapValuableSpriteRenderer != null ? "found" : "null"));

            return snap;
        }

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

        internal static bool ApplyHide(GameObject go, ComponentSnapshot snap)
        {
            int viewID = snap != null ? snap.ViewID : 0;
            int actor = snap != null ? snap.ActorNumber : 0;
            return HidePocketedItemState(go, viewID, actor, HideMode.Local, "ApplyHide", snap);
        }

        internal static void RestoreSnapshot(GameObject go, ComponentSnapshot snap, Vector3 targetPosition)
        {
            int viewID = snap != null ? snap.ViewID : 0;
            int actor = snap != null ? snap.ActorNumber : 0;
            RestorePocketedItemState(go, viewID, actor, RestoreMode.Local, targetPosition, "RestoreSnapshot", snap);
        }

        internal static bool HidePocketedItemState(
            GameObject go, int viewID, int ownerActor, HideMode mode, string reason, ComponentSnapshot snap = null)
        {
            if (go == null)
            {
                QuickPocketPlugin.Log.LogInfo("[QuickPocket] Hide cancelado: GameObject nulo.");
                return false;
            }

            if (snap != null && !ValidateSnapshot(snap))
            {
                QuickPocketPlugin.Log.LogInfo("[QuickPocket] Hide cancelado: snapshot invalido.");
                return false;
            }

            string itemName = snap != null ? snap.ItemName : go.name;

            var pgoComp = go.GetComponent<PhysGrabObject>();
            if (pgoComp != null) pgoComp.enabled = false;

            var rb = go.GetComponent<Rigidbody>();
            if (rb != null)
            {
                if (!rb.isKinematic)
                {
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.detectCollisions = false;
            }

            int colliders = 0;
            int collidersDisabled = 0;
            int triggers = 0;
            var colliderSnapshot = snap != null && snap.ModifiedColliders != null
                ? snap.ModifiedColliders
                : BuildColliderSnapshot(go);
            foreach (var (col, _) in colliderSnapshot)
            {
                if (col == null) continue;
                colliders++;
                if (col.isTrigger) triggers++;
                if (col.enabled) collidersDisabled++;
                col.enabled = false;
            }

            int renderers = 0;
            int renderersDisabled = 0;
            var rendererSnapshot = snap != null && snap.ModifiedRenderers != null
                ? snap.ModifiedRenderers
                : BuildRendererSnapshot(go);
            foreach (var (rend, _) in rendererSnapshot)
            {
                if (rend == null) continue;
                renderers++;
                if (rend.enabled) renderersDisabled++;
                rend.enabled = false;
            }

            var lightSnapshot = snap != null && snap.ModifiedLights != null
                ? snap.ModifiedLights
                : BuildLightSnapshot(go);
            foreach (var (lt, _) in lightSnapshot)
                if (lt != null) lt.enabled = false;

            int audioSources = 0;
            int audioStopped = 0;
            int audioMuted = 0;
            var audioSnapshot = snap != null && snap.ModifiedAudioSources != null
                ? snap.ModifiedAudioSources
                : BuildAudioSnapshot(go);
            foreach (var (src, _, _, _, _) in audioSnapshot)
            {
                if (src == null) continue;
                audioSources++;
                if (src.isPlaying)
                {
                    src.Stop();
                    audioStopped++;
                }
                if (!src.enabled) src.enabled = true;
                if (!src.mute || src.volume != 0f) audioMuted++;
                src.mute = true;
                src.volume = 0f;
            }

            var particleSnapshot = snap != null && snap.ModifiedParticleSystems != null
                ? snap.ModifiedParticleSystems
                : BuildParticleSnapshot(go);
            foreach (var (ps, _) in particleSnapshot)
                if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var trails = snap != null && snap.ModifiedTrailRenderers != null
                ? snap.ModifiedTrailRenderers
                : go.GetComponentsInChildren<TrailRenderer>(true);
            foreach (var tr in trails)
                if (tr != null) tr.Clear();

            int behavioursDisabled = 0;
            int damageBehaviours = 0;
            var behaviourSnapshot = snap != null && snap.ModifiedMapBehaviours != null
                ? snap.ModifiedMapBehaviours
                : BuildSuppressedBehaviourSnapshot(go);
            foreach (var (comp, _) in behaviourSnapshot)
            {
                if (comp == null) continue;
                if (IsDamageOrHazardBehaviour(comp)) damageBehaviours++;
                if (comp.enabled)
                {
                    comp.enabled = false;
                    behavioursDisabled++;
                }
            }

            bool markerHidden = SetMapMarkerVisible(
                go, snap, viewID, ownerActor, visible: false, finalPos: null,
                side: MarkerSide(mode), reason: reason);

            var layers = snap != null && snap.ModifiedLayers != null
                ? snap.ModifiedLayers
                : BuildLayerSnapshot(go);
            foreach (var (obj, _) in layers)
                if (obj != null) obj.layer = LAYER_IGNORE_RAYCAST;

            QuickPocketPlugin.Log.LogDebug(
                "[QP:AudioHide] viewID=" + viewID +
                " audioSources=" + audioSources +
                " stopped=" + audioStopped +
                " muted=" + audioMuted +
                " behavioursDisabled=" + behavioursDisabled);

            QuickPocketPlugin.Log.LogDebug(
                "[QP:HideState] viewID=" + viewID +
                " item=" + itemName +
                " mode=" + mode +
                " reason=" + reason +
                " renderers=" + renderers +
                " renderersDisabled=" + renderersDisabled +
                " colliders=" + colliders +
                " disabled=" + collidersDisabled +
                " triggers=" + triggers +
                " rbDetect=" + (rb != null ? rb.detectCollisions.ToString() : "none") +
                " rbKinematic=" + (rb != null ? rb.isKinematic.ToString() : "none") +
                " damageBehaviours=" + damageBehaviours +
                " audio=" + audioSources +
                " marker=" + markerHidden);

            return true;
        }

        internal static void RestorePocketedItemState(
            GameObject go, int viewID, int ownerActor, RestoreMode mode, Vector3? finalPos,
            string reason, ComponentSnapshot snap = null)
        {
            if (go == null) return;

            string itemName = snap != null ? snap.ItemName : go.name;
            int collidersRestored = 0;
            int audioRestored = 0;

            if (snap != null)
            {
                if (snap.ModifiedLayers != null)
                    foreach (var (obj, origLayer) in snap.ModifiedLayers)
                        if (obj != null) obj.layer = origLayer;

                var rb = go.GetComponent<Rigidbody>();
                if (rb != null && snap.ModifiedRigidbody)
                {
                    rb.isKinematic = true;

                    if (finalPos.HasValue)
                    {
                        go.transform.position = finalPos.Value;
                        rb.position = finalPos.Value;
                    }

                    rb.isKinematic = snap.OriginalKinematic;
                    rb.useGravity = snap.OriginalGravity;
                    rb.detectCollisions = snap.OriginalDetectCollisions;

                    if (!rb.isKinematic)
                    {
                        rb.velocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                    }
                }
                else if (finalPos.HasValue)
                {
                    go.transform.position = finalPos.Value;
                }

                if (snap.ModifiedRenderers != null)
                    foreach (var (rend, origEnabled) in snap.ModifiedRenderers)
                        if (rend != null) rend.enabled = origEnabled;

                if (snap.ModifiedLights != null)
                    foreach (var (lt, origEnabled) in snap.ModifiedLights)
                        if (lt != null) lt.enabled = origEnabled;

                if (snap.ModifiedAudioSources != null)
                {
                    foreach (var (src, wasEnabled, wasPlaying, wasMuted, originalVolume) in snap.ModifiedAudioSources)
                    {
                        if (src == null) continue;
                        src.mute = wasMuted;
                        src.volume = originalVolume;
                        src.enabled = wasEnabled;
                        if (wasEnabled && wasPlaying && src.loop) src.Play();
                        audioRestored++;
                    }
                }

                if (snap.ModifiedParticleSystems != null)
                {
                    foreach (var (ps, wasPlaying) in snap.ModifiedParticleSystems)
                    {
                        if (ps == null) continue;
                        if (wasPlaying) ps.Play();
                        else ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    }
                }

                if (snap.ModifiedTrailRenderers != null)
                    foreach (var tr in snap.ModifiedTrailRenderers)
                        if (tr != null) tr.Clear();

                if (snap.ModifiedMapBehaviours != null)
                    foreach (var (comp, wasEnabled) in snap.ModifiedMapBehaviours)
                        if (comp != null) comp.enabled = wasEnabled;

                if (snap.ModifiedColliders != null)
                {
                    foreach (var (col, origEnabled) in snap.ModifiedColliders)
                    {
                        if (col == null) continue;
                        col.enabled = origEnabled;
                        if (origEnabled) collidersRestored++;
                    }
                }

                var pgoComp = go.GetComponent<PhysGrabObject>();
                if (pgoComp != null) pgoComp.enabled = snap.PgoWasEnabled;

                if (finalPos.HasValue)
                {
                    go.transform.position = finalPos.Value;
                    if (rb != null)
                    {
                        rb.position = finalPos.Value;
                        if (!rb.isKinematic)
                        {
                            rb.velocity = Vector3.zero;
                            rb.angularVelocity = Vector3.zero;
                        }
                    }
                }
            }
            else
            {
                if (finalPos.HasValue)
                    go.transform.position = finalPos.Value;

                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                    if (t != null) t.gameObject.layer = 0;

                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    if (r != null) r.enabled = true;

                foreach (var c in go.GetComponentsInChildren<Collider>(true))
                {
                    if (c == null) continue;
                    c.enabled = true;
                    collidersRestored++;
                }

                var rb = go.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.isKinematic = false;
                    rb.useGravity = true;
                    rb.detectCollisions = true;
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }

                foreach (var src in go.GetComponentsInChildren<AudioSource>(true))
                {
                    if (src == null) continue;
                    src.mute = false;
                    if (src.volume == 0f) src.volume = 1f;
                    src.enabled = true;
                    audioRestored++;
                }

                foreach (var comp in CollectSuppressibleBehaviours(go))
                    if (comp != null) comp.enabled = true;
            }

            string markerResult = SetMapMarkerVisible(
                go, snap, viewID, ownerActor, visible: true, finalPos: finalPos,
                side: MarkerSide(mode), reason: reason)
                ? "OK"
                : "SkippedStale";

            var rbFinal = go.GetComponent<Rigidbody>();
            QuickPocketPlugin.Log.LogDebug(
                "[QP:AudioRestore] viewID=" + viewID + " restored=" + audioRestored);
            QuickPocketPlugin.Log.LogDebug(
                "[QP:RestoreState] viewID=" + viewID +
                " item=" + itemName +
                " mode=" + mode +
                " reason=" + reason +
                " collidersRestored=" + collidersRestored +
                " detectCollisions=" + (rbFinal != null ? rbFinal.detectCollisions.ToString() : "none") +
                " isKinematic=" + (rbFinal != null ? rbFinal.isKinematic.ToString() : "none") +
                " marker=" + markerResult);
        }

        internal static bool IsPocketStateConsistent(GameObject go, ComponentSnapshot snap)
        {
            if (go == null || snap == null) return false;

            if (snap.ModifiedLayers != null)
            {
                foreach (var (obj, _) in snap.ModifiedLayers)
                {
                    if (obj == null) continue;
                    if (obj.layer != LAYER_IGNORE_RAYCAST) return false;
                }
            }

            if (snap.ModifiedRenderers != null)
                foreach (var (rend, _) in snap.ModifiedRenderers)
                    if (rend != null && rend.enabled) return false;

            if (snap.ModifiedColliders != null)
                foreach (var (col, _) in snap.ModifiedColliders)
                    if (col != null && col.enabled) return false;

            if (snap.ModifiedLights != null)
                foreach (var (lt, _) in snap.ModifiedLights)
                    if (lt != null && lt.enabled) return false;

            var rb = go.GetComponent<Rigidbody>();
            if (rb != null)
            {
                if (!rb.isKinematic) return false;
                if (rb.detectCollisions) return false;
            }

            if (snap.ModifiedAudioSources != null)
            {
                foreach (var (src, _, _, _, _) in snap.ModifiedAudioSources)
                {
                    if (src == null) continue;
                    if (!src.enabled) return false;
                    if (src.isPlaying) return false;
                    if (!src.mute) return false;
                    if (src.volume > 0.001f) return false;
                }
            }

            if (snap.ModifiedMapBehaviours != null)
                foreach (var (comp, _) in snap.ModifiedMapBehaviours)
                    if (comp != null && comp.enabled) return false;

            if (!IsMapMarkerHidden(go, snap)) return false;

            return true;
        }

        private static (Collider col, bool originalEnabled)[] BuildColliderSnapshot(GameObject go)
        {
            var cols = go.GetComponentsInChildren<Collider>(true);
            var result = new List<(Collider, bool)>(cols.Length);
            foreach (var col in cols)
                if (col != null) result.Add((col, col.enabled));
            return result.ToArray();
        }

        private static (Renderer rend, bool originalEnabled)[] BuildRendererSnapshot(GameObject go)
        {
            var rends = go.GetComponentsInChildren<Renderer>(true);
            var result = new List<(Renderer, bool)>(rends.Length);
            foreach (var r in rends)
                if (r != null) result.Add((r, r.enabled));
            return result.ToArray();
        }

        private static (GameObject go, int originalLayer)[] BuildLayerSnapshot(GameObject go)
        {
            var transforms = go.GetComponentsInChildren<Transform>(true);
            var result = new (GameObject, int)[transforms.Length];
            for (int i = 0; i < transforms.Length; i++)
            {
                var child = transforms[i].gameObject;
                result[i] = (child, child.layer);
            }
            return result;
        }

        private static (Light lt, bool originalEnabled)[] BuildLightSnapshot(GameObject go)
        {
            var lights = go.GetComponentsInChildren<Light>(true);
            var result = new List<(Light, bool)>(lights.Length);
            foreach (var lt in lights)
                if (lt != null) result.Add((lt, lt.enabled));
            return result.ToArray();
        }

        private static (AudioSource src, bool wasEnabled, bool wasPlaying, bool wasMuted, float originalVolume)[] BuildAudioSnapshot(GameObject go)
        {
            var audios = go.GetComponentsInChildren<AudioSource>(true);
            var result = new List<(AudioSource, bool, bool, bool, float)>(audios.Length);
            foreach (var src in audios)
                if (src != null) result.Add((src, src.enabled, src.isPlaying, src.mute, src.volume));
            return result.ToArray();
        }

        private static (ParticleSystem ps, bool wasEmitting)[] BuildParticleSnapshot(GameObject go)
        {
            var particles = go.GetComponentsInChildren<ParticleSystem>(true);
            var result = new List<(ParticleSystem, bool)>(particles.Length);
            foreach (var ps in particles)
                if (ps != null) result.Add((ps, ps.isPlaying || ps.isEmitting));
            return result.ToArray();
        }

        private static (Behaviour comp, bool wasEnabled)[] BuildSuppressedBehaviourSnapshot(GameObject go)
        {
            var behaviours = CollectSuppressibleBehaviours(go);
            var result = new List<(Behaviour, bool)>(behaviours.Count);
            foreach (var b in behaviours)
                if (b != null) result.Add((b, b.enabled));
            return result.ToArray();
        }

        private static List<Behaviour> CollectSuppressibleBehaviours(GameObject go)
        {
            var result = new List<Behaviour>();
            var all = go.GetComponentsInChildren<Behaviour>(true);
            foreach (var b in all)
            {
                if (b == null) continue;
                if (b is AudioSource) continue;
                if (b is PhysGrabObject) continue;

                string tn = b.GetType().Name;
                if (tn == "PhotonView") continue;
                if (IsSuppressibleBehaviourName(tn))
                    result.Add(b);
            }
            return result;
        }

        private static bool IsSuppressibleBehaviourName(string tn)
        {
            return Contains(tn, "Map") ||
                   Contains(tn, "Minimap") ||
                   Contains(tn, "Radar") ||
                   Contains(tn, "Discover") ||
                   Contains(tn, "Blip") ||
                   Contains(tn, "Marker") ||
                   Contains(tn, "Tracker") ||
                   Contains(tn, "MapIcon") ||
                   Contains(tn, "ItemIcon") ||
                   Contains(tn, "Projector") ||
                   Contains(tn, "Boombox") ||
                   Contains(tn, "Musical") ||
                   Contains(tn, "Glow") ||
                   Contains(tn, "Flash") ||
                   Contains(tn, "Blink") ||
                   Contains(tn, "Audio") ||
                   Contains(tn, "Sound") ||
                   Contains(tn, "Music") ||
                   Contains(tn, "Speaker") ||
                   Contains(tn, "Loop") ||
                   Contains(tn, "Play") ||
                   Contains(tn, "Damage") ||
                   Contains(tn, "Hurt") ||
                   Contains(tn, "Hit") ||
                   Contains(tn, "Saw") ||
                   Contains(tn, "Blade") ||
                   Contains(tn, "Attack") ||
                   Contains(tn, "Hazard");
        }

        private static bool IsDamageOrHazardBehaviour(Behaviour comp)
        {
            if (comp == null) return false;
            string tn = comp.GetType().Name;
            return Contains(tn, "Damage") ||
                   Contains(tn, "Hurt") ||
                   Contains(tn, "Hit") ||
                   Contains(tn, "Saw") ||
                   Contains(tn, "Blade") ||
                   Contains(tn, "Attack") ||
                   Contains(tn, "Hazard");
        }

        private static bool SetMapMarkerVisible(
            GameObject go, ComponentSnapshot snap, int viewID, int ownerActor,
            bool visible, Vector3? finalPos, string side, string reason)
        {
            MapValuable marker = snap != null ? snap.MapValuableMarker : null;
            SpriteRenderer sr = snap != null ? snap.MapValuableSpriteRenderer : null;

            if (marker == null && sr == null)
                FindMapMarker(go, out marker, out sr);

            bool found = marker != null || sr != null;
            bool ok = false;

            if (!visible)
            {
                if (sr != null)
                {
                    sr.enabled = false;
                    ok = true;
                }
                else if (marker != null && marker.gameObject != null)
                {
                    marker.gameObject.SetActive(false);
                    ok = true;
                }

                QuickPocketPlugin.Log.LogDebug(
                    "[QP:MapMarkerState] hide actor=" + ownerActor +
                    " viewID=" + viewID +
                    " side=" + side +
                    " found=" + found +
                    " disabled=" + ok +
                    " reason=" + reason);
                return ok || !found;
            }

            if (!finalPos.HasValue)
            {
                QuickPocketPlugin.Log.LogDebug(
                    "[QP:MapMarkerState] restore actor=" + ownerActor +
                    " viewID=" + viewID +
                    " finalPos=n/a result=SkippedStale reason=" + reason);
                return false;
            }

            if (marker != null && marker.target == null)
            {
                QuickPocketPlugin.Log.LogDebug(
                    "[QP:MapMarkerState] restore actor=" + ownerActor +
                    " viewID=" + viewID +
                    " finalPos=" + finalPos.Value.ToString("F2") +
                    " result=SkippedDestroyed reason=" + reason);
                return false;
            }

            if (sr != null)
            {
                sr.enabled = true;
                ok = true;
            }
            else if (marker != null && marker.gameObject != null)
            {
                marker.gameObject.SetActive(true);
                ok = true;
            }

            QuickPocketPlugin.Log.LogDebug(
                "[QP:MapMarkerState] restore actor=" + ownerActor +
                " viewID=" + viewID +
                " finalPos=" + finalPos.Value.ToString("F2") +
                " result=" + (ok ? "OK" : "SkippedStale") +
                " reason=" + reason);

            return ok;
        }

        private static string MarkerSide(HideMode mode)
        {
            switch (mode)
            {
                case HideMode.HostHosted:
                    return "Host";
                case HideMode.OwnerAck:
                    return "Owner";
                case HideMode.Remote:
                case HideMode.LateJoin:
                    return "Observer";
                default:
                    return mode.ToString();
            }
        }

        private static string MarkerSide(RestoreMode mode)
        {
            switch (mode)
            {
                case RestoreMode.HostHosted:
                    return "Host";
                case RestoreMode.OwnerAck:
                    return "Owner";
                case RestoreMode.Remote:
                    return "Observer";
                default:
                    return mode.ToString();
            }
        }

        private static bool IsMapMarkerHidden(GameObject go, ComponentSnapshot snap)
        {
            MapValuable marker = snap != null ? snap.MapValuableMarker : null;
            SpriteRenderer sr = snap != null ? snap.MapValuableSpriteRenderer : null;

            if (marker == null && sr == null)
                FindMapMarker(go, out marker, out sr);

            if (sr != null) return !sr.enabled;
            if (marker != null && marker.gameObject != null) return !marker.gameObject.activeSelf;
            return true;
        }

        private static void FindMapMarker(GameObject go, out MapValuable marker, out SpriteRenderer spriteRenderer)
        {
            marker = null;
            spriteRenderer = null;

            if (go == null) return;
            var valuableObj = go.GetComponent<ValuableObject>();
            if (valuableObj == null) return;

            foreach (var mv in UnityEngine.Object.FindObjectsOfType<MapValuable>(true))
            {
                if (mv == null || mv.target != valuableObj) continue;
                marker = mv;
                spriteRenderer = GetMapValuableSpriteRenderer(mv);
                return;
            }
        }

        private static SpriteRenderer GetMapValuableSpriteRenderer(MapValuable marker)
        {
            if (marker == null) return null;

            if (!_mapValuableSpriteRendererChecked)
            {
                _mapValuableSpriteRendererChecked = true;
                _mapValuableSpriteRendererField = AccessTools.Field(typeof(MapValuable), "spriteRenderer");
            }

            return _mapValuableSpriteRendererField != null
                ? _mapValuableSpriteRendererField.GetValue(marker) as SpriteRenderer
                : null;
        }

        private static bool Contains(string source, string value)
        {
            return source != null && source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
