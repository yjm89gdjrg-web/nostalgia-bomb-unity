using NUnit.Framework;

namespace NostalgiaBomb.Tests
{
    public sealed class RoundRulesTests
    {
        static RoundRules Live()
        {
            var r=new RoundRules(); r.StartRound(new[]{0,1,2},new[]{3,4,5},0); r.Tick(3); return r;
        }
        static RoundRules Planted()
        {
            var r=Live(); r.Tick(3,0); Assert.That(r.Phase,Is.EqualTo(RoundPhase.Planted)); return r;
        }
        [Test] public void WarmupStartsLiveWithFullClock()
        {
            var r=Live(); Assert.That(r.Remaining,Is.EqualTo(95)); Assert.That(r.AliveAttackers,Is.EqualTo(3));
        }
        [Test] public void CarrierDeathDropsAndLivingTeammateRecovers()
        {
            var r=Live(); r.Kill(0); Assert.That(r.BombDropped,Is.True); Assert.That(r.Carrier,Is.EqualTo(-1));
            Assert.That(r.Pickup(3),Is.False); Assert.That(r.Pickup(0),Is.False); Assert.That(r.Pickup(1),Is.True);
            Assert.That(r.Carrier,Is.EqualTo(1)); Assert.That(r.BombDropped,Is.False);
        }
        [Test] public void InterruptedPlantResetsAndRequiresContinuousHold()
        {
            var r=Live(); r.Tick(2,0); r.Tick(.1f); Assert.That(r.InteractionProgress,Is.Zero);
            r.Tick(2,0); Assert.That(r.Phase,Is.EqualTo(RoundPhase.Live)); r.Tick(1,0);
            Assert.That(r.Phase,Is.EqualTo(RoundPhase.Planted)); Assert.That(r.Carrier,Is.EqualTo(-1));
        }
        [Test] public void AttackerWipeBeforePlantWinsForDefense()
        {
            var r=Live(); r.Kill(0); r.Kill(1); r.Kill(2);
            Assert.That(r.Winner,Is.EqualTo(Team.Defenders)); Assert.That(r.Reason,Is.EqualTo(WinReason.Elimination));
        }
        [Test] public void AttackerWipeAfterPlantStillExplodes()
        {
            var r=Planted(); r.Kill(0); r.Kill(1); r.Kill(2);
            Assert.That(r.Phase,Is.EqualTo(RoundPhase.Planted)); r.Tick(35);
            Assert.That(r.Winner,Is.EqualTo(Team.Attackers)); Assert.That(r.Reason,Is.EqualTo(WinReason.Detonated));
        }
        [Test] public void DefenderWipeWinsImmediatelyEvenAfterPlant()
        {
            var r=Planted(); r.Kill(3); r.Kill(4); r.Kill(5);
            Assert.That(r.Reason,Is.EqualTo(WinReason.Elimination)); Assert.That(r.Winner,Is.EqualTo(Team.Attackers));
        }
        [Test] public void DefenderCanDefuseAfterAllAttackersDie()
        {
            var r=Planted(); r.Kill(0); r.Kill(1); r.Kill(2); r.Tick(5,-1,3);
            Assert.That(r.Reason,Is.EqualTo(WinReason.Defused)); Assert.That(r.Winner,Is.EqualTo(Team.Defenders));
        }
        [Test] public void SwitchingOrLosingDefuserResetsProgress()
        {
            var r=Planted(); r.Tick(3,-1,3); r.Tick(1,-1,4);
            Assert.That(r.InteractionProgress,Is.EqualTo(1)); r.Kill(4); Assert.That(r.InteractionProgress,Is.Zero);
            r.Tick(4,-1,3); Assert.That(r.Phase,Is.EqualTo(RoundPhase.Planted));
        }
        [Test] public void RoundTimeoutWinsBeforeLatePlant()
        {
            var r=Live(); r.Tick(93); r.Tick(5,0);
            Assert.That(r.Reason,Is.EqualTo(WinReason.TimeExpired)); Assert.That(r.Winner,Is.EqualTo(Team.Defenders));
        }
        [Test] public void FuseWinsBeforeLateDefuseAndOnTie()
        {
            var r=Planted(); r.Tick(30); r.Tick(5,-1,3);
            Assert.That(r.Reason,Is.EqualTo(WinReason.Detonated));
        }
        [Test] public void LargePlantStepConsumesLeftoverFuseTime()
        {
            var r=Live(); r.Tick(5,0); Assert.That(r.Remaining,Is.EqualTo(33));
        }
        [Test] public void FinishedRoundScoresOnlyOnceAndNextRoundResets()
        {
            var r=Live(); r.Tick(95); r.Tick(500); r.Kill(3);
            Assert.That(r.DefenseScore,Is.EqualTo(1));
            r.StartRound(new[]{0,1,2},new[]{3,4,5},0);
            Assert.That(r.Phase,Is.EqualTo(RoundPhase.Warmup)); Assert.That(r.RoundNumber,Is.EqualTo(2));
            Assert.That(r.DefenseScore,Is.EqualTo(1)); Assert.That(r.Reason,Is.EqualTo(WinReason.None));
        }
        [Test] public void InvalidTimeAndTeamsAreRejected()
        {
            var r=Live(); Assert.Throws<System.ArgumentOutOfRangeException>(()=>r.Tick(-1));
            Assert.Throws<System.ArgumentOutOfRangeException>(()=>r.Tick(float.NaN));
            Assert.Throws<System.ArgumentException>(()=>r.StartRound(new[]{0},new[]{0},0));
        }
        [Test] public void NonCarrierCannotPlantAndAttackerCannotDefuse()
        {
            var r=Live(); r.Tick(3,1); Assert.That(r.Phase,Is.EqualTo(RoundPhase.Live));
            r.Tick(3,0); r.Tick(5,-1,1); Assert.That(r.Phase,Is.EqualTo(RoundPhase.Planted));
        }
    }
}
