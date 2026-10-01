using System.Collections.Generic;
using UnityEngine;

namespace NostalgiaBomb
{
    public sealed class MatchGame : MonoBehaviour
    {
        public RoundRules Rules { get; private set; }
        public Arena Map { get; private set; }
        public TouchControls Controls { get; private set; }
        public Combatant Player { get; private set; }
        public readonly List<Combatant> Actors=new List<Combatant>();
        public Vector3 BombPosition { get; private set; }
        public string Hint { get; private set; }
        Transform actorRoot,bomb;
        float nextRound=-1;
        int defuserId=-1;
        Material bombMaterial,tracerMaterial;
        int attackSite;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void FallbackBootstrap()
        {
            if(FindFirstObjectByType<MatchGame>()==null) new GameObject("NostalgiaBomb Bootstrap").AddComponent<MatchGame>();
        }
        void Awake()
        {
            Application.targetFrameRate=60; Screen.sleepTimeout=SleepTimeout.NeverSleep;
            Screen.orientation=ScreenOrientation.LandscapeLeft;
            QualitySettings.vSyncCount=0;
            Rules=new RoundRules();
            Map=new Arena(transform);
            var sun=new GameObject("Sun").AddComponent<Light>(); sun.transform.SetParent(transform);
            sun.type=LightType.Directional; sun.intensity=1.2f; sun.transform.rotation=Quaternion.Euler(50,-35,0);
            RenderSettings.ambientLight=new Color(.5f,.53f,.58f);
            Controls=gameObject.AddComponent<TouchControls>(); Controls.Game=this;
            actorRoot=new GameObject("Round actors").transform; actorRoot.SetParent(transform);
            var bombObject=GameObject.CreatePrimitive(PrimitiveType.Cube); bomb=bombObject.transform;
            bomb.name="Original bomb proxy"; bomb.SetParent(transform); bomb.localScale=new Vector3(.45f,.25f,.35f);
            bombObject.GetComponent<Collider>().enabled=false; Destroy(bombObject.GetComponent<Collider>());
            bombMaterial=new Material(Shader.Find("NostalgiaBomb/Greybox")); bombObject.GetComponent<Renderer>().sharedMaterial=bombMaterial;
            tracerMaterial=new Material(Shader.Find("NostalgiaBomb/Greybox")); tracerMaterial.SetFloat("_VertexTint",1);
            StartRound();
        }
        void StartRound()
        {
            foreach(var a in Actors) { a.gameObject.SetActive(false); Destroy(a.gameObject); }
            Actors.Clear(); defuserId=-1; attackSite=(Rules.RoundNumber%2);
            for(int i=0;i<6;i++)
            {
                bool attacker=i<3;
                Vector3 spawn=new Vector3((i%3-1)*3,0,attacker?-16:17);
                var actorObject=new GameObject(i==0?"Player":"Bot "+i); actorObject.transform.SetParent(actorRoot);
                actorObject.transform.position=spawn; actorObject.transform.rotation=Quaternion.Euler(0,attacker?0:180,0);
                var actor=actorObject.AddComponent<Combatant>(); actor.Initialize(this,i,attacker?Team.Attackers:Team.Defenders,i==0);
                Actors.Add(actor); if(i==0) Player=actor;
            }
            Rules.StartRound(new[]{0,1,2},new[]{3,4,5},0);
            BombPosition=Player.transform.position; nextRound=-1; Physics.SyncTransforms();
        }
        void Update()
        {
            // Pausing in background does not consume the whole fuse on resume.
            float dt=Mathf.Min(Time.deltaTime,.1f);
            Controls.Poll(dt);
            if(Rules.Phase==RoundPhase.Finished)
            {
                Hint=$"{Rules.Winner} win: {Rules.Reason}. New round in {Mathf.CeilToInt(Mathf.Max(0,nextRound))}s";
                if(nextRound<0) nextRound=5;
                nextRound-=dt;
                if(nextRound<=0) StartRound();
                return;
            }
            foreach(var actor in Actors) actor.Simulate(dt);
            Physics.SyncTransforms();
            if(Rules.BombDropped)
                foreach(var actor in Actors)
                    if(actor.Alive && actor.Team==Team.Attackers && Arena.FlatDistance(actor.transform.position,BombPosition)<1.2f && Rules.Pickup(actor.Id)) break;
            if(Rules.Carrier>=0) BombPosition=Actors[Rules.Carrier].transform.position;
            int planter=-1,defuser=-1;
            foreach(var actor in Actors)
            {
                if(!actor.Alive || !actor.WantsInteract || !CanInteract(actor)) continue;
                if(actor.Team==Team.Attackers) planter=actor.Id;
                else if(defuser<0) defuser=actor.Id;
            }
            RoundPhase before=Rules.Phase;
            Rules.Tick(dt,planter,defuser);
            if(before==RoundPhase.Live && (Rules.Phase==RoundPhase.Planted || Rules.LastPlanter>=0))
                BombPosition=Actors[Rules.LastPlanter].transform.position;
            bomb.gameObject.SetActive(Rules.BombDropped || Rules.Phase==RoundPhase.Planted || Rules.Reason==WinReason.Defused);
            bomb.position=BombPosition+Vector3.up*.16f;
            bombMaterial.color=Rules.Phase==RoundPhase.Planted?Color.Lerp(Color.red,Color.yellow,Mathf.PingPong(Time.time*3,1)):Color.yellow;
            if(Rules.Phase==RoundPhase.Warmup) Hint="BLUE attacks. You carry the bomb. Reach A / B and HOLD USE while standing still.";
            else if(Rules.Phase==RoundPhase.Planted) Hint=$"Bomb planted: cover it! Defenders can defuse. Bomb at {(Map.SiteAt(BombPosition)==0?"A":"B")}.";
            else if(Rules.BombDropped) Hint="Bomb dropped! Blue recovers automatically by walking over it.";
            else Hint=Rules.Carrier==0?"Carry bomb to orange A or blue B; hold USE for 3s. Do not move.":"Friendly bot has the bomb. Support the push to A / B.";
        }
        public bool CanInteract(Combatant actor)
        {
            if(!actor.Alive) return false;
            if(Rules.Phase==RoundPhase.Live) return actor.Team==Team.Attackers && Rules.Carrier==actor.Id && Map.SiteAt(actor.transform.position)>=0;
            if(Rules.Phase==RoundPhase.Planted)
            {
                Vector3 delta=BombPosition+Vector3.up*.2f-actor.Eye;
                return actor.Team==Team.Defenders && Arena.FlatDistance(actor.transform.position,BombPosition)<1.8f &&
                    !Physics.Raycast(actor.Eye,delta.normalized,delta.magnitude,Arena.WorldMask,QueryTriggerInteraction.Ignore);
            }
            return false;
        }
        public bool Visible(Combatant observer,Combatant target)
        {
            Vector3 delta=target.Eye-observer.Eye;
            if(!Physics.Raycast(observer.Eye,delta.normalized,out var hit,delta.magnitude+.15f,Arena.ShotMask,QueryTriggerInteraction.Ignore)) return false;
            return hit.collider.GetComponent<Combatant>()==target;
        }
        public Combatant DesignatedDefuser()
        {
            if(defuserId>=0 && Actors[defuserId].Alive) return Actors[defuserId];
            Combatant nearest=null; float best=float.PositiveInfinity;
            foreach(var a in Actors)
            {
                if(!a.Alive || a.Team!=Team.Defenders) continue;
                var route=Map.Path(a.transform.position,BombPosition);
                if(route.Count==0) continue;
                float length=0; Vector3 previous=a.transform.position;
                foreach(var node in route) { length+=Vector3.Distance(previous,node); previous=node; }
                if(length<best) { best=length; nearest=a; }
            }
            defuserId=nearest==null?-1:nearest.Id; return nearest;
        }
        public Vector3 BotDestination(Combatant actor)
        {
            if(Rules.Phase==RoundPhase.Planted)
            {
                if(actor.Team==Team.Defenders && DesignatedDefuser()==actor) return BombPosition;
                // Cover from spaced, walkable offsets. Other defenders also move to the planted site.
                Vector3 cover=BombPosition+new Vector3(actor.Id%2==0?-2.8f:2.8f,0,-3.5f);
                return Map.ClearWalk(BombPosition,cover)?cover:BombPosition+Vector3.back*2;
            }
            if(actor.Team==Team.Attackers)
            {
                if(Rules.BombDropped) return BombPosition;
                if(Rules.Carrier==actor.Id) return Map.Sites[attackSite];
                var carrier=Actors[Rules.Carrier];
                // Escorts follow carrier until near objective, including the player choosing B.
                return carrier.transform.position+new Vector3(actor.Id==1?-1.3f:1.3f,0,1.8f);
            }
            Vector3 site=Map.Sites[actor.Id==4?1:0];
            // Roaming defender intercepts carrier through the graph, not through walls.
            if(actor.Id==5 && Rules.Carrier>=0) return Actors[Rules.Carrier].transform.position;
            return site+Vector3.back*3;
        }
        public void ActorDied(Combatant actor)
        {
            if(Rules.Carrier==actor.Id) BombPosition=actor.transform.position;
            Rules.Kill(actor.Id);
        }
        public void Tracer(Vector3 start,Vector3 end,Team team)
        {
            var o=new GameObject("Shot trace"); o.transform.SetParent(transform);
            var line=o.AddComponent<LineRenderer>(); line.sharedMaterial=tracerMaterial;
            line.positionCount=2; line.SetPosition(0,start); line.SetPosition(1,end);
            line.startWidth=.025f; line.endWidth=.008f;
            line.startColor=line.endColor=team==Team.Attackers?Color.cyan:Color.yellow;
            Destroy(o,.055f);
        }
        void OnDestroy()
        {
            if(bombMaterial!=null) Destroy(bombMaterial);
            if(tracerMaterial!=null) Destroy(tracerMaterial);
        }
    }
}
