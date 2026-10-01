using UnityEngine;

namespace NostalgiaBomb
{
    // Legacy input polling supports multitouch without an EventSystem or external input package.
    public sealed class TouchControls : MonoBehaviour
    {
        public Vector2 Move { get; private set; }
        public Vector2 Look { get; private set; }
        public bool Fire { get; private set; }
        public bool Interact { get; private set; }
        public bool ReloadPressed { get; private set; }
        public bool SwapPressed { get; private set; }
        public MatchGame Game;
        int moveFinger=-1,lookFinger=-1;
        Vector2 moveOrigin,moveCurrent,mousePrevious;
        Rect safe,fire,reload,swap,interact,joystick;
        float scale;
        GUIStyle label,button;
        public void Poll(float dt)
        {
            Layout(); Move=Vector2.zero; Look=Vector2.zero;
            Fire=Interact=ReloadPressed=SwapPressed=false;
            if(Application.isMobilePlatform || Input.touchCount>0)
            {
                for(int i=0;i<Input.touchCount;i++)
                {
                    Touch t=Input.GetTouch(i); Vector2 p=new Vector2(t.position.x,Screen.height-t.position.y);
                    bool began=t.phase==TouchPhase.Began, ended=t.phase==TouchPhase.Ended || t.phase==TouchPhase.Canceled;
                    if(ended)
                    {
                        if(t.fingerId==moveFinger) moveFinger=-1;
                        if(t.fingerId==lookFinger) lookFinger=-1;
                        continue;
                    }
                    if(began)
                    {
                        if(reload.Contains(p)) ReloadPressed=true;
                        else if(swap.Contains(p)) SwapPressed=true;
                        else if(!OnButton(p) && joystick.Contains(p) && moveFinger<0) { moveFinger=t.fingerId; moveOrigin=p; }
                        else if(!OnButton(p) && p.x>safe.center.x && safe.Contains(p) && lookFinger<0) lookFinger=t.fingerId;
                    }
                    if(fire.Contains(p)) Fire=true;
                    if(interact.Contains(p)) Interact=true;
                    if(t.fingerId==moveFinger) { moveCurrent=p; Vector2 delta=p-moveOrigin; Move=Vector2.ClampMagnitude(new Vector2(delta.x,-delta.y)/(65*scale),1); }
                    if(t.fingerId==lookFinger) Look+=t.deltaPosition*.12f/scale;
                }
                return;
            }
            Move=new Vector2((Input.GetKey(KeyCode.D)?1:0)-(Input.GetKey(KeyCode.A)?1:0),
                             (Input.GetKey(KeyCode.W)?1:0)-(Input.GetKey(KeyCode.S)?1:0));
            Move=Vector2.ClampMagnitude(Move,1);
            Vector2 mouse=new Vector2(Input.mousePosition.x,Screen.height-Input.mousePosition.y);
            if(Input.GetMouseButton(1) && !Input.GetMouseButtonDown(1))
            { Vector2 delta=mouse-mousePrevious; Look=new Vector2(delta.x,-delta.y)*.15f; }
            mousePrevious=mouse;
            Look+=new Vector2((Input.GetKey(KeyCode.RightArrow)?1:0)-(Input.GetKey(KeyCode.LeftArrow)?1:0),
                             (Input.GetKey(KeyCode.UpArrow)?1:0)-(Input.GetKey(KeyCode.DownArrow)?1:0))*90*dt;
            Fire=Input.GetMouseButton(0) && (!OnButton(mouse) || fire.Contains(mouse));
            Interact=Input.GetKey(KeyCode.E) || Input.GetMouseButton(0) && interact.Contains(mouse);
            ReloadPressed=Input.GetKeyDown(KeyCode.R) || Input.GetMouseButtonDown(0) && reload.Contains(mouse);
            SwapPressed=Input.GetKeyDown(KeyCode.Q) || Input.GetMouseButtonDown(0) && swap.Contains(mouse);
        }
        bool OnButton(Vector2 p) => fire.Contains(p) || reload.Contains(p) || swap.Contains(p) || interact.Contains(p);
        void Layout()
        {
            var area=Screen.safeArea; safe=new Rect(area.x,Screen.height-area.yMax,area.width,area.height);
            scale=Mathf.Max(.5f,Mathf.Min(safe.height/720f,safe.width/1100f));
            float s=scale;
            fire=new Rect(safe.xMax-155*s,safe.yMax-210*s,125*s,85*s);
            interact=new Rect(safe.xMax-305*s,safe.yMax-110*s,135*s,75*s);
            reload=new Rect(safe.xMax-155*s,safe.yMax-110*s,125*s,75*s);
            swap=new Rect(safe.xMax-305*s,safe.yMax-210*s,135*s,75*s);
            joystick=new Rect(safe.x+20*s,safe.y+safe.height*.45f,safe.width*.36f,safe.height*.55f);
        }
        void OnGUI()
        {
            if(Game==null || Game.Rules==null) return;
            Layout();
            label=new GUIStyle(GUI.skin.label) { fontSize=Mathf.RoundToInt(21*scale), wordWrap=true };
            button=new GUIStyle(GUI.skin.box) { fontSize=Mathf.RoundToInt(22*scale), alignment=TextAnchor.MiddleCenter };
            var r=Game.Rules; var p=Game.Player;
            GUI.Label(new Rect(safe.x+20*scale,safe.y+8*scale,safe.width-40*scale,40*scale),
                $"ROUND {r.RoundNumber}   BLUE {r.AttackScore} : {r.DefenseScore} RED    {r.Phase} {Mathf.CeilToInt(r.Remaining)}s    ALIVE {r.AliveAttackers}:{r.AliveDefenders}",label);
            string status=p.Alive?$"HP {p.Health}   {(p.Rifle?"RIFLE":"PISTOL")}  {p.Ammo}/{p.Reserve} {(p.Reloading?"RELOADING":"")}":"YOU ARE DOWN - bots continue; next round respawns you";
            GUI.Label(new Rect(safe.x+20*scale,safe.y+50*scale,safe.width-40*scale,45*scale),status,label);
            GUI.Label(new Rect(safe.x+20*scale,safe.y+95*scale,safe.width-40*scale,70*scale),Game.Hint,label);
            if(r.InteractionProgress>0) GUI.Label(new Rect(safe.center.x-130*scale,safe.center.y+50*scale,300*scale,50*scale),$"{(r.Phase==RoundPhase.Planted?"DEFUSING":"PLANTING")} {r.InteractionProgress:0.0}s",label);
            GUI.Box(fire,"FIRE",button); GUI.Box(reload,"RELOAD",button); GUI.Box(swap,"SWAP",button); GUI.Box(interact,"HOLD USE",button);
            Vector2 center=moveFinger>=0?moveOrigin:new Vector2(safe.x+120*scale,safe.yMax-130*scale);
            GUI.Box(new Rect(center.x-65*scale,center.y-65*scale,130*scale,130*scale),"MOVE",button);
            if(moveFinger>=0)
            {
                var knob=center+Vector2.ClampMagnitude(moveCurrent-center,65*scale);
                GUI.Box(new Rect(knob.x-18*scale,knob.y-18*scale,36*scale,36*scale),"");
            }
            GUI.Label(new Rect(safe.center.x-10*scale,safe.center.y-20*scale,40*scale,40*scale),"+",label);
            if(!Application.isMobilePlatform) GUI.Label(new Rect(safe.x+20*scale,safe.yMax-35*scale,600*scale,35*scale),"WASD | RMB drag / arrows look | LMB fire | R reload | Q swap | hold E use",new GUIStyle(label){fontSize=Mathf.RoundToInt(15*scale)});
        }
        void OnApplicationFocus(bool focus) { moveFinger=lookFinger=-1; }
        void OnApplicationPause(bool pause) { moveFinger=lookFinger=-1; }
    }
}
