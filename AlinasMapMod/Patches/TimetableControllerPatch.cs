using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Model.Ops;
using Model.Ops.Timetable;

namespace AlinasMapMod.Patches;

[HarmonyPatch(typeof(TimetableController), nameof(TimetableController.GetAllStations))]
[HarmonyPatchCategory("AlinasMapMod")]
internal static class TimetableControllerPatch
{
    [HarmonyPrefix]
    internal static bool GetAllStaitonsModified(TimetableController __instance, ref IReadOnlyList<TimetableStation> __result,
        TimetableBranch branch, bool includeDisabled, bool includeDuplicates)
    {
        List<TimetableBranch> source;
        if (branch == null) source = __instance.branches;
        else source = [branch];

        var list = source.SelectMany(br => br.stations).Where(st => includeDisabled || st.IsEnabled)
            .Where(ts => includeDuplicates || !ts.IsBranchJunctionDuplicate).Distinct().OrderBy(b =>
            b.passengerStop?.transform.GetComponentInParent<Area>().transform.GetSiblingIndex() ?? int.MaxValue).ToList();
        __result = list;
        return false;
    }
}
