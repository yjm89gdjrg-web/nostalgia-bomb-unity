using System;
using System.Collections.Generic;

namespace NostalgiaBomb
{
    public enum Team { Attackers, Defenders }
    public enum RoundPhase { Warmup, Live, Planted, Finished }
    public enum WinReason { None, Elimination, TimeExpired, Detonated, Defused }

    // Pure rules: Unity supplies validated proximity/site/line-of-sight candidates.
    public sealed class RoundRules
    {
        public const float WarmupSeconds = 3, RoundSeconds = 95, FuseSeconds = 35;
        public const float PlantSeconds = 3, DefuseSeconds = 5;
        readonly HashSet<int> attackers = new HashSet<int>();
        readonly HashSet<int> defenders = new HashSet<int>();
        int interactionActor = -1;
        public RoundPhase Phase { get; private set; }
        public float Remaining { get; private set; }
        public float InteractionProgress { get; private set; }
        public int Carrier { get; private set; } = -1;
        public bool BombDropped { get; private set; }
        public Team Winner { get; private set; }
        public WinReason Reason { get; private set; }
        public int AttackScore { get; private set; }
        public int DefenseScore { get; private set; }
        public int RoundNumber { get; private set; }
        public int AliveAttackers => attackers.Count;
        public int AliveDefenders => defenders.Count;
        public int LastPlanter { get; private set; } = -1;
        public bool IsAlive(int id) => attackers.Contains(id) || defenders.Contains(id);

        public void StartRound(IEnumerable<int> attackIds, IEnumerable<int> defenseIds, int carrier)
        {
            attackers.Clear(); defenders.Clear();
            foreach (int id in attackIds) attackers.Add(id);
            foreach (int id in defenseIds) defenders.Add(id);
            if (attackers.Count == 0 || defenders.Count == 0 || !attackers.Contains(carrier))
                throw new ArgumentException("Round needs two teams and a living attacking carrier.");
            foreach (int id in attackers)
                if (defenders.Contains(id)) throw new ArgumentException("Team IDs must be unique.");
            RoundNumber++; Carrier = carrier; BombDropped = false; LastPlanter = -1;
            Phase = RoundPhase.Warmup; Remaining = WarmupSeconds; Reason = WinReason.None;
            ResetInteraction();
        }
        public bool Pickup(int id)
        {
            if (Phase != RoundPhase.Live || !BombDropped || !attackers.Contains(id)) return false;
            Carrier = id; BombDropped = false; return true;
        }
        public void Kill(int id)
        {
            if (Phase != RoundPhase.Live && Phase != RoundPhase.Planted) return;
            bool attacker = attackers.Remove(id), defender = defenders.Remove(id);
            if (!attacker && !defender) return;
            if (id == interactionActor) ResetInteraction();
            if (id == Carrier) { Carrier = -1; BombDropped = Phase == RoundPhase.Live; }
            // A planted bomb remains a threat even with no attackers alive.
            if (defenders.Count == 0) Finish(Team.Attackers, WinReason.Elimination);
            else if (attackers.Count == 0 && Phase == RoundPhase.Live) Finish(Team.Defenders, WinReason.Elimination);
        }
        public void Tick(float dt, int planter = -1, int defuser = -1)
        {
            if (dt < 0 || float.IsNaN(dt) || float.IsInfinity(dt)) throw new ArgumentOutOfRangeException(nameof(dt));
            if (Phase == RoundPhase.Finished) return;
            if (Phase == RoundPhase.Warmup)
            {
                Remaining -= dt;
                if (Remaining <= 0) { dt = -Remaining; Phase = RoundPhase.Live; Remaining = RoundSeconds; }
                else return;
            }
            bool plant = Phase == RoundPhase.Live;
            int candidate = plant ? planter : defuser;
            bool valid = plant ? candidate >= 0 && candidate == Carrier && attackers.Contains(candidate)
                               : candidate >= 0 && defenders.Contains(candidate);
            if (!valid) ResetInteraction();
            else if (interactionActor != candidate) { ResetInteraction(); interactionActor = candidate; }
            float required = plant ? PlantSeconds : DefuseSeconds;
            float untilAction = valid ? required - InteractionProgress : float.PositiveInfinity;
            // Deadline wins ties. Large time steps never allow a late plant/defuse.
            if (Remaining <= dt && Remaining <= untilAction)
            {
                Remaining = 0;
                Finish(plant ? Team.Defenders : Team.Attackers, plant ? WinReason.TimeExpired : WinReason.Detonated);
                return;
            }
            if (valid && untilAction <= dt)
            {
                Remaining -= untilAction;
                if (plant)
                {
                    LastPlanter = candidate; Carrier = -1; BombDropped = false;
                    Phase = RoundPhase.Planted; Remaining = FuseSeconds; ResetInteraction();
                    Tick(dt - untilAction);
                }
                else Finish(Team.Defenders, WinReason.Defused);
                return;
            }
            Remaining -= dt;
            if (valid) InteractionProgress += dt;
        }
        void ResetInteraction() { interactionActor = -1; InteractionProgress = 0; }
        void Finish(Team team, WinReason reason)
        {
            if (Phase == RoundPhase.Finished) return;
            Winner = team; Reason = reason; Phase = RoundPhase.Finished; ResetInteraction();
            if (team == Team.Attackers) AttackScore++; else DefenseScore++;
        }
    }
}
