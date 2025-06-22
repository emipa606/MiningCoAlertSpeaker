using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace AlertSpeaker;

[StaticConstructorOnStartup]
internal class Building_AlertSpeaker : Building
{
    private const float AlertSpeakerMaxRange = 6.9f;

    private const int EarlyPhaseTicksThreshold = 15000;

    private const int AlarmSoundPeriodInTicks = 1200;

    private const int UpdatePeriodInTicks = 60;

    private static int nextUpdateTick;

    private static int alertStartTick;

    private static StoryDanger previousDangerRate = StoryDanger.None;

    private static StoryDanger currentDangerRate = StoryDanger.None;

    private static int lastDrawingUpdateTick;

    private static float glowRadius;

    private static ColorInt glowColor = new(0, 0, 0, 255);

    private static float redAlertLightAngle;

    private static float redAlertLightIntensity = 0.25f;

    private static readonly Material redAlertLight =
        MaterialPool.MatFrom("Effects/RedAlertLight", ShaderDatabase.Transparent);

    private static Matrix4x4 redAlertLightMatrix;

    private static readonly Vector3 redAlertLightScale = new(5f, 1f, 5f);

    private static bool soundIsEnabled = true;

    private static int nextAlarmSoundTick;

    private static readonly SoundDef lowDangerAlarmSound = SoundDef.Named("LowDangerAlarm");

    private static readonly SoundDef highDangerAlarmSound = SoundDef.Named("HighDangerAlarm");

    private CompGlower glowerComp;

    private CompPowerTrader powerComp;

    public override void SpawnSetup(Map map, bool respawningAfterLoad)
    {
        base.SpawnSetup(map, respawningAfterLoad);
        glowerComp = GetComp<CompGlower>();
        powerComp = GetComp<CompPowerTrader>();
        nextUpdateTick = Find.TickManager.TicksGame + UpdatePeriodInTicks;
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref nextUpdateTick, "nextUpdateTick");
        Scribe_Values.Look(ref alertStartTick, "alertStartTick");
        Scribe_Values.Look(ref previousDangerRate, "previousDangerRate");
        Scribe_Values.Look(ref currentDangerRate, "currentDangerRate");
        Scribe_Values.Look(ref nextAlarmSoundTick, "nextAlarmSoundTick");
    }

    public static bool IsSupportAlive(Map map, IntVec3 position, Rot4 rotation)
    {
        var c = position + new IntVec3(0, 0, -1).RotatedBy(rotation);
        var edifice = c.GetEdifice(map);
        return edifice != null && (edifice.def == ThingDefOf.Wall || edifice.def.building.isNaturalRock ||
                                   edifice.def.fillPercent >= 0.8f);
    }

    public static List<IntVec3> GetAreaOfEffectCells(Map map, IntVec3 position)
    {
        var list = new List<IntVec3>();
        foreach (var intVec in GenRadial.RadialCellsAround(position, AlertSpeakerMaxRange, true))
        {
            if (intVec.GetRoom(map) == position.GetRoom(map))
            {
                list.Add(intVec);
            }
        }

        return list;
    }

    protected override void Tick()
    {
        base.Tick();
        if (Map == null)
        {
            return;
        }

        if (!IsSupportAlive(Map, Position, Rotation))
        {
            Destroy(DestroyMode.Deconstruct);
            return;
        }

        if (Find.TickManager.TicksGame > nextUpdateTick)
        {
            nextUpdateTick = Find.TickManager.TicksGame + UpdatePeriodInTicks;
            previousDangerRate = currentDangerRate;
            currentDangerRate = Map.dangerWatcher.DangerRating;
            if (currentDangerRate != previousDangerRate)
            {
                PerformTreatmentOnDangerRateChange();
            }

            performSoundTreatment();
        }

        if (nextUpdateTick == Find.TickManager.TicksGame + UpdatePeriodInTicks)
        {
            updateGlowerParameters();
            var powerOn = powerComp.PowerOn;
            if (powerOn)
            {
                tryApplyAdrenalineBonus();
            }
        }

        if (Find.TickManager.TicksGame <= lastDrawingUpdateTick)
        {
            return;
        }

        lastDrawingUpdateTick = Find.TickManager.TicksGame;
        updateDrawingParameters();
    }

    private void PerformTreatmentOnDangerRateChange()
    {
        switch (previousDangerRate)
        {
            case StoryDanger.None when
                currentDangerRate is StoryDanger.Low or StoryDanger.High:
                alertStartTick = Find.TickManager.TicksGame;
                nextAlarmSoundTick = 0;
                return;
            case StoryDanger.Low or StoryDanger.High when
                currentDangerRate == StoryDanger.None:
                removeAnyAdrenalineHediffFromAllColonists();
                nextAlarmSoundTick = 0;
                return;
            case StoryDanger.Low when currentDangerRate == StoryDanger.High:
            {
                nextAlarmSoundTick = 0;
                foreach (var colonist in Map.mapPawns.FreeColonists)
                {
                    if (!hasHediffAdrenalineSmall(colonist))
                    {
                        continue;
                    }

                    removeHediffAdrenalineSmall(colonist);
                    applyHediffAdrenalineMedium(colonist);
                }

                return;
            }
            case StoryDanger.High when currentDangerRate == StoryDanger.Low:
                nextAlarmSoundTick = 0;
                break;
        }
    }

    private void tryApplyAdrenalineBonus()
    {
        if (currentDangerRate == StoryDanger.None)
        {
            return;
        }

        if (Find.TickManager.TicksGame > alertStartTick + EarlyPhaseTicksThreshold)
        {
            return;
        }

        foreach (var pawn in Map.mapPawns.FreeColonists)
        {
            if (pawn.Downed || pawn.Dead || !pawn.Awake() || pawn.GetRoom() != this.GetRoom() ||
                !pawn.Position.InHorDistOf(Position, AlertSpeakerMaxRange))
            {
                continue;
            }

            if (currentDangerRate == StoryDanger.Low)
            {
                if (!hasHediffAdrenalineMedium(pawn))
                {
                    applyHediffAdrenalineSmall(pawn);
                }

                continue;
            }

            removeHediffAdrenalineSmall(pawn);
            applyHediffAdrenalineMedium(pawn);
        }
    }

    private static bool hasHediffAdrenalineSmall(Pawn colonist)
    {
        var firstHediffOfDef =
            colonist.health.hediffSet.GetFirstHediffOfDef(Util_AlertSpeaker.HediffAdrenalineSmallDef);
        return firstHediffOfDef != null;
    }

    private static bool hasHediffAdrenalineMedium(Pawn colonist)
    {
        var firstHediffOfDef =
            colonist.health.hediffSet.GetFirstHediffOfDef(Util_AlertSpeaker.HediffAdrenalineMediumDef);
        return firstHediffOfDef != null;
    }

    private static void applyHediffAdrenalineSmall(Pawn colonist)
    {
        if (!hasHediffAdrenalineSmall(colonist))
        {
            var moteBubble = (MoteBubble)ThingMaker.MakeThing(ThingDefOf.Mote_ThoughtGood);
            moteBubble.SetupMoteBubble(ContentFinder<Texture2D>.Get("Things/Mote/IncapIcon"), null);
            moteBubble.Attach(colonist);
            GenSpawn.Spawn(moteBubble, colonist.Position, colonist.Map);
        }

        colonist.health.AddHediff(Util_AlertSpeaker.HediffAdrenalineSmallDef);
    }

    private static void applyHediffAdrenalineMedium(Pawn colonist)
    {
        if (!hasHediffAdrenalineMedium(colonist))
        {
            var moteBubble = (MoteBubble)ThingMaker.MakeThing(ThingDefOf.Mote_ThoughtBad);
            moteBubble.SetupMoteBubble(ContentFinder<Texture2D>.Get("Things/Mote/IncapIcon"), null);
            moteBubble.Attach(colonist);
            GenSpawn.Spawn(moteBubble, colonist.Position, colonist.Map);
        }

        colonist.health.AddHediff(Util_AlertSpeaker.HediffAdrenalineMediumDef);
    }

    private static void removeHediffAdrenalineSmall(Pawn colonist)
    {
        var firstHediffOfDef =
            colonist.health.hediffSet.GetFirstHediffOfDef(Util_AlertSpeaker.HediffAdrenalineSmallDef);
        if (firstHediffOfDef != null)
        {
            colonist.health.RemoveHediff(firstHediffOfDef);
        }
    }

    private void removeAnyAdrenalineHediffFromAllColonists()
    {
        IEnumerable<Pawn> freeColonists = Map.mapPawns.FreeColonists;
        foreach (var pawn in freeColonists)
        {
            var firstHediffOfDef =
                pawn.health.hediffSet.GetFirstHediffOfDef(Util_AlertSpeaker.HediffAdrenalineSmallDef);
            if (firstHediffOfDef != null)
            {
                pawn.health.RemoveHediff(firstHediffOfDef);
            }

            firstHediffOfDef = pawn.health.hediffSet.GetFirstHediffOfDef(Util_AlertSpeaker.HediffAdrenalineMediumDef);
            if (firstHediffOfDef != null)
            {
                pawn.health.RemoveHediff(firstHediffOfDef);
            }
        }
    }

    private void performSoundTreatment()
    {
        if (currentDangerRate == StoryDanger.None)
        {
            return;
        }

        if (Find.TickManager.TicksGame < nextAlarmSoundTick)
        {
            return;
        }

        nextAlarmSoundTick = Find.TickManager.TicksGame +
                             (AlarmSoundPeriodInTicks * (int)Find.TickManager.CurTimeSpeed);
        if (currentDangerRate == StoryDanger.Low)
        {
            PlayOneLowDangerAlarmSound();
        }
        else
        {
            PlayOneHighDangerAlarmSound();
        }
    }

    private static void PlayOneLowDangerAlarmSound()
    {
        if (soundIsEnabled)
        {
            lowDangerAlarmSound.PlayOneShotOnCamera();
        }
    }

    private static void PlayOneHighDangerAlarmSound()
    {
        if (soundIsEnabled)
        {
            highDangerAlarmSound.PlayOneShotOnCamera();
        }
    }

    private void updateDrawingParameters()
    {
        switch (currentDangerRate)
        {
            case StoryDanger.None:
                glowRadius = 0f;
                glowColor.r = 0;
                glowColor.g = 0;
                glowColor.b = 0;
                break;
            case StoryDanger.Low:
                glowRadius = 4f;
                glowColor.r = 242;
                glowColor.g = 185;
                glowColor.b = 0;
                break;
            case StoryDanger.High:
            {
                glowRadius = 0f;
                glowColor.r = 220;
                glowColor.g = 0;
                glowColor.b = 0;
                redAlertLightAngle = (redAlertLightAngle + (3f / Find.TickManager.TickRateMultiplier)) % 360f;
                switch (redAlertLightAngle)
                {
                    case < 90f:
                        redAlertLightIntensity = redAlertLightAngle / 90f;
                        break;
                    case < 180f:
                        redAlertLightIntensity = 1f - ((redAlertLightAngle - 90f) / 90f);
                        break;
                    default:
                        redAlertLightIntensity = 0f;
                        break;
                }

                redAlertLightMatrix.SetTRS(
                    Position.ToVector3ShiftedWithAltitude(AltitudeLayer.Silhouettes) +
                    new Vector3(0f, 0f, -0.25f).RotatedBy(Rotation.AsAngle) + Altitudes.AltIncVect,
                    (Rotation.AsAngle + redAlertLightAngle).ToQuat(), redAlertLightScale);
                break;
            }
        }
    }

    private void updateGlowerParameters()
    {
        if (glowerComp.Props.glowRadius == glowRadius && glowerComp.Props.glowColor == glowColor)
        {
            return;
        }

        glowerComp.Props.glowRadius = glowRadius;
        glowerComp.Props.glowColor = glowColor;
        Map.mapDrawer.MapMeshDirty(Position, 1);
        Map.glowGrid.DirtyCell(Position);
    }

    protected override void DrawAt(Vector3 drawLoc, bool flip = false)
    {
        base.DrawAt(drawLoc, flip);
        if (currentDangerRate == StoryDanger.High && powerComp.PowerOn)
        {
            Graphics.DrawMesh(MeshPool.plane10, redAlertLightMatrix,
                FadedMaterialPool.FadedVersionOf(redAlertLight, redAlertLightIntensity), 0);
        }

        if (!Find.Selector.IsSelected(this))
        {
            return;
        }

        var areaOfEffectCells = GetAreaOfEffectCells(Map, Position);
        GenDraw.DrawFieldEdges(areaOfEffectCells);
    }

    public override IEnumerable<Gizmo> GetGizmos()
    {
        IList<Gizmo> list = new List<Gizmo>();
        const int num = 700000100;
        var commandAction = new Command_Action();
        if (soundIsEnabled)
        {
            commandAction.icon = ContentFinder<Texture2D>.Get("Ui/Commands/CommandButton_SirenSoundEnabled");
            commandAction.defaultLabel = "MCAS.DisableSound".Translate();
            commandAction.defaultDesc = "MCAS.DisableSoundTT".Translate();
        }
        else
        {
            commandAction.icon = ContentFinder<Texture2D>.Get("Ui/Commands/CommandButton_SirenSoundDisabled");
            commandAction.defaultLabel = "MCAS.EnableSound".Translate();
            commandAction.defaultDesc = "CAS.EnableSoundTT".Translate();
        }

        commandAction.activateSound = SoundDef.Named("Click");
        commandAction.action = performSirenSoundAction;
        commandAction.groupKey = num + 1;
        list.Add(commandAction);
        var gizmos = base.GetGizmos();
        var result = gizmos != null ? list.AsEnumerable().Concat(gizmos) : list.AsEnumerable();

        return result;
    }

    private static void performSirenSoundAction()
    {
        soundIsEnabled = !soundIsEnabled;
    }
}