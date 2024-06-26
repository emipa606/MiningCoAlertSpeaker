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
    public const float alertSpeakerMaxRange = 6.9f;

    public const int earlyPhaseTicksThreshold = 15000;

    public const int alarmSoundPeriodInTicks = 1200;

    public static readonly int updatePeriodInTicks = 60;

    public static int nextUpdateTick;

    public static int alertStartTick;

    public static StoryDanger previousDangerRate = StoryDanger.None;

    public static StoryDanger currentDangerRate = StoryDanger.None;

    public static int lastDrawingUpdateTick;

    public static float glowRadius;

    public static ColorInt glowColor = new ColorInt(0, 0, 0, 255);

    public static float redAlertLightAngle;

    public static float redAlertLightIntensity = 0.25f;

    public static readonly Material redAlertLight =
        MaterialPool.MatFrom("Effects/RedAlertLight", ShaderDatabase.Transparent);

    public static Matrix4x4 redAlertLightMatrix;

    public static Vector3 redAlertLightScale = new Vector3(5f, 1f, 5f);

    public static bool soundIsEnabled = true;

    public static int nextAlarmSoundTick;

    public static readonly SoundDef lowDangerAlarmSound = SoundDef.Named("LowDangerAlarm");

    public static readonly SoundDef highDangerAlarmSound = SoundDef.Named("HighDangerAlarm");

    public CompGlower glowerComp;

    public CompPowerTrader powerComp;

    public override void SpawnSetup(Map map, bool respawningAfterLoad)
    {
        base.SpawnSetup(map, respawningAfterLoad);
        glowerComp = GetComp<CompGlower>();
        powerComp = GetComp<CompPowerTrader>();
        nextUpdateTick = Find.TickManager.TicksGame + updatePeriodInTicks;
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
        foreach (var intVec in GenRadial.RadialCellsAround(position, alertSpeakerMaxRange, true))
        {
            if (intVec.GetRoom(map) == position.GetRoom(map))
            {
                list.Add(intVec);
            }
        }

        return list;
    }

    public override void Tick()
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
            nextUpdateTick = Find.TickManager.TicksGame + updatePeriodInTicks;
            previousDangerRate = currentDangerRate;
            currentDangerRate = Map.dangerWatcher.DangerRating;
            if (currentDangerRate != previousDangerRate)
            {
                PerformTreatmentOnDangerRateChange();
            }

            PerformSoundTreatment();
        }

        if (nextUpdateTick == Find.TickManager.TicksGame + updatePeriodInTicks)
        {
            UpdateGlowerParameters();
            var powerOn = powerComp.PowerOn;
            if (powerOn)
            {
                TryApplyAdrenalineBonus();
            }
        }

        if (Find.TickManager.TicksGame <= lastDrawingUpdateTick)
        {
            return;
        }

        lastDrawingUpdateTick = Find.TickManager.TicksGame;
        UpdateDrawingParameters();
    }

    public void PerformTreatmentOnDangerRateChange()
    {
        if (previousDangerRate == StoryDanger.None &&
            currentDangerRate is StoryDanger.Low or StoryDanger.High)
        {
            alertStartTick = Find.TickManager.TicksGame;
            nextAlarmSoundTick = 0;
            return;
        }

        if (previousDangerRate is StoryDanger.Low or StoryDanger.High &&
            currentDangerRate == StoryDanger.None)
        {
            RemoveAnyAdrenalineHediffFromAllColonists();
            nextAlarmSoundTick = 0;
            return;
        }

        if (previousDangerRate == StoryDanger.Low && currentDangerRate == StoryDanger.High)
        {
            nextAlarmSoundTick = 0;
            foreach (var colonist in Map.mapPawns.FreeColonists)
            {
                if (!HasHediffAdrenalineSmall(colonist))
                {
                    continue;
                }

                RemoveHediffAdrenalineSmall(colonist);
                ApplyHediffAdrenalineMedium(colonist);
            }

            return;
        }

        if (previousDangerRate == StoryDanger.High && currentDangerRate == StoryDanger.Low)
        {
            nextAlarmSoundTick = 0;
        }
    }

    public void TryApplyAdrenalineBonus()
    {
        if (currentDangerRate == StoryDanger.None)
        {
            return;
        }

        if (Find.TickManager.TicksGame > alertStartTick + earlyPhaseTicksThreshold)
        {
            return;
        }

        foreach (var pawn in Map.mapPawns.FreeColonists)
        {
            if (pawn.Downed || pawn.Dead || !pawn.Awake() || pawn.GetRoom() != this.GetRoom() ||
                !pawn.Position.InHorDistOf(Position, alertSpeakerMaxRange))
            {
                continue;
            }

            if (currentDangerRate == StoryDanger.Low)
            {
                if (!HasHediffAdrenalineMedium(pawn))
                {
                    ApplyHediffAdrenalineSmall(pawn);
                }

                continue;
            }

            RemoveHediffAdrenalineSmall(pawn);
            ApplyHediffAdrenalineMedium(pawn);
        }
    }

    public static bool HasHediffAdrenalineSmall(Pawn colonist)
    {
        var firstHediffOfDef =
            colonist.health.hediffSet.GetFirstHediffOfDef(Util_AlertSpeaker.HediffAdrenalineSmallDef);
        return firstHediffOfDef != null;
    }

    public static bool HasHediffAdrenalineMedium(Pawn colonist)
    {
        var firstHediffOfDef =
            colonist.health.hediffSet.GetFirstHediffOfDef(Util_AlertSpeaker.HediffAdrenalineMediumDef);
        return firstHediffOfDef != null;
    }

    public static void ApplyHediffAdrenalineSmall(Pawn colonist)
    {
        if (!HasHediffAdrenalineSmall(colonist))
        {
            var moteBubble = (MoteBubble)ThingMaker.MakeThing(ThingDefOf.Mote_ThoughtGood);
            moteBubble.SetupMoteBubble(ContentFinder<Texture2D>.Get("Things/Mote/IncapIcon"), null);
            moteBubble.Attach(colonist);
            GenSpawn.Spawn(moteBubble, colonist.Position, colonist.Map);
        }

        colonist.health.AddHediff(Util_AlertSpeaker.HediffAdrenalineSmallDef);
    }

    public static void ApplyHediffAdrenalineMedium(Pawn colonist)
    {
        if (!HasHediffAdrenalineMedium(colonist))
        {
            var moteBubble = (MoteBubble)ThingMaker.MakeThing(ThingDefOf.Mote_ThoughtBad);
            moteBubble.SetupMoteBubble(ContentFinder<Texture2D>.Get("Things/Mote/IncapIcon"), null);
            moteBubble.Attach(colonist);
            GenSpawn.Spawn(moteBubble, colonist.Position, colonist.Map);
        }

        colonist.health.AddHediff(Util_AlertSpeaker.HediffAdrenalineMediumDef);
    }

    public static void RemoveHediffAdrenalineSmall(Pawn colonist)
    {
        var firstHediffOfDef =
            colonist.health.hediffSet.GetFirstHediffOfDef(Util_AlertSpeaker.HediffAdrenalineSmallDef);
        if (firstHediffOfDef != null)
        {
            colonist.health.RemoveHediff(firstHediffOfDef);
        }
    }

    public void RemoveAnyAdrenalineHediffFromAllColonists()
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

    public void PerformSoundTreatment()
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
                             (alarmSoundPeriodInTicks * (int)Find.TickManager.CurTimeSpeed);
        if (currentDangerRate == StoryDanger.Low)
        {
            PlayOneLowDangerAlarmSound();
        }
        else
        {
            PlayOneHighDangerAlarmSound();
        }
    }

    public void PlayOneLowDangerAlarmSound()
    {
        if (soundIsEnabled)
        {
            lowDangerAlarmSound.PlayOneShotOnCamera();
        }
    }

    public void PlayOneHighDangerAlarmSound()
    {
        if (soundIsEnabled)
        {
            highDangerAlarmSound.PlayOneShotOnCamera();
        }
    }

    public void UpdateDrawingParameters()
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
                if (redAlertLightAngle < 90f)
                {
                    redAlertLightIntensity = redAlertLightAngle / 90f;
                }
                else
                {
                    if (redAlertLightAngle < 180f)
                    {
                        redAlertLightIntensity = 1f - ((redAlertLightAngle - 90f) / 90f);
                    }
                    else
                    {
                        redAlertLightIntensity = 0f;
                    }
                }

                redAlertLightMatrix.SetTRS(
                    Position.ToVector3ShiftedWithAltitude(AltitudeLayer.Silhouettes) +
                    new Vector3(0f, 0f, -0.25f).RotatedBy(Rotation.AsAngle) + Altitudes.AltIncVect,
                    (Rotation.AsAngle + redAlertLightAngle).ToQuat(), redAlertLightScale);
                break;
            }
        }
    }

    public void UpdateGlowerParameters()
    {
        if (glowerComp.Props.glowRadius == glowRadius && glowerComp.Props.glowColor == glowColor)
        {
            return;
        }

        glowerComp.Props.glowRadius = glowRadius;
        glowerComp.Props.glowColor = glowColor;
        Map.mapDrawer.MapMeshDirty(Position, 1);
        Map.glowGrid.DirtyCache(Position);
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
        var num = 700000100;
        var command_Action = new Command_Action();
        if (soundIsEnabled)
        {
            command_Action.icon = ContentFinder<Texture2D>.Get("Ui/Commands/CommandButton_SirenSoundEnabled");
            command_Action.defaultLabel = "MCAS.DisableSound".Translate();
            command_Action.defaultDesc = "MCAS.DisableSoundTT".Translate();
        }
        else
        {
            command_Action.icon = ContentFinder<Texture2D>.Get("Ui/Commands/CommandButton_SirenSoundDisabled");
            command_Action.defaultLabel = "MCAS.EnableSound".Translate();
            command_Action.defaultDesc = "CAS.EnableSoundTT".Translate();
        }

        command_Action.activateSound = SoundDef.Named("Click");
        command_Action.action = PerformSirenSoundAction;
        command_Action.groupKey = num + 1;
        list.Add(command_Action);
        var gizmos = base.GetGizmos();
        var result = gizmos != null ? list.AsEnumerable().Concat(gizmos) : list.AsEnumerable();

        return result;
    }

    public void PerformSirenSoundAction()
    {
        soundIsEnabled = !soundIsEnabled;
    }
}