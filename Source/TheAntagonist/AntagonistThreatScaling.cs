using HarmonyLib;
using IsekaiLeveling;
using RimWorld;
using Verse;

namespace TheAntagonist
{
    [StaticConstructorOnStartup]
    public static class TheAntagonistMod
    {
        static TheAntagonistMod()
        {
            new Harmony("wishRobber.theantagonist").PatchAll();
        }
    }

    // Scale The Antagonist's threat budget to the colony's Isekai POWER, not its wealth.
    //
    // Vanilla DefaultThreatPointsNow sizes raids mostly from colony WEALTH: a flat wealth term, PLUS a
    // per-colonist term that itself scales with wealth (points per colonist climb from 15 at low wealth
    // to 140+ at high wealth). So a hoard of gear and silver balloons raids no matter how strong your
    // pawns actually are - the opposite of what an Isekai run wants, where difficulty should track levels.
    //
    // Two changes, both only while The Antagonist is the active storyteller:
    //   1. WEALTH IS NEUTRALISED. For the duration of the threat calc we feed the formula a fixed wealth
    //      (FixedThreatWealth) in place of the real number, so raid size stops tracking what you own.
    //      Everything else vanilla reads (pawn count, animals, mechs, adaptation, days passed) is kept.
    //   2. LEVEL SCALES IT UP. We then multiply the now wealth-free budget by a factor from the party's
    //      Isekai levels, so a strong party gets bigger raids and a weak one does not. Count is already in
    //      the vanilla baseline (points per colonist), so this works out to roughly "sum of per-pawn
    //      power". The multiplier is floored at 1.0x, so this never drops below the wealth-free baseline.
    [HarmonyPatch(typeof(StorytellerUtility), nameof(StorytellerUtility.DefaultThreatPointsNow))]
    public static class Patch_StorytellerUtility_DefaultThreatPointsNow
    {
        // True only for the duration of DefaultThreatPointsNow under The Antagonist; while set, the map's
        // wealth-for-storyteller reads back as FixedThreatWealth (see the Map getter patch below).
        internal static bool NeutralizeWealth;

        // Wealth fed to the threat formula in place of the colony's real wealth. Pinned to ~15 - below
        // the 14k where the flat wealth term begins, and at the floor of the per-colonist curve - so
        // wealth is effectively ELIMINATED: the flat wealth term is 0 and every colonist contributes only
        // the minimum 15 points. Raids are then driven by pawn count + the Isekai level multiplier alone.
        // If that leaves raids too soft, raise the LEVEL curve (ThreatCurve) rather than this value.
        internal const float FixedThreatWealth = 15f;

        public static void Prefix()
        {
            if (RankClamp.AntagonistActive)
            {
                NeutralizeWealth = true;
            }
        }

        public static void Postfix(IIncidentTarget target, ref float __result)
        {
            if (target == null || __result <= 0f || !RankClamp.AntagonistActive)
            {
                return;
            }

            float baseBudget = __result; // vanilla budget, already computed with wealth neutralised
            float multiplier = IsekaiPower.ThreatMultiplierFor(target, out float weightedLevel, out int isekaiPawns);
            if (multiplier > 1f)
            {
                __result *= multiplier;
            }

            if (Prefs.DevMode)
            {
                // Real wealth read straight from the watcher (bypasses our PlayerWealthForStoryteller
                // override) so the log shows what it WOULD have been, proving wealth is no longer used.
                float realWealth = (target as Map)?.wealthWatcher?.WealthTotal ?? -1f;
                Log.Message($"[The Antagonist] Threat budget: wealth-free base {baseBudget:F0} x level {multiplier:F2} = {__result:F0} pts "
                    + $"| party {isekaiPawns} Isekai pawns, weighted-avg lvl {weightedLevel:F0} "
                    + $"| wealth ignored (real {realWealth:F0}, fed {FixedThreatWealth})");
            }
        }

        // Always clear the flag, even if the original threw, so wealth reads are never left neutralised.
        public static void Finalizer()
        {
            NeutralizeWealth = false;
        }
    }

    // Feed the threat calculation a fixed wealth so raid size no longer tracks colony wealth. Guarded by
    // the flag above, so this only affects reads made inside DefaultThreatPointsNow under The Antagonist;
    // every other read of PlayerWealthForStoryteller returns the real value untouched.
    [HarmonyPatch(typeof(Map), nameof(Map.PlayerWealthForStoryteller), MethodType.Getter)]
    public static class Patch_Map_PlayerWealthForStoryteller_Neutralize
    {
        public static void Postfix(ref float __result)
        {
            if (Patch_StorytellerUtility_DefaultThreatPointsNow.NeutralizeWealth)
            {
                __result = Patch_StorytellerUtility_DefaultThreatPointsNow.FixedThreatWealth;
            }
        }
    }

    internal static class IsekaiPower
    {
        // Power-weighted average level -> threat multiplier. Linear, ~+0.01x per level, from 1.0x at
        // level 1 to 5.0x at level 400 (and capped there for anything beyond).
        // Deliberately NOT front-loaded: an earlier curve hit 4.4x by level 200 and then crawled to 5.0x
        // at 400, which is backwards - levels 200-400 (SS -> SSS) are where Isekai power grows most, so
        // flattening there would let the strongest parties outrun the storyteller.
        // Isekai tiers for reference: ~50 = A, 100 = S, 200 = SS, 400 = SSS.
        private static readonly SimpleCurve ThreatCurve = new SimpleCurve
        {
            new CurvePoint(1f, 1.0f),
            new CurvePoint(50f, 1.5f),
            new CurvePoint(100f, 2.0f),
            new CurvePoint(200f, 3.0f),
            new CurvePoint(400f, 5.0f),
        };

        public static float ThreatMultiplierFor(IIncidentTarget target, out float weightedAverageLevel, out int isekaiPawnCount)
        {
            // Power-weighted (contraharmonic) mean level = sum(level^2) / sum(level).
            // A flat average would let nine level-1 pawns hide one level-300 hero; weighting by level
            // means the party's real muscle drives the threat.
            double sumLevel = 0d;
            double sumLevelSquared = 0d;
            isekaiPawnCount = 0;

            foreach (Pawn pawn in target.PlayerPawnsForStoryteller)
            {
                if (pawn == null || !pawn.IsFreeColonist || pawn.IsQuestLodger())
                {
                    continue;
                }

                int level = IsekaiComponent.GetCached(pawn)?.Level ?? 0;
                if (level <= 0)
                {
                    continue; // not an Isekai pawn, or unlevelled
                }

                sumLevel += level;
                sumLevelSquared += (double)level * level;
                isekaiPawnCount++;
            }

            if (sumLevel <= 0d)
            {
                weightedAverageLevel = 0f;
                return 1f; // no Isekai pawns - no scaling
            }

            weightedAverageLevel = (float)(sumLevelSquared / sumLevel);
            float multiplier = ThreatCurve.Evaluate(weightedAverageLevel);
            return multiplier < 1f ? 1f : multiplier;
        }
    }
}
