using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
// Alias explícito: Hashtable usada neste arquivo é sempre a do Photon.
// System.Collections.Hashtable não é usada aqui — alias elimina ambiguidade.
using Hashtable = ExitGames.Client.Photon.Hashtable;
using UnityEngine;
using UnityEngine.SceneManagement;
using ExitGames.Client.Photon;

namespace QuickPocket
{
    [BepInPlugin("com.hiarlyscripter.quickpocket", "QuickPocket", "1.0.11")]
    [BepInProcess("REPO.exe")]
    public class QuickPocketPlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static QuickPocketPlugin Instance;

        // ── [General] ───────────────────────────────────────────────────────────
        internal static ConfigEntry<KeyboardShortcut> PocketKey;
        internal static ConfigEntry<int>   MaxPocketSlots;
        internal static ConfigEntry<float> MaxItemBoundsSize;
        internal static ConfigEntry<float> PocketCooldown;

        // ── [Restrictions] ──────────────────────────────────────────────────────
        internal static ConfigEntry<bool> AllowWeapons;
        internal static ConfigEntry<bool> AllowMelee;
        internal static ConfigEntry<bool> AllowOrbs;
        internal static ConfigEntry<bool> AllowDrones;
        internal static ConfigEntry<bool> AllowEnemyItems;
        internal static ConfigEntry<bool> AllowDuplicates;

        // ── [Multiplayer] ───────────────────────────────────────────────────────
        internal static ConfigEntry<bool> UseHostPocketLimit;
        internal static ConfigEntry<int>  HostMaxOverride;
        internal static ConfigEntry<bool> SyncPocketState;

        // ── [HUD] ────────────────────────────────────────────────────────────────
        internal static ConfigEntry<bool>  ShowPocketHUD;
        internal static ConfigEntry<bool>  HudAlwaysVisible;
        internal static ConfigEntry<float> HudFadeSeconds;

        // ── [Behavior] ──────────────────────────────────────────────────────────
        internal static ConfigEntry<bool> AutoDropIfHandsFull;
        internal static ConfigEntry<bool> DropOnDeath;
        internal static ConfigEntry<bool> DropOnLevelChange;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            BindConfigs();

            // Validar compatibilidade com a versao atual do REPO antes de patchear
            RepoCompat.Initialize();

            if (!RepoCompat.IsCompatible)
            {
                Log.LogError("[QuickPocket] Inicializacao cancelada por incompatibilidade. Ver logs acima.");
                return;
            }

            _harmony = new Harmony("com.hiarlyscripter.quickpocket");
            _harmony.PatchAll();

            Log.LogInfo("[QuickPocket] v1.0.11 inicializado. Aguardando jogador...");
        }

        private void BindConfigs()
        {
            // ── [General] ──────────────────────────────────────────────────────────
            // Note: KeyboardShortcut is not rendered by REPOConfig 1.2.6.
            // Edit PocketKey directly in this .cfg file.
            PocketKey = Config.Bind("General", "PocketKey",
                new KeyboardShortcut(KeyCode.H),
                "Key to store/retrieve items from the pocket. Edit via .cfg (not shown in REPOConfig).");

            MaxPocketSlots = Config.Bind("General", "MaxPocketSlots", 4,
                new ConfigDescription(
                    "Maximum number of pocket slots (personal). Host limit may override this.",
                    new AcceptableValueRange<int>(1, 8)));

            MaxItemBoundsSize = Config.Bind("General", "MaxItemBoundsSize", 1.25f,
                new ConfigDescription(
                    "Max item size in meters (largest dimension). Items above this are rejected.",
                    new AcceptableValueRange<float>(0.1f, 5.0f)));

            PocketCooldown = Config.Bind("General", "PocketCooldown", 0.5f,
                new ConfigDescription(
                    "Cooldown in seconds between pocket actions.",
                    new AcceptableValueRange<float>(0.1f, 2.0f)));

            // ── [Restrictions] ────────────────────────────────────────────────────
            AllowWeapons = Config.Bind("Restrictions", "AllowWeapons", false,
                "Allow storing firearms, explosives and launchers in the pocket.");

            AllowMelee = Config.Bind("Restrictions", "AllowMelee", false,
                "Allow storing melee weapons in the pocket.");

            AllowOrbs = Config.Bind("Restrictions", "AllowOrbs", true,
                "Allow storing orbs (health/utility pickups) in the pocket.");

            AllowDrones = Config.Bind("Restrictions", "AllowDrones", false,
                "Allow storing drones in the pocket.");

            AllowEnemyItems = Config.Bind("Restrictions", "AllowEnemyItems", false,
                "Allow storing items physically attached to enemy entities.");

            AllowDuplicates = Config.Bind("Restrictions", "AllowDuplicates", true,
                "Allow storing multiple items of the same type simultaneously.");

            // ── [Multiplayer] ─────────────────────────────────────────────────────
            UseHostPocketLimit = Config.Bind("Multiplayer", "UseHostLimit", true,
                "If true, clients use the host's HostMaxOverride slot limit.");

            HostMaxOverride = Config.Bind("Multiplayer", "HostMaxOverride", 4,
                new ConfigDescription(
                    "Slot limit published by the host for the whole room. Requires UseHostLimit=true.",
                    new AcceptableValueRange<int>(1, 8)));

            SyncPocketState = Config.Bind("Multiplayer", "SyncPocketState", true,
                "If true, other players see items disappear/reappear when pocketed/retrieved.");

            // ── [HUD] ─────────────────────────────────────────────────────────────
            ShowPocketHUD = Config.Bind("HUD", "ShowPocketHUD", true,
                "Show the QuickPocket HUD on screen.");

            HudAlwaysVisible = Config.Bind("HUD", "HudAlwaysVisible", false,
                "If true, HUD stays visible while items are in the pocket. If false, it fades.");

            HudFadeSeconds = Config.Bind("HUD", "HudFadeSeconds", 4f,
                new ConfigDescription(
                    "Seconds the HUD remains visible after a pocket/unpocket action.",
                    new AcceptableValueRange<float>(1f, 10f)));

            // ── [Behavior] ────────────────────────────────────────────────────────
            AutoDropIfHandsFull = Config.Bind("Behavior", "AutoDropIfHandsFull", true,
                "If hands are occupied when retrieving, drop the item beside the player instead.");

            DropOnDeath = Config.Bind("Behavior", "DropOnDeath", true,
                "Drop all pocketed items at the death position when the player dies.");

            DropOnLevelChange = Config.Bind("Behavior", "DropOnLevelChange", true,
                "Drop all pocketed items when the level changes.");

            Log.LogDebug("[QuickPocket] Configs carregadas: Key=" + PocketKey.Value.MainKey +
                         " Slots=" + MaxPocketSlots.Value +
                         " HostMax=" + HostMaxOverride.Value +
                         " UseHostLimit=" + UseHostPocketLimit.Value +
                         " Sync=" + SyncPocketState.Value);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PocketManager — MonoBehaviour adicionado ao PlayerAvatar local.
    // Implementa IInRoomCallbacks (Photon sala) e IMatchmakingCallbacks (join/leave).
    // ═══════════════════════════════════════════════════════════════════════════
    internal class PendingRequest
    {
        internal int Nonce;
        internal int ViewID;
        internal float Timestamp;
        internal int Actor;
        internal string Type;
        internal string State;

        internal PendingRequest(int nonce, int viewID, float timestamp, int actor, string type, string state)
        {
            Nonce = nonce;
            ViewID = viewID;
            Timestamp = timestamp;
            Actor = actor;
            Type = type;
            State = state;
        }
    }

    public class PocketManager : MonoBehaviour, IInRoomCallbacks, IMatchmakingCallbacks, IOnEventCallback
    {
        // ── Singleton local ───────────────────────────────────────────────────
        internal static PocketManager Instance { get; private set; }

        // ── Room Property keys ────────────────────────────────────────────────
        private const string PROP_HOST_MAX_SLOTS = "QP_HostMaxSlots";
        private const string PROP_USE_HOST_LIMIT = "QP_UseHostLimit";
        private const float  LATE_JOIN_TIMEOUT   = 10f;  // segundos antes de desistir de item pendente

        private string PropKeyForActor(int actor) => "QP_" + actor;

        // ── Estado local ──────────────────────────────────────────────────────
        private readonly List<int>                        _localPocket = new List<int>();
        private readonly Dictionary<int, ComponentSnapshot> _snapshots = new Dictionary<int, ComponentSnapshot>();

        // ── Referências diretas — singleplayer (InstanceID → GameObject) ──────
        // Em singleplayer, Photon opera em modo offline e PhotonView.ViewID == 0 para
        // todos os objetos. PhotonView.Find(0) retorna null → enforcement remove itens
        // imediatamente. Solução: usar GetInstanceID() como chave local e armazenar
        // referência direta. Em multiplayer esta estrutura permanece vazia; a lookup
        // é feita exclusivamente via PhotonView.Find(viewID).
        private readonly Dictionary<int, GameObject> _singleplayerObjects = new Dictionary<int, GameObject>();

        // ── Estado remoto (Phase 4) ────────────────────────────────────────────
        // actorNumber → ViewIDs que aquele jogador tem pocketados
        private readonly Dictionary<int, List<int>> _remoteHides     = new Dictionary<int, List<int>>();
        // ViewID → tempo de expiracao da tentativa de hide
        private readonly Dictionary<int, float>      _pendingLateJoin = new Dictionary<int, float>();
        private readonly Dictionary<int, int>        _pendingLateJoinActors = new Dictionary<int, int>();

        // ── Anti-race ─────────────────────────────────────────────────────────
        private bool  _isPocketing   = false;
        private float _cooldownTimer = 0f;
        private float _lastEnforce   = 0f;

        // ── Flags de HUD — sistema fail-closed ───────────────────────────────
        //
        // _hasLocalPocketManager: true após PocketManager.Awake(). Apenas indica que
        //   o componente existe no avatar local. NÃO indica gameplay renderizável.
        //   Nunca setado false manualmente — PocketManager.OnDestroy() encerra o ciclo.
        //
        // _hudGameplayReady: true apenas após _cachedRunIsLevel confirmar nível ativo
        //   em Update(). Setado false em HandleLevelChange, ClearAllState, HandleMainMenu.
        //
        // _hudSuppressedForTransition: true por default e após qualquer transição/reset.
        //   Limpo SOMENTE em Update() quando _cachedRunIsLevel == true.
        //   Garante fail-closed: HUD off até confirmação positiva de nível ativo.
        //   PocketManager.Awake() SETA true (não limpa) — Awake dispara durante loading.
        //
        // _cachedRunIsLevel: resultado de TrySemiRunIsLevel() atualizado em Update().
        //   ShouldRenderHud() lê este campo — sem reflexão por frame em OnGUI.
        private bool _hasLocalPocketManager      = false;
        private bool _hudGameplayReady           = false;
        private bool _hudSuppressedForTransition = true;   // fail-closed por default
        private bool _cachedRunIsLevel           = false;

        // Cache para SemiFunc.RunIsLevel() via reflexão (lazy, one-time lookup).
        private static MethodInfo _runIsLevelMethod  = null;
        private static bool       _runIsLevelChecked = false;

        // Cache do campo privado LoadingUI.levelAnimationCompleted (lazy, one-time lookup).
        private static FieldInfo _loadingUIAnimCompleted = null;

        // Estabilidade do HUD: RunIsLevel() precisa retornar true por N frames consecutivos
        // antes de liberar o HUD. Impede que um true transitório durante loading libere o HUD.
        // Valor: 5 frames (~83ms a 60fps) — imperceptível ao jogador, robusto contra flicker.
        private const  int _HUD_STABLE_FRAMES = 5;
        private        int _runIsLevelTrueStreak = 0;

        // Gate temporal após _loadingAnimDone=true — previne HUD falso na tela inicial.
        // Causa raiz Bug A (HUD ~1s no login): LoadingUI.LevelAnimationComplete() dispara
        // durante o boot → _loadingAnimDone=true; depois RunIsLevel() retorna true brevemente
        // (~1s = ~60 frames). Com apenas 5 frames de streak, o HUD aparece na tela inicial.
        // Fix: exigir HUD_UNLOCK_MIN_WAIT segundos após _loadingAnimDone antes do streak contar.
        // 2.0s >> duração da false positive do boot (~1s) → HUD só libera em nível real.
        private const float HUD_UNLOCK_MIN_WAIT = 2.0f;

        // Cache do estado de loading — atualizado uma vez por frame em Update().
        // Lido em ShouldRenderHud() e OnGUI() sem chamada por frame.
        private bool _cachedIsLoading = true;  // fail-closed: assume loading até prova contrária

        // ── Generation/latch — sistema definitivo anti-HUD-no-loading ──────────
        //
        // Problema raiz: RunIsLevel() fica true antes da animação de loading terminar
        // visualmente. Manager stale (fase anterior) também pode religar HUD.
        //
        // Solução:
        //   _hudGeneration (static): incrementado em QUALQUER transição de cena.
        //   _managerGeneration (instance): capturado no Awake() com valor atual de _hudGeneration.
        //   Manager com _managerGeneration != _hudGeneration = STALE → Update/OnGUI retornam.
        //
        //   _loadingAnimDone: setado true pelo patch LoadingUI.LevelAnimationComplete().
        //   HUD só liberado quando: generation match + RunIsLevel streak + _loadingAnimDone.
        //
        //   _loadingUIWasSeen: setado true quando _cachedIsLoading==true ao menos uma vez
        //   no ciclo atual. Garante que o fallback de _loadingAnimDone só dispare DEPOIS
        //   que o LoadingUI esteve ativo — evita falso-positivo quando LoadingUI.instance==null
        //   brevemente (antes de ser criado) com RunIsLevel()==true (causa raiz Bug A).
        private static int _hudGeneration      = 0;
        private        int _managerGeneration  = -1;
        private        bool _loadingAnimDone   = false;  // fail-closed: false até evento de anim
        private        bool _loadingUIWasSeen  = false;  // true após _cachedIsLoading==true neste ciclo
        private        float _loadingAnimDoneTime = 0f;  // Time.time quando _loadingAnimDone foi setado true

        // Layers REPO cacheadas — inicializadas na primeira chamada a IsDropSphereClear.
        // RoomVolume: volumes de sala (colisores sólidos que preenchem salas inteiras —
        //   NÃO são obstáculos físicos para drop; filtrar para evitar GATE4 falso-positivo).
        private static int  _layerRoomVolume   = -1;
        private static bool _layersInitialized = false;

        // ── Confirmacao diferida de drop ──────────────────────────────────────
        // ViewIDs com RestoreSnapshot ja chamado, aguardando coroutine de confirmacao.
        // EnforcePocketState ignora esses itens para nao lutar contra o restore.
        private readonly HashSet<int> _pendingRestore = new HashSet<int>();

        // ── Multiplayer host-authoritative — event codes ──────────────────────
        // Range 71-76: não conflita com REPO nem com mods populares (ExtractionPoint, REPORoles).
        // Apenas 6 events: request/ack/deny para pocket e drop.
        // SYNC visual de outros jogadores via Room Properties já existentes (QP_{actor}).
        private const byte EV_REQUEST_POCKET = 71;  // Client → Master: solicitar pocket
        private const byte EV_ACK_POCKET     = 72;  // Master → Client: pocket aprovado
        private const byte EV_DENY_POCKET    = 73;  // Master → Client: pocket negado
        private const byte EV_REQUEST_DROP   = 74;  // Client → Master: solicitar drop
        private const byte EV_ACK_DROP       = 75;  // Master → Client: drop aprovado (+ posição)
        private const byte EV_DENY_DROP      = 76;  // Master → Client: drop negado

        // Timeout de requests pendentes — se o host não responder em N segundos, cancelar.
        private const float MP_REQUEST_TIMEOUT = 5f;

        // ── Host: snapshots de itens pocketados POR outros atores ─────────────
        // Apenas no master. Chave: actorNumber do cliente.
        // O host executa SaveSnapshot + ApplyHide, armazena snap, executa RestoreSnapshot no drop.
        // Motivo: todos os itens do REPO são OwnershipTransfer=Fixed owned pelo master.
        // Clients NÃO PODEM aplicar ApplyHide diretamente — o host overrides via Photon sync.
        private readonly Dictionary<int, List<(int vid, ComponentSnapshot snap)>> _hostedPockets =
            new Dictionary<int, List<(int vid, ComponentSnapshot snap)>>();

        private readonly Dictionary<int, ComponentSnapshot> _remoteSnapshots =
            new Dictionary<int, ComponentSnapshot>();

        // ── Cliente: itens pocketados via host (sem snapshot local) ────────────
        // ViewIDs que este cliente pocketou via EV_REQUEST_POCKET/EV_ACK_POCKET.
        // Esses itens estão em _localPocket (para HUD) mas SEM _snapshots[viewID].
        // Drop e restore passam pelo host (EV_REQUEST_DROP/EV_ACK_DROP).
        private readonly HashSet<int> _clientHostedItems = new HashSet<int>();

        // ── Requests pendentes (aguardando ACK do host) ───────────────────────
        // nonce → (viewID, timestamp)
        private readonly Dictionary<int, PendingRequest> _pendingPocketAcks =
            new Dictionary<int, PendingRequest>();
        private readonly Dictionary<int, PendingRequest> _pendingDropAcks =
            new Dictionary<int, PendingRequest>();
        private int _nonce = 0;  // incrementado a cada request, identifica o par request/ack

        // ── TryFindSafeDropPosition — constantes ─────────────────────────────
        // Candidatos em ordem normal (frente primeiro) e lateral (lados primeiro).
        // Indice circular evita drops sequenciais no mesmo ponto.
        // 12 angulos dao cobertura completa de 360 graus em passos de 30.
        private static readonly float[] DROP_ANGLES_NORMAL =
            { 0f, 30f, -30f, 60f, -60f, 90f, -90f, 120f, -120f, 150f, -150f, 180f };
        private static readonly float[] DROP_ANGLES_SIDE   =
            { 90f, -90f, 60f, -60f, 120f, -120f, 0f, 30f, -30f, 150f, -150f, 180f };
        private int   _dropCandidateIndex  = 0;
        private float _dropCandidateResetT = 0f;
        private const float DROP_RESET_SECS      = 6f;

        // Buffer estático para OverlapSphereNonAlloc em GATE4 — sem alocação por candidato
        private static readonly Collider[] _overlapBuffer = new Collider[32];
        private const float DROP_SEARCH_RADIUS   = 1.2f;  // distancia horizontal de busca (m)
        private const float DROP_SPHERE_R        = 0.28f; // raio da esfera de verificacao (C: era 0.18 — muito pequeno vs tamanho real dos itens)
        private const float DROP_MAX_FLOOR_DELTA = 1.5f;  // max diferenca de andar aceita (m) (B: era 0.8 — rejeitava degraus validos do REPO)
        private const float DROP_ITEM_HEIGHT     = 0.2f;  // altura acima do chao ao spawnar
        private const float DROP_CEILING_MIN     = 0.5f;  // espaco minimo entre item e teto
        private const float DROP_GROUND_CAST     = 4.0f;  // alcance do raycast de chao

        // ── HUD — mensagem de acao temporaria ────────────────────────────────
        // Exibida na linha acima do contador. Pode ter fade (HudAlwaysVisible=false).
        private string _hudActionMessage     = "";
        private float  _hudActionMessageTime = -999f;

        private void SetHudMessage(string msg)
        {
            _hudActionMessage     = msg;
            _hudActionMessageTime = Time.time;
        }

        // Retorna mensagem de HUD para cada motivo de rejeição de elegibilidade.
        // String vazia = sem mensagem (não exibir nada).
        private static string GetRejectHudMessage(string reason)
        {
            switch (reason)
            {
                case "PlayerObjectBlocked":    return "Nao cabe gente viva no bolso.";
                case "PlayerBodyBlocked":      return "Esse ai ainda tem CPF.";
                case "PlayerDeathHeadBlocked": return "Cabeca de jogador fica fora do bolso.";
                case "PhaseBridgeBlocked":
                case "WorldPropBlocked":       return "Isso e parte do nivel, nao um item.";
                case "UpgradeBlocked":         return "Upgrades ficam no lugar.";
                case "ScreamDollBlocked":
                case "SpecialItemBlocked":     return "Esse item nao pode ser guardado.";
                case "DeployableBlocked":      return "Deployables ficam no lugar.";
                case "WeaponBlocked":
                case "MeleeBlocked":           return "Armas nao cabem no bolso.";
                case "DroneBlocked":           return "Drones nao cabem no bolso.";
                case "EnemyBlocked":           return "Isso e um inimigo, nao um item.";
                case "EnemyParentBlocked":     return "Esse item esta preso a um inimigo.";
                case "OrbBlocked":             return "Orbs bloqueados nas configs.";
                default:                       return "";
            }
        }

        // Remove sufixo "(Clone)" que Unity adiciona automaticamente em objetos instanciados.
        private static string CleanItemName(string raw)
        {
            if (raw == null) return "";
            return raw.Replace("(Clone)", "").TrimEnd();
        }

        private static bool HasPendingRequest(Dictionary<int, PendingRequest> requests, int viewID)
        {
            foreach (var kv in requests)
                if (kv.Value != null && kv.Value.ViewID == viewID) return true;
            return false;
        }

        private void TrackPending(Dictionary<int, PendingRequest> requests, int nonce, int viewID, string type)
        {
            int actor = SemiFunc.IsMultiplayer() && PhotonNetwork.LocalPlayer != null
                ? PhotonNetwork.LocalPlayer.ActorNumber
                : 0;
            requests[nonce] = new PendingRequest(nonce, viewID, Time.time, actor, type, "Waiting");
        }

        private void ClearPendingRequests(string reason, bool hudReset)
        {
            int actor = SemiFunc.IsMultiplayer() && PhotonNetwork.LocalPlayer != null
                ? PhotonNetwork.LocalPlayer.ActorNumber
                : 0;
            int cleared = _pendingPocketAcks.Count + _pendingDropAcks.Count;
            _pendingPocketAcks.Clear();
            _pendingDropAcks.Clear();
            if (hudReset && _hudActionMessage == "Aguarde...")
                SetHudMessage("");

            if (cleared > 0)
            {
                QuickPocketPlugin.Log.LogInfo(
                    "[QP:MP] cleanup actor=" + actor +
                    " reason=" + reason +
                    " restored=0 pendingCleared=" + cleared +
                    " hudReset=" + hudReset);
            }
        }

        private ComponentSnapshot SaveLocalSnapshotFor(GameObject go, int viewID, int actorNumber)
        {
            if (go == null) return null;
            var pgo = go.GetComponent<PhysGrabObject>();
            if (pgo == null) return null;

            var snap = PocketOperations.SaveSnapshot(pgo, viewID, actorNumber);
            return PocketOperations.ValidateSnapshot(snap) ? snap : null;
        }

        private void HideRemoteItem(int viewID, GameObject go, int actorNumber, HideMode mode, string reason)
        {
            if (go == null) return;

            var snap = SaveLocalSnapshotFor(go, viewID, actorNumber);
            if (snap != null)
                _remoteSnapshots[viewID] = snap;

            PocketOperations.HidePocketedItemState(go, viewID, actorNumber, mode, reason, snap);
        }

        private void RestoreRemoteItem(int viewID, GameObject go, int actorNumber, RestoreMode mode, Vector3? finalPos, string reason)
        {
            if (go == null) return;

            _remoteSnapshots.TryGetValue(viewID, out var snap);
            PocketOperations.RestorePocketedItemState(go, viewID, actorNumber, mode, finalPos, reason, snap);
            _remoteSnapshots.Remove(viewID);
        }

        private void SendHostedDropRequest(int viewID, Vector3 clientPos, string reason, bool trackPending)
        {
            int n = ++_nonce;
            var reqData = new Hashtable
            {
                { "vid",   viewID },
                { "nonce", n },
                { "cpx",   clientPos.x },
                { "cpy",   clientPos.y },
                { "cpz",   clientPos.z },
                { "reason", reason }
            };
            var reqOpts = new RaiseEventOptions { Receivers = ReceiverGroup.MasterClient };
            PhotonNetwork.RaiseEvent(EV_REQUEST_DROP, reqData, reqOpts, SendOptions.SendReliable);

            if (trackPending)
                TrackPending(_pendingDropAcks, n, viewID, "Drop");

            int localActor = PhotonNetwork.LocalPlayer.ActorNumber;
            QuickPocketPlugin.Log.LogInfo(
                "[QP:MP] request drop actor=" + localActor +
                " viewID=" + viewID +
                " nonce=" + n +
                " clientPos=" + clientPos.ToString("F2") +
                " reason=" + reason);
        }

        // ── Sincronizacao de limite ────────────────────────────────────────────
        private bool _limitSynced         = false;
        private int  _cachedEffectiveLimit = -1;
        private bool _lateJoinProcessed   = false;

        // ── Ciclo de vida Unity ───────────────────────────────────────────────

        private void Awake()
        {
            // Singleton rígido: registrar este como a instância ativa.
            // Se já existia uma instância (PocketManager stale de fase anterior),
            // ela continua viva até o PlayerAvatar antigo ser destruído — mas
            // Update() e OnGUI() do stale terão guards de singleton + generation
            // e serão silenciados imediatamente a partir deste momento.
            if (Instance != null && Instance != this)
                QuickPocketPlugin.Log.LogDebug(
                    "[QP:Manager] stale suppressed id=" + Instance.GetInstanceID() +
                    " -> new id=" + GetInstanceID() +
                    " gen=" + _hudGeneration);
            else
                QuickPocketPlugin.Log.LogDebug(
                    "[QP:Manager] instance set id=" + GetInstanceID() +
                    " gen=" + _hudGeneration);

            Instance = this;

            // Capturar geração atual — este manager só é válido se _hudGeneration não mudar.
            // Se uma transição ocorrer depois deste Awake (incrementa _hudGeneration),
            // _managerGeneration != _hudGeneration e este manager vira stale automaticamente.
            _managerGeneration          = _hudGeneration;

            _hasLocalPocketManager      = true;
            _hudSuppressedForTransition = true;
            _runIsLevelTrueStreak       = 0;
            _loadingAnimDone            = false;  // fail-closed: aguardar evento de anim
            _loadingUIWasSeen           = false;  // aguardar LoadingUI aparecer neste ciclo
            _loadingAnimDoneTime        = 0f;     // gate temporal: será setado em OnLoadingAnimationComplete
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnEnable()
        {
            PhotonNetwork.AddCallbackTarget(this);
            // Kill-switch de HUD: qualquer troca de cena supprime HUD imediatamente.
            // Garante fail-closed mesmo que RunIsLevel() volte true antes do nível estar pronto.
            SceneManager.sceneLoaded += OnSceneLoaded;
            QuickPocketPlugin.Log.LogDebug("[QuickPocket] PocketManager: callbacks Photon + SceneManager registrados.");
        }

        private void OnDisable()
        {
            PhotonNetwork.RemoveCallbackTarget(this);
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        // Chamado pelo Unity quando QUALQUER cena termina de carregar.
        // Supprime HUD imediatamente — RunIsLevel() confirma gameplay posteriormente em Update().
        // Este é o kill-switch definitivo: loading screen visível → cena nova → HUD off.
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (this != Instance) return;  // stale instance ignora eventos
            if (_managerGeneration != _hudGeneration) return;  // generation guard
            if (_hudSuppressedForTransition && !_hudGameplayReady) return;  // já supprimido, nao logar
            _hudGeneration++;
            _hudGameplayReady           = false;
            _hudSuppressedForTransition = true;
            _runIsLevelTrueStreak       = 0;
            _loadingAnimDone            = false;
            _loadingUIWasSeen           = false;  // novo ciclo: aguardar LoadingUI neste ciclo
            // Atualizar managerGeneration para acompanhar a nova geração
            _managerGeneration = _hudGeneration;
            QuickPocketPlugin.Log.LogDebug(
                "[QP:HUDState] gen=" + _hudGeneration +
                " allowed=false reason=SceneLoaded scene=" + scene.name);
        }

        private void Start()
        {
            // Cobre o caso em que PocketManager e criado depois do OnJoinedRoom
            if (PhotonNetwork.InRoom)
                ProcessLateJoin();
        }

        private void Update()
        {
            // Guard 1 — Singleton: PocketManager stale silenciado quando nova instância
            // seta Instance em Awake(). Sem este guard, stale continua rodando e pode
            // renderizar HUD com _hudGameplayReady=true.
            if (this != Instance) return;

            // Guard 2 — Generation: manager cujo _managerGeneration difere de _hudGeneration
            // é stale de uma geração anterior (transição ocorreu após este Awake).
            // Logar apenas uma vez seria melhor, mas logar por frame causaria spam.
            // Silenciar sem log — o log de "stale suppressed" em Awake já cobre o caso.
            if (_managerGeneration != _hudGeneration) return;

            if (_cooldownTimer > 0f)
                _cooldownTimer -= Time.deltaTime;

            if (Time.time - _lastEnforce > 1f)
            {
                EnforcePocketState();
                _lastEnforce = Time.time;
            }

            if (_pendingLateJoin.Count > 0)
                ProcessPendingLateJoin();

            if (_pendingPocketAcks.Count > 0 || _pendingDropAcks.Count > 0)
                CheckRequestTimeouts();

            // ── Cache LoadingUI — kill-switch primário de loading ──────────────
            // LoadingUI.instance é o singleton da tela de loading do REPO.
            // levelAnimationCompleted=false = loading em andamento; true = concluído.
            // Cache aqui (uma vez por frame) para que ShouldRenderHud() leia sem custo.
            _cachedIsLoading = IsLoadingActive();

            // Rastrear se LoadingUI esteve ativo ao menos uma vez neste ciclo.
            // Necessário para o fallback de _loadingAnimDone — evita disparo prematuro quando
            // LoadingUI.instance==null (antes de ser criado) com RunIsLevel()==true.
            if (_cachedIsLoading)
                _loadingUIWasSeen = true;

            // ── Cache RunIsLevel() + controle fail-closed do HUD ─────────────
            //
            // _cachedRunIsLevel atualizado aqui (uma vez por frame) — sem reflexão em OnGUI.
            //
            // Design fail-closed QUÍNTUPLO:
            //   1. _hudSuppressedForTransition=true por default (Awake, HandleLevelChange, etc.)
            //   2. SceneManager.sceneLoaded kill-switch: qualquer cena nova → supprime
            //   3. Stability gate: RunIsLevel() precisa retornar true por _HUD_STABLE_FRAMES
            //      frames consecutivos E _loadingAnimDone=true (patch LoadingUI)
            //   4. Generation guard: _managerGeneration != _hudGeneration → stale (acima)
            //   5. _loadingUIWasSeen: fallback só ativa DEPOIS do LoadingUI ter aparecido
            _cachedRunIsLevel = TrySemiRunIsLevel();

            // Fallback para _loadingAnimDone: se o patch LevelAnimationComplete não disparou
            // mas LoadingUI esteve ativo E agora não está E RunIsLevel já é true:
            // considerar animação concluída.
            //
            // REQUER _loadingUIWasSeen=true — sem isso, o fallback poderia disparar quando
            // LoadingUI.instance==null (antes de ser criado) com RunIsLevel()==true transitório,
            // causando HUD visível durante a tela preta de loading (causa raiz Bug A).
            if (!_loadingAnimDone && _loadingUIWasSeen && !_cachedIsLoading && _cachedRunIsLevel)
            {
                _loadingAnimDone     = true;
                _loadingAnimDoneTime = Time.time;  // gate temporal: 2s antes do streak começar
                QuickPocketPlugin.Log.LogDebug(
                    "[QP:HUDState] gen=" + _hudGeneration + " loadingAnimDone=true reason=Fallback");
            }

            if (_hudSuppressedForTransition)
            {
                // Gate quádruplo: RunIsLevel true + anim concluída + gate temporal + streak
                //
                // HUD_UNLOCK_MIN_WAIT (2.0s): impede que o HUD apareça durante o boot inicial.
                // Causa raiz: LevelAnimationComplete dispara no boot → _loadingAnimDone=true;
                // RunIsLevel() retorna true por ~1s (transitório do boot). Com streak=5, o
                // HUD aparecia antes do gate temporal existir. 2.0s > 1s do boot false positive.
                // Em gameplay real: RunIsLevel permanece true indefinidamente → streak dispara
                // normalmente após os 2s, imperceptível pois o nível já está em curso.
                bool timeGateOk = (Time.time - _loadingAnimDoneTime >= HUD_UNLOCK_MIN_WAIT);
                if (_cachedRunIsLevel && _loadingAnimDone && timeGateOk)
                {
                    _runIsLevelTrueStreak++;
                    if (_runIsLevelTrueStreak >= _HUD_STABLE_FRAMES)
                    {
                        _hudSuppressedForTransition = false;
                        _hudGameplayReady           = true;
                        QuickPocketPlugin.Log.LogDebug(
                            "[QP:HUDState] gen=" + _hudGeneration +
                            " allowed=true reason=FreshAvatarGameplayConfirmed streak=" + _runIsLevelTrueStreak);
                    }
                }
                else
                {
                    // RunIsLevel false, loading não concluído, ou gate temporal não passou: resetar streak
                    _runIsLevelTrueStreak = 0;
                }
            }
            else if (!_cachedRunIsLevel)
            {
                // Estava em gameplay, saiu (level terminou, menu, etc.) — supprime HUD
                _hudGameplayReady           = false;
                _hudSuppressedForTransition = true;
                _runIsLevelTrueStreak       = 0;
                _loadingAnimDone            = false;
                QuickPocketPlugin.Log.LogDebug(
                    "[QP:HUDState] gen=" + _hudGeneration + " allowed=false reason=RunIsLevelFalse");
            }

            // Input.GetKeyDown(MainKey) detecta a tecla independente de modifier keys.
            // KeyboardShortcut.IsDown() rejeita quando qualquer modifier nao declarado
            // esta pressionado — isso bloqueia H quando REPO usa Ctrl para agachar.
            if (Input.GetKeyDown(QuickPocketPlugin.PocketKey.Value.MainKey))
                HandlePocketKey();
        }

        // ── Lookup de objeto pocketado ────────────────────────────────────────
        //
        // Separação arquitetural singleplayer / multiplayer:
        //   Multiplayer  → PhotonView.Find(key) onde key == ViewID (compartilhado entre clientes)
        //   Singleplayer → referência direta em _singleplayerObjects onde key == InstanceID local
        //
        // Nunca usar PhotonView.Find em singleplayer: ViewID==0 para todos os objetos em
        // modo offline, portanto Find(0) retorna null e o item seria falsamente removido.

        private GameObject FindPocketedObject(int key)
        {
            bool isMP = SemiFunc.IsMultiplayer();

            if (isMP)
            {
                var pv = PhotonView.Find(key);
                return pv != null ? pv.gameObject : null;
            }
            else
            {
                bool dictHasKey = _singleplayerObjects.TryGetValue(key, out var go);
                // Referência pode ter sido destruída pela engine (ex: troca de cena).
                // Usar operador Unity (go != null) que detecta objetos Destroy()ados.
                bool goNull = (go == null);   // Unity == override: true se destruído
                return dictHasKey && !goNull ? go : null;
            }
        }

        // ── Input ─────────────────────────────────────────────────────────────

        private void HandlePocketKey()
        {
            // Try/catch externo protege SemiFunc.PhysGrabberLocalIsGrabbing() contra NPE.
            // Confirmado em log multiplayer: metodo REPO pode lancar NullReferenceException
            // internamente quando PhysGrabber nao esta inicializado (transicao de lobby).
            try
            {
                if (_isPocketing) return;

                if (_cooldownTimer > 0f)
                {
                    // Uma mensagem curta por pressionamento — IsDown() ja limita a 1x por frame
                    SetHudMessage("Aguarde...");
                    return;
                }

                if (SemiFunc.PhysGrabberLocalIsGrabbing())
                {
                    TryPocket();
                }
                else if (_localPocket.Count > 0)
                {
                    // TODO (AutoGrab — Phase 5+):
                    //   Quando Mouse1 esta pressionado com maos vazias, o item restaurado
                    //   e imediatamente pego pelo PhysGrabber e entra em estado invalido
                    //   → destruido a ~0.2s. Investigar APIs reais antes de implementar:
                    //     PhysGrabber.TryGrab / GrabObject / grabbed field
                    //     PhysGrabObject.playerGrabbing / grabbed state
                    //     PlayerAvatar.physGrabber reference
                    //   Sem API segura confirmada: bloquear unpocket quando Mouse1 esta
                    //   pressionado. Fallback obrigatorio: drop normal sempre funcional.
                    if (Input.GetMouseButton(0))
                    {
                        SetHudMessage("Solte o botao de grab antes de retirar.");
                        return;
                    }

                    TryUnpocket();
                }
                // else: bolso vazio e nao esta segurando nada — sem acao
            }
            catch (System.Exception ex)
            {
                QuickPocketPlugin.Log.LogDebug(
                    "[QP:HandlePocketKey] excecao: " + ex.GetType().Name + " — " + ex.Message);
            }
        }

        // ── TryPocket ─────────────────────────────────────────────────────────

        private void TryPocket()
        {
            _isPocketing = true;
            try
            {
                int limit = GetEffectiveLimit();
                if (limit < 0)
                {
                    SetHudMessage("Sincronizando limite...");
                    QuickPocketPlugin.Log.LogInfo("[QuickPocket] Sincronizando limite — tente novamente.");
                    return;
                }

                if (_localPocket.Count >= limit)
                {
                    SetHudMessage("Bolso cheio! (" + _localPocket.Count + "/" + limit + ")");
                    QuickPocketPlugin.Log.LogInfo(
                        "[QuickPocket] Bolso cheio (" + _localPocket.Count + "/" + limit + ").");
                    return;
                }

                // SemiFunc.PhysGrabberLocalGetGrabbedPhysGrabObject() pode lançar NPE
                // internamente no REPO quando PhysGrabber não está inicializado
                // (confirmado em log multiplayer: NullReferenceException dentro do método REPO).
                PhysGrabObject pgoRaw;
                try
                {
                    pgoRaw = SemiFunc.PhysGrabberLocalGetGrabbedPhysGrabObject();
                }
                catch (System.Exception ex)
                {
                    QuickPocketPlugin.Log.LogDebug(
                        "[QP:TryPocket] fallback=ExceptionCaught source=PhysGrabberLocal exception=" + ex.GetType().Name);
                    return;
                }
                if (pgoRaw == null)
                {
                    QuickPocketPlugin.Log.LogDebug("[QuickPocket] PhysGrabObject nulo.");
                    return;
                }

                // Resolver para o PhysGrabObject raiz da hierarquia.
                // Evita pegar componentes de fisica filhos (ex: "Rigidbody") em vez do item raiz.
                var pgo = ResolveRootPgo(pgoRaw);
                if (pgo == null)
                {
                    QuickPocketPlugin.Log.LogInfo(
                        "[QuickPocket] Rejected: Item=" + pgoRaw.gameObject.name +
                        " Reason=InvalidHeldObject");
                    return;
                }

                // Log diagnóstico precoce — confirma o par heldRaw/resolvedPgo e contexto MP/SP.
                QuickPocketPlugin.Log.LogDebug(
                    "[QP:TryPocket] heldRaw=" + pgoRaw.gameObject.name +
                    " resolvedPgo=" + pgo.gameObject.name +
                    " actor=" + (SemiFunc.IsMultiplayer() ? PhotonNetwork.LocalPlayer.ActorNumber.ToString() : "SP") +
                    " isMaster=" + SemiFunc.IsMasterClientOrSingleplayer());

                // Rejeitar componentes de fisica de entidades (isEnemy=true, sem ItemAttributes).
                // Um corpo de inimigo ragdoll nao e um item de jogo — nao deve ser classificado
                // como EnemyBlocked; o motivo correto e InvalidHeldObject.
                if (RepoCompat.GetIsEnemy(pgo) &&
                    pgo.gameObject.GetComponent<ItemAttributes>() == null)
                {
                    QuickPocketPlugin.Log.LogInfo(
                        "[QuickPocket] Rejected: Item=" + pgo.gameObject.name +
                        " Reason=InvalidHeldObject");
                    return;
                }

                var info = EligibilityChecker.Evaluate(pgo);
                if (!info.Eligible)
                {
                    // TooLarge: frase engracacada calibrada pelo ratio de tamanho
                    if (info.RejectReason == "TooLarge" && info.RejectedMaxDim > 0f)
                    {
                        float boundsLimit = QuickPocketPlugin.MaxItemBoundsSize.Value;
                        float ratio       = boundsLimit > 0f ? info.RejectedMaxDim / boundsLimit : 2f;
                        SetHudMessage(FunnyMessages.GetTooLargeMessage(ratio));
                    }
                    else
                    {
                        // Todos os outros motivos de rejeicao exibem mensagem de HUD.
                        string hudMsg = GetRejectHudMessage(info.RejectReason);
                        if (!string.IsNullOrEmpty(hudMsg))
                            SetHudMessage(hudMsg);
                    }
                    return;
                }

                // Verificar duplicatas
                if (!QuickPocketPlugin.AllowDuplicates.Value)
                {
                    foreach (int vid in _localPocket)
                    {
                        if (_snapshots.TryGetValue(vid, out var existing) &&
                            existing.ItemName == info.ItemName)
                        {
                            SetHudMessage("Ja tem um desse no bolso.");
                            QuickPocketPlugin.Log.LogInfo(
                                "[QuickPocket] Rejected: Item=" + info.ItemName + " Reason=DuplicateBlocked");
                            return;
                        }
                    }
                }

                // Obter PhotonView — pgo.photonView nao e publico; sempre via GetComponent
                var pv = pgo.gameObject.GetComponent<PhotonView>();

                if (SemiFunc.IsMultiplayer())
                {
                    // ── Multiplayer: exige PhotonView e validacao de ownership ──────
                    if (pv == null)
                    {
                        QuickPocketPlugin.Log.LogInfo(
                            "[QuickPocket] Item sem PhotonView em multiplayer — nao pode ser sincronizado.");
                        return;
                    }

                    // Log diagnóstico de ownership — rastreamento em testes multiplayer.
                    string ownerPolicy = pv.IsMine ? "IsMine" :
                        (pv.OwnershipTransfer == OwnershipOption.Takeover ? "Takeover" : "DirectFixed");
                    QuickPocketPlugin.Log.LogDebug(
                        "[QP:TryPocket] pv=" + pv.ViewID +
                        " isMine=" + pv.IsMine +
                        " owner=" + pv.OwnerActorNr +
                        " ownershipTransfer=" + pv.OwnershipTransfer +
                        " policy=" + ownerPolicy);

                    // ── Modelo host-authoritative para itens Fixed ────────────────
                    // No REPO, todos os itens têm OwnershipTransfer=Fixed owned pelo master.
                    // Clients NÃO podem aplicar ApplyHide diretamente — o master overrides via
                    // Photon sync (~50ms), desfazendo rb.isKinematic + componentes.
                    // Resultado sem fix: item "desaparece" mas voltou para o estado do master,
                    // key not found em enforcement → removido do bolso → HUD fica 0/5.
                    //
                    // Fix: cliente não-master envia EV_REQUEST_POCKET ao master.
                    // Master valida, executa SaveSnapshot+ApplyHide (ele é o owner → changes persistem),
                    // publica QP_{actor} nas Room Properties, envia EV_ACK_POCKET ao solicitante.
                    // Cliente atualiza _localPocket+HUD SOMENTE após ACK.
                    if (!SemiFunc.IsMasterClientOrSingleplayer())
                    {
                        int viewIDmp = pv.ViewID;

                        if (_localPocket.Contains(viewIDmp))
                        {
                            QuickPocketPlugin.Log.LogInfo("[QuickPocket] Item ja esta no bolso.");
                            return;
                        }

                        if (HasPendingRequest(_pendingPocketAcks, viewIDmp))
                        {
                            // Já tem request pendente para este item
                            SetHudMessage("Aguarde...");
                            return;
                        }

                        int n = ++_nonce;
                        var reqData = new Hashtable
                        {
                            { "vid",   viewIDmp },
                            { "nonce", n },
                            { "item",  info.ItemName }
                        };
                        var reqOpts = new RaiseEventOptions { Receivers = ReceiverGroup.MasterClient };
                        PhotonNetwork.RaiseEvent(EV_REQUEST_POCKET, reqData, reqOpts, SendOptions.SendReliable);

                        TrackPending(_pendingPocketAcks, n, viewIDmp, "Pocket");
                        SetHudMessage("Aguarde...");

                        int localActor = PhotonNetwork.LocalPlayer.ActorNumber;
                        QuickPocketPlugin.Log.LogInfo(
                            "[QP:MP] request pocket actor=" + localActor +
                            " viewID=" + viewIDmp +
                            " item=" + info.ItemName +
                            " nonce=" + n);
                        return;  // sair — pocket só confirmado após ACK do master
                    }

                    // Master: pode ter items Takeover (raro, mas tratar)
                    if (!pv.IsMine && pv.OwnershipTransfer == OwnershipOption.Takeover)
                    {
                        pv.RequestOwnership();
                        QuickPocketPlugin.Log.LogDebug(
                            "[QuickPocket] RequestOwnership (Takeover) para " + info.ItemName);
                    }
                }
                // else: singleplayer — sem restricao de ownership Photon

                // ── Pocket direto — master ou singleplayer ─────────────────────
                // Chave de identificação do item:
                //   Multiplayer  → pv.ViewID (compartilhado entre todos os clientes)
                //   Singleplayer → GetInstanceID() (local, único por objeto Unity)
                int viewID = SemiFunc.IsMultiplayer()
                    ? pv.ViewID
                    : pgo.gameObject.GetInstanceID();

                // Em singleplayer, armazenar referência direta para lookup posterior
                if (!SemiFunc.IsMultiplayer())
                    _singleplayerObjects[viewID] = pgo.gameObject;

                if (_localPocket.Contains(viewID))
                {
                    QuickPocketPlugin.Log.LogInfo("[QuickPocket] Item ja esta no bolso.");
                    return;
                }

                int ownerActor = pv != null ? pv.OwnerActorNr : 0;
                var snap       = PocketOperations.SaveSnapshot(pgo, viewID, ownerActor);

                if (!PocketOperations.ValidateSnapshot(snap))
                {
                    QuickPocketPlugin.Log.LogInfo(
                        "[QuickPocket] Pocket cancelado: snapshot invalido para " + info.ItemName);
                    return;
                }

                if (!PocketOperations.HidePocketedItemState(pgo.gameObject, viewID, ownerActor, HideMode.Local, "TryPocket", snap))
                {
                    QuickPocketPlugin.Log.LogInfo(
                        "[QuickPocket] Pocket cancelado: HideState falhou para " + info.ItemName);
                    return;
                }

                _localPocket.Add(viewID);
                _snapshots[viewID] = snap;

                SetHudMessage("Guardado: " + CleanItemName(info.ItemName) + " (" + _localPocket.Count + "/" + limit + ")");
                PublishPocketedItems();

                QuickPocketPlugin.Log.LogInfo(
                    "[QuickPocket] Pocketado: " + info.ItemName +
                    " (" + _localPocket.Count + "/" + limit + ")");
            }
            finally
            {
                _isPocketing   = false;
                _cooldownTimer = QuickPocketPlugin.PocketCooldown.Value;
            }
        }

        // ── TryFindSafeDropPosition ────────────────────────────────────────────
        //
        // Sistema definitivo de posicao de drop. Requer que a posicao seja
        // fisicamente valida antes de qualquer RestoreSnapshot.
        //
        // Principios arquiteturais:
        //   1. Mask = Physics.DefaultRaycastLayers (todas as layers exceto IgnoreRaycast=2).
        //      Captura chao/paredes em qualquer layer de cena — corrige o bug critico de
        //      GEOMETRY_MASK=1<<0 que ignorava layers proprietarias do REPO.
        //   2. Y do drop determinado por raycast DOWN para o chao real + offset.
        //      Elimina itens flutuando ou afundando em geometria.
        //   3. Forward da camera (nao do player.transform) — correto agachado e em pe.
        //   4. Cintura calculada por camY-0.6 quando disponivel — segue agachamento.
        //   5. Validacoes em ordem: chao, andar, espaco livre, teto, parede.
        //   6. Sem candidato valido: retorna false, item permanece no bolso (sem restore).
        //
        // preferSide=true: prioriza angulos 90/-90 (lados) — usado quando maos ocupadas.

        private bool TryFindSafeDropPosition(PlayerAvatar player, bool preferSide,
            out Vector3 position, out string failReason)
        {
            position  = Vector3.zero;
            failReason = null;

            if (Time.time - _dropCandidateResetT > DROP_RESET_SECS)
                _dropCandidateIndex = 0;

            // Mask: tudo exceto IgnoreRaycast (layer 2). Captura chao/paredes do nivel REPO.
            int mask = Physics.DefaultRaycastLayers;

            var playerPos = player.transform.position;

            // Item atualmente segurado pelo jogador — filtrado em GATE4 (nao e obstaculo)
            var grabbedPgo = SemiFunc.PhysGrabberLocalGetGrabbedPhysGrabObject();
            var grabbedGo  = (grabbedPgo != null) ? grabbedPgo.gameObject : null;

            // Forward horizontal da camera — funciona em pe e agachado
            var     cam = Camera.main;
            Vector3 forward;
            if (cam != null)
            {
                var f = cam.transform.forward;
                var h = new Vector3(f.x, 0f, f.z);
                forward = h.sqrMagnitude > 0.01f ? h.normalized : player.transform.forward;
            }
            else
            {
                forward = player.transform.forward;
            }

            // Cintura: origem do SphereCast de parede.
            // cam.y - 0.6 acompanha agachamento; fallback se camera ausente.
            float waistY   = (cam != null) ? cam.transform.position.y - 0.6f : playerPos.y + 0.7f;
            var   waistPos = new Vector3(playerPos.x, waistY, playerPos.z);

            // Chao sob o jogador — referencia de andar
            float playerFloorY;
            bool  hasFloor = Physics.Raycast(
                playerPos + Vector3.up * 0.1f, Vector3.down,
                out RaycastHit pFloor, 3.0f, mask, QueryTriggerInteraction.Ignore);
            playerFloorY = hasFloor ? pFloor.point.y : playerPos.y;

            float[] angles = preferSide ? DROP_ANGLES_SIDE : DROP_ANGLES_NORMAL;
            int     total  = angles.Length;

            for (int i = 0; i < total; i++)
            {
                int   idx   = (_dropCandidateIndex + i) % total;
                float angle = angles[idx];
                var   dir   = Quaternion.AngleAxis(angle, Vector3.up) * forward;

                // Ponto horizontal no raio de busca.
                // Y = playerPos.y + 0.8 e apenas a origem do raycast descendente —
                // nao e a posicao final do item. Elevado de 0.5 para 0.8 para garantir
                // que a origem esta acima de geometria rente ao chao (base de paredes,
                // bordas de prateleira) e evitar NoFloor falso em GATE1.
                var horizPt = new Vector3(
                    playerPos.x + dir.x * DROP_SEARCH_RADIUS,
                    playerPos.y + 0.8f,
                    playerPos.z + dir.z * DROP_SEARCH_RADIUS);

                // GATE 1 — Chao valido abaixo do ponto horizontal
                if (!Physics.Raycast(horizPt, Vector3.down, out RaycastHit candFloor,
                        DROP_GROUND_CAST, mask, QueryTriggerInteraction.Ignore))
                {
                    QuickPocketPlugin.Log.LogDebug(
                        "[QP:SafeDrop] ang=" + angle.ToString("F0") +
                        " gate=GATE1 result=FAIL reason=NoFloor" +
                        " origin=" + horizPt.ToString("F2") +
                        " castDist=" + DROP_GROUND_CAST.ToString("F1"));
                    continue;
                }
                QuickPocketPlugin.Log.LogDebug(
                    "[QP:SafeDrop] ang=" + angle.ToString("F0") +
                    " gate=GATE1 result=OK" +
                    " floor=" + candFloor.point.ToString("F2") +
                    " normal.y=" + candFloor.normal.y.ToString("F2") +
                    " layer=" + LayerMask.LayerToName(candFloor.collider.gameObject.layer));

                // GATE 1b — Superficie nao muito inclinada: normal.y >= 0.7 (aprox. 45 graus max).
                //           Impede drops em rampas onde o Rigidbody deslizaria apos o restore.
                if (candFloor.normal.y < 0.7f)
                {
                    QuickPocketPlugin.Log.LogDebug(
                        "[QP:SafeDrop] ang=" + angle.ToString("F0") + " gate=GATE1b result=FAIL reason=TooSteep normal.y=" + candFloor.normal.y.ToString("F2"));
                    continue;
                }

                // GATE 2 — Mesmo andar que o jogador (tolerancia aumentada de 0.8 para 1.5m
                //          para aceitar degraus e variacoes de piso comuns no REPO).
                float floorDelta = Mathf.Abs(candFloor.point.y - playerFloorY);
                if (floorDelta > DROP_MAX_FLOOR_DELTA)
                {
                    QuickPocketPlugin.Log.LogDebug(
                        "[QP:SafeDrop] ang=" + angle.ToString("F0") + " gate=GATE2 result=FAIL reason=FloorDelta=" + floorDelta.ToString("F2"));
                    continue;
                }

                // GATE 3 — Posicao final: sobre o chao real + offset de spawn
                var dropPos = new Vector3(horizPt.x, candFloor.point.y + DROP_ITEM_HEIGHT, horizPt.z);

                // GATE 4 — Espaco livre com filtragem de colliders irrelevantes.
                //
                // ANTES (bug): Physics.CheckSphere cego detectava colliders do PlayerAvatar
                //   local (hitbox, PhysGrabber area, etc.) como obstaculos, causando falsos
                //   bloqueios em areas abertas mesmo sem obstrucao real.
                //
                // AGORA: OverlapSphereNonAlloc com filtragem explicita:
                //   - Triggers ignorados via QueryTriggerInteraction.Ignore
                //   - Colliders do PlayerAvatar local ignorados explicitamente
                //   - Primeiro bloqueador real logado para diagnostico
                //
                // Centro em floor.y + DROP_SPHERE_R + 0.02 garante folga acima do chao.
                var gate4CheckPos = new Vector3(dropPos.x,
                    candFloor.point.y + DROP_SPHERE_R + 0.02f, dropPos.z);
                if (!IsDropSphereClear(gate4CheckPos, DROP_SPHERE_R, mask, player, grabbedGo, out string gate4Blocker))
                {
                    QuickPocketPlugin.Log.LogDebug(
                        "[QP:SafeDrop] ang=" + angle.ToString("F0") +
                        " gate=GATE4 result=FAIL reason=SphereFull blocker=" + (gate4Blocker ?? "?"));
                    continue;
                }

                // GATE 5 — Teto suficientemente alto para o item
                if (Physics.Raycast(dropPos, Vector3.up, DROP_CEILING_MIN, mask,
                        QueryTriggerInteraction.Ignore))
                {
                    QuickPocketPlugin.Log.LogDebug(
                        "[QP:SafeDrop] ang=" + angle.ToString("F0") + " gate=GATE5 result=FAIL reason=LowCeiling");
                    continue;
                }

                // GATE 6 — Parede entre jogador e ponto de drop.
                //
                // ANTES (bug): SphereCast(waistPos → dropPos) — trajetoria DIAGONAL para baixo
                //   de ~1.1m ate floor+0.2m. Com raio 0.18m, margem sobre o chao era 0.02m.
                //   Qualquer tessellation do mesh causava colisao com o proprio chao → falso negativo.
                //
                // AGORA: SphereCast HORIZONTAL na altura da cintura (waistPos.y constante).
                //   Verifica apenas se ha parede/obstaculo em linha reta entre jogador e dropPos.
                //   Nao vai ate o chao — elimina o clipping com o piso.
                var wallCheckEnd = new Vector3(dropPos.x, waistPos.y, dropPos.z);
                var toWall       = wallCheckEnd - waistPos;
                float wallDist   = toWall.magnitude;
                if (wallDist > 0.05f && Physics.SphereCast(
                        waistPos, DROP_SPHERE_R, toWall.normalized,
                        out _, wallDist, mask, QueryTriggerInteraction.Ignore))
                {
                    QuickPocketPlugin.Log.LogDebug(
                        "[QP:SafeDrop] ang=" + angle.ToString("F0") + " gate=GATE6 result=FAIL reason=WallBlocked");
                    continue;
                }

                // Candidato valido
                _dropCandidateIndex  = (idx + 1) % total;
                _dropCandidateResetT = Time.time;
                position = dropPos;
                QuickPocketPlugin.Log.LogInfo(
                    "[QP:SafeDrop] ang=" + angle.ToString("F0") + " result=OK pos=" + dropPos.ToString("F2"));
                return true;
            }

            failReason = "Sem espaco para soltar aqui.";
            QuickPocketPlugin.Log.LogInfo("[QuickPocket] Drop bloqueado: sem candidato valido.");
            return false;
        }

        // ── IsDropSphereClear ─────────────────────────────────────────────────
        //
        // Verifica se há espaço físico livre para dropar o item no ponto 'center'.
        // Usa OverlapSphereNonAlloc com filtragem explícita para evitar falsos bloqueios:
        //
        //   - Triggers: excluídos por QueryTriggerInteraction.Ignore.
        //   - Layer RoomVolume: volumes de sala do REPO que preenchem salas inteiras.
        //     São colisores sólidos (não-triggers) mas NÃO são obstáculos físicos para drop.
        //     Causa raiz confirmada do bug GATE4 falso-positivo (log: "Extra (3) layer:RoomVolume").
        //   - Colliders do PlayerAvatar local: jogador está ali por definição.
        //   - Colliders do item atualmente segurado: item nas mãos do jogador, não é obstáculo.
        //
        // blockerName: "NAME layer=LAYER path=ROOT/CHILD" para diagnóstico em log.
        private static bool IsDropSphereClear(
            Vector3 center, float radius, int mask,
            PlayerAvatar player, GameObject grabbedGo, out string blockerName)
        {
            blockerName = null;
            EnsureLayersInitialized();

            int hitCount = Physics.OverlapSphereNonAlloc(
                center, radius, _overlapBuffer, mask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hitCount; i++)
            {
                var col = _overlapBuffer[i];
                if (col == null) continue;

                var colGo    = col.gameObject;
                int colLayer = colGo.layer;

                // Filtrar: layer RoomVolume — volumes de sala REPO, nao sao obstaculos fisicos
                if (_layerRoomVolume >= 0 && colLayer == _layerRoomVolume) continue;

                // Filtrar: colliders do PlayerAvatar local
                if (player != null && col.transform.IsChildOf(player.transform)) continue;

                // Filtrar: colliders do item atualmente segurado pelo jogador
                if (grabbedGo != null && col.transform.IsChildOf(grabbedGo.transform)) continue;

                // Collider solido relevante — obstaculo real
                blockerName = colGo.name +
                              " layer=" + LayerMask.LayerToName(colLayer) +
                              " path=" + BuildShortPath(col.transform);
                return false;
            }

            return true;
        }

        // "Root/Child" — identifica o objeto raiz e o filho direto do collider.
        private static string BuildShortPath(Transform t)
        {
            if (t == null) return "?";
            var root = t.root;
            return (root != t) ? root.name + "/" + t.name : t.name;
        }

        // Inicialização lazy das layers REPO. LayerMask.NameToLayer retorna -1 se
        // a layer não existir — as condições `>= 0` evitam filtro acidental.
        private static void EnsureLayersInitialized()
        {
            if (_layersInitialized) return;
            _layersInitialized = true;
            _layerRoomVolume   = LayerMask.NameToLayer("RoomVolume");
            QuickPocketPlugin.Log.LogDebug(
                "[QuickPocket] Layers inicializadas: RoomVolume=" + _layerRoomVolume);
        }

        // ── Confirmacao diferida de drop ──────────────────────────────────────
        //
        // Aguarda 0.3s apos RestoreSnapshot para verificar se o item sobreviveu.
        // Somente entao remove do bolso e exibe "Retirado" no HUD.
        // Se o REPO destruiu o item (raro, mas possivel), loga warning e limpa estado.
        //
        // Enquanto esta coroutine estiver ativa, EnforcePocketState ignora o viewID
        // (o item estara visivelmente restaurado — forcar hide seria incorreto).

        // Limites de validacao de posicao pós-drop.
        // Se o item se afastou demais do ponto escolhido em 0.3s: drop falhou.
        private const float DROP_CONFIRM_MAX_DIST_CHOSEN = 1.0f;  // distancia do ponto escolhido
        private const float DROP_CONFIRM_MAX_DIST_PLAYER = 2.5f;  // distancia do jogador
        private const float DROP_CONFIRM_MAX_DELTA_Y     = 1.0f;  // delta vertical

        // Coroutine de confirmacao de drop:
        //   1. Aguarda 0.3s para o item se estabilizar.
        //   2. Verifica sobrevivencia E posicao.
        //   3. Se OK: remove do bolso, exibe "Retirado".
        //   4. Se FAIL: re-aplica hide (item volta ao bolso), exibe mensagem.
        //
        // Item so sai de _localPocket apos resultado OK.
        private IEnumerator ConfirmAndFinalizeDrop(
            GameObject go, ComponentSnapshot snap, int viewID,
            Vector3 chosenDropPos, Vector3 playerPosAtDrop, Vector3 originalPos)
        {
            yield return new WaitForSeconds(0.3f);

            _pendingRestore.Remove(viewID);

            // --- Verificar sobrevivencia ---
            bool survived = (go != null);

            if (!survived)
            {
                // Item foi destruido pelo REPO — limpar estado sem re-pocket
                _localPocket.Remove(viewID);
                _snapshots.Remove(viewID);
                _singleplayerObjects.Remove(viewID);
                SetHudMessage("Drop falhou. Tente em outro lugar.");
                QuickPocketPlugin.Log.LogWarning(
                    "[QuickPocket] Drop FALHOU: " + snap.ItemName + " destruido apos 0.3s.");
                PublishPocketedItems();
                yield break;
            }

            // --- Verificar posicao final ---
            var   finalPos   = go.transform.position;
            float distChosen = Vector3.Distance(finalPos, chosenDropPos);
            float distPlayer = Vector3.Distance(finalPos, playerPosAtDrop);
            float deltaY     = Mathf.Abs(finalPos.y - chosenDropPos.y);

            bool posOk = distChosen <= DROP_CONFIRM_MAX_DIST_CHOSEN
                      && distPlayer <= DROP_CONFIRM_MAX_DIST_PLAYER
                      && deltaY     <= DROP_CONFIRM_MAX_DELTA_Y;

            // Log de identidade: original → escolhido → final
            QuickPocketPlugin.Log.LogInfo(
                "[QP:DropConfirm] item=" + snap.ItemName +
                " viewID=" + viewID +
                " original=" + originalPos.ToString("F2") +
                " chosen=" + chosenDropPos.ToString("F2") +
                " final=" + finalPos.ToString("F2") +
                " distChosen=" + distChosen.ToString("F2") +
                " distPlayer=" + distPlayer.ToString("F2") +
                " deltaY=" + deltaY.ToString("F2") +
                " result=" + (posOk ? "OK" : "FAIL"));

            if (!posOk)
            {
                // Item se afastou do ponto escolhido — re-aplicar hide.
                // Item permanece em _localPocket para proxima tentativa.
                PocketOperations.ApplyHide(go, snap);
                SetHudMessage("Drop falhou. Tente em outro lugar.");
                QuickPocketPlugin.Log.LogWarning(
                    "[QuickPocket] Drop FALHOU: " + snap.ItemName +
                    " moveu " + distChosen.ToString("F2") + "m — re-pocketado.");
                yield break;
            }

            // --- Log de estado fisico pos-restore (somente quando posOk=true) ---
            // Confirma que colliders/rb/layer foram restaurados corretamente — diagnostico
            // de itens atravessaveis/nao-interagiveis reportados em teste multiplayer.
            var rbFinal  = go.GetComponent<Rigidbody>();
            var colFinal = go.GetComponentInChildren<Collider>();
            QuickPocketPlugin.Log.LogDebug(
                "[QP:DropConfirm] item=" + snap.ItemName +
                " result=OK" +
                " layer=" + LayerMask.LayerToName(go.layer) +
                " colliderEnabled=" + (colFinal != null ? colFinal.enabled.ToString() : "none") +
                " detectCollisions=" + (rbFinal  != null ? rbFinal.detectCollisions.ToString() : "none") +
                " isKinematic=" + (rbFinal  != null ? rbFinal.isKinematic.ToString() : "none") +
                " useGravity=" + (rbFinal  != null ? rbFinal.useGravity.ToString() : "none"));

            // --- Drop confirmado: finalizar remocao do bolso ---
            _localPocket.Remove(viewID);
            _snapshots.Remove(viewID);
            _singleplayerObjects.Remove(viewID);

            int newCount = _localPocket.Count;
            int curLimit = GetEffectiveLimit();
            SetHudMessage("Retirado: " + CleanItemName(snap.ItemName) +
                          " (" + newCount + "/" + curLimit + ")");
            QuickPocketPlugin.Log.LogInfo(
                "[QuickPocket] Drop confirmado: " + snap.ItemName +
                " (" + newCount + "/" + curLimit + ")");
            PublishPocketedItems();
        }

        // Sobe a hierarquia a partir do PGO detectado para encontrar o PhysGrabObject raiz.
        // Necessario porque em alguns objetos (ex: ragdolls de inimigos) o PGO detectado
        // pelo PhysGrabber e um filho da hierarquia real do item.
        private static PhysGrabObject ResolveRootPgo(PhysGrabObject pgo)
        {
            if (pgo == null) return null;
            PhysGrabObject root = pgo;
            var t = pgo.transform.parent;
            while (t != null)
            {
                var parentPgo = t.GetComponent<PhysGrabObject>();
                if (parentPgo != null) root = parentPgo;
                t = t.parent;
            }
            return root;
        }

        // ── TryUnpocket ───────────────────────────────────────────────────────

        private void TryUnpocket()
        {
            _isPocketing = true;
            try
            {
                int viewID = _localPocket[_localPocket.Count - 1];

                // Nao processar item aguardando confirmacao de drop anterior
                if (_pendingRestore.Contains(viewID))
                    return;

                // ── Item hospedado pelo master (sem snapshot local) ────────────
                // Este cliente pocketou via EV_REQUEST_POCKET/ACK. O master tem o snapshot.
                // Drop deve ser solicitado ao master via EV_REQUEST_DROP.
                if (_clientHostedItems.Contains(viewID))
                {
                    // Verificar se já tem request de drop pendente para este item
                    if (HasPendingRequest(_pendingDropAcks, viewID))
                    {
                        SetHudMessage("Aguarde...");
                        return;
                    }

                    int n = ++_nonce;

                    // Incluir posição local do cliente no request — usada pelo host como
                    // fallback diagnóstico caso GetPositionByActorNumber não encontre o avatar.
                    var playerLocal  = SemiFunc.PlayerAvatarLocal();
                    Vector3 localPos = playerLocal != null ? playerLocal.transform.position : Vector3.zero;

                    var reqData = new Hashtable
                    {
                        { "vid",   viewID },
                        { "nonce", n },
                        { "cpx",   localPos.x },
                        { "cpy",   localPos.y },
                        { "cpz",   localPos.z }
                    };
                    var reqOpts = new RaiseEventOptions { Receivers = ReceiverGroup.MasterClient };
                    PhotonNetwork.RaiseEvent(EV_REQUEST_DROP, reqData, reqOpts, SendOptions.SendReliable);

                    TrackPending(_pendingDropAcks, n, viewID, "Drop");
                    SetHudMessage("Aguarde...");

                    int localActor = PhotonNetwork.LocalPlayer.ActorNumber;
                    QuickPocketPlugin.Log.LogInfo(
                        "[QP:MP] request drop actor=" + localActor +
                        " viewID=" + viewID +
                        " nonce=" + n +
                        " clientPos=" + localPos.ToString("F2"));
                    return;
                }

                var go = FindPocketedObject(viewID);
                if (go == null)
                {
                    QuickPocketPlugin.Log.LogInfo(
                        "[QuickPocket] Item (key=" + viewID + ") nao encontrado — removido do bolso.");
                    _localPocket.RemoveAt(_localPocket.Count - 1);
                    _snapshots.Remove(viewID);
                    _singleplayerObjects.Remove(viewID);
                    PublishPocketedItems();
                    return;
                }

                if (!_snapshots.TryGetValue(viewID, out var snap))
                {
                    QuickPocketPlugin.Log.LogInfo(
                        "[QuickPocket] Snapshot ausente para key=" + viewID + " — removido.");
                    _localPocket.RemoveAt(_localPocket.Count - 1);
                    _singleplayerObjects.Remove(viewID);
                    PublishPocketedItems();
                    return;
                }

                var  player        = SemiFunc.PlayerAvatarLocal();
                bool handsOccupied = SemiFunc.PhysGrabberLocalIsGrabbing();

                if (handsOccupied && !QuickPocketPlugin.AutoDropIfHandsFull.Value)
                {
                    QuickPocketPlugin.Log.LogInfo(
                        "[QuickPocket] Solte o que esta segurando antes de retirar do bolso.");
                    return;
                }

                // Calcular posicao segura ANTES de restaurar.
                // Se nao houver espaco valido, item permanece no bolso — sem restore.
                if (!TryFindSafeDropPosition(player, preferSide: handsOccupied,
                        out Vector3 targetPos, out string blockMsg))
                {
                    SetHudMessage(blockMsg ?? "Sem espaco para soltar aqui.");
                    return;
                }

                if (handsOccupied)
                    QuickPocketPlugin.Log.LogInfo("[QuickPocket] Maos ocupadas — item dropado proximo.");

                // Capturar posicao original do item (antes do restore) e do jogador —
                // ambas usadas em ConfirmAndFinalizeDrop para identidade e validacao de posicao.
                Vector3 originalPos     = go.transform.position;
                Vector3 playerPosAtDrop = player != null ? player.transform.position : Vector3.zero;

                PocketOperations.RestoreSnapshot(go, snap, targetPos);

                // Log apos RestoreSnapshot para capturar posicao real pos-restore.
                // afterTransform e afterRb confirmam se rb.position foi sincronizado corretamente.
                var rbPost = go.GetComponent<Rigidbody>();
                QuickPocketPlugin.Log.LogInfo(
                    "[QP:Restore] item=" + snap.ItemName +
                    " key=" + viewID +
                    " original=" + originalPos.ToString("F2") +
                    " target=" + targetPos.ToString("F2") +
                    " afterTransform=" + go.transform.position.ToString("F2") +
                    " afterRb=" + (rbPost != null ? rbPost.position.ToString("F2") : "no-rb"));

                // Adiar remocao do bolso ate validar posicao apos 0.3s (sobrevivencia + posicao).
                // EnforcePocketState ignora este viewID enquanto pendente.
                _pendingRestore.Add(viewID);
                StartCoroutine(ConfirmAndFinalizeDrop(go, snap, viewID, targetPos, playerPosAtDrop, originalPos));
            }
            finally
            {
                _isPocketing   = false;
                _cooldownTimer = QuickPocketPlugin.PocketCooldown.Value;
            }
        }

        // ── Enforcement local ─────────────────────────────────────────────────

        private void EnforcePocketState()
        {
            for (int i = _localPocket.Count - 1; i >= 0; i--)
            {
                int key = _localPocket[i];

                // Nao interferir com itens aguardando confirmacao de drop (coroutine ativa)
                if (_pendingRestore.Contains(key)) continue;

                var go  = FindPocketedObject(key);

                if (go == null)
                {
                    // Objeto destruído ou não encontrado — remover do bolso.
                    // Ocultar o SpriteRenderer do MapValuable marker (sem SetActive para não matar Logic()).
                    // Quando o item é destruído, Logic() vai detectar target==null e terminar a coroutine.
                    // O SpriteRenderer precisa ser desabilitado manualmente para o marker desaparecer do mapa.
                    if (_snapshots.TryGetValue(key, out var sn))
                    {
                        if (sn.MapValuableSpriteRenderer != null)
                        {
                            sn.MapValuableSpriteRenderer.enabled = false;
                            QuickPocketPlugin.Log.LogDebug(
                                "[QP:MapMarker] item destroyed, marker hidden item=" + sn.ItemName);
                        }
                        else if (sn.MapValuableMarker != null && sn.MapValuableMarker.gameObject != null)
                        {
                            // Fallback: SpriteRenderer não cacheado — usar SetActive
                            sn.MapValuableMarker.gameObject.SetActive(false);
                            QuickPocketPlugin.Log.LogDebug(
                                "[QP:MapMarker] item destroyed, marker hidden (SetActive fallback) item=" + sn.ItemName);
                        }
                    }
                    _localPocket.RemoveAt(i);
                    _snapshots.Remove(key);
                    _singleplayerObjects.Remove(key);
                    PublishPocketedItems();
                    continue;
                }

                // Itens hospedados pelo master: sem snapshot local no cliente.
                // O master é o único com o snapshot e autoridade de ApplyHide.
                // Não tentar enforce local — o master já enforce no lado dele.
                if (_clientHostedItems.Contains(key))
                {
                    if (!_remoteSnapshots.TryGetValue(key, out var hostedSnap))
                    {
                        int localActor = SemiFunc.IsMultiplayer() ? PhotonNetwork.LocalPlayer.ActorNumber : 0;
                        hostedSnap = SaveLocalSnapshotFor(go, key, localActor);
                        if (hostedSnap != null) _remoteSnapshots[key] = hostedSnap;
                    }

                    if (hostedSnap != null && !PocketOperations.IsPocketStateConsistent(go, hostedSnap))
                    {
                        QuickPocketPlugin.Log.LogDebug(
                            "[QP:EnforceState] viewID=" + key + " fixed=ClientHosted reason=OwnerAck");
                        int localActor = SemiFunc.IsMultiplayer() ? PhotonNetwork.LocalPlayer.ActorNumber : 0;
                        PocketOperations.HidePocketedItemState(go, key, localActor, HideMode.OwnerAck, "EnforceClientHosted", hostedSnap);
                    }
                    continue;
                }

                if (!_snapshots.TryGetValue(key, out var snap)) continue;

                bool consistent = PocketOperations.IsPocketStateConsistent(go, snap);
                if (!consistent)
                {
                    QuickPocketPlugin.Log.LogDebug(
                        "[QP:EnforceState] viewID=" + key + " fixed=Local reason=SnapshotInconsistent");
                    PocketOperations.HidePocketedItemState(go, key, snap.ActorNumber, HideMode.Local, "EnforceLocal", snap);
                }
            }
            EnforceHostedPocketState();
            EnforceRemotePocketState();
        }

        private void EnforceHostedPocketState()
        {
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;

            foreach (var kv in _hostedPockets)
            {
                int actor = kv.Key;
                var list = kv.Value;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    int vid = list[i].vid;
                    var snap = list[i].snap;
                    var pv = PhotonView.Find(vid);
                    if (pv == null || pv.gameObject == null || snap == null) continue;

                    if (!PocketOperations.IsPocketStateConsistent(pv.gameObject, snap))
                    {
                        QuickPocketPlugin.Log.LogDebug(
                            "[QP:EnforceState] viewID=" + vid + " fixed=HostHosted actor=" + actor);
                        PocketOperations.HidePocketedItemState(pv.gameObject, vid, actor, HideMode.HostHosted, "EnforceHosted", snap);
                    }
                }
            }
        }

        private void EnforceRemotePocketState()
        {
            foreach (var kv in _remoteHides)
            {
                int actor = kv.Key;
                var list = kv.Value;
                for (int i = 0; i < list.Count; i++)
                {
                    int vid = list[i];
                    if (!_remoteSnapshots.TryGetValue(vid, out var snap)) continue;

                    var pv = PhotonView.Find(vid);
                    if (pv == null || pv.gameObject == null) continue;

                    if (!PocketOperations.IsPocketStateConsistent(pv.gameObject, snap))
                    {
                        QuickPocketPlugin.Log.LogDebug(
                            "[QP:EnforceState] viewID=" + vid + " fixed=Remote actor=" + actor);
                        PocketOperations.HidePocketedItemState(pv.gameObject, vid, actor, HideMode.Remote, "EnforceRemote", snap);
                    }
                }
            }
        }

        // ── Estado remoto ─────────────────────────────────────────────────────

        // Aplica hide a um item de outro jogador (sem snapshot — nao somos donos).
        private void HideRemoteItem(int viewID, GameObject go)
        {
            HideRemoteItem(viewID, go, 0, HideMode.Remote, "LegacyRemoteHide");
            /*
            if (go == null) return;

            var rb = go.GetComponent<Rigidbody>();
            if (rb != null)
            {
                // Guard kinematic: zerar velocity apenas se não for kinematic.
                // Se o host já aplicou ApplyHide (kinematic=true) e o estado foi sincronizado
                // por Photon, setar velocity em corpo kinematic gera Warning Unity.
                if (!rb.isKinematic)
                {
                    rb.velocity        = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                rb.isKinematic      = true;
                rb.useGravity       = false;
                rb.detectCollisions = false;
            }

            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                if (r != null) r.enabled = false;

            foreach (var c in go.GetComponentsInChildren<Collider>(true))
                if (c != null) c.enabled = false;

            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t != null) t.gameObject.layer = 2;  // Ignore Raycast

            // AudioSources — parar sons residuais
            foreach (var src in go.GetComponentsInChildren<AudioSource>(true))
                if (src != null) { src.Stop(); src.enabled = false; }

            // ParticleSystems — parar emissão
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            // TrailRenderers — limpar rastro
            foreach (var tr in go.GetComponentsInChildren<TrailRenderer>(true))
                if (tr != null) tr.Clear();

            // Lights — desativar
            foreach (var lt in go.GetComponentsInChildren<Light>(true))
                if (lt != null) lt.enabled = false;

            var pos = go.transform.position;
            go.transform.position = new Vector3(pos.x, -9999f, pos.z);

            QuickPocketPlugin.Log.LogDebug("[QP] HideRemoteItem ViewID=" + viewID);
            */
        }

        // Restaura item sem snapshot (fallback seguro — estados padrao do Unity).
        private void RestoreWithSafeDefaults(GameObject go, Vector3 position)
        {
            PocketOperations.RestorePocketedItemState(go, 0, 0, RestoreMode.SafeDefaults, position, "RestoreWithSafeDefaults", null);
            /*
            if (go == null) return;

            // Layer default (0 = Default)
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t != null) t.gameObject.layer = 0;

            go.transform.position = position;

            foreach (var c in go.GetComponentsInChildren<Collider>(true))
                if (c != null) c.enabled = true;

            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                if (r != null) r.enabled = true;

            var rb = go.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic      = false;
                rb.useGravity       = true;
                rb.detectCollisions = true;
                rb.velocity         = Vector3.zero;
                rb.angularVelocity  = Vector3.zero;
            }

            QuickPocketPlugin.Log.LogDebug("[QP] RestoreWithSafeDefaults em " + go.name);
            */
        }

        // Restaura visuais (renderers/colliders/rb) de um item remoto SEM alterar posição.
        //
        // Usado em ProcessRemotePocketUpdate quando outro jogador dropa um item.
        // O host autoritativo já posicionou o item via RestoreSnapshot — Photon sync
        // propaga a posição correta para este cliente em ~50ms. Não devemos sobrescrever
        // essa posição com GetSafeDropPosition() (posição local do viewer).
        //
        // Diferença de RestoreWithSafeDefaults: não seta go.transform.position.
        private void RestoreRemoteVisuals(GameObject go)
        {
            int viewID = 0;
            var pv = go != null ? go.GetComponent<PhotonView>() : null;
            if (pv != null) viewID = pv.ViewID;
            RestoreRemoteItem(viewID, go, 0, RestoreMode.Remote, null, "RestoreRemoteVisuals");
            /*
            if (go == null) return;

            // Restaurar layer para Default (0) em toda a hierarquia
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t != null) t.gameObject.layer = 0;

            // Re-habilitar colliders
            foreach (var c in go.GetComponentsInChildren<Collider>(true))
                if (c != null) c.enabled = true;

            // Re-habilitar renderers (inclui SpriteRenderer do MapValuable se na hierarquia)
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                if (r != null) r.enabled = true;

            // Liberar Rigidbody — posição NÃO alterada (Photon sync trata isso)
            var rb = go.GetComponent<Rigidbody>();
            if (rb != null)
            {
                if (rb.isKinematic)
                {
                    rb.isKinematic = false;
                    rb.useGravity  = true;
                }
                rb.detectCollisions = true;
            }

            // Reativar AudioSources (para itens com som)
            foreach (var src in go.GetComponentsInChildren<AudioSource>(true))
                if (src != null) src.enabled = true;

            QuickPocketPlugin.Log.LogDebug("[QP] RestoreRemoteVisuals em " + go.name);
            */
        }

        // Compara lista nova com lista anterior e aplica diffs (hide/restore).
        private void ProcessRemotePocketUpdate(int actorNumber, int[] newViewIDs)
        {
            // HOTFIX v1.0.10 — Master early return.
            //
            // Causa raiz do drop no local errado:
            //   HandleHostDropRequest chama RestoreSnapshot(go, dropPos) → posição correta.
            //   Depois publica QP_{actor}=[] → OnRoomPropertiesUpdate → aqui.
            //   "ViewIDs removidos" chamaria RestoreWithSafeDefaults(go, GetSafeDropPosition()).
            //   GetSafeDropPosition() = PlayerAvatarLocal().position = posição DO HOST.
            //   Resultado: item teleporta para o host, sobrescrevendo o restore correto.
            //
            // O master gerencia todos os pockets via _hostedPockets diretamente.
            // HideRemoteItem/RestoreWithSafeDefaults aqui seriam operações redundantes
            // (pocket: ApplyHide já fez; drop: RestoreSnapshot já fez) E com posição errada.
            // _remoteHides no master não é necessário — _hostedPockets cobre os mesmos itens,
            // e OnPlayerLeftRoom os restaura via _hostedPockets antes de verificar _remoteHides.
            if (SemiFunc.IsMasterClientOrSingleplayer()) return;

            _remoteHides.TryGetValue(actorNumber, out var oldList);
            oldList = oldList ?? new List<int>();

            var newList = newViewIDs != null ? new List<int>(newViewIDs) : new List<int>();

            var oldSet = new HashSet<int>(oldList);
            var newSet = new HashSet<int>(newList);

            // ViewIDs adicionados → esconder
            foreach (int vid in newSet)
            {
                if (oldSet.Contains(vid)) continue;

                var pv = PhotonView.Find(vid);
                if (pv != null)
                {
                    HideRemoteItem(vid, pv.gameObject, actorNumber, HideMode.Remote, "RoomProperties");
                }
                else
                {
                    // Item ainda nao instanciado — agendar tentativa
                    _pendingLateJoin[vid] = Time.time + LATE_JOIN_TIMEOUT;
                    _pendingLateJoinActors[vid] = actorNumber;
                    QuickPocketPlugin.Log.LogDebug(
                        "[QP] LateJoin: ViewID=" + vid + " pendente.");
                }
            }

            // ViewIDs removidos → restaurar visuais SEM alterar posição.
            //
            // O host já posicionou o item via RestoreSnapshot — Photon sync propaga
            // a posição correta para este cliente em ~50ms. Chamar RestoreWithSafeDefaults
            // com GetSafeDropPosition() (posição local do viewer) teleportaria o item
            // para a posição errada antes do sync do host chegar.
            // RestoreRemoteVisuals re-habilita renderers/colliders/rb sem tocar na posição.
            foreach (int vid in oldSet)
            {
                if (newSet.Contains(vid)) continue;

                _pendingLateJoin.Remove(vid);
                _pendingLateJoinActors.Remove(vid);
                var pv = PhotonView.Find(vid);
                if (pv != null)
                    RestoreRemoteItem(vid, pv.gameObject, actorNumber, RestoreMode.Remote, null, "RoomProperties");
            }

            if (newList.Count > 0)
                _remoteHides[actorNumber] = newList;
            else
                _remoteHides.Remove(actorNumber);
        }

        // Retenta itens remotos cujo PhotonView ainda nao estava disponivel.
        private void ProcessPendingLateJoin()
        {
            var toRemove = new List<int>();

            foreach (var kvp in _pendingLateJoin)
            {
                if (Time.time > kvp.Value)
                {
                    QuickPocketPlugin.Log.LogDebug(
                        "[QP] LateJoin: timeout para ViewID=" + kvp.Key + " — desistindo.");
                    toRemove.Add(kvp.Key);
                    continue;
                }

                var pv = PhotonView.Find(kvp.Key);
                if (pv != null)
                {
                    int actorNumber = _pendingLateJoinActors.TryGetValue(kvp.Key, out var actor) ? actor : 0;
                    HideRemoteItem(kvp.Key, pv.gameObject, actorNumber, HideMode.LateJoin, "LateJoin");
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (int id in toRemove)
            {
                _pendingLateJoin.Remove(id);
                _pendingLateJoinActors.Remove(id);
            }
        }

        // Le todo o estado de bolso de outros jogadores das Room Properties.
        private void ProcessLateJoin()
        {
            if (_lateJoinProcessed) return;
            _lateJoinProcessed = true;

            ReadHostLimit();

            if (!PhotonNetwork.InRoom) return;

            int localActor = PhotonNetwork.LocalPlayer.ActorNumber;
            var props      = PhotonNetwork.CurrentRoom.CustomProperties;

            foreach (object key in props.Keys)
            {
                string propKey = key as string;
                if (propKey == null) continue;
                if (propKey == PROP_HOST_MAX_SLOTS || propKey == PROP_USE_HOST_LIMIT) continue;
                if (!propKey.StartsWith("QP_")) continue;
                if (!int.TryParse(propKey.Substring(3), out int actorNumber)) continue;
                if (actorNumber == localActor) continue;

                int[] viewIDs = props[key] as int[];
                if (viewIDs == null || viewIDs.Length == 0) continue;

                ProcessRemotePocketUpdate(actorNumber, viewIDs);
                QuickPocketPlugin.Log.LogDebug(
                    "[QP] LateJoin: actor=" + actorNumber + " tem " + viewIDs.Length + " item(s) pocketado(s).");
            }
        }

        // ── Publicacao de Room Properties ─────────────────────────────────────

        private void PublishPocketedItems()
        {
            if (!SemiFunc.IsMultiplayer()) return;
            if (!PhotonNetwork.InRoom) return;
            // SyncPocketState=false: bolso local apenas — nao publicar estado na sala
            if (!QuickPocketPlugin.SyncPocketState.Value) return;

            int    actor = PhotonNetwork.LocalPlayer.ActorNumber;
            var    props = new Hashtable { { PropKeyForActor(actor), _localPocket.ToArray() } };
            PhotonNetwork.CurrentRoom.SetCustomProperties(props);
        }

        // Limpa apenas o bolso local (mantendo estado remoto e limite sincronizado).
        private void ClearLocalPocket(bool publishToRoom)
        {
            if (publishToRoom)
            {
                // Publicar array vazio antes de limpar a lista
                if (SemiFunc.IsMultiplayer() && PhotonNetwork.InRoom)
                {
                    int   actor = PhotonNetwork.LocalPlayer.ActorNumber;
                    var   props = new Hashtable { { PropKeyForActor(actor), new int[0] } };
                    PhotonNetwork.CurrentRoom.SetCustomProperties(props);
                }
            }

            foreach (int key in _localPocket)
                _remoteSnapshots.Remove(key);

            ClearPendingRequests("ClearLocalPocket", hudReset: true);
            _localPocket.Clear();
            _snapshots.Clear();
            _singleplayerObjects.Clear();
            _pendingRestore.Clear();
            _clientHostedItems.Clear();
            _isPocketing   = false;
            _cooldownTimer = 0f;
            EligibilityChecker.ClearCache();
        }

        // Limpa TODO o estado (usado em OnLeftRoom e MainMenu).
        private void ClearAllState()
        {
            _localPocket.Clear();
            _snapshots.Clear();
            _singleplayerObjects.Clear();
            _pendingRestore.Clear();
            _remoteHides.Clear();
            _pendingLateJoin.Clear();
            _pendingLateJoinActors.Clear();
            _hostedPockets.Clear();
            _clientHostedItems.Clear();
            _remoteSnapshots.Clear();
            ClearPendingRequests("ClearAllState", hudReset: true);
            _nonce                    = 0;
            _isPocketing              = false;
            _cooldownTimer            = 0f;
            _limitSynced              = false;
            _cachedEffectiveLimit     = -1;
            _lateJoinProcessed        = false;
            _hudGeneration++;
            _managerGeneration          = _hudGeneration;  // acompanhar nova geração
            _hudGameplayReady           = false;  // HUD desativado
            _hudSuppressedForTransition = true;   // Fail-closed: requer confirmacao de nivel
            _runIsLevelTrueStreak       = 0;      // Resetar stability gate
            _loadingAnimDone            = false;  // aguardar evento de anim
            _loadingUIWasSeen           = false;  // novo ciclo: aguardar LoadingUI
            EligibilityChecker.ClearCache();
            QuickPocketPlugin.Log.LogDebug(
                "[QuickPocket] Estado limpo. gen=" + _hudGeneration +
                " _hudGameplayReady=false _hudSuppressedForTransition=true");
        }

        // ── Handlers chamados pelos patches ────────────────────────────────────

        internal void HandleDeath(Vector3 deathPos)
        {
            if (_localPocket.Count == 0)
            {
                EligibilityChecker.ClearCache();
                return;
            }

            if (QuickPocketPlugin.DropOnDeath.Value)
            {
                Vector3 dropPos = deathPos + Vector3.up * 0.5f;
                foreach (int key in _localPocket)
                {
                    // Itens hospedados: solicitar drop ao master
                    if (_clientHostedItems.Contains(key))
                    {
                        if (SemiFunc.IsMultiplayer() && PhotonNetwork.InRoom)
                        {
                            SendHostedDropRequest(key, deathPos, "Death", trackPending: true);
                        }
                        continue;
                    }
                    var go = FindPocketedObject(key);
                    if (go == null) continue;
                    if (_snapshots.TryGetValue(key, out var snap))
                        PocketOperations.RestoreSnapshot(go, snap, dropPos);
                }
                QuickPocketPlugin.Log.LogInfo(
                    "[QuickPocket] Morte: " + _localPocket.Count + " item(s) dropado(s).");
            }
            else
            {
                QuickPocketPlugin.Log.LogInfo(
                    "[QuickPocket] Morte: estado limpo (DropOnDeath=false — itens restaurados pelos outros clientes).");
            }

            ClearLocalPocket(publishToRoom: true);
        }

        internal void HandleLevelChange()
        {
            // Idempotency guard: RunManager.ChangeLevel é chamado múltiplas vezes por
            // transição no REPO (confirmado em log: ~300x durante Arena Race).
            // _hudSuppressedForTransition == false apenas durante gameplay ativo.
            // Já estando true = transição já foi processada nesta janela → ignorar chamada duplicada.
            if (_hudSuppressedForTransition)
                return;

            // Desativar HUD imediatamente — loading screen começa aqui.
            // Incrementar geração: qualquer manager com geração anterior fica stale.
            _hudGeneration++;
            _managerGeneration          = _hudGeneration;  // este manager acompanha a nova geração
            _hudGameplayReady           = false;
            _hudSuppressedForTransition = true;
            _runIsLevelTrueStreak       = 0;
            _loadingAnimDone            = false;
            _loadingUIWasSeen           = false;  // novo ciclo: aguardar LoadingUI neste ciclo
            QuickPocketPlugin.Log.LogDebug(
                "[QP:HUDState] gen=" + _hudGeneration + " allowed=false reason=LevelChangeStart");

            if (_localPocket.Count == 0)
            {
                EligibilityChecker.ClearCache();
                return;
            }

            // Aviso breve: o HUD pode nao ser visivel por muito tempo durante a transicao
            // de fase, mas e melhor exibir do que nao exibir.
            // TODO (Fase 5): investigar se ha evento pre-transicao mais adequado para o aviso.
            SetHudMessage("Voce ainda tem itens no bolso!");
            QuickPocketPlugin.Log.LogInfo(
                "[QuickPocket] Fase mudando com " + _localPocket.Count + " item(s) no bolso.");

            if (QuickPocketPlugin.DropOnLevelChange.Value)
            {
                Vector3 dropPos = GetSafeDropPosition();
                foreach (int key in _localPocket)
                {
                    // Itens hospedados: solicitar drop ao master antes do clear
                    if (_clientHostedItems.Contains(key))
                    {
                        if (SemiFunc.IsMultiplayer() && PhotonNetwork.InRoom)
                        {
                            SendHostedDropRequest(key, dropPos, "LevelChange", trackPending: true);
                        }
                        continue;
                    }
                    var go = FindPocketedObject(key);
                    if (go == null) continue;
                    if (_snapshots.TryGetValue(key, out var snap))
                        PocketOperations.RestoreSnapshot(go, snap, dropPos);
                }
                QuickPocketPlugin.Log.LogInfo(
                    "[QuickPocket] Fase mudou: " + _localPocket.Count + " item(s) dropado(s).");
            }
            else
            {
                QuickPocketPlugin.Log.LogInfo(
                    "[QuickPocket] Fase mudou: estado limpo (DropOnLevelChange=false).");
            }

            ClearLocalPocket(publishToRoom: true);
        }

        internal void HandleMainMenu()
        {
            QuickPocketPlugin.Log.LogDebug("[QuickPocket] Retorno ao menu — limpando estado.");
            // Sala ja foi destruida; nao tentar publicar Room Properties
            ClearAllState();
        }

        // ── HUD ───────────────────────────────────────────────────────────────
        //
        // Layout (canto inferior esquerdo):
        //   Linha 2 (y = Screen.height - 54): mensagem de acao temporaria (fade)
        //   Linha 1 (y = Screen.height - 30): contador permanente
        //
        // Contador: sempre visivel quando ShowPocketHUD=true.
        // Mensagem: HudAlwaysVisible=true => permanente; false => fade por HudFadeSeconds.
        //
        // Nao interferir com outros mods que usam GUI.Label na mesma regiao:
        // posicionamento em y-fixo no canto inferior esquerdo e a convencao mais segura.

        // ── ShouldRenderHud ───────────────────────────────────────────────────
        //
        // Lê apenas campos cacheados — sem reflexão, sem mutação de estado.
        // O estado do HUD é controlado em Update() e nos handlers de evento.
        // Design fail-closed: retorna false por default; cada flag deve estar
        // explicitamente em estado "pronto".
        private bool ShouldRenderHud()
        {
            if (!QuickPocketPlugin.ShowPocketHUD.Value) return false;
            if (!_hasLocalPocketManager)                return false;
            if (_managerGeneration != _hudGeneration)   return false;  // generation guard
            if (_hudSuppressedForTransition)            return false;
            if (!_hudGameplayReady)                     return false;
            if (!_cachedRunIsLevel)                     return false;
            if (_cachedIsLoading)                       return false;  // LoadingUI kill-switch
            if (!_loadingAnimDone)                      return false;  // animação de loading concluída
            if (SemiFunc.PlayerAvatarLocal() == null)   return false;
            return true;
        }

        // Helper: LoadingUI.instance existe e animação de loading ainda não completou.
        // LoadingUI.levelAnimationCompleted (campo privado) = false → loading em andamento.
        // Acessado via AccessTools (lazy, cached) — evita reflection por frame.
        // Chamado em Update() uma vez por frame — resultado cacheado em _cachedIsLoading.
        private static bool IsLoadingActive()
        {
            try
            {
                var lui = LoadingUI.instance;
                if (lui == null) return false;  // sem instância → não está em loading

                if (_loadingUIAnimCompleted == null)
                    _loadingUIAnimCompleted = AccessTools.Field(typeof(LoadingUI), "levelAnimationCompleted");

                if (_loadingUIAnimCompleted == null) return false;  // campo não encontrado → fail-safe

                bool animDone = (bool)_loadingUIAnimCompleted.GetValue(lui);
                return !animDone;  // animacao ainda nao concluída = loading ativo = HUD OFF
            }
            catch { return false; }  // excecao nao bloqueia HUD (fail-safe)
        }

        // ── Callbacks de LoadingUI (chamados pelos patches Harmony) ──────────
        //
        // OnLoadingStarted: LoadingUI.StartLoading() disparou — loading começa.
        //   Suprime HUD se ainda não estava suprimido (complementa HandleLevelChange).
        //
        // OnLoadingAnimationComplete: LoadingUI.LevelAnimationComplete() disparou.
        //   A animação visual de loading terminou — autoriza a verificação de HUD.
        //   ESTE É O EVENTO PRIMÁRIO para liberar _loadingAnimDone.

        internal static void OnLoadingStarted()
        {
            var pm = Instance;
            if (pm == null) return;
            if (pm._hudSuppressedForTransition) return;  // já suprimido — sem ação

            // Suprimir HUD sem incrementar _hudGeneration (HandleLevelChange já fará isso)
            pm._hudGameplayReady           = false;
            pm._hudSuppressedForTransition = true;
            pm._runIsLevelTrueStreak       = 0;
            pm._loadingAnimDone            = false;
            pm._loadingUIWasSeen           = false;  // novo ciclo de loading: aguardar LoadingUI
            QuickPocketPlugin.Log.LogDebug(
                "[QP:HUDState] gen=" + _hudGeneration + " allowed=false reason=StartLoading");
        }

        internal void OnLoadingAnimationComplete()
        {
            // Guard generation: apenas o manager correto processa o evento.
            if (this != Instance) return;
            if (_managerGeneration != _hudGeneration) return;

            _loadingAnimDone     = true;
            _loadingAnimDoneTime = Time.time;  // gate temporal: streak só começa após HUD_UNLOCK_MIN_WAIT
            QuickPocketPlugin.Log.LogDebug(
                "[QP:HUDState] gen=" + _hudGeneration + " loadingAnimDone=true reason=LevelAnimationComplete" +
                " animDoneTime=" + _loadingAnimDoneTime.ToString("F1"));
        }

        // ── TrySemiRunIsLevel ─────────────────────────────────────────────────
        //
        // Verifica SemiFunc.RunIsLevel() via reflexão (lazy init, one-time lookup).
        // Retorna true = em nível ativo; false = loading, shop ou menu.
        // Fallback true se método não encontrado (não bloqueia — _inGameplay + timer
        // cobrem esse caso via LEVEL_TRANSITION_SETTLE_SECS).
        private static bool TrySemiRunIsLevel()
        {
            if (!_runIsLevelChecked)
            {
                _runIsLevelChecked = true;
                _runIsLevelMethod  = AccessTools.Method(typeof(SemiFunc), "RunIsLevel");
                QuickPocketPlugin.Log.LogDebug(
                    "[QuickPocket] SemiFunc.RunIsLevel(): " +
                    (_runIsLevelMethod != null ? "encontrado." : "nao encontrado — HUD permanece OFF ate confirmacao."));
            }
            // FAIL-CLOSED: se metodo nao encontrado, retornar false (HUD off).
            // NUNCA retornar true como fallback — causaria HUD durante loading.
            if (_runIsLevelMethod == null) return false;
            try   { return (bool)_runIsLevelMethod.Invoke(null, null); }
            catch { return false; }  // fail-closed: excecao nao libera HUD
        }

        private void OnGUI()
        {
            // Guard singleton: stale PocketManager (fase anterior) não deve renderizar.
            // Instance foi atualizado no Awake() da nova instância — este guard silencia stale.
            if (this != Instance) return;
            if (!ShouldRenderHud()) return;

            int  limit   = GetEffectiveLimit();
            bool syncing = limit < 0;
            int  count   = _localPocket.Count;

            // ── Estilos base (criados por frame — custo minimo para GUI simples) ──
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                normal   = { textColor = Color.white }
            };
            var shadowStyle = new GUIStyle(style)
            {
                normal = { textColor = new Color(0f, 0f, 0f, 0.6f) }
            };

            var oldColor = GUI.color;

            // ── Linha 1: contador — SEMPRE visivel (nao tem fade) ────────────────
            GUI.color = Color.white;
            string counterText = syncing
                ? "QuickPocket: sync..."
                : "QuickPocket: " + count + "/" + limit;

            GUI.Label(new Rect(11f, Screen.height - 29f, 400f, 24f), counterText, shadowStyle);
            GUI.Label(new Rect(10f, Screen.height - 30f, 400f, 24f), counterText, style);

            // ── Linha 2: mensagem de acao temporaria ─────────────────────────────
            if (!string.IsNullOrEmpty(_hudActionMessage))
            {
                float alpha = 1f;
                bool  alwaysVisible = QuickPocketPlugin.HudAlwaysVisible.Value;

                if (!alwaysVisible)
                {
                    float elapsed   = Time.time - _hudActionMessageTime;
                    float fadeTotal = QuickPocketPlugin.HudFadeSeconds.Value;

                    if (elapsed >= fadeTotal)
                    {
                        // Tempo esgotado: nao renderizar (mensagem some naturalmente)
                        GUI.color = oldColor;
                        return;
                    }

                    // Fade-out nos ultimos 20% do tempo (minimo 0.5s)
                    float fadeDuration = Mathf.Min(0.5f, fadeTotal * 0.2f);
                    float remaining    = fadeTotal - elapsed;
                    if (remaining < fadeDuration)
                        alpha = Mathf.Clamp01(remaining / fadeDuration);
                }

                GUI.color = new Color(1f, 1f, 1f, alpha);
                // shadowStyle.textColor = (0,0,0,0.6); GUI.color fornece o alpha global.
                // Efetivo: (0,0,0, alpha*0.6) — correto. Nao multiplicar textColor.a por alpha
                // novamente (causaria alpha^2 na sombra).
                GUI.Label(new Rect(11f, Screen.height - 53f, 600f, 24f), _hudActionMessage, shadowStyle);
                GUI.Label(new Rect(10f, Screen.height - 54f, 600f, 24f), _hudActionMessage, style);
            }

            GUI.color = oldColor;
        }

        // ── Limite efetivo ────────────────────────────────────────────────────

        internal int GetEffectiveLimit()
        {
            if (!SemiFunc.IsMultiplayer())
                return Mathf.Clamp(QuickPocketPlugin.MaxPocketSlots.Value, 1, 8);

            if (!_limitSynced) return -1;

            return _cachedEffectiveLimit;
        }

        private void ReadHostLimit()
        {
            if (!PhotonNetwork.InRoom) return;

            var props = PhotonNetwork.CurrentRoom.CustomProperties;

            if (props.TryGetValue(PROP_USE_HOST_LIMIT, out object useHostObj) &&
                props.TryGetValue(PROP_HOST_MAX_SLOTS, out object hostMaxObj))
            {
                bool useHost = (bool)useHostObj;
                int  hostMax = (int)hostMaxObj;

                if (useHost && QuickPocketPlugin.UseHostPocketLimit.Value)
                    _cachedEffectiveLimit = Mathf.Clamp(hostMax, 1, 8);
                else
                    _cachedEffectiveLimit = Mathf.Clamp(QuickPocketPlugin.MaxPocketSlots.Value, 1, 8);

                _limitSynced = true;
                QuickPocketPlugin.Log.LogDebug(
                    "[QuickPocket] Limite efetivo: " + _cachedEffectiveLimit +
                    " (hostMax=" + hostMax + " useHost=" + useHost + ")");
            }
            else
            {
                _limitSynced = false;
                QuickPocketPlugin.Log.LogDebug(
                    "[QuickPocket] Room Properties QP_Host* ainda nao disponiveis.");
            }
        }

        private Vector3 GetSafeDropPosition()
        {
            var player = SemiFunc.PlayerAvatarLocal();
            if (player != null)
                return player.transform.position + Vector3.up * 0.5f;
            return new Vector3(0f, 2f, 0f);
        }

        // ── Helpers: MapValuable no cliente não-master ───────────────────────
        //
        // O estado de SpriteRenderer.enabled NÃO é sincronizado pelo Photon.
        // O host desabilita/habilita em ApplyHide/RestoreSnapshot, mas o cliente
        // precisa fazer o mesmo localmente quando recebe ACK_POCKET/ACK_DROP.
        //
        // NÃO usamos SetActive(false) — mata a coroutine Logic() do MapValuable.
        // Desabilitamos/habilitamos apenas o SpriteRenderer, mantendo Logic() ativo.

        private static void ClientHideMapValuable(int vid, string itemName)
        {
            var pm = Instance;
            var pvLegacy = PhotonView.Find(vid);
            if (pm != null && pvLegacy != null)
                pm.HideRemoteItem(vid, pvLegacy.gameObject,
                    SemiFunc.IsMultiplayer() ? PhotonNetwork.LocalPlayer.ActorNumber : 0,
                    HideMode.OwnerAck, "ClientHideMapValuableLegacy");
            /*
            var pv = PhotonView.Find(vid);
            if (pv == null) return;
            var valuableObj = pv.gameObject.GetComponent<ValuableObject>();
            if (valuableObj == null) return;

            foreach (var mv in UnityEngine.Object.FindObjectsOfType<MapValuable>(true))
            {
                if (mv == null || mv.target != valuableObj) continue;
                var srField = HarmonyLib.AccessTools.Field(typeof(MapValuable), "spriteRenderer");
                var sr = srField?.GetValue(mv) as SpriteRenderer;
                if (sr != null)
                {
                    sr.enabled = false;
                    QuickPocketPlugin.Log.LogDebug(
                        "[QP:MapMarkerMP] hide viewID=" + vid +
                        " item=" + itemName + " markerFound=True disabled=True");
                }
                break;
            }
            */
        }

        private static void ClientRestoreMapValuable(int vid, string itemName)
        {
            var pm = Instance;
            var pvLegacy = PhotonView.Find(vid);
            if (pm != null && pvLegacy != null)
                pm.RestoreRemoteItem(vid, pvLegacy.gameObject,
                    SemiFunc.IsMultiplayer() ? PhotonNetwork.LocalPlayer.ActorNumber : 0,
                    RestoreMode.OwnerAck, null, "ClientRestoreMapValuableLegacy");
            /*
            var pv = PhotonView.Find(vid);
            if (pv == null) return;
            var valuableObj = pv.gameObject.GetComponent<ValuableObject>();
            if (valuableObj == null) return;

            foreach (var mv in UnityEngine.Object.FindObjectsOfType<MapValuable>(true))
            {
                if (mv == null || mv.target != valuableObj) continue;
                // Garantir que o alvo ainda existe (não foi destruído durante o pocket)
                if (mv.target == null) break;

                var srField = HarmonyLib.AccessTools.Field(typeof(MapValuable), "spriteRenderer");
                var sr = srField?.GetValue(mv) as SpriteRenderer;
                if (sr != null)
                {
                    sr.enabled = true;
                    QuickPocketPlugin.Log.LogDebug(
                        "[QP:MapMarkerMP] restore viewID=" + vid +
                        " item=" + itemName + " markerRestored=True");
                }
                break;
            }
            */
        }

        // ── IOnEventCallback ──────────────────────────────────────────────────

        public void OnEvent(EventData photonEvent)
        {
            if (photonEvent.CustomData == null) return;
            var data = photonEvent.CustomData as Hashtable;
            if (data == null) return;

            switch (photonEvent.Code)
            {
                case EV_REQUEST_POCKET:
                    if (SemiFunc.IsMasterClientOrSingleplayer())
                        HandleHostPocketRequest(data, photonEvent.Sender);
                    break;
                case EV_ACK_POCKET:
                    HandleClientPocketAck(data);
                    break;
                case EV_DENY_POCKET:
                    HandleClientPocketDeny(data);
                    break;
                case EV_REQUEST_DROP:
                    if (SemiFunc.IsMasterClientOrSingleplayer())
                        HandleHostDropRequest(data, photonEvent.Sender);
                    break;
                case EV_ACK_DROP:
                    HandleClientDropAck(data);
                    break;
                case EV_DENY_DROP:
                    HandleClientDropDeny(data);
                    break;
            }
        }

        // ── Host: processar REQUEST_POCKET vindo de um cliente ────────────────

        private void HandleHostPocketRequest(Hashtable data, int senderActor)
        {
            int  vid   = (int)data["vid"];
            int  nonce = (int)data["nonce"];
            string itemName = data.ContainsKey("item") ? (string)data["item"] : "?";

            // Validar: PhotonView existe?
            var pv = PhotonView.Find(vid);
            bool exists = pv != null && pv.gameObject != null;

            // Validar: item já está pocketado por alguém?
            bool alreadyHosted = false;
            foreach (var kv in _hostedPockets)
                foreach (var entry in kv.Value)
                    if (entry.vid == vid) { alreadyHosted = true; break; }
            if (_localPocket.Contains(vid)) alreadyHosted = true;

            // Validar: limite do solicitante
            int senderCount = _hostedPockets.ContainsKey(senderActor)
                ? _hostedPockets[senderActor].Count : 0;
            int effectiveLimit = Mathf.Clamp(QuickPocketPlugin.HostMaxOverride.Value, 1, 8);
            bool limitOK = senderCount < effectiveLimit;
            bool actorFound;
            TryGetPositionByActorNumber(senderActor, out actorFound);
            bool hostReady = !_hudSuppressedForTransition && _cachedRunIsLevel;

            QuickPocketPlugin.Log.LogInfo(
                "[QP:MP] host received pocket actor=" + senderActor +
                " viewID=" + vid +
                " exists=" + exists +
                " alreadyHosted=" + alreadyHosted +
                " limitOK=" + limitOK +
                " actorFound=" + actorFound +
                " hostReady=" + hostReady);

            var ackOpts = new RaiseEventOptions { TargetActors = new int[] { senderActor } };

            if (!exists || alreadyHosted || !limitOK || !actorFound || !hostReady)
            {
                string reason = !exists ? "NotFound" :
                    alreadyHosted ? "AlreadyPocketed" :
                    !limitOK ? "LimitReached" :
                    !actorFound ? "ActorNotFound" : "LevelChanging";
                var denyData  = new Hashtable { { "vid", vid }, { "nonce", nonce }, { "reason", reason } };
                PhotonNetwork.RaiseEvent(EV_DENY_POCKET, denyData, ackOpts, SendOptions.SendReliable);
                QuickPocketPlugin.Log.LogInfo(
                    "[QP:MP] host denied pocket actor=" + senderActor +
                    " viewID=" + vid + " reason=" + reason);
                return;
            }

            // Executar pocket no host (owner=master → ApplyHide persiste via Photon sync)
            var pgo = pv.gameObject.GetComponent<PhysGrabObject>();
            if (pgo == null)
            {
                var denyData = new Hashtable { { "vid", vid }, { "nonce", nonce }, { "reason", "NoPgo" } };
                PhotonNetwork.RaiseEvent(EV_DENY_POCKET, denyData, ackOpts, SendOptions.SendReliable);
                return;
            }

            var info = EligibilityChecker.Evaluate(pgo);
            if (!info.Eligible)
            {
                var denyData = new Hashtable { { "vid", vid }, { "nonce", nonce }, { "reason", info.RejectReason ?? "NotEligible" } };
                PhotonNetwork.RaiseEvent(EV_DENY_POCKET, denyData, ackOpts, SendOptions.SendReliable);
                QuickPocketPlugin.Log.LogInfo(
                    "[QP:MP] host denied pocket actor=" + senderActor +
                    " viewID=" + vid + " reason=" + (info.RejectReason ?? "NotEligible"));
                return;
            }

            var snap = PocketOperations.SaveSnapshot(pgo, vid, senderActor);
            if (!PocketOperations.ValidateSnapshot(snap))
            {
                var denyData = new Hashtable { { "vid", vid }, { "nonce", nonce }, { "reason", "SnapshotFailed" } };
                PhotonNetwork.RaiseEvent(EV_DENY_POCKET, denyData, ackOpts, SendOptions.SendReliable);
                return;
            }

            if (!PocketOperations.HidePocketedItemState(pv.gameObject, vid, senderActor, HideMode.HostHosted, "HostPocketRequest", snap))
            {
                var denyData = new Hashtable { { "vid", vid }, { "nonce", nonce }, { "reason", "HideFailed" } };
                PhotonNetwork.RaiseEvent(EV_DENY_POCKET, denyData, ackOpts, SendOptions.SendReliable);
                return;
            }

            // Armazenar snap no host
            if (!_hostedPockets.ContainsKey(senderActor))
                _hostedPockets[senderActor] = new List<(int, ComponentSnapshot)>();
            _hostedPockets[senderActor].Add((vid, snap));

            // Publicar nas Room Properties para sync com outros clientes
            int[] newVids = new int[_hostedPockets[senderActor].Count];
            for (int i = 0; i < newVids.Length; i++)
                newVids[i] = _hostedPockets[senderActor][i].vid;
            var roomProps = new Hashtable { { PropKeyForActor(senderActor), newVids } };
            PhotonNetwork.CurrentRoom.SetCustomProperties(roomProps);

            // ACK ao cliente solicitante
            var ackData = new Hashtable { { "vid", vid }, { "nonce", nonce }, { "item", snap.ItemName } };
            PhotonNetwork.RaiseEvent(EV_ACK_POCKET, ackData, ackOpts, SendOptions.SendReliable);

            QuickPocketPlugin.Log.LogInfo(
                "[QP:MP] host pocket actor=" + senderActor +
                " viewID=" + vid +
                " item=" + snap.ItemName +
                " result=ACK hostedCount=" + _hostedPockets[senderActor].Count);
        }

        // ── Host: processar REQUEST_DROP vindo de um cliente ──────────────────

        private void HandleHostDropRequest(Hashtable data, int senderActor)
        {
            int vid   = (int)data["vid"];
            int nonce = (int)data["nonce"];

            // Localizar snap no _hostedPockets
            ComponentSnapshot snap = null;
            int snapIdx = -1;
            if (_hostedPockets.TryGetValue(senderActor, out var list))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].vid == vid)
                    {
                        snap    = list[i].snap;
                        snapIdx = i;
                        break;
                    }
                }
            }

            var ackOpts = new RaiseEventOptions { TargetActors = new int[] { senderActor } };

            if (snap == null)
            {
                var denyData = new Hashtable { { "vid", vid }, { "nonce", nonce }, { "reason", "NotHosted" } };
                PhotonNetwork.RaiseEvent(EV_DENY_DROP, denyData, ackOpts, SendOptions.SendReliable);
                QuickPocketPlugin.Log.LogInfo(
                    "[QP:MP] host deny drop actor=" + senderActor + " viewID=" + vid + " reason=NotHosted");
                return;
            }

            // Calcular posição de drop perto do avatar do cliente solicitante.
            // Estratégia: usar posição do avatar remoto no host (autoritativa via Photon sync).
            // Fallback: posição reportada pelo cliente no request (quando avatar não encontrado).
            bool    avatarFound;
            Vector3 avatarPos = TryGetPositionByActorNumber(senderActor, out avatarFound);

            Vector3 dropBase;
            if (data.ContainsKey("cpx"))
            {
                float cpx = (float)data["cpx"];
                float cpy = (float)data["cpy"];
                float cpz = (float)data["cpz"];
                var clientReqPos = new Vector3(cpx, cpy, cpz);
                dropBase = avatarFound ? avatarPos : clientReqPos;
                QuickPocketPlugin.Log.LogInfo(
                    "[QP:MP] host resolve actor=" + senderActor +
                    " avatarFound=" + avatarFound +
                    " avatarPos=" + avatarPos.ToString("F2") +
                    " reqPos=" + clientReqPos.ToString("F2") +
                    " dist=" + Vector3.Distance(avatarPos, clientReqPos).ToString("F2") +
                    " base=" + (avatarFound ? "RemoteAvatar" : "RequestFallback"));
            }
            else
            {
                dropBase = avatarPos;
                QuickPocketPlugin.Log.LogInfo(
                    "[QP:MP] host resolve actor=" + senderActor +
                    " avatarFound=" + avatarFound +
                    " avatarPos=" + avatarPos.ToString("F2") +
                    " base=AvatarOnly");
            }

            Vector3 dropPos = dropBase + Vector3.up * 0.5f;

            var pv = PhotonView.Find(vid);
            if (pv == null || pv.gameObject == null)
            {
                var denyData = new Hashtable { { "vid", vid }, { "nonce", nonce }, { "reason", "ItemDestroyed" }, { "clear", true } };
                PhotonNetwork.RaiseEvent(EV_DENY_DROP, denyData, ackOpts, SendOptions.SendReliable);
                list.RemoveAt(snapIdx);
                if (list.Count == 0) _hostedPockets.Remove(senderActor);
                int[] remaining = _hostedPockets.ContainsKey(senderActor)
                    ? (int[])System.Array.ConvertAll(_hostedPockets[senderActor].ToArray(), e => e.vid)
                    : new int[0];
                var props = new Hashtable { { PropKeyForActor(senderActor), remaining.Length > 0 ? (object)remaining : null } };
                PhotonNetwork.CurrentRoom.SetCustomProperties(props);
                QuickPocketPlugin.Log.LogInfo(
                    "[QP:MP] host deny drop actor=" + senderActor + " viewID=" + vid + " reason=ItemDestroyed clear=True");
                return;
            }

            PocketOperations.RestorePocketedItemState(pv.gameObject, vid, senderActor, RestoreMode.HostHosted, dropPos, "HostDropRequest", snap);

            // Remover do hostedPockets
            list.RemoveAt(snapIdx);
            if (list.Count == 0) _hostedPockets.Remove(senderActor);

            // Atualizar Room Properties
            int[] newVids = _hostedPockets.ContainsKey(senderActor)
                ? (int[])System.Array.ConvertAll(_hostedPockets[senderActor].ToArray(), e => e.vid)
                : new int[0];
            var roomProps = new Hashtable { { PropKeyForActor(senderActor), newVids.Length > 0 ? (object)newVids : null } };
            PhotonNetwork.CurrentRoom.SetCustomProperties(roomProps);

            // ACK ao cliente com posição de drop
            var ackData = new Hashtable
            {
                { "vid", vid }, { "nonce", nonce },
                { "px", dropPos.x }, { "py", dropPos.y }, { "pz", dropPos.z },
                { "item", snap.ItemName }
            };
            PhotonNetwork.RaiseEvent(EV_ACK_DROP, ackData, ackOpts, SendOptions.SendReliable);

            QuickPocketPlugin.Log.LogInfo(
                "[QP:MP] host drop actor=" + senderActor +
                " viewID=" + vid +
                " item=" + snap.ItemName +
                " pos=" + dropPos.ToString("F2") +
                " result=OK");
        }

        // ── Cliente: ACK pocket ───────────────────────────────────────────────

        private void HandleClientPocketAck(Hashtable data)
        {
            int vid   = (int)data["vid"];
            int nonce = (int)data["nonce"];
            string itemName = data.ContainsKey("item") ? (string)data["item"] : "?";

            if (!_pendingPocketAcks.ContainsKey(nonce)) return;
            _pendingPocketAcks.Remove(nonce);

            if (_localPocket.Contains(vid)) return;  // duplicado

            _localPocket.Add(vid);
            _clientHostedItems.Add(vid);  // marcar como hospedado (sem snapshot local)

            // Ocultar MapValuable SpriteRenderer no lado do cliente.
            //
            // O host desabilitou o SpriteRenderer em ApplyHide, mas o estado de componentes
            // NÃO é sincronizado pelo Photon. Na perspectiva do cliente, o SpriteRenderer
            // permanece enabled → marker do TAB/minimap continua visível na posição original.
            // Solução: desabilitar explicitamente aqui após ACK.
            var pv = PhotonView.Find(vid);
            if (pv != null && pv.gameObject != null)
                HideRemoteItem(vid, pv.gameObject, PhotonNetwork.LocalPlayer.ActorNumber, HideMode.OwnerAck, "ACK_POCKET");
            else
            {
                _pendingLateJoin[vid] = Time.time + LATE_JOIN_TIMEOUT;
                _pendingLateJoinActors[vid] = PhotonNetwork.LocalPlayer.ActorNumber;
            }

            int limit = GetEffectiveLimit();
            SetHudMessage("Guardado: " + CleanItemName(itemName) + " (" + _localPocket.Count + "/" + limit + ")");

            int localActor = SemiFunc.IsMultiplayer() ? PhotonNetwork.LocalPlayer.ActorNumber : 0;
            QuickPocketPlugin.Log.LogInfo(
                "[QP:MP] ack pocket actor=" + localActor +
                " viewID=" + vid +
                " result=OK count=" + _localPocket.Count + "/" + limit);
        }

        // ── Cliente: DENY pocket ──────────────────────────────────────────────

        private void HandleClientPocketDeny(Hashtable data)
        {
            int vid    = (int)data["vid"];
            int nonce  = (int)data["nonce"];
            string why = data.ContainsKey("reason") ? (string)data["reason"] : "?";

            _pendingPocketAcks.Remove(nonce);
            SetHudMessage("Nao foi possivel guardar: " + why);
            QuickPocketPlugin.Log.LogInfo(
                "[QP:MP] deny pocket viewID=" + vid + " reason=" + why);
        }

        // ── Cliente: ACK drop ─────────────────────────────────────────────────

        private void HandleClientDropAck(Hashtable data)
        {
            int vid   = (int)data["vid"];
            int nonce = (int)data["nonce"];
            string itemName = data.ContainsKey("item") ? (string)data["item"] : "?";

            if (!_pendingDropAcks.ContainsKey(nonce)) return;
            _pendingDropAcks.Remove(nonce);

            // Extrair finalPos do ACK — posição onde o host restaurou o item.
            // Usada para diagnóstico e para reposicionar o MapValuable marker.
            Vector3 finalPos = Vector3.zero;
            bool    hasFinalPos = data.ContainsKey("px");
            if (hasFinalPos)
            {
                finalPos = new Vector3(
                    (float)data["px"],
                    (float)data["py"],
                    (float)data["pz"]);
            }

            _localPocket.Remove(vid);
            _clientHostedItems.Remove(vid);

            // Restaurar MapValuable SpriteRenderer no lado do cliente.
            // O host re-habilitou via RestoreSnapshot, mas o estado de componente não sincroniza.
            // MapValuable.Logic() continuou rastreando o item durante o pocket (SpriteRenderer off
            // não mata a coroutine). Quando re-habilitamos, o marker aponta para a posição atual
            // do item — que será atualizada pelo Photon position sync em ~50ms.
            var pv = PhotonView.Find(vid);
            if (pv != null && pv.gameObject != null)
                RestoreRemoteItem(vid, pv.gameObject, PhotonNetwork.LocalPlayer.ActorNumber, RestoreMode.OwnerAck,
                    hasFinalPos ? (Vector3?)finalPos : null, "ACK_DROP");
            else
                _remoteSnapshots.Remove(vid);

            int limit = GetEffectiveLimit();
            int count = _localPocket.Count;
            SetHudMessage("Retirado: " + CleanItemName(itemName) + " (" + count + "/" + limit + ")");

            int localActor = SemiFunc.IsMultiplayer() ? PhotonNetwork.LocalPlayer.ActorNumber : 0;
            QuickPocketPlugin.Log.LogInfo(
                "[QP:MP] ack drop actor=" + localActor +
                " viewID=" + vid +
                " result=OK finalPos=" + (hasFinalPos ? finalPos.ToString("F2") : "n/a") +
                " count=" + count + "/" + limit);
        }

        // ── Cliente: DENY drop ────────────────────────────────────────────────

        private void HandleClientDropDeny(Hashtable data)
        {
            int vid   = (int)data["vid"];
            int nonce = (int)data["nonce"];
            string why = data.ContainsKey("reason") ? (string)data["reason"] : "?";

            _pendingDropAcks.Remove(nonce);
            if (data.ContainsKey("clear") && (bool)data["clear"])
            {
                _localPocket.Remove(vid);
                _clientHostedItems.Remove(vid);
                _remoteSnapshots.Remove(vid);
            }
            SetHudMessage("Nao foi possivel retirar: " + why);
            QuickPocketPlugin.Log.LogInfo(
                "[QP:MP] deny drop viewID=" + vid + " reason=" + why);
        }

        // ── Timeout de requests pendentes ─────────────────────────────────────

        private void CheckRequestTimeouts()
        {
            float now = Time.time;
            var expiredPocket = new List<int>();
            foreach (var kv in _pendingPocketAcks)
                if (now - kv.Value.Timestamp > MP_REQUEST_TIMEOUT) expiredPocket.Add(kv.Key);
            foreach (int n in expiredPocket)
            {
                var req = _pendingPocketAcks[n];
                _pendingPocketAcks.Remove(n);
                SetHudMessage("Timeout — tente novamente.");
                QuickPocketPlugin.Log.LogInfo(
                    "[QP:MP] timeout actor=" + req.Actor +
                    " nonce=" + n +
                    " type=" + req.Type +
                    " viewID=" + req.ViewID +
                    " cleared=True hudReset=True");
            }

            var expiredDrop = new List<int>();
            foreach (var kv in _pendingDropAcks)
                if (now - kv.Value.Timestamp > MP_REQUEST_TIMEOUT) expiredDrop.Add(kv.Key);
            foreach (int n in expiredDrop)
            {
                var req = _pendingDropAcks[n];
                _pendingDropAcks.Remove(n);
                SetHudMessage("Timeout no drop — tente novamente.");
                QuickPocketPlugin.Log.LogInfo(
                    "[QP:MP] timeout actor=" + req.Actor +
                    " nonce=" + n +
                    " type=" + req.Type +
                    " viewID=" + req.ViewID +
                    " cleared=True hudReset=True");
            }
        }

        // ── Helper: posição do avatar de outro ator ───────────────────────────

        // Versão com flag de sucesso — permite distinguir "encontrado em (0,2,0)" de "não encontrado".
        private static Vector3 TryGetPositionByActorNumber(int actorNumber, out bool found)
        {
            foreach (var pa in UnityEngine.Object.FindObjectsOfType<PlayerAvatar>())
            {
                var pv = pa.GetComponent<PhotonView>();
                if (pv != null && pv.OwnerActorNr == actorNumber)
                {
                    found = true;
                    return pa.transform.position;
                }
            }
            found = false;
            return Vector3.up * 2f;  // fallback seguro
        }

        // Wrapper de compatibilidade (mantido para uso interno sem necessidade da flag).
        private static Vector3 GetPositionByActorNumber(int actorNumber)
        {
            return TryGetPositionByActorNumber(actorNumber, out _);
        }

        // ── IInRoomCallbacks ──────────────────────────────────────────────────

        public void OnRoomPropertiesUpdate(Hashtable changedProps)
        {
            // Recalcular limite se props do host mudaram
            if (changedProps.ContainsKey(PROP_HOST_MAX_SLOTS) ||
                changedProps.ContainsKey(PROP_USE_HOST_LIMIT))
            {
                ReadHostLimit();
                QuickPocketPlugin.Log.LogDebug(
                    "[QuickPocket] Limite recalculado apos update: " + _cachedEffectiveLimit);
            }

            // Processar mudancas de bolso de outros jogadores
            int localActor = PhotonNetwork.InRoom ? PhotonNetwork.LocalPlayer.ActorNumber : -1;

            foreach (object key in changedProps.Keys)
            {
                string propKey = key as string;
                if (propKey == null) continue;
                if (propKey == PROP_HOST_MAX_SLOTS || propKey == PROP_USE_HOST_LIMIT) continue;
                if (!propKey.StartsWith("QP_")) continue;
                if (!int.TryParse(propKey.Substring(3), out int actorNumber)) continue;
                if (actorNumber == localActor) continue;

                int[] newViewIDs = changedProps[key] as int[];
                ProcessRemotePocketUpdate(actorNumber, newViewIDs);
            }
        }

        public void OnPlayerEnteredRoom(Player newPlayer) { }

        public void OnPlayerLeftRoom(Player otherPlayer)
        {
            // Somente o master restaura os itens do jogador que saiu
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;

            int actorNumber = otherPlayer.ActorNumber;
            QuickPocketPlugin.Log.LogDebug(
                "[QuickPocket] Jogador " + actorNumber + " saiu. Master restaurando itens.");

            // Restaurar itens hospedados (pocketados pelo cliente via EV_REQUEST_POCKET)
            if (_hostedPockets.TryGetValue(actorNumber, out var hostedList))
            {
                Vector3 safePos = GetSafeDropPosition();
                foreach (var (vid, snap) in hostedList)
                {
                    var pv = PhotonView.Find(vid);
                    if (pv != null)
                    {
                        if (snap != null)
                            PocketOperations.RestorePocketedItemState(pv.gameObject, vid, actorNumber, RestoreMode.Cleanup, safePos, "PlayerLeft", snap);
                        else
                            PocketOperations.RestorePocketedItemState(pv.gameObject, vid, actorNumber, RestoreMode.SafeDefaults, safePos, "PlayerLeft", null);
                    }
                }
                _hostedPockets.Remove(actorNumber);
                QuickPocketPlugin.Log.LogDebug(
                    "[QP:MP] host restored hostedPockets for disconnected actor=" + actorNumber +
                    " count=" + hostedList.Count);
            }

            if (_remoteHides.TryGetValue(actorNumber, out var viewIDs))
            {
                Vector3 safePos = GetSafeDropPosition();
                foreach (int vid in viewIDs)
                {
                    _pendingLateJoin.Remove(vid);
                    _pendingLateJoinActors.Remove(vid);
                    var pv = PhotonView.Find(vid);
                    if (pv != null)
                        PocketOperations.RestorePocketedItemState(pv.gameObject, vid, actorNumber, RestoreMode.SafeDefaults, safePos, "PlayerLeftRemote", null);
                }
                _remoteHides.Remove(actorNumber);
            }

            // Limpar a Room Property do jogador desconectado
            var props = new Hashtable { { PropKeyForActor(actorNumber), null } };
            PhotonNetwork.CurrentRoom.SetCustomProperties(props);
        }

        public void OnMasterClientSwitched(Player newMasterClient) { }
        public void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps) { }

        // ── IMatchmakingCallbacks ─────────────────────────────────────────────

        public void OnJoinedRoom()
        {
            ProcessLateJoin();
        }

        public void OnLeftRoom()
        {
            // Sala destruida — limpar tudo sem publicar Room Properties
            ClearAllState();
        }

        public void OnFriendListUpdate(List<FriendInfo> friendList) { }
        public void OnCreatedRoom() { }
        public void OnCreateRoomFailed(short returnCode, string message) { }
        public void OnJoinRoomFailed(short returnCode, string message) { }
        public void OnJoinRandomFailed(short returnCode, string message) { }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Patches Harmony
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Adiciona PocketManager ao PlayerAvatar local.
    ///
    /// DIAGNOSTICO: usa GetComponent<PhotonView>() em vez de __instance.photonView para
    /// evitar NullReferenceException silenciosa caso o campo ainda nao esteja inicializado
    /// pelo Photon no momento do Awake.
    /// Trata singleplayer separadamente (sem IsMine valido em modo offline).
    /// </summary>
    [HarmonyPatch(typeof(PlayerAvatar), "Awake")]
    internal static class AddPocketManagerPatch
    {
        [HarmonyPostfix]
        static void Postfix(PlayerAvatar __instance)
        {
            try
            {
                QuickPocketPlugin.Log.LogInfo(
                    "[QuickPocket] AddPocketManagerPatch: PlayerAvatar.Awake disparado. GO=" +
                    __instance.gameObject.name);

                // Identificar se e o jogador local.
                // __instance.photonView pode ser null em Awake (Photon ainda nao inicializou o campo).
                // Usar GetComponent como fallback null-safe.
                bool isLocal;

                if (!SemiFunc.IsMultiplayer())
                {
                    // Singleplayer: nao ha IsMine confiavel, adicionar sempre
                    // (so ha um PlayerAvatar local no singleplayer)
                    isLocal = true;
                    QuickPocketPlugin.Log.LogInfo("[QuickPocket] Singleplayer — assumindo jogador local.");
                }
                else
                {
                    var pv = __instance.GetComponent<PhotonView>();
                    if (pv == null)
                    {
                        QuickPocketPlugin.Log.LogInfo(
                            "[QuickPocket] PhotonView nulo em Awake — ignorando este avatar.");
                        return;
                    }
                    QuickPocketPlugin.Log.LogInfo(
                        "[QuickPocket] PhotonView: ViewID=" + pv.ViewID + " IsMine=" + pv.IsMine);
                    isLocal = pv.IsMine;
                }

                if (!isLocal)
                {
                    QuickPocketPlugin.Log.LogInfo("[QuickPocket] Avatar remoto — nao adicionar PocketManager.");
                    return;
                }

                if (__instance.GetComponent<PocketManager>() != null)
                {
                    QuickPocketPlugin.Log.LogInfo("[QuickPocket] PocketManager ja existe neste avatar.");
                    return;
                }

                __instance.gameObject.AddComponent<PocketManager>();
                QuickPocketPlugin.Log.LogInfo(
                    "[QuickPocket] PocketManager adicionado com sucesso ao PlayerAvatar local.");
            }
            catch (System.Exception ex)
            {
                QuickPocketPlugin.Log.LogError(
                    "[QuickPocket] AddPocketManagerPatch EXCECAO: " + ex.GetType().Name + " — " + ex.Message);
            }
        }
    }

    /// <summary>Host publica QP_HostMaxSlots e QP_UseHostLimit — uma vez por sessao.</summary>
    [HarmonyPatch(typeof(PlayerAvatar), "Awake")]
    internal static class PublishHostLimitPatch
    {
        [HarmonyPostfix]
        static void Postfix(PlayerAvatar __instance)
        {
            try
            {
                if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
                if (!PhotonNetwork.InRoom) return;
                if (PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey("QP_HostMaxSlots")) return;

                var props = new Hashtable
                {
                    { "QP_HostMaxSlots", Mathf.Clamp(QuickPocketPlugin.HostMaxOverride.Value, 1, 8) },
                    { "QP_UseHostLimit", QuickPocketPlugin.UseHostPocketLimit.Value }
                };
                PhotonNetwork.CurrentRoom.SetCustomProperties(props);
                QuickPocketPlugin.Log.LogInfo(
                    "[QuickPocket] Host publicou limite: HostMaxOverride=" + QuickPocketPlugin.HostMaxOverride.Value +
                    " UseHostLimit=" + QuickPocketPlugin.UseHostPocketLimit.Value);
            }
            catch (System.Exception ex)
            {
                QuickPocketPlugin.Log.LogError(
                    "[QuickPocket] PublishHostLimitPatch EXCECAO: " + ex.GetType().Name + " — " + ex.Message);
            }
        }
    }

    /// <summary>Dropa/limpa itens ao morrer.</summary>
    [HarmonyPatch(typeof(PlayerHealth), "Death")]
    internal static class DeathDropPatch
    {
        [HarmonyPostfix]
        static void Postfix(PlayerHealth __instance)
        {
            // Verificar se e o jogador local
            var pv = __instance.GetComponent<PhotonView>();
            if (pv != null && !pv.IsMine) return;

            var pm = PocketManager.Instance;
            if (pm == null) return;

            pm.HandleDeath(__instance.transform.position);
        }
    }

    /// <summary>Limpa estado ao voltar ao menu principal.</summary>
    [HarmonyPatch(typeof(MainMenuOpen), "Start")]
    internal static class MainMenuResetPatch
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            PocketManager.Instance?.HandleMainMenu();
        }
    }

    /// <summary>Dropa/limpa itens ao mudar de fase.</summary>
    [HarmonyPatch(typeof(RunManager), "ChangeLevel")]
    internal static class LevelChangePatch
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            PocketManager.Instance?.HandleLevelChange();
        }
    }

    /// <summary>
    /// LoadingUI.StartLoading() disparou — loading começou.
    /// Suprime HUD imediatamente se ainda estava ativo.
    /// Complementa HandleLevelChange/OnSceneLoaded para casos onde o loading
    /// começa sem que RunManager.ChangeLevel tenha sido detectado.
    /// </summary>
    [HarmonyPatch(typeof(LoadingUI), "StartLoading")]
    internal static class LoadingStartPatch
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            PocketManager.OnLoadingStarted();
        }
    }

    /// <summary>
    /// LoadingUI.LevelAnimationComplete() disparou — animação de loading terminou.
    /// Autoriza o release do HUD (mas não libera sozinho — RunIsLevel streak ainda necessário).
    /// Este é o evento primário para _loadingAnimDone=true.
    /// </summary>
    [HarmonyPatch(typeof(LoadingUI), "LevelAnimationComplete")]
    internal static class LoadingAnimCompletePatch
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            PocketManager.Instance?.OnLoadingAnimationComplete();
        }
    }
}
