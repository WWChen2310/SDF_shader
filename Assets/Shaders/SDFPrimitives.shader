Shader "Custom/SDFPrimitives"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _MaxSteps ("Max Steps", Range(1, 128)) = 64
        _MaxDistance ("Max Distance", Range(0.1, 100)) = 20
        _SurfaceDistance ("Surface Distance", Range(0.001, 0.1)) = 0.01
        _PrimitiveType ("Primitive Type", Range(0, 4)) = 0
        _Size ("Size", Range(0.1, 5)) = 1
        _Smoothness ("Smoothness", Range(0, 1)) = 0.1
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float3 ro : TEXCOORD1; // ray origin
                float3 hitPos : TEXCOORD2;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            int _MaxSteps;
            float _MaxDistance;
            float _SurfaceDistance;
            int _PrimitiveType;
            float _Size;
            float _Smoothness;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                
                // Calculate ray origin and hit position for raymarching
                o.ro = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1));
                o.hitPos = v.vertex.xyz;
                
                return o;
            }

            // SDF primitive functions
            float sdSphere(float3 p, float r)
            {
                return length(p) - r;
            }

            float sdBox(float3 p, float3 b)
            {
                float3 q = abs(p) - b;
                return length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0);
            }

            float sdTorus(float3 p, float2 t)
            {
                float2 q = float2(length(p.xz) - t.x, p.y);
                return length(q) - t.y;
            }

            float sdCylinder(float3 p, float h, float r)
            {
                float2 d = abs(float2(length(p.xz), p.y)) - float2(r, h);
                return min(max(d.x, d.y), 0.0) + length(max(d, 0.0));
            }

            float sdCapsule(float3 p, float h, float r)
            {
                p.y -= clamp(p.y, 0.0, h);
                return length(p) - r;
            }

            // Main SDF function - combines all primitives
            float GetDist(float3 p)
            {
                float d = _MaxDistance;
                
                // Select primitive based on _PrimitiveType
                if (_PrimitiveType == 0) // Sphere
                {
                    d = sdSphere(p, _Size);
                }
                else if (_PrimitiveType == 1) // Box
                {
                    d = sdBox(p, float3(_Size, _Size, _Size));
                }
                else if (_PrimitiveType == 2) // Torus
                {
                    d = sdTorus(p, float2(_Size, _Size * 0.3));
                }
                else if (_PrimitiveType == 3) // Cylinder
                {
                    d = sdCylinder(p, _Size, _Size * 0.5);
                }
                else if (_PrimitiveType == 4) // Capsule
                {
                    d = sdCapsule(p, _Size, _Size * 0.3);
                }
                
                return d;
            }

            // Raymarching function
            float RayMarch(float3 ro, float3 rd)
            {
                float dO = 0;
                
                for (int i = 0; i < _MaxSteps; i++)
                {
                    float3 p = ro + rd * dO;
                    float dS = GetDist(p);
                    dO += dS;
                    
                    if (dO > _MaxDistance || abs(dS) < _SurfaceDistance)
                        break;
                }
                
                return dO;
            }

            // Calculate normal using gradient
            float3 GetNormal(float3 p)
            {
                float d = GetDist(p);
                float2 e = float2(0.01, 0);
                
                float3 n = d - float3(
                    GetDist(p - e.xyy),
                    GetDist(p - e.yxy),
                    GetDist(p - e.yyx));
                
                return normalize(n);
            }

            // Simple lighting calculation
            float GetLight(float3 p)
            {
                float3 lightPos = float3(0, 5, 6);
                float3 l = normalize(lightPos - p);
                float3 n = GetNormal(p);
                
                float dif = clamp(dot(n, l), 0., 1.);
                
                // Soft shadows
                float d = RayMarch(p + n * _SurfaceDistance * 2., l);
                if (d < length(lightPos - p))
                    dif *= 0.1;
                
                return dif;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = (i.uv - 0.5) * 2.0; // Center UV coordinates
                float3 ro = i.ro; // Ray origin (camera position in object space)
                float3 rd = normalize(i.hitPos - ro); // Ray direction
                
                // Perform raymarching
                float d = RayMarch(ro, rd);
                
                fixed4 col = fixed4(0, 0, 0, 1);
                
                if (d < _MaxDistance)
                {
                    // Hit surface - calculate lighting
                    float3 p = ro + rd * d;
                    float dif = GetLight(p);
                    
                    col.rgb = _Color.rgb * dif;
                    
                    // Add some ambient lighting
                    col.rgb += 0.2;
                }
                else
                {
                    // Miss - transparent or background color
                    discard;
                }
                
                return col;
            }
            ENDCG
        }
    }
}