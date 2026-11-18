Shader "CityDog/Dog Fur Cards"
{
    Properties
    {
        _MainTex ("Coat and strand opacity", 2D) = "white" {}
        _BumpMap ("Coat normals", 2D) = "bump" {}
        _Cutoff ("Strand coverage", Range(0, 1)) = 0.45
        _Glossiness ("Smoothness", Range(0, 1)) = 0.12
    }
    SubShader
    {
        Tags { "Queue" = "AlphaTest" "RenderType" = "TransparentCutout" }
        Cull Off

        CGPROGRAM
        #pragma target 3.0
        #pragma surface Coat Standard alphatest:_Cutoff addshadow

        sampler2D _MainTex, _BumpMap;
        half _Glossiness;

        struct Input
        {
            float2 uv_MainTex;
            float2 uv_BumpMap;
        };

        void Coat(Input input, inout SurfaceOutputStandard output)
        {
            fixed4 color = tex2D(_MainTex, input.uv_MainTex);
            output.Albedo = color.rgb;
            output.Alpha = color.a;
            output.Normal = UnpackNormal(tex2D(_BumpMap, input.uv_BumpMap));
            output.Smoothness = _Glossiness;
        }
        ENDCG
    }
    Fallback "Transparent/Cutout/Diffuse"
}
