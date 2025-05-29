Shader "Custom/SDFPrimitivesArray"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _PrimitiveData ("Primitive Data Texture", 2D) = "black" {}
        _MaxPrimitives ("Max Primitives", Int) = 16
        _SmoothFactor ("Global Smooth Factor", Float) = 0.5
        _SceneRotation ("Scene Rotation", Vector) = (0, 0, 0, 0)
        
        [Header(Lighting)]
        _LightPos ("Light Position", Vector) = (5, 5, 5, 0)
        _LightColor ("Light Color", Color) = (1, 1, 1, 1)
        _LightIntensity ("Light Intensity", Range(0, 10)) = 1.0
        _AmbientColor ("Ambient Color", Color) = (0.1, 0.1, 0.2, 1)
        _SpecularPower ("Specular Power", Range(1, 128)) = 32
        _SpecularIntensity ("Specular Intensity", Range(0, 1)) = 0.5
        
        [Header(Shadows)]
        _ShadowIntensity ("Shadow Intensity", Range(0, 1)) = 0.8
        _ShadowSoftness ("Shadow Softness", Range(1, 32)) = 8
        
        [Header(Additional Effects)]
        _FresnelPower ("Fresnel Power", Range(0, 5)) = 1.0
        _FresnelIntensity ("Fresnel Intensity", Range(0, 1)) = 0.3
        _AOSteps ("Ambient Occlusion Steps", Range(1, 10)) = 5
        _AOIntensity ("AO Intensity", Range(0, 1)) = 0.5
        _AORadius ("AO Radius", Range(0.01, 0.5)) = 0.1
        
        [Header(Raymarching)]
        _MaxSteps ("Max Raymarching Steps", Int) = 100
        _MaxDist ("Max Distance", Float) = 100
        _SurfaceDist ("Surface Distance", Float) = 0.01
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
            #include "Lighting.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float3 rayOrigin : TEXCOORD1;
                float3 rayDir : TEXCOORD2;
            };

            // Primitive data structure
            struct Primitive
            {
                float3 position;
                float3 rotation;
                float3 scale;
                float type; // 0 = sphere, 1 = cube, 2 = cylinder, etc.
                float enabled;
            };

            // Properties
            fixed4 _Color;
            sampler2D _PrimitiveData;
            float4 _PrimitiveData_TexelSize;
            int _MaxPrimitives;
            float _SmoothFactor;
            float3 _SceneRotation;
            
            // Lighting
            float3 _LightPos;
            fixed4 _LightColor;
            float _LightIntensity;
            fixed4 _AmbientColor;
            float _SpecularPower;
            float _SpecularIntensity;
            
            // Shadows
            float _ShadowIntensity;
            float _ShadowSoftness;
            
            // Effects
            float _FresnelPower;
            float _FresnelIntensity;
            int _AOSteps;
            float _AOIntensity;
            float _AORadius;
            
            // Raymarching
            int _MaxSteps;
            float _MaxDist;
            float _SurfaceDist;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                
                o.rayOrigin = _WorldSpaceCameraPos;
                float4 worldPos = mul(unity_ObjectToWorld, v.vertex);
                o.rayDir = normalize(worldPos.xyz - o.rayOrigin);
                
                return o;
            }

            // Rotation matrices
            float3x3 rotateX(float theta)
            {
                float c = cos(theta);
                float s = sin(theta);
                return float3x3(
                    1, 0, 0,
                    0, c, -s,
                    0, s, c
                );
            }
            
            float3x3 rotateY(float theta)
            {
                float c = cos(theta);
                float s = sin(theta);
                return float3x3(
                    c, 0, s,
                    0, 1, 0,
                    -s, 0, c
                );
            }
            
            float3x3 rotateZ(float theta)
            {
                float c = cos(theta);
                float s = sin(theta);
                return float3x3(
                    c, -s, 0,
                    s, c, 0,
                    0, 0, 1
                );
            }
            
            float3x3 rotateXYZ(float3 euler)
            {
                euler *= 0.01745329251994329576923690768489; // PI/180
                return mul(rotateZ(euler.z), mul(rotateY(euler.y), rotateX(euler.x)));
            }

            // Read primitive data from texture
            Primitive ReadPrimitive(int index)
            {
                Primitive prim;
                
                // Each primitive uses 3 pixels (12 floats total)
                float2 uv0 = float2((index * 3 + 0.5) * _PrimitiveData_TexelSize.x, 0.5);
                float2 uv1 = float2((index * 3 + 1.5) * _PrimitiveData_TexelSize.x, 0.5);
                float2 uv2 = float2((index * 3 + 2.5) * _PrimitiveData_TexelSize.x, 0.5);
                
                float4 data0 = tex2Dlod(_PrimitiveData, float4(uv0, 0, 0));
                float4 data1 = tex2Dlod(_PrimitiveData, float4(uv1, 0, 0));
                float4 data2 = tex2Dlod(_PrimitiveData, float4(uv2, 0, 0));
                
                prim.position = data0.xyz;
                prim.type = data0.w;
                prim.rotation = data1.xyz;
                prim.enabled = data1.w;
                prim.scale = data2.xyz;
                
                return prim;
            }

            // SDF primitives
            float sdSphere(float3 p, float3 center, float radius)
            {
                return length(p - center) - radius;
            }

            float sdBox(float3 p, float3 center, float3 size, float3 rotation)
            {
                float3 q = p - center;
                float3x3 rot = rotateXYZ(rotation);
                q = mul(transpose(rot), q);
                float3 d = abs(q) - size;
                return length(max(d, 0.0)) + min(max(d.x, max(d.y, d.z)), 0.0);
            }

            float sdCylinder(float3 p, float3 center, float2 hr, float3 rotation)
            {
                float3 q = p - center;
                float3x3 rot = rotateXYZ(rotation);
                q = mul(transpose(rot), q);
                float2 d = abs(float2(length(q.xz), q.y)) - hr;
                return min(max(d.x, d.y), 0.0) + length(max(d, 0.0));
            }

            float sdTorus(float3 p, float3 center, float2 t, float3 rotation)
            {
                float3 q = p - center;
                float3x3 rot = rotateXYZ(rotation);
                q = mul(transpose(rot), q);
                float2 pq = float2(length(q.xz) - t.x, q.y);
                return length(pq) - t.y;
            }

            // Evaluate single primitive
            float EvaluatePrimitive(float3 p, Primitive prim)
            {
                if (prim.enabled < 0.5) return _MaxDist;
                
                float d = _MaxDist;
                
                if (prim.type < 0.5) // Sphere
                {
                    d = sdSphere(p, prim.position, prim.scale.x);
                }
                else if (prim.type < 1.5) // Cube
                {
                    d = sdBox(p, prim.position, prim.scale, prim.rotation);
                }
                else if (prim.type < 2.5) // Cylinder
                {
                    d = sdCylinder(p, prim.position, float2(prim.scale.x, prim.scale.y), prim.rotation);
                }
                else if (prim.type < 3.5) // Torus
                {
                    d = sdTorus(p, prim.position, float2(prim.scale.x, prim.scale.y), prim.rotation);
                }
                
                return d;
            }

            // Smooth union
            float sdfSmoothUnion(float d1, float d2, float k)
            {
                float h = clamp(0.5 + 0.5 * (d2 - d1) / k, 0.0, 1.0);
                return lerp(d2, d1, h) - k * h * (1.0 - h);
            }

            // Combined SDF scene
            float GetDist(float3 p)
            {
                // Apply inverse scene rotation to the point
                float3x3 sceneRot = rotateXYZ(-_SceneRotation);
                p = mul(sceneRot, p);
                
                float d = _MaxDist;
                
                // Read and evaluate all primitives
                for (int i = 0; i < _MaxPrimitives; i++)
                {
                    Primitive prim = ReadPrimitive(i);
                    float primDist = EvaluatePrimitive(p, prim);
                    
                    if (i == 0 || primDist >= _MaxDist)
                    {
                        d = min(d, primDist);
                    }
                    else
                    {
                        d = sdfSmoothUnion(d, primDist, _SmoothFactor);
                    }
                }
                
                return d;
            }

            // Calculate normal
            float3 GetNormal(float3 p)
            {
                float d = GetDist(p);
                float2 e = float2(0.001, 0);
                
                float3 n = d - float3(
                    GetDist(p - e.xyy),
                    GetDist(p - e.yxy),
                    GetDist(p - e.yyx)
                );
                
                return normalize(n);
            }

            // Raymarching
            float RayMarch(float3 ro, float3 rd)
            {
                float dO = 0.0;
                
                for(int i = 0; i < _MaxSteps; i++)
                {
                    float3 p = ro + rd * dO;
                    float dS = GetDist(p);
                    dO += dS;
                    
                    if(dO > _MaxDist || dS < _SurfaceDist) break;
                }
                
                return dO;
            }

            // Soft shadows
            float GetSoftShadow(float3 ro, float3 rd, float mint, float maxt)
            {
                float res = 1.0;
                float t = mint;
                
                for(int i = 0; i < 16; i++)
                {
                    float h = GetDist(ro + rd * t);
                    res = min(res, _ShadowSoftness * h / t);
                    t += clamp(h, 0.02, 0.2);
                    if(h < 0.001 || t > maxt) break;
                }
                
                return clamp(res, 1.0 - _ShadowIntensity, 1.0);
            }

            // Ambient occlusion
            float GetAO(float3 p, float3 n)
            {
                float occ = 0.0;
                float sca = 1.0;
                
                for(int i = 0; i < _AOSteps; i++)
                {
                    float h = _AORadius * float(i) / float(_AOSteps - 1);
                    float d = GetDist(p + h * n);
                    occ += (h - d) * sca;
                    sca *= 0.95;
                }
                
                return 1.0 - _AOIntensity * occ;
            }

            // Lighting
            float3 GetLighting(float3 p, float3 rd)
            {
                float3 n = GetNormal(p);
                float3 col = float3(0, 0, 0);
                
                // Apply scene rotation to light position
                float3x3 sceneRot = rotateXYZ(_SceneRotation);
                float3 rotatedLightPos = mul(sceneRot, _LightPos);
                
                float3 lightDir = normalize(rotatedLightPos - p);
                float lightDist = length(rotatedLightPos - p);
                
                float diff = max(dot(n, lightDir), 0.0);
                
                float3 viewDir = normalize(-rd);
                float3 halfDir = normalize(lightDir + viewDir);
                float spec = pow(max(dot(n, halfDir), 0.0), _SpecularPower) * _SpecularIntensity;
                
                float shadow = GetSoftShadow(p + n * _SurfaceDist * 2.0, lightDir, 0.02, lightDist);
                float ao = GetAO(p, n);
                float fresnel = pow(1.0 - max(dot(n, viewDir), 0.0), _FresnelPower) * _FresnelIntensity;
                
                float3 ambient = _AmbientColor.rgb * ao;
                float3 diffuse = _LightColor.rgb * _LightIntensity * diff * shadow;
                float3 specular = _LightColor.rgb * spec * shadow;
                float3 fresnelColor = _LightColor.rgb * fresnel;
                
                col = _Color.rgb * (ambient + diffuse) + specular + fresnelColor;
                
                return col;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 ro = i.rayOrigin;
                float3 rd = normalize(i.rayDir);
                
                float d = RayMarch(ro, rd);
                
                fixed4 col = fixed4(0.05, 0.05, 0.1, 1);
                
                if(d < _MaxDist)
                {
                    float3 p = ro + rd * d;
                    col.rgb = GetLighting(p, rd);
                    
                    float fog = 1.0 - exp(-0.01 * d);
                    col.rgb = lerp(col.rgb, float3(0.05, 0.05, 0.1), fog);
                }
                
                col.rgb = pow(col.rgb, 0.4545);
                
                return col;
            }
            ENDCG
        }
    }
}