Shader "NostalgiaBomb/Greybox"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _VertexTint ("Use vertex colors", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            float _VertexTint;
            struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; fixed4 color:COLOR; };
            struct v2f { float4 position:SV_POSITION; fixed4 color:COLOR; };
            v2f vert(appdata v)
            {
                v2f o;
                o.position=UnityObjectToClipPos(v.vertex);
                float lighting=.65+.35*saturate(dot(UnityObjectToWorldNormal(v.normal),normalize(float3(.4,1,-.3))));
                o.color=_Color*lerp(fixed4(lighting,lighting,lighting,1),v.color,saturate(_VertexTint));
                return o;
            }
            fixed4 frag(v2f i):SV_Target { return i.color; }
            ENDCG
        }
    }
    Fallback "Unlit/Color"
}
