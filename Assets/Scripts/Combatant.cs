using System.Collections.Generic;
using UnityEngine;

namespace NostalgiaBomb
{
    public sealed class Combatant : MonoBehaviour
    {
        public int Id { get; private set; }
        public Team Team { get; private set; }
        public bool IsPlayer { get; private set; }
        public bool Alive => Health > 0;
        public int Health { get; private set; }
        public bool WantsInteract { get; private set; }
        public bool Rifle { get; private set; } = true;
        public int Ammo => ammo[Rifle?0:1];
        public int Reserve => reserve[Rifle?0:1];
        public bool Reloading => reloadTime > 0;
        public float Pitch { get; private set; }
        public Vector3 Eye => transform.position + Vector3.up*1.55f;
        readonly int[] ammo={30,12}, reserve={120,48};
        CharacterController controller;
        MatchGame game;
        Renderer body;
        Camera view;
        float cooldown,reloadTime,decisionTime,pathTime,stuckTime;
        Vector3 destination,lastPosition;
        List<Vector3> path = new List<Vector3>();
        int pathIndex;
        Combatant target;
        public void Initialize(MatchGame owner,int id,Team team,bool player)
        {
            game=owner; Id=id; Team=team; IsPlayer=player; Health=100;
            gameObject.layer=Arena.ActorLayer;
            controller=gameObject.AddComponent<CharacterController>(); controller.height=1.8f; controller.radius=.35f;
            controller.center=Vector3.up*.9f; controller.stepOffset=.25f;
            var model=GameObject.CreatePrimitive(PrimitiveType.Capsule); model.name="Team silhouette";
            model.transform.SetParent(transform,false); model.transform.localPosition=Vector3.up*.9f;
            model.transform.localScale=new Vector3(.7f,.9f,.7f);
            model.GetComponent<Collider>().enabled=false; Destroy(model.GetComponent<Collider>());
            body=model.GetComponent<Renderer>(); var mat=new Material(Shader.Find("NostalgiaBomb/Greybox"));
            mat.color=team==Team.Attackers?new Color(.2f,.65f,.95f):new Color(.95f,.3f,.25f); body.sharedMaterial=mat;
            if(player)
            {
                body.enabled=false;
                var cameraObject=new GameObject("Player camera"); cameraObject.transform.SetParent(transform,false);
                cameraObject.transform.localPosition=Vector3.up*1.55f; view=cameraObject.AddComponent<Camera>();
                view.fieldOfView=72; view.nearClipPlane=.08f; view.farClipPlane=120; view.tag="MainCamera";
                view.clearFlags=CameraClearFlags.SolidColor; view.backgroundColor=new Color(.45f,.57f,.67f);
                cameraObject.AddComponent<AudioListener>();
                var gun=GameObject.CreatePrimitive(PrimitiveType.Cube); gun.name="Original rifle proxy";
                gun.transform.SetParent(cameraObject.transform,false); gun.transform.localPosition=new Vector3(.25f,-.23f,.55f);
                gun.transform.localScale=new Vector3(.1f,.12f,.55f); gun.GetComponent<Renderer>().sharedMaterial=mat; gun.GetComponent<Collider>().enabled=false;
                Destroy(gun.GetComponent<Collider>());
            }
            destination=transform.position; lastPosition=transform.position;
        }
        public void Simulate(float dt)
        {
            WantsInteract=false;
            if(!Alive) return;
            cooldown=Mathf.Max(0,cooldown-dt);
            if(reloadTime>0)
            {
                reloadTime-=dt;
                if(reloadTime<=0)
                {
                    int slot=Rifle?0:1, amount=Mathf.Min((Rifle?30:12)-ammo[slot],reserve[slot]);
                    ammo[slot]+=amount; reserve[slot]-=amount;
                }
            }
            if(game.Rules.Phase!=RoundPhase.Live && game.Rules.Phase!=RoundPhase.Planted) return;
            if(IsPlayer) PlayerStep(dt); else BotStep(dt);
        }
        void PlayerStep(float dt)
        {
            var input=game.Controls;
            transform.Rotate(0,input.Look.x,0);
            Pitch=Mathf.Clamp(Pitch-input.Look.y,-75,75); view.transform.localRotation=Quaternion.Euler(Pitch,0,0);
            Vector3 movement=transform.right*input.Move.x+transform.forward*input.Move.y;
            controller.Move((movement*4.6f+Vector3.down*2)*dt);
            WantsInteract=input.Interact && movement.sqrMagnitude<.04f;
            if(input.SwapPressed) Swap();
            if(input.ReloadPressed) Reload();
            if(input.Fire && !WantsInteract) Shoot(view.transform.forward,false);
        }
        void BotStep(float dt)
        {
            decisionTime-=dt; pathTime-=dt;
            if(decisionTime<=0)
            {
                decisionTime=.22f+Id*.013f;
                target=null; float best=32;
                foreach(var other in game.Actors)
                {
                    if(!other.Alive || other.Team==Team) continue;
                    float distance=Vector3.Distance(Eye,other.Eye);
                    if(distance<best && game.Visible(this,other)) { best=distance; target=other; }
                }
                destination=game.BotDestination(this);
            }
            if(Ammo==0 && Reserve==0 && Rifle) Swap();
            if(Ammo==0) Reload();
            // Interaction is exclusive: designated defuser commits; teammates cover.
            bool objective=game.CanInteract(this) && (Team==Team.Attackers || game.DesignatedDefuser()==this);
            WantsInteract=objective;
            if(objective) return;
            if(target!=null && target.Alive && game.Visible(this,target))
            {
                Vector3 look=target.Eye-Eye; look.y=0;
                if(look.sqrMagnitude>.01f) transform.rotation=Quaternion.RotateTowards(transform.rotation,Quaternion.LookRotation(look),240*dt);
                if(Vector3.Angle(transform.forward,look)<12) Shoot((target.Eye-Eye).normalized,true);
                // Keep pushing the objective under fire, except defending a planted bomb.
                if(Team==Team.Attackers && game.Rules.Phase==RoundPhase.Planted) return;
            }
            if(Arena.FlatDistance(transform.position,destination)<.65f) return;
            if(pathTime<=0 || stuckTime>.8f)
            {
                path=game.Map.Path(transform.position,destination); pathIndex=0; pathTime=.8f; stuckTime=0;
            }
            while(pathIndex<path.Count && Arena.FlatDistance(transform.position,path[pathIndex])<.45f) pathIndex++;
            if(pathIndex>=path.Count) return; // Never fall back to direct movement through a wall.
            Vector3 direction=path[pathIndex]-transform.position; direction.y=0; direction.Normalize();
            if(target==null) transform.rotation=Quaternion.RotateTowards(transform.rotation,Quaternion.LookRotation(direction),210*dt);
            // Small separation avoids six controllers queuing on exactly the same line.
            Vector3 separation=Vector3.zero;
            foreach(var other in game.Actors)
            {
                if(other==this || !other.Alive) continue;
                Vector3 away=transform.position-other.transform.position; away.y=0;
                if(away.sqrMagnitude<1.3f && away.sqrMagnitude>.001f) separation+=away.normalized*.8f;
            }
            Vector3 step=(direction+separation).normalized;
            if(!game.Map.ClearWalk(transform.position,transform.position+step*.65f)) step=direction;
            controller.Move((step*3.3f+Vector3.down*2)*dt);
            if((transform.position-lastPosition).sqrMagnitude<.0001f) stuckTime+=dt; else stuckTime=0;
            lastPosition=transform.position;
        }
        public void Swap() { if(Reloading) return; Rifle=!Rifle; cooldown=.25f; }
        public void Reload()
        {
            if(Reloading || Reserve==0 || Ammo==(Rifle?30:12)) return;
            reloadTime=Rifle?1.8f:1.3f;
        }
        void Shoot(Vector3 direction,bool bot)
        {
            if(cooldown>0 || Reloading || Ammo<=0) return;
            ammo[Rifle?0:1]--; cooldown=Rifle?.115f:.3f;
            if(bot) direction=(direction+Random.insideUnitSphere*.045f).normalized;
            Vector3 end=Eye+direction*60;
            if(Physics.Raycast(Eye,direction,out RaycastHit hit,60,Arena.ShotMask,QueryTriggerInteraction.Ignore))
            {
                end=hit.point;
                var victim=hit.collider.GetComponent<Combatant>();
                if(victim!=null && victim.Team!=Team) victim.Damage(Rifle?27:34);
            }
            game.Tracer(Eye,end,Team);
        }
        void OnDestroy()
        {
            if(body!=null && body.sharedMaterial!=null) Destroy(body.sharedMaterial);
        }
        public void Damage(int amount)
        {
            if(!Alive) return;
            Health=Mathf.Max(0,Health-amount);
            if(Health>0) return;
            controller.enabled=false; body.enabled=!IsPlayer;
            if(!IsPlayer) transform.localScale=new Vector3(1,.15f,1);
            game.ActorDied(this);
        }
    }
}
