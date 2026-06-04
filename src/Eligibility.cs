using System.Collections.Generic;
using UnityEngine;

namespace QuickPocket
{
    // =========================================================================
    // EligibilityInfo — resultado cacheado por item (chave: InstanceID)
    // =========================================================================
    internal class EligibilityInfo
    {
        public bool IsWeapon;    // arma de fogo, explosivo, launcher
        public bool IsMelee;     // arma corpo-a-corpo (separada de IsWeapon)
        public bool IsDrone;
        public bool IsOrb;
        public bool IsSmall;
        public bool Eligible;
        public string RejectReason;  // null se elegivel
        public string ItemName;

        // Dimensao maxima real avaliada pelo IsSmallItem (em metros).
        // Populado independente do resultado — usado para calibrar mensagem TooLarge.
        public float RejectedMaxDim;
    }

    // =========================================================================
    // ComponentSnapshot — captura completa do estado de um item antes do pocket.
    // Ownership exclusivo do cliente que pocketou: nao criar a partir de item
    // ja escondido (late join ou outros clientes usam RestoreWithSafeDefaults).
    // =========================================================================
    internal class ComponentSnapshot
    {
        public int    ViewID;
        public int    ActorNumber;
        public string ItemName;

        // Colliders e renderers ativos no momento do pocket
        public (Collider col,   bool originalEnabled)[] ModifiedColliders;
        public (Renderer rend,  bool originalEnabled)[] ModifiedRenderers;

        // Layer salva por objeto individualmente (hierarquia completa)
        public (GameObject go, int originalLayer)[] ModifiedLayers;

        // Rigidbody — todos os campos que o mod altera
        public bool ModifiedRigidbody;
        public bool OriginalKinematic;
        public bool OriginalGravity;
        public bool OriginalDetectCollisions;

        // PhysGrabObject.enabled antes de desativar (para restaurar exato)
        // O PhysGrabObject.Update() reseta detectCollisions/layer/etc — desabilitar é obrigatório.
        public bool PgoWasEnabled;

        // Luzes ativas no momento do pocket — desativadas no hide, restauradas no restore.
        // Sem isso, luzes ficam visíveis no chão enquanto o item está pocketado.
        public (Light lt, bool originalEnabled)[] ModifiedLights;

        // AudioSources — parados e desativados no hide para suprimir sons residuais.
        // Campos: src=componente, wasEnabled=estado original, wasPlaying=estava tocando.
        // No restore: re-habilitar; replay apenas se era loop (evitar replay de one-shots).
        public (AudioSource src, bool wasEnabled, bool wasPlaying)[] ModifiedAudioSources;

        // ParticleSystems — parados e limpos no hide.
        // No restore: Play() somente se estava emitindo originalmente.
        public (ParticleSystem ps, bool wasEmitting)[] ModifiedParticleSystems;

        // TrailRenderers — já capturados em ModifiedRenderers (subclasse de Renderer),
        // mas precisam de Clear() separado para apagar os dados do rastro.
        // Guardamos referências apenas para chamar Clear() no hide.
        public TrailRenderer[] ModifiedTrailRenderers;

        // Behaviours de mapa/minimap/discovery — desativados no hide para suprimir
        // marcadores visuais no TAB/minimap enquanto o item está pocketado.
        // Capturados por tipo: qualquer Behaviour cujo nome contenha palavras-chave.
        public (Behaviour comp, bool wasEnabled)[] ModifiedMapBehaviours;

        // MapValuable — objeto SEPARADO no Map system que representa o ponto dourado
        // do item no TAB. NÃO é filho do item — está na hierarquia do Map.
        // Encontrado por MapValuable.target == ValuableObject do item.
        //
        // IMPORTANTE: NÃO usar gameObject.SetActive(false/true) para esconder/mostrar.
        // Motivo: SetActive(false) mata a coroutine Logic(). Start() só roda UMA VEZ
        // por ciclo de vida — SetActive(true) chama OnEnable() mas NÃO reinicia Logic().
        // O marker fica congelado na última posição (P1 = posição original do pocket).
        //
        // Solução: desabilitar/habilitar APENAS o SpriteRenderer (MapValuableSpriteRenderer).
        // Logic() continua rodando, rastrea target.transform.position continuamente,
        // e o marker aparece no local correto quando reativado.
        public MapValuable    MapValuableMarker;
        public SpriteRenderer MapValuableSpriteRenderer;  // cache do spriteRenderer do marker
    }

    // =========================================================================
    // ItemTypeIds — valores numericos do enum SemiFunc+itemType (privado/aninhado).
    // Confirmados via reflexao em 2026-05-21.
    // Usar sempre estes nomes — nunca literais numericos no codigo.
    // =========================================================================
    internal static class ItemTypeIds
    {
        public const int DRONE    = 0;
        public const int ORB      = 1;
        public const int GRENADE  = 6;
        public const int MELEE    = 7;
        public const int GUN      = 9;
        public const int MINE     = 11;
        public const int LAUNCHER = 15;
    }

    // =========================================================================
    // EligibilityChecker — analise estatica com cache por InstanceID.
    //
    // Todos os acessos a campos privados do jogo passam por RepoCompat.
    // Esta classe nao tem mais nenhum AccessTools.Field() direto.
    // =========================================================================
    internal static class EligibilityChecker
    {
        // Delegar para RepoCompat — sem FieldInfo local nesta classe
        private static bool  IsGun(PhysGrabObject pgo)          => RepoCompat.GetIsGun(pgo);
        private static bool  IsMelee(PhysGrabObject pgo)        => RepoCompat.GetIsMelee(pgo);
        private static bool  IsEnemy(PhysGrabObject pgo)        => RepoCompat.GetIsEnemy(pgo);
        private static float WidthX(PhysGrabObject pgo)         => RepoCompat.GetWidthX(pgo);
        private static float HeightY(PhysGrabObject pgo)        => RepoCompat.GetHeightY(pgo);
        private static float LengthZ(PhysGrabObject pgo)        => RepoCompat.GetLengthZ(pgo);
        private static int   GetItemTypeInt(ItemAttributes a)   => RepoCompat.GetItemType(a);

        // ---------------------------------------------------------------------
        // Cache: InstanceID -> EligibilityInfo
        // Limpo em morte, mudanca de fase e retorno ao menu.
        // ---------------------------------------------------------------------
        private static readonly Dictionary<int, EligibilityInfo> _cache =
            new Dictionary<int, EligibilityInfo>();

        internal static void ClearCache() => _cache.Clear();

        // ---------------------------------------------------------------------
        // Ponto de entrada publico
        // ---------------------------------------------------------------------
        internal static EligibilityInfo Evaluate(PhysGrabObject pgo)
        {
            if (pgo == null)
                return MakeReject(null, "NullItem", "PhysGrabObject nulo");

            int id = pgo.gameObject.GetInstanceID();

            if (_cache.TryGetValue(id, out var cached))
                return cached;

            var info = Analyse(pgo);
            _cache[id] = info;
            return info;
        }

        // ---------------------------------------------------------------------
        // Analise completa (executada somente uma vez por item por sessao)
        // ---------------------------------------------------------------------
        private static EligibilityInfo Analyse(PhysGrabObject pgo)
        {
            var    go   = pgo.gameObject;
            string name = go.name;
            var    info = new EligibilityInfo { ItemName = name };

            // PASSO 1 — Entidade com IA (inimigo ativo): rejeitar sempre.
            // Nao configuravel — nao faz sentido pocketar um inimigo vivo.
            if (IsEnemy(pgo))
                return Finalise(info, false, "EnemyBlocked");

            // PASSO 2 — Classificacao por flags rapidas e itemType
            ClassifyItem(pgo, go, info);

            // PASSO 3 — Regras de config por categoria
            if (info.IsWeapon && !QuickPocketPlugin.AllowWeapons.Value)
                return Finalise(info, false, "WeaponBlocked");

            if (info.IsMelee && !QuickPocketPlugin.AllowMelee.Value)
                return Finalise(info, false, "MeleeBlocked");

            if (info.IsDrone && !QuickPocketPlugin.AllowDrones.Value)
                return Finalise(info, false, "DroneBlocked");

            if (info.IsOrb && !QuickPocketPlugin.AllowOrbs.Value)
                return Finalise(info, false, "OrbBlocked");

            // PASSO 4 — Hierarquia: filho de entidade de inimigo?
            // AllowEnemyItems=true permite pocketar drops fisicamente na hierarquia
            // do inimigo (ex: itens presos ao inimigo derrotado).
            if (!QuickPocketPlugin.AllowEnemyItems.Value &&
                go.GetComponentInParent<EnemyParent>() != null)
                return Finalise(info, false, "EnemyParentBlocked");

            // PASSO 4a — Objetos de jogador: nunca pocketar.
            // PlayerAvatar: avatar vivo (GetComponentInParent cobre toda a hierarquia).
            // "Player Tumble" / "Player Death Head": ragdolls do jogador morto —
            //   objetos clonados existentes como raiz separada, detectados por nome.
            // Checks: case-insensitive + verifica tanto go.name quanto transform.root.name.
            if (go.GetComponentInParent<PlayerAvatar>() != null)
                return Finalise(info, false, "PlayerObjectBlocked");

            // Nomes normalizados — sem "(Clone)" e sem espaco final.
            // nameClean: nome do objeto diretamente segurado.
            // rootName:  nome do ancestral raiz (cobre objetos filhos de hierarquia).
            string nameClean = go.name.Replace("(Clone)", "").Trim();
            string rootName  = go.transform.root.gameObject.name.Replace("(Clone)", "").Trim();

            if (Contains(nameClean, "Player Tumble") || Contains(rootName, "Player Tumble"))
                return Finalise(info, false, "PlayerBodyBlocked");
            if (Contains(nameClean, "Player Death Head") || Contains(rootName, "Player Death Head"))
                return Finalise(info, false, "PlayerDeathHeadBlocked");

            // PASSO 4b — Objetos de cenario / itens especiais: nunca pocketar.
            // Checks: case-insensitive + nome limpo + root name.
            // "Phase Bridge" / "PhaseBridge": partes do nivel que bugam ao ser removidas.
            // "Door" / "Hinge": portas e dobradicas — partes estruturais do REPO.
            // "Cart" / "Truck" / "Fridge": veiculos e objetos grandes de cenario.
            // "Scream Doll" / "Screen Doll": bonecas com comportamento especial (grito/queda).
            // "Upgrade": upgrades permanentes que nao sao itens de bolso.
            // "Deployable": objetos com comportamento de deploy que mudam gameplay.
            if (Contains(nameClean, "Phase Bridge")  || Contains(rootName, "Phase Bridge") ||
                Contains(nameClean, "PhaseBridge")   || Contains(rootName, "PhaseBridge"))
                return Finalise(info, false, "PhaseBridgeBlocked");
            if (Contains(nameClean, "Door")  || Contains(rootName, "Door") ||
                Contains(nameClean, "Hinge") || Contains(rootName, "Hinge"))
                return Finalise(info, false, "WorldPropBlocked");
            if (Contains(nameClean, "Cart")   || Contains(rootName, "Cart") ||
                Contains(nameClean, "Truck")  || Contains(rootName, "Truck") ||
                Contains(nameClean, "Fridge") || Contains(rootName, "Fridge"))
                return Finalise(info, false, "WorldPropBlocked");
            if (Contains(nameClean, "Scream Doll") || Contains(rootName, "Scream Doll") ||
                Contains(nameClean, "Screen Doll") || Contains(rootName, "Screen Doll"))
                return Finalise(info, false, "ScreamDollBlocked");
            if (Contains(nameClean, "Upgrade") || Contains(rootName, "Upgrade"))
                return Finalise(info, false, "UpgradeBlocked");
            if (Contains(nameClean, "Deployable") || Contains(rootName, "Deployable"))
                return Finalise(info, false, "DeployableBlocked");

            // PASSO 5 — Tamanho
            info.IsSmall       = IsSmallItem(pgo, out float maxDim);
            info.RejectedMaxDim = maxDim;  // disponivel para calibrar mensagem TooLarge
            if (!info.IsSmall)
                return Finalise(info, false, "TooLarge");

            // PASSO 6 — Aprovado
            return Finalise(info, true, null);
        }

        // ---------------------------------------------------------------------
        // Classificacao por tipo
        // ---------------------------------------------------------------------
        private static void ClassifyItem(PhysGrabObject pgo, GameObject go, EligibilityInfo info)
        {
            // Caminho rapido: flags booleanas em PhysGrabObject (via RepoCompat).
            // IsGun → arma de fogo (IsWeapon). IsMelee → corpo-a-corpo (IsMelee separado).
            if (IsGun(pgo))  { info.IsWeapon = true; return; }
            if (IsMelee(pgo)){ info.IsMelee  = true; return; }

            // Para drone/orb/launcher/grenade/mine precisamos do ItemAttributes
            var attrs = go.GetComponent<ItemAttributes>();
            if (attrs == null) return;

            // itemType e privado aninhado em SemiFunc — comparar via ItemTypeIds
            int t = GetItemTypeInt(attrs);
            if (t == ItemTypeIds.GUN || t == ItemTypeIds.LAUNCHER ||
                t == ItemTypeIds.GRENADE || t == ItemTypeIds.MINE)
            {
                info.IsWeapon = true;
            }
            else if (t == ItemTypeIds.MELEE)
            {
                // itemType MELEE via ItemAttributes (fallback se flag nao detectada acima)
                info.IsMelee = true;
            }
            else if (t == ItemTypeIds.DRONE)
            {
                info.IsDrone = true;
            }
            else if (t == ItemTypeIds.ORB)
            {
                info.IsOrb = true;
            }
        }

        // ---------------------------------------------------------------------
        // Verificacao de tamanho — maior dimensao (evita falsos positivos).
        // out maxDim: dimensao maxima avaliada, independente do resultado.
        // Usada para calibrar o humor da mensagem TooLarge.
        // ---------------------------------------------------------------------
        private static bool IsSmallItem(PhysGrabObject pgo, out float maxDim)
        {
            maxDim = 0f;  // default de saida
            float limit = QuickPocketPlugin.MaxItemBoundsSize.Value;

            // Primario: campos do PhysGrabObject via RepoCompat
            float wx = WidthX(pgo);
            float hy = HeightY(pgo);
            float lz = LengthZ(pgo);

            // Validar antes de usar: qualquer dimensao invalida ou todos zerados cai para fallback
            bool dimsValid = IsValidDim(wx) && IsValidDim(hy) && IsValidDim(lz)
                             && (wx > 0f || hy > 0f || lz > 0f);

            if (dimsValid)
            {
                maxDim = Mathf.Max(wx, hy, lz);
                QuickPocketPlugin.Log.LogDebug(
                    "[QP] Size/" + pgo.gameObject.name +
                    ": dims=(" + wx.ToString("F2") + "," + hy.ToString("F2") + "," + lz.ToString("F2") +
                    ") max=" + maxDim.ToString("F2") + " limit=" + limit);
                return maxDim < limit;
            }

            // Fallback A: GetComponentsInChildren<Collider>
            // (pgo.colliders e List<Transform>, nao List<Collider>)
            var cols = pgo.gameObject.GetComponentsInChildren<Collider>(false);
            if (cols.Length > 0)
            {
                Bounds b     = default;
                bool   first = true;
                foreach (var col in cols)
                {
                    if (!col.enabled || col.isTrigger) continue;
                    if (first) { b = col.bounds; first = false; }
                    else b.Encapsulate(col.bounds);
                }
                if (!first)
                {
                    maxDim = Mathf.Max(b.size.x, b.size.y, b.size.z);
                    QuickPocketPlugin.Log.LogDebug(
                        "[QP] Size/" + pgo.gameObject.name +
                        ": fallback-colliders max=" + maxDim.ToString("F2") + " limit=" + limit);
                    return maxDim < limit;
                }
            }

            // Fallback B: Renderer bounds
            var rends = pgo.gameObject.GetComponentsInChildren<Renderer>(false);
            if (rends.Length > 0)
            {
                Bounds b     = default;
                bool   first = true;
                foreach (var r in rends)
                {
                    if (!r.enabled) continue;
                    if (first) { b = r.bounds; first = false; }
                    else b.Encapsulate(r.bounds);
                }
                if (!first)
                {
                    maxDim = Mathf.Max(b.size.x, b.size.y, b.size.z);
                    QuickPocketPlugin.Log.LogDebug(
                        "[QP] Size/" + pgo.gameObject.name +
                        ": fallback-renderers max=" + maxDim.ToString("F2") + " limit=" + limit);
                    return maxDim < limit;
                }
            }

            // Fallback C: localScale
            var s = pgo.gameObject.transform.localScale;
            maxDim = Mathf.Max(s.x, s.y, s.z);
            QuickPocketPlugin.Log.LogDebug(
                "[QP] Size/" + pgo.gameObject.name +
                ": fallback-scale max=" + maxDim.ToString("F2") + " limit=" + limit);
            return maxDim < limit;
        }

        // ---------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------

        // Case-insensitive substring check — sem alocar strings novas alem de IndexOf.
        // Equivalente a source.Contains(value, StringComparison.OrdinalIgnoreCase).
        private static bool Contains(string source, string value) =>
            source != null && source.IndexOf(value, System.StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool IsValidDim(float v) =>
            !float.IsNaN(v) && !float.IsInfinity(v) && v >= 0f;

        private static EligibilityInfo Finalise(EligibilityInfo info, bool eligible, string reason)
        {
            info.Eligible     = eligible;
            info.RejectReason = reason;
            if (!eligible)
            {
                QuickPocketPlugin.Log.LogInfo(
                    "[QuickPocket] Rejected: Item=" + info.ItemName + " Reason=" + reason);
            }
            return info;
        }

        private static EligibilityInfo MakeReject(EligibilityInfo info, string name, string reason)
        {
            if (info == null) info = new EligibilityInfo { ItemName = name };
            return Finalise(info, false, reason);
        }
    }
}
