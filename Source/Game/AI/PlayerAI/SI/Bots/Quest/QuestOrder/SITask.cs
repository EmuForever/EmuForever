using Framework.Constants;
using Game.DataStorage;
using Game.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Game.AI
{
    public enum SITaskKind { PickUp, TurnIn, Objective, Train, Vendor, Grind }

    // One unit of work chosen by the planner (HB QuestOrder node, generated from the database instead of a profile).
    public sealed class SITask
    {
        public SITaskKind Kind;
        public Quest Quest;
        public SIObjectivePlan Objective;
        public int[] Creatures = Array.Empty<int>();
        public int[] Objects = Array.Empty<int>();
        public List<Vector3> Hotspots = new();
        public int Hotspot = -1;
        public float Score;
        public string Label;
        public long StartedAt, LastProgressAt;
        public long? ApproachStartedAt;
        public int ProgressMark = -1;
        public float ClosestApproach = float.MaxValue;
        public readonly HashSet<int> Visited = new();
        public SISpawn? Destination;
        public bool InteractionCompleted;
        public SIVendorPurpose VendorPurpose;
        public ObjectGuid Target;
        internal bool PatrolChecked;
        internal SIQuestGiverPatrol PatrolSearch;
        public long HotspotWaitUntil;
        public readonly SIInteractionExecution Interaction = new();
        public bool Started;
        // Reached the objective's work area; the bot finishes it before other quest work.
        public bool Engaged;
        long? _suspendedAt;
        public void Suspend(long now) { _suspendedAt ??= now; Interaction.Pause(now); }
        public void Resume(long now)
        {
            if (_suspendedAt is not long paused) return;
            long elapsed = Math.Max(0, now - paused);
            StartedAt += elapsed; LastProgressAt += elapsed;
            if (ApproachStartedAt != null) ApproachStartedAt += elapsed;
            if (HotspotWaitUntil != 0) HotspotWaitUntil += elapsed;
            Interaction.Resume(now); _suspendedAt = null;
        }

        public string Key => $"{Kind}:{Quest?.Id ?? 0}:{Objective?.Objective.Id ?? 0}:" +
            (Kind == SITaskKind.Vendor ? $"{VendorPurpose}:" : "") +
            (Destination is SISpawn spawn ? $"{spawn.Entry}:{spawn.SpawnId}" : "area");
    }

}
