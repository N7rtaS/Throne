// Mobile approximation of Dota 2 hero.vfx (heroes, items, creeps) for URP.
// Texture layout follows Dota's compiled hero textures:
//   _ColorMap   rgb albedo, a alpha (alpha test)
//   _Masks1     r detail mask, g diffuse-warp mask, b metalness, a self-illumination
//   _Masks2     r specular intensity, g rim intensity, b tint specular by base colour, a specular exponent
//   _DiffuseWarp  1D ramp indexed by half-Lambert
//   _FresnelWarp  1D ramp indexed by N·V: r colour, g rim, b specular
// One directional light + spherical-harmonics ambient, no shadows, no cube map (planned).
// Differences from the original are listed in Research/FullGame/Differences.md (Б4).
Shader "ThornThrone/DotaHero"
{
    Properties
    {
        _ColorMap ("Color (g_tColor)", 2D) = "white" {}
        _NormalMap ("Normal (g_tNormal)", 2D) = "bump" {}
        _Masks1 ("Masks 1 (g_tMasks1)", 2D) = "black" {}
        _Masks2 ("Masks 2 (g_tMasks2)", 2D) = "black" {}
        _DiffuseWarp ("Diffuse warp (g_tDiffuseWarp)", 2D) = "gray" {}
        _FresnelWarp ("Fresnel warp (g_tFresnelWarp)", 2D) = "black" {}
        _DetailMap ("Detail (g_tDetail)", 2D) = "gray" {}

        [Toggle(_ALPHATEST_ON)] _AlphaTest ("F_ALPHA_TEST", Float) = 0
        _Cutoff ("g_flAlphaTestReference", Range(0, 1)) = 0.5
        [Toggle(_DIFFUSE_WARP)] _UseDiffuseWarp ("F_DIFFUSE_WARP", Float) = 0
        [Toggle(_DETAIL)] _UseDetail ("F_DETAIL", Float) = 0

        _SpecularExponent ("g_flSpecularExponent", Float) = 32
        _SpecularScale ("g_flSpecularScale", Float) = 5
        _SpecularColor ("g_vSpecularColor", Color) = (1, 1, 1, 1)
        _RimLightScale ("g_flRimLightScale", Float) = 0
        _RimLightColor ("g_vRimLightColor", Color) = (1, 1, 1, 1)
        _SelfIllumScale ("g_flSelfIllumScale", Float) = 1
        _DetailBlend ("g_flDetailBlendFactor", Float) = 1
        _DetailTint ("g_vDetail1ColorTint", Color) = (1, 1, 1, 1)
        _DetailScale ("g_vDetailTexCoordScale", Vector) = (1, 1, 0, 0)
        _AmbientScale ("g_flAmbientScale", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        [HideInInspector] _VatInfo ("VAT info (unused here)", Vector) = (1, 1, 1, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DotaVert
            #pragma fragment DotaFrag
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local _DIFFUSE_WARP
            #pragma shader_feature_local _DETAIL
            #pragma multi_compile_instancing
                        #include "DotaHeroCore.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DotaDepthVert
            #pragma fragment DotaDepthFrag
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_instancing
                        #include "DotaHeroCore.hlsl"
            ENDHLSL
        }
    }
}
