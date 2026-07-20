using System;
using System.Collections.Generic;
using HarmonyLib;
using IsekaiLeveling;
using IsekaiLeveling.MobRanking;
using IsekaiLeveling.Quests;
using RimWorld;
using Verse;

namespace TheAntagonist
{
    // Keep raid enemies - both their power and their gear - tied to the colony's own Isekai rank.
    //
    // Isekai ranks enemies (from raid points) and forge-enhances their weapon/apparel (from their rank).
    // Its "Adaptive Raid Ranks" setting is meant to cap raiders at colony-best +1 but doesn't reliably
    // fire, so raids arrive several tiers above the party with heavily upgraded kit. The Antagonist
    // enforces the ceiling itself, in two places because Isekai does the two things at different times:
    //   * Stats  are set at raid Lord creation  -> clamped in Patch_RaidRankSystem_AssignRaidPawnRank.
    //   * Gear   is forged at pawn generation    -> clamped in Patch_PawnGenerator_ClampGearRank, before
    //                                               Isekai's Low-priority enhancer rolls the gear.
    // Both only ever clamp DOWN (weaker raiders keep Isekai's variance), skip bounty/quest targets and
    // colonists-turned-hostile, and are gated to The Antagonist so this stays its own behaviour.
    internal static class RankClamp
    {
        internal const string AntagonistDefName = "WR_TheAntagonist";

        // How many rank tiers above the colony's best pawn an enemy may be. Matches Isekai's own
        // adaptive intent (+1): "slightly tougher than you", tracking the party as it levels.
        internal const int RankHeadroom = 1;

        internal static bool AntagonistActive =>
            Find.Storyteller?.def != null && Find.Storyteller.def.defName == AntagonistDefName;

        internal static int CapFor(MobRankTier colonyBest) =>
            Math.Min((int)MobRankTier.S, (int)colonyBest + RankHeadroom); // never above S for raids

        // Best rank across all player home maps (the strongest colony). Used by the gear clamp at
        // generation time, when the specific target map isn't known yet.
        internal static MobRankTier ColonyBestAcrossHomeMaps()
        {
            MobRankTier best = MobRankTier.F;
            List<Map> maps = Find.Maps;
            if (maps == null) return best;
            for (int i = 0; i < maps.Count; i++)
            {
                Map m = maps[i];
                if (m == null || !m.IsPlayerHome) continue;
                MobRankTier r = RaidRankSystem.GetColonyBestRank(m);
                if ((int)r > (int)best) best = r;
            }
            return best;
        }

        internal static bool AnyPlayerHome()
        {
            List<Map> maps = Find.Maps;
            if (maps == null) return false;
            for (int i = 0; i < maps.Count; i++)
            {
                if (maps[i] != null && maps[i].IsPlayerHome) return true;
            }
            return false;
        }

        // Skip pawns that must never be re-ranked: colonists turned hostile, and forced bounty/quest
        // targets (whose rank the hunt system set on purpose).
        internal static bool Protected(Pawn pawn) =>
            pawn.playerSettings != null || IncidentWorker_IsekaiHunt.IsBountyPawn(pawn);

        // Force a pawn to a specific rank and regenerate its stats to match (mirrors the tail of
        // RaidRankSystem.AssignRaidPawnRank so GetRank() and the rank trait stay consistent).
        internal static void ForceRank(Pawn pawn, IsekaiComponent comp, MobRankTier target)
        {
            string rankStr = MobRankUtility.GetRankString(target);
            PawnStatGenerator.GenerateStatsForRank(rankStr, comp.stats);
            comp.currentLevel = LevelForRank(target);
            comp.stats.availableStatPoints = 0;
            PawnStatGenerator.UpdateRankTraitFromStats(pawn, comp);
        }

        // Representative level within each tier, matching Isekai's own GetLevelForRank so GetRank()
        // resolves back to the intended tier.
        internal static int LevelForRank(MobRankTier rank)
        {
            switch (rank)
            {
                case MobRankTier.F: return 3;
                case MobRankTier.E: return 8;
                case MobRankTier.D: return 14;
                case MobRankTier.C: return 22;
                case MobRankTier.B: return 38;
                case MobRankTier.A: return 70;
                case MobRankTier.S: return 130;
                default: return 3;
            }
        }
    }

    // Stats: after Isekai ranks a raider at Lord creation, clamp its rank/stats down to colony-best +1.
    [HarmonyPatch(typeof(RaidRankSystem), nameof(RaidRankSystem.AssignRaidPawnRank))]
    public static class Patch_RaidRankSystem_AssignRaidPawnRank
    {
        public static void Postfix(Pawn pawn, Map map)
        {
            if (!RankClamp.AntagonistActive || pawn == null || map == null || RankClamp.Protected(pawn))
            {
                return;
            }

            IsekaiComponent comp = IsekaiComponent.GetCached(pawn);
            if (comp == null) return;

            MobRankTier colonyBest = RaidRankSystem.GetColonyBestRank(map);
            int cap = RankClamp.CapFor(colonyBest);
            MobRankTier current = comp.GetRank();
            if ((int)current <= cap) return; // already within the ceiling

            var target = (MobRankTier)cap;
            RankClamp.ForceRank(pawn, comp, target);

            if (Prefs.DevMode)
            {
                Log.Message($"[The Antagonist] Stat clamp {pawn.LabelShort}: {MobRankUtility.GetRankString(current)} -> " +
                            $"{MobRankUtility.GetRankString(target)} (colony best {MobRankUtility.GetRankString(colonyBest)} +{RankClamp.RankHeadroom})");
            }
        }
    }

    // Gear: at pawn generation, clamp a hostile pawn's rank down to colony-best +1 BEFORE Isekai's
    // forge enhancer runs, so the gear it rolls matches the clamped rank. Priority sits between
    // Isekai's rank assignment (Normal) and its enhancer (Low), and it only ever clamps down.
    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), typeof(PawnGenerationRequest))]
    public static class Patch_PawnGenerator_ClampGearRank
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.LowerThanNormal)] // 300: after rank assignment (400), before gear enhancer (Low 200)
        public static void Postfix(Pawn __result)
        {
            Pawn pawn = __result;
            if (!RankClamp.AntagonistActive || pawn == null || !pawn.RaceProps.Humanlike)
            {
                return;
            }

            // Raiders only: hostile, non-player, and not a protected (colonist/bounty) pawn.
            if (Faction.OfPlayer == null || pawn.Faction == null || !pawn.Faction.HostileTo(Faction.OfPlayer))
            {
                return;
            }
            if (RankClamp.Protected(pawn)) return;

            IsekaiComponent comp = pawn.GetComp<IsekaiComponent>();
            if (comp == null) return;

            // Need a colony to measure against; during worldgen there is none, so skip.
            if (!RankClamp.AnyPlayerHome()) return;

            MobRankTier colonyBest = RankClamp.ColonyBestAcrossHomeMaps();
            int cap = RankClamp.CapFor(colonyBest);
            MobRankTier current = comp.GetRank();
            if ((int)current <= cap) return; // never raise

            var target = (MobRankTier)cap;
            RankClamp.ForceRank(pawn, comp, target);

            if (Prefs.DevMode)
            {
                Log.Message($"[The Antagonist] Gear-rank clamp {pawn.LabelShort}: {MobRankUtility.GetRankString(current)} -> " +
                            $"{MobRankUtility.GetRankString(target)} (colony best {MobRankUtility.GetRankString(colonyBest)} +{RankClamp.RankHeadroom})");
            }
        }
    }
}
