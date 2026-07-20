using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;

namespace TheAntagonist
{
    // Dev-mode diagnostics for The Antagonist's event generation.
    //
    // Every storyteller-generated incident is fired through Storyteller.TryFire(FiringIncident). A
    // FiringIncident carries the selected incident (def), the parameters (points/faction/target) and
    // the source comp that produced it (which "pool"/generator in the storyteller). We log, for each
    // fired event: the source comp, the incident's category, points/faction, and the full candidate
    // pool for that category with each incident's baseChance weight - marking the one that was picked.
    //
    // Only active while The Antagonist is the storyteller AND Development mode is on, so it stays out
    // of normal play. Storyteller-generated events only (forced/dev-executed incidents skip TryFire).
    [HarmonyPatch(typeof(Storyteller), nameof(Storyteller.TryFire))]
    public static class Patch_Storyteller_TryFire_Debug
    {
        public static void Postfix(FiringIncident fi, bool __result)
        {
            if (!Prefs.DevMode || !RankClamp.AntagonistActive) return;
            if (fi?.def == null || !__result) return; // log only events that actually fired

            IncidentCategoryDef category = fi.def.category;
            string cat = category?.defName ?? "(none)";
            string src = fi.source?.GetType().Name ?? "(none)";
            string faction = fi.parms?.faction?.Name ?? "-";
            float points = fi.parms?.points ?? 0f;

            var sb = new StringBuilder();
            sb.Append($"[Antagonist] fired '{fi.def.defName}'  category={cat}  source={src}  points={points:F0}  faction={faction}");

            // Candidate pool for that category + weights (baseChance), highest weight first.
            if (category != null)
            {
                var pool = DefDatabase<IncidentDef>.AllDefs
                    .Where(d => d.category == category)
                    .OrderByDescending(d => d.baseChance)
                    .ToList();

                sb.Append($"\n    pool [{cat}] ({pool.Count} incidents, weight = baseChance):");
                foreach (IncidentDef d in pool)
                {
                    bool selected = d == fi.def;
                    sb.Append(selected
                        ? $"\n      -> {d.defName} = {d.baseChance:0.###}   <== SELECTED"
                        : $"\n         {d.defName} = {d.baseChance:0.###}");
                }
            }

            Log.Message(sb.ToString());
        }
    }
}
