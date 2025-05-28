Shader "Custom/SDFPrimitives"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _SpherePos1 ("Sphere 1 Position", Vector) = (0, 0, 0, 1)
        _SphereRadius1 ("Sphere 1 Radius", Float) = 1.0
        _CubePos1 ("Cube 1 Position", Vector) = (2, 0, 0, 1)
        _CubeSize1 ("Cube 1 Size", Vector) = (1, 1, 1, 0)
        _SmoothFactor ("Smooth Union Factor", Float) = 0.5
        
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

            // Properties
            fixed4 _Color;
            float3 _SpherePos1;
            float _SphereRadius1;
            float3 _CubePos1;
            float3 _CubeSize1;
            float _SmoothFactor;
            
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
                
                // Ray origin is camera position in world space
                o.rayOrigin = _WorldSpaceCameraPos;
                
                // Calculate ray direction
                float4 worldPos = mul(unity_ObjectToWorld, v.vertex);
                o.rayDir = normalize(worldPos.xyz - o.rayOrigin);
                
                return o;
            }

            // SDF for sphere
            float sdSphere(float3 p, float3 center, float radius)
            {
                return length(p - center) - radius;
            }

            // SDF for box/cube
            float sdBox(float3 p, float3 center, float3 size)
            {
                float3 q = abs(p - center) - size;
                return length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0);
            }

            // Smooth union operation
            float sdfSmoothUnion(float d1, float d2, float k)
            {
                float h = clamp(0.5 + 0.5 * (d2 - d1) / k, 0.0, 1.0);
                return lerp(d2, d1, h) - k * h * (1.0 - h);
            }

            // Combined SDF scene
            float GetDist(float3 p)
            {
                float sphere1 = sdSphere(p, _SpherePos1, _SphereRadius1);
                float cube1 = sdBox(p, _CubePos1, _CubeSize1);
                
                // Combine primitives with smooth union
                return sdfSmoothUnion(sphere1, cube1, _SmoothFactor);
            }

            // Calculate normal using gradient
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

            // Main lighting calculation
            float3 GetLighting(float3 p, float3 rd)
            {
                float3 n = GetNormal(p);
                float3 col = float3(0, 0, 0);
                
                // Main light direction and calculations
                float3 lightDir = normalize(_LightPos - p);
                float lightDist = length(_LightPos - p);
                
                // Diffuse lighting (Lambertian)
                float diff = max(dot(n, lightDir), 0.0);
                
                // Specular lighting (Blinn-Phong)
                float3 viewDir = normalize(-rd);
                float3 halfDir = normalize(lightDir + viewDir);
                float spec = pow(max(dot(n, halfDir), 0.0), _SpecularPower) * _SpecularIntensity;
                
                // Shadows
                float shadow = GetSoftShadow(p + n * _SurfaceDist * 2.0, lightDir, 0.02, lightDist);
                
                // Ambient occlusion
                float ao = GetAO(p, n);
                
                // Fresnel effect
                float fresnel = pow(1.0 - max(dot(n, viewDir), 0.0), _FresnelPower) * _FresnelIntensity;
                
                // Combine lighting components
                float3 ambient = _AmbientColor.rgb * ao;
                float3 diffuse = _LightColor.rgb * _LightIntensity * diff * shadow;
                float3 specular = _LightColor.rgb * spec * shadow;
                float3 fresnelColor = _LightColor.rgb * fresnel;
                
                col = _Color.rgb * (ambient + diffuse) + specular + fresnelColor;
                
                // Unity's additional lights (point/spot lights)
                #ifdef VERTEXLIGHT_ON
                for(int i = 0; i < 4; i++)
                {
                    float3 lightPos = float3(unity_4LightPosX0[i], unity_4LightPosY0[i], unity_4LightPosZ0[i]);
                    float3 lightDir2 = normalize(lightPos - p);
                    float lightDist2 = length(lightPos - p);
                    float atten = 1.0 / (1.0 + lightDist2 * lightDist2);
                    
                    float diff2 = max(dot(n, lightDir2), 0.0);
                    col += unity_LightColor[i].rgb * diff2 * atten;
                }
                #endif
                
                return col;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 ro = i.rayOrigin;
                float3 rd = normalize(i.rayDir);
                
                float d = RayMarch(ro, rd);
                
                fixed4 col = fixed4(0.05, 0.05, 0.1, 1); // Sky color
                
                if(d < _MaxDist)
                {
                    float3 p = ro + rd * d;
                    col.rgb = GetLighting(p, rd);
                    
                    // Simple fog for depth
                    float fog = 1.0 - exp(-0.01 * d);
                    col.rgb = lerp(col.rgb, float3(0.05, 0.05, 0.1), fog);
                }
                
                // Gamma correction
                col.rgb = pow(col.rgb, 0.4545);
                
                return col;
            }
            ENDCG
        }
    }
}