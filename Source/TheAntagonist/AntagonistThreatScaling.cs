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

    // Scale The Antagonist's threat budget by the colony's Isekai power.
    //
    // Vanilla sizes raids from wealth + pawn count, which badly under-reads an Isekai party: a level-300
    // swordsman in plain gear looks "cheap" to the storyteller, so late-game raids become trivial. We
    // postfix StorytellerUtility.DefaultThreatPointsNow - the single function that sets the point budget
    // for essentially every threat - and multiply it by a factor derived from the party's Isekai levels.
    //
    // Pawn COUNT is already handled: vanilla adds points per colonist, so 10 pawns already yield ~10x the
    // baseline of 1 pawn. Multiplying that count-aware baseline by a per-pawn power factor works out to
    // roughly "sum of per-pawn power", which is what we want.
    //
    // Only applies while The Antagonist is the active storyteller, and only ever raises the budget
    // (floored at 1.0x) - it never makes the game easier than baseline.
    [HarmonyPatch(typeof(StorytellerUtility), nameof(StorytellerUtility.DefaultThreatPointsNow))]
    public static class Patch_StorytellerUtility_DefaultThreatPointsNow
    {
        private const string AntagonistDefName = "WR_TheAntagonist";

        public static void Postfix(IIncidentTarget target, ref float __result)
        {
            if (target == null || __result <= 0f)
            {
                return;
            }

            if (Find.Storyteller?.def == null || Find.Storyteller.def.defName != AntagonistDefName)
            {
                return; // another storyteller is running; leave vanilla alone
            }

            float multiplier = IsekaiPower.ThreatMultiplierFor(target);
            if (multiplier > 1f)
            {
                __result *= multiplier;
            }
        }
    }

    internal static class IsekaiPower
    {
        // Power-weighted average level -> threat multiplier.
        // Isekai tiers for reference: ~50 = A, 100 = S, 200 = SS, 400 = SSS.
        private static readonly SimpleCurve ThreatCurve = new SimpleCurve
        {
            new CurvePoint(1f, 1.0f),
            new CurvePoint(50f, 1.7f),
            new CurvePoint(100f, 2.5f),
            new CurvePoint(200f, 4.0f),
            new CurvePoint(400f, 4.5f),
        };

        public static float ThreatMultiplierFor(IIncidentTarget target)
        {
            // Power-weighted (contraharmonic) mean level = sum(level^2) / sum(level).
            // A flat average would let nine level-1 pawns hide one level-300 hero; weighting by level
            // means the party's real muscle drives the threat.
            double sumLevel = 0d;
            double sumLevelSquared = 0d;

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
            }

            if (sumLevel <= 0d)
            {
                return 1f; // no Isekai pawns - no scaling
            }

            float weightedAverageLevel = (float)(sumLevelSquared / sumLevel);
            float multiplier = ThreatCurve.Evaluate(weightedAverageLevel);
            return multiplier < 1f ? 1f : multiplier;
        }
    }
}
