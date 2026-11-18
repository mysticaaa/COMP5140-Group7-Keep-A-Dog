Shader "CityDog/Skybox/Anime Blue Sky"
{
    Properties
    {
        _Zenith ("Zenith", Color) = (0.14, 0.43, 0.78, 1)
        _SkyBlue ("Sky Blue", Color) = (0.26, 0.66, 0.94, 1)
        _Horizon ("Horizon", Color) = (0.78, 0.90, 0.98, 1)
        _CloudLight ("Sunlit Clouds", Color) = (1, 0.993, 0.96, 1)
        _CloudShade ("Cloud Shadows", Color) = (0.68, 0.81, 0.93, 1)
        _Rotation ("Rotation", Range(0, 360)) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            float4 _Zenith, _SkyBlue, _Horizon, _CloudLight, _CloudShade;
            float _Rotation;

            struct SkyVertex
            {
                float4 position : SV_POSITION;
                float3 direction : TEXCOORD0;
            };

            SkyVertex Vert(float4 vertex : POSITION)
            {
                SkyVertex output;
                output.position = UnityObjectToClipPos(vertex);
                output.direction = vertex.xyz;
                return output;
            }

            float Hash(float2 cell)
            {
                float3 value = frac(cell.xyx * 0.1031);
                value += dot(value, value.yzx + 33.33);
                return frac((value.x + value.y) * value.z);
            }

            float Noise(float2 samplePosition)
            {
                float2 cell = floor(samplePosition);
                float2 fraction = frac(samplePosition);
                float2 weight = fraction * fraction * (3.0 - 2.0 * fraction);
                return lerp(
                    lerp(Hash(cell), Hash(cell + float2(1, 0)), weight.x),
                    lerp(Hash(cell + float2(0, 1)), Hash(cell + 1), weight.x),
                    weight.y);
            }

            float Ellipse(float2 samplePosition, float2 center, float2 radii)
            {
                return (length((samplePosition - center) / radii) - 1.0) * min(radii.x, radii.y);
            }

            float SoftUnion(float a, float b, float radius)
            {
                float blend = saturate(0.5 + 0.5 * (b - a) / radius);
                return lerp(b, a, blend) - radius * blend * (1.0 - blend);
            }

            float2 CloudCoordinates(float2 angles, float2 center, float2 size)
            {
                const float turn = 6.2831853;
                float delta = frac((angles.x - center.x) / turn + 0.5) * turn - turn * 0.5;
                return float2(delta, angles.y - center.y) / size;
            }

            // Draw a bank of rounded cumulus lobes with a higher crown and a soft, cool underside.
            float4 Cumulus(float2 samplePosition, float seed)
            {
                if (abs(samplePosition.x) > 1.40 || samplePosition.y < -0.50 || samplePosition.y > 1.30) return 0;

                float lift = 0.16 * (Noise(float2(seed, 2.0)) - 0.5);
                float d = Ellipse(samplePosition, float2(-0.68, 0.06), float2(0.49, 0.23));
                d = SoftUnion(d, Ellipse(samplePosition, float2(-0.48, 0.23), float2(0.32, 0.32)), 0.15);
                d = SoftUnion(d, Ellipse(samplePosition, float2(-0.24, 0.43 + lift), float2(0.35, 0.43)), 0.14);
                d = SoftUnion(d, Ellipse(samplePosition, float2(0.13, 0.50), float2(0.34, 0.59)), 0.15);
                d = SoftUnion(d, Ellipse(samplePosition, float2(0.42, 0.31 - lift), float2(0.34, 0.42)), 0.13);
                d = SoftUnion(d, Ellipse(samplePosition, float2(0.70, 0.10), float2(0.41, 0.29)), 0.13);
                d = SoftUnion(d, Ellipse(samplePosition, float2(0.96, -0.03), float2(0.31, 0.17)), 0.09);
                d = SoftUnion(d, Ellipse(samplePosition, float2(-0.12, -0.045), float2(0.85, 0.23)), 0.12);

                float breakup = (Noise(samplePosition * 8.5 + seed) - 0.5) * 0.060;
                breakup += (Noise(samplePosition * 21.0 - seed) - 0.5) * 0.022;
                d += breakup;
                float cloudBase = -0.19 + 0.085 * pow(abs(samplePosition.x), 1.8) + 0.033 * breakup;
                d = max(d, cloudBase - samplePosition.y);

                float shadowEdge = 0.10 + 0.11 * Noise(samplePosition * float2(4.0, 3.0) + seed);
                shadowEdge += 0.070 * sin(samplePosition.x * 6.9 + seed) + 0.06 * samplePosition.x;
                float lit = smoothstep(shadowEdge - 0.16, shadowEdge + 0.18, samplePosition.y);
                float relief = smoothstep(0.42, 0.74, Noise(samplePosition * float2(5.8, 4.5) + seed * 3.3));
                float rim = smoothstep(-0.052, -0.012, d) * smoothstep(0.17, 0.63, samplePosition.y);
                float shadedPuffs = Ellipse(samplePosition, float2(0.19, 0.20), float2(0.36, 0.22));
                shadedPuffs = SoftUnion(shadedPuffs,
                    Ellipse(samplePosition, float2(0.37, 0.44 + lift), float2(0.23, 0.28)), 0.07);
                shadedPuffs = SoftUnion(shadedPuffs,
                    Ellipse(samplePosition, float2(-0.15, 0.08), float2(0.37, 0.14)), 0.10);
                float layeredShade = (1.0 - smoothstep(-0.03, 0.05, shadedPuffs + breakup)) * 0.48;
                float lightAmount = saturate(lit + relief * 0.19 + rim * 0.18 - layeredShade);
                float3 color = lerp(_CloudShade.rgb, _CloudLight.rgb, lightAmount);
                float alpha = 1.0 - smoothstep(-0.009, 0.015, d);
                return float4(color, alpha);
            }

            float Wisp(float2 samplePosition, float seed)
            {
                if (abs(samplePosition.x) > 1.3 || abs(samplePosition.y) > 1.3) return 0;
                float irregularity = Noise(samplePosition * float2(5.0, 1.6) + seed) - 0.5;
                float shape = 1.0 - smoothstep(0.37, 1.02, length(samplePosition + float2(0, 0.28 * irregularity)));
                return shape * (0.39 + 0.40 * irregularity);
            }

            float3 Layer(float3 background, float4 cloud)
            {
                return lerp(background, cloud.rgb, cloud.a);
            }

            fixed4 Frag(SkyVertex input) : SV_Target
            {
                float3 direction = normalize(input.direction);
                float2 angles = float2(atan2(direction.x, direction.z) - radians(_Rotation),
                    asin(clamp(direction.y, -1.0, 1.0)));
                float altitude = saturate(direction.y);
                float3 color = lerp(_Horizon.rgb, _SkyBlue.rgb, 1.0 - exp2(-altitude * 8.6));
                color = lerp(color, _Zenith.rgb, smoothstep(0.31, 1.0, altitude));

                float wisps = Wisp(CloudCoordinates(angles, float2(-2.17, 0.098), float2(0.71, 0.026)), 2.4);
                wisps += Wisp(CloudCoordinates(angles, float2(1.15, 0.43), float2(0.68, 0.047)), 5.8);
                wisps += Wisp(CloudCoordinates(angles, float2(-0.73, 0.68), float2(0.64, 0.050)), 8.7);
                color = lerp(color, _CloudLight.rgb, saturate(wisps));

                color = Layer(color, Cumulus(CloudCoordinates(angles, float2(-2.51, 0.36), float2(0.25, 0.095)), 2.6));
                color = Layer(color, Cumulus(CloudCoordinates(angles, float2(2.11, 0.38), float2(0.30, 0.107)), 15.5));
                color = Layer(color, Cumulus(CloudCoordinates(angles, float2(-0.18, 0.10), float2(0.55, 0.20)), 1.5));
                color = Layer(color, Cumulus(CloudCoordinates(angles, float2(2.93, 0.115), float2(0.57, 0.25)), 8.3));
                color = Layer(color, Cumulus(CloudCoordinates(angles, float2(-1.44, 0.24), float2(0.43, 0.17)), 4.4));
                color = Layer(color, Cumulus(CloudCoordinates(angles, float2(1.70, 0.065), float2(0.35, 0.14)), 11.2));
                color = Layer(color, Cumulus(CloudCoordinates(angles, float2(0.59, 0.49), float2(0.43, 0.15)), 6.0));

                return fixed4(color, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
