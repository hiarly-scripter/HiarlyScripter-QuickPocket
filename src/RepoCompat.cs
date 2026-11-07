using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;

namespace QuickPocket
{
    // =========================================================================
    // RepoCompat — camada de compatibilidade com a versao atual do REPO.
    //
    // Centraliza TODOS os AccessTools.Field() e AccessTools.Method() do mod.
    // Initialize() valida tudo no carregamento — se qualquer campo critico
    // estiver ausente, IsCompatible = false e o mod desabilita-se de forma
    // limpa sem corromper o jogo.
    //
    // Ao atualizar o REPO, basta olhar os logs de erro desta classe para
    // saber exatamente o que mudou.
    // =========================================================================
    internal static class RepoCompat
    {
        // ── Estado de compatibilidade ─────────────────────────────────────────
        internal static bool IsCompatible { get; private set; } = false;

        // ── Campos criticos (PhysGrabObject) ──────────────────────────────────
        private static FieldInfo _fIsGun;
        private static FieldInfo _fIsMelee;
        private static FieldInfo _fIsEnemy;
        private static FieldInfo _fWidthX;
        private static FieldInfo _fHeightY;
        private static FieldInfo _fLengthZ;

        // ── Campos criticos (ItemAttributes) ──────────────────────────────────
        private static FieldInfo _fItemType;

        // ── Metodos de patch (advisory: falha nao desabilita o mod) ───────────
        private static MethodInfo _mPlayerAvatarAwake;
        private static MethodInfo _mPlayerHealthDeath;
        private static MethodInfo _mMainMenuOpenStart;
        private static MethodInfo _mRunManagerChangeLevel;

        // =====================================================================
        // Initialize — chamar uma vez no Awake do plugin, antes do PatchAll.
        // =====================================================================
        internal static void Initialize()
        {
            var criticalErrors   = new List<string>();
            var advisoryWarnings = new List<string>();

            // ── Campos criticos ───────────────────────────────────────────────
            _fIsGun    = RequireField(typeof(PhysGrabObject), "isGun",       criticalErrors);
            _fIsMelee  = RequireField(typeof(PhysGrabObject), "isMelee",     criticalErrors);
            _fIsEnemy  = RequireField(typeof(PhysGrabObject), "isEnemy",     criticalErrors);
            _fWidthX   = RequireField(typeof(PhysGrabObject), "itemWidthX",  criticalErrors);
            _fHeightY  = RequireField(typeof(PhysGrabObject), "itemHeightY", criticalErrors);
            _fLengthZ  = RequireField(typeof(PhysGrabObject), "itemLengthZ", criticalErrors);
            _fItemType = RequireField(typeof(ItemAttributes),  "itemType",   criticalErrors);

            // ── Metodos de patch (advisory) ────────────────────────────────────
            _mPlayerAvatarAwake     = CheckMethod(typeof(PlayerAvatar), "Awake",       null, advisoryWarnings);
            _mPlayerHealthDeath     = CheckMethod(typeof(PlayerHealth), "Death",       null, advisoryWarnings);
            _mMainMenuOpenStart     = CheckMethod(typeof(MainMenuOpen), "Start",       null, advisoryWarnings);

            // Assinatura real: ChangeLevel(Boolean, Boolean, RunManager+ChangeLevelType)
            // RunManager+ChangeLevelType e enum aninhado — nao referenciavel com typeof()
            // em tempo de compilacao sem o assembly do jogo como referencia direta.
            // Busca por nome sem tipos: ha exatamente 1 overload → seguro.
            // O patch Harmony (LevelChangePatch) tambem usa atributo sem tipos de parametro
            // e funciona corretamente.
            _mRunManagerChangeLevel = CheckMethod(typeof(RunManager),   "ChangeLevel", null, advisoryWarnings);

            // Reflexao usada em Core.cs/PocketOperations.cs (advisory: o mod tem fallback).
            // Validada aqui para o log apontar logo o que mudou apos um update do jogo.
            if (AccessTools.Field(typeof(MapValuable), "spriteRenderer") == null)
                advisoryWarnings.Add("missing field MapValuable.spriteRenderer (marcador do mapa)");
            if (AccessTools.Field(typeof(LoadingUI), "levelAnimationCompleted") == null)
                advisoryWarnings.Add("missing field LoadingUI.levelAnimationCompleted (gate do HUD)");
            CheckMethod(typeof(SemiFunc), "RunIsLevel", null, advisoryWarnings);
            CheckMethod(typeof(LoadingUI), "StartLoading", null, advisoryWarnings);
            CheckMethod(typeof(LoadingUI), "LevelAnimationComplete", null, advisoryWarnings);

            // ── Resultado ─────────────────────────────────────────────────────
            if (criticalErrors.Count > 0)
            {
                foreach (var err in criticalErrors)
                    QuickPocketPlugin.Log.LogError("[QuickPocket] Compatibility failed: " + err);

                QuickPocketPlugin.Log.LogError(
                    "[QuickPocket] " + criticalErrors.Count +
                    " campo(s) critico(s) ausente(s). QuickPocket desabilitado. " +
                    "Aguarde atualizacao do mod para a versao atual do REPO.");

                IsCompatible = false;
            }
            else
            {
                QuickPocketPlugin.Log.LogInfo("[QuickPocket] RepoCompat OK — todos os campos criticos validados.");
                IsCompatible = true;
            }

            foreach (var warn in advisoryWarnings)
                QuickPocketPlugin.Log.LogWarning("[QuickPocket] Compat warning: " + warn);
        }

        // ── Acessores tipados — usados por EligibilityChecker ─────────────────
        // Retornam valores seguros se o campo for null (defensividade em profundidade).

        internal static bool GetIsGun(PhysGrabObject pgo)
            => _fIsGun != null && (bool)_fIsGun.GetValue(pgo);

        internal static bool GetIsMelee(PhysGrabObject pgo)
            => _fIsMelee != null && (bool)_fIsMelee.GetValue(pgo);

        internal static bool GetIsEnemy(PhysGrabObject pgo)
            => _fIsEnemy != null && (bool)_fIsEnemy.GetValue(pgo);

        internal static float GetWidthX(PhysGrabObject pgo)
            => _fWidthX != null ? (float)_fWidthX.GetValue(pgo) : 0f;

        internal static float GetHeightY(PhysGrabObject pgo)
            => _fHeightY != null ? (float)_fHeightY.GetValue(pgo) : 0f;

        internal static float GetLengthZ(PhysGrabObject pgo)
            => _fLengthZ != null ? (float)_fLengthZ.GetValue(pgo) : 0f;

        // Retorna -1 se o campo estiver ausente (valor invalido que nenhum ItemTypeId usa).
        internal static int GetItemType(ItemAttributes attrs)
            => _fItemType != null ? (int)_fItemType.GetValue(attrs) : -1;

        // ── Helpers privados ──────────────────────────────────────────────────

        private static FieldInfo RequireField(System.Type type, string name, List<string> errors)
        {
            var f = AccessTools.Field(type, name);
            if (f == null)
                errors.Add("missing " + type.Name + "." + name);
            return f;
        }

        private static MethodInfo CheckMethod(System.Type type, string name, System.Type[] paramTypes, List<string> warnings)
        {
            MethodInfo m = paramTypes != null
                ? AccessTools.Method(type, name, paramTypes)
                : AccessTools.Method(type, name);

            if (m == null)
                warnings.Add("missing method " + type.Name + "." + name + "()");

            return m;
        }
    }
}
