using UnityEngine;
using Verse;

namespace AlertSpeaker;

public class PlaceWorker_AlertSpeaker : PlaceWorker
{
    public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map,
        Thing thingToIgnore = null, Thing thing = null)
    {
        AcceptanceReport result;
        if (!Building_AlertSpeaker.IsSupportAlive(map, loc, rot))
        {
            result = new AcceptanceReport("MCAS.PlaceWorker".Translate());
        }
        else
        {
            result = true;
        }

        return result;
    }

    public override void DrawGhost(ThingDef def, IntVec3 center, Rot4 rot, Color ghostCol, Thing thing = null)
    {
        base.DrawGhost(def, center, rot, ghostCol, thing);
        if (center.GetEdifice(Find.CurrentMap) != null)
        {
            return;
        }

        var areaOfEffectCells = Building_AlertSpeaker.GetAreaOfEffectCells(Find.CurrentMap, center);
        GenDraw.DrawFieldEdges(areaOfEffectCells);
    }
}