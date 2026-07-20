using System;
using HarmonyLib;
using IsekaiLeveling;
using IsekaiLeveling.MobRanking;
using IsekaiLeveling.Quests;
using RimWorld;
using Verse;

namespace TheAntagonist
{
    // Keep raid enemy ranks tied to the colony's own Isekai rank.
    //
    // Isekai ranks raiders from raid points, and has an "Adaptive Raid Ranks" setting that is supposed to
    // clamp them to within +1 of the colony's best pawn - but in practice it doesn't reliably fire (raids
    // still arrive several tiers above the party). Rather than rely on that setting, The Antagonist enforces
    // the ceiling itself: after Isekai finishes ranking a raider, if that raider outranks the colony's best
    // colonist by more than one tier, we re-rank it down to (colony best + 1) and regenerate its stats to
    // match. This runs every raid, needs no configuration, and scales automatically as the party levels.
    //
    // Only the ceiling is enforced - weaker raiders keep Isekai's normal variance, so raids still vary.
    // Bounty/quest targets and colonists-turned-hostile are left untouched (same guards Isekai uses).
    // Gated to The Antagonist so it stays this storyteller's own behaviour.
    [HarmonyPatch(typeof(RaidRankSystem), nameof(RaidRankSystem.AssignRaidPawnRank))]
    public static class Patch_RaidRankSystem_AssignRaidPawnRank
    {
        private const string AntagonistDefName = "WR_TheAntagonist";

        // How many rank tiers above the colony's best pawn a raider may be. Matches Isekai's own
        // adaptive intent (+1): a fair "slightly tougher than you" ceiling that tracks the party.
        private const int RankHeadroom = 1;

        public static void Postfix(Pawn pawn, Map map)
        {
            if (Find.Storyteller?.def == null || Find.Storyteller.def.defName != AntagonistDefName)
            {
                return; // another storyteller is running; leave Isekai's ranking alone
            }

            if (pawn == null || map == null)
            {
                return;
            }

            // Don't touch colonists temporarily turned hostile, or forced bounty/quest targets.
            if (pawn.playerSettings != null || IncidentWorker_IsekaiHunt.IsBountyPawn(pawn))
            {
                return;
            }

            IsekaiComponent comp = IsekaiComponent.GetCached(pawn);
            if (comp == null)
            {
                return;
            }

            MobRankTier colonyBest = RaidRankSystem.GetColonyBestRank(map);
            int maxAllowed = Math.Min((int)MobRankTier.S, (int)colonyBest + RankHeadroom); // never above S for raids

            MobRankTier current = comp.GetRank();
            if ((int)current <= maxAllowed)
            {
                return; // already within the ceiling
            }

            // Re-rank down to the ceiling and regenerate stats to match (mirrors the tail of
            // RaidRankSystem.AssignRaidPawnRank so GetRank() and the rank trait stay consistent).
            var target = (MobRankTier)maxAllowed;
            string rankStr = MobRankUtility.GetRankString(target);
            PawnStatGenerator.GenerateStatsForRank(rankStr, comp.stats);
            comp.currentLevel = LevelForRank(target);
            comp.stats.availableStatPoints = 0;
            PawnStatGenerator.UpdateRankTraitFromStats(pawn, comp);

            if (Prefs.DevMode)
            {
                Log.Message($"[The Antagonist] Clamped {pawn.LabelShort}: {MobRankUtility.GetRankString(current)} -> " +
                            $"{rankStr} (colony best {MobRankUtility.GetRankString(colonyBest)} +{RankHeadroom})");
            }
        }

        // Representative level within each tier, matching Isekai's own GetLevelForRank so GetRank()
        // resolves back to the intended tier.
        private static int LevelForRank(MobRankTier rank)
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
}
