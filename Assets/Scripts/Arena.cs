using System.Collections.Generic;
using UnityEngine;

namespace NostalgiaBomb
{
    public sealed class Arena
    {
        public const int WorldLayer = 8, ActorLayer = 9;
        public const int WorldMask = 1 << WorldLayer;
        public const int ShotMask = WorldMask | (1 << ActorLayer);
        public readonly Vector3[] Sites = { new Vector3(-10, 0, 13), new Vector3(10, 0, 13) };
        public readonly List<Vector3> Nodes = new List<Vector3>();
        readonly List<List<int>> edges = new List<List<int>>();
        readonly Transform root;
        public Arena(Transform parent)
        {
            root = new GameObject("Original arena - Split Foundry").transform; root.SetParent(parent);
            Box("Floor", new Vector3(0, -.25f, 0), new Vector3(32, .5f, 40), new Color(.26f,.29f,.32f));
            Box("West", new Vector3(-16, 1.5f, 0), new Vector3(1,3,41), Color.gray);
            Box("East", new Vector3(16, 1.5f, 0), new Vector3(1,3,41), Color.gray);
            Box("North", new Vector3(0, 1.5f, 20), new Vector3(32,3,1), Color.gray);
            Box("South", new Vector3(0, 1.5f,-20), new Vector3(32,3,1), Color.gray);
            Box("Central workshop", new Vector3(0,1.6f,0), new Vector3(8,3.2f,12), new Color(.4f,.43f,.47f));
            Box("West dogleg", new Vector3(-9.5f,1.3f,1.5f), new Vector3(5,2.6f,5), new Color(.42f,.38f,.32f));
            Box("East dogleg", new Vector3(9.5f,1.3f,-1.5f), new Vector3(5,2.6f,5), new Color(.42f,.38f,.32f));
            Box("A cover", new Vector3(-6, .9f,13), new Vector3(2,1.8f,3), new Color(.4f,.4f,.4f));
            Box("B cover", new Vector3(6, .9f,13), new Vector3(2,1.8f,3), new Color(.4f,.4f,.4f));
            for (int i=0;i<Sites.Length;i++)
            {
                Box("Site " + (i==0?"A":"B"), Sites[i]+Vector3.up*.015f, new Vector3(5,.03f,5), i==0?new Color(.85f,.5f,.15f):new Color(.25f,.6f,.85f), false);
                var label = new GameObject("Site label"); label.transform.SetParent(root, false);
                label.transform.position = Sites[i]+Vector3.up*3;
                // World-space text is independent of screen HUD.
                var text=label.AddComponent<TextMesh>(); text.text=i==0?"A":"B"; text.characterSize=.6f; text.fontSize=64;
                text.anchor=TextAnchor.MiddleCenter; text.color=Color.white;
            }
            Physics.SyncTransforms();
            for (float z=-18;z<=18;z+=4)
                for (float x=-14;x<=14;x+=4)
                {
                    var p=new Vector3(x,0,z);
                    if (!Physics.CheckCapsule(p+Vector3.up*.7f,p+Vector3.up*1.3f,.65f,WorldMask,QueryTriggerInteraction.Ignore))
                        Nodes.Add(p);
                }
            for(int i=0;i<Nodes.Count;i++)
            {
                edges.Add(new List<int>());
                for(int j=0;j<Nodes.Count;j++)
                    if(i!=j && Vector3.Distance(Nodes[i],Nodes[j])<=5.7f && ClearWalk(Nodes[i],Nodes[j])) edges[i].Add(j);
            }
        }
        GameObject Box(string name,Vector3 position,Vector3 scale,Color color,bool solid=true)
        {
            var o=GameObject.CreatePrimitive(PrimitiveType.Cube); o.name=name; o.transform.SetParent(root);
            o.transform.position=position; o.transform.localScale=scale; o.layer=WorldLayer;
            var shader=Shader.Find("NostalgiaBomb/Greybox"); var material=new Material(shader); material.color=color;
            o.GetComponent<Renderer>().sharedMaterial=material;
            if(!solid) { o.GetComponent<Collider>().enabled=false; Object.Destroy(o.GetComponent<Collider>()); }
            return o;
        }
        public bool ClearWalk(Vector3 a,Vector3 b)
        {
            Vector3 delta=b-a; delta.y=0;
            if(delta.sqrMagnitude<.001f) return true;
            return !Physics.SphereCast(a+Vector3.up*.9f,.45f,delta.normalized,out _,delta.magnitude,WorldMask,QueryTriggerInteraction.Ignore);
        }
        int Closest(Vector3 p)
        {
            int best=-1; float distance=float.PositiveInfinity;
            for(int i=0;i<Nodes.Count;i++)
            {
                float d=(p-Nodes[i]).sqrMagnitude;
                if(d<distance && ClearWalk(p,Nodes[i])) { best=i; distance=d; }
            }
            return best;
        }
        public List<Vector3> Path(Vector3 start,Vector3 goal)
        {
            var result=new List<Vector3>();
            if(ClearWalk(start,goal)) { result.Add(goal); return result; }
            int source=Closest(start),destination=Closest(goal);
            if(source<0 || destination<0) return result;
            var distances=new float[Nodes.Count]; var previous=new int[Nodes.Count]; var visited=new bool[Nodes.Count];
            for(int i=0;i<Nodes.Count;i++) { distances[i]=float.PositiveInfinity; previous[i]=-1; }
            distances[source]=0;
            for(int count=0;count<Nodes.Count;count++)
            {
                int current=-1; float min=float.PositiveInfinity;
                for(int i=0;i<Nodes.Count;i++) if(!visited[i] && distances[i]<min) { current=i; min=distances[i]; }
                if(current<0) break;
                if(current==destination) break;
                visited[current]=true;
                foreach(int next in edges[current])
                {
                    float d=min+Vector3.Distance(Nodes[current],Nodes[next]);
                    if(d<distances[next]) { distances[next]=d; previous[next]=current; }
                }
            }
            if(float.IsPositiveInfinity(distances[destination])) return result;
            for(int node=destination;node>=0;node=previous[node]) { result.Insert(0,Nodes[node]); if(node==source) break; }
            if(ClearWalk(Nodes[destination],goal)) result.Add(goal);
            return result;
        }
        public int SiteAt(Vector3 p)
        {
            for(int i=0;i<Sites.Length;i++) if(FlatDistance(p,Sites[i])<=2.5f) return i;
            return -1;
        }
        public static float FlatDistance(Vector3 a,Vector3 b) { a.y=0; b.y=0; return Vector3.Distance(a,b); }
    }
}
