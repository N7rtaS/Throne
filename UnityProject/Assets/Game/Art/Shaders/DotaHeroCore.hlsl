// Shared lighting of ThornThrone/DotaHero and ThornThrone/DotaHeroVAT (see DotaHero.shader for the
// texture layout). Define DOTA_VAT before including to fetch skinned positions from a vertex animation texture.
#ifndef THORN_DOTA_HERO_CORE
#define THORN_DOTA_HERO_CORE

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

TEXTURE2D(_ColorMap); SAMPLER(sampler_ColorMap);
TEXTURE2D(_NormalMap);
TEXTURE2D(_Masks1);
TEXTURE2D(_Masks2);
TEXTURE2D(_DiffuseWarp);
TEXTURE2D(_FresnelWarp);
SAMPLER(sampler_linear_clamp);
TEXTURE2D(_DetailMap); SAMPLER(sampler_DetailMap);
#if defined(DOTA_VAT)
TEXTURE2D(_VatPositions); TEXTURE2D(_VatNormals); SAMPLER(sampler_point_clamp);
#endif

CBUFFER_START(UnityPerMaterial)
    float4 _ColorMap_ST;
    float _Cutoff, _SpecularExponent, _SpecularScale, _RimLightScale, _SelfIllumScale, _DetailBlend, _AmbientScale;
    float4 _SpecularColor, _RimLightColor, _DetailTint, _DetailScale;
    // x: texture width, y: rows per frame, z: texture height, w: unused
    float4 _VatInfo;
CBUFFER_END

#if defined(DOTA_VAT)
UNITY_INSTANCING_BUFFER_START(VatProps)
    UNITY_DEFINE_INSTANCED_PROP(float, _Frame)
UNITY_INSTANCING_BUFFER_END(VatProps)
#endif

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 uv : TEXCOORD0;
    uint vertexID : SV_VertexID;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    half3 normalWS : TEXCOORD2;
    half4 tangentWS : TEXCOORD3;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

#if defined(DOTA_VAT)
// Vertex v of frame f is stored at (v % width, f * rowsPerFrame + v / width).
float _ThornVatDebug; // global diagnostics switch: 1 = keep rest pose
void FetchVat(uint vertexID, inout float3 position, inout float3 normal)
{
    if (_ThornVatDebug > 0.5) return;
    float frame = floor(UNITY_ACCESS_INSTANCED_PROP(VatProps, _Frame));
    float width = _VatInfo.x;
    float column = fmod((float)vertexID, width);
    float row = frame * _VatInfo.y + floor((float)vertexID / width);
    float2 uv = float2((column + 0.5) / width, (row + 0.5) / _VatInfo.z);
    position = SAMPLE_TEXTURE2D_LOD(_VatPositions, sampler_point_clamp, uv, 0).xyz;
    normal = SAMPLE_TEXTURE2D_LOD(_VatNormals, sampler_point_clamp, uv, 0).xyz * 2 - 1;
}
#endif

Varyings DotaVert(Attributes input)
{
    Varyings o;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, o);
    float3 position = input.positionOS.xyz;
    float3 normal = input.normalOS;
    #if defined(DOTA_VAT)
        FetchVat(input.vertexID, position, normal);
    #endif
    VertexPositionInputs p = GetVertexPositionInputs(position);
    VertexNormalInputs n = GetVertexNormalInputs(normal, input.tangentOS);
    o.positionCS = p.positionCS;
    o.positionWS = p.positionWS;
    o.normalWS = n.normalWS;
    o.tangentWS = half4(n.tangentWS, input.tangentOS.w * GetOddNegativeScale());
    o.uv = TRANSFORM_TEX(input.uv, _ColorMap);
    return o;
}

half4 DotaFrag(Varyings i) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(i);
    half4 color = SAMPLE_TEXTURE2D(_ColorMap, sampler_ColorMap, i.uv);
    #if defined(_ALPHATEST_ON)
        clip(color.a - _Cutoff);
    #endif
    half4 m1 = SAMPLE_TEXTURE2D(_Masks1, sampler_ColorMap, i.uv);
    half4 m2 = SAMPLE_TEXTURE2D(_Masks2, sampler_ColorMap, i.uv);
    half3 albedo = color.rgb;
    #if defined(_DETAIL)
        half3 detail = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, i.uv * _DetailScale.xy).rgb * _DetailTint.rgb;
        albedo = lerp(albedo, albedo * detail * 2, saturate(m1.r * _DetailBlend));
    #endif

    // Tangent-space normal; the exported maps are RGB with Y up.
    half3 nTS = SAMPLE_TEXTURE2D(_NormalMap, sampler_ColorMap, i.uv).rgb * 2 - 1;
    half3 bitangent = cross(i.normalWS, i.tangentWS.xyz) * i.tangentWS.w;
    half3 N = normalize(nTS.x * i.tangentWS.xyz + nTS.y * bitangent + nTS.z * i.normalWS);
    half3 V = normalize(GetWorldSpaceViewDir(i.positionWS));
    Light light = GetMainLight();
    half3 L = light.direction;
    half ndl = dot(N, L);
    half metal = m1.b;

    #if defined(_DIFFUSE_WARP)
        half3 diffuseLight = SAMPLE_TEXTURE2D(_DiffuseWarp, sampler_linear_clamp, half2(ndl * 0.5 + 0.5, 0.5)).rgb;
    #else
        half3 diffuseLight = saturate(ndl).xxx;
    #endif
    half3 ambient = SampleSH(N) * _AmbientScale;
    half3 diffuse = albedo * (diffuseLight * light.color + ambient) * (1 - metal);

    half3 fresnel = SAMPLE_TEXTURE2D(_FresnelWarp, sampler_linear_clamp, half2(saturate(dot(N, V)), 0.5)).rgb;
    half3 R = reflect(-L, N);
    half exponent = max(1, m2.a * _SpecularExponent);
    half3 specTint = lerp(half3(1, 1, 1), albedo, max(m2.b, metal)) * _SpecularColor.rgb;
    half3 specular = pow(saturate(dot(R, V)), exponent) * m2.r * fresnel.b * _SpecularScale * saturate(ndl) * light.color * specTint;
    // Metals reflect the environment; without a cube map the ambient stands in for it.
    specular += ambient * albedo * metal;

    half3 rim = m2.g * fresnel.g * _RimLightScale * _RimLightColor.rgb * saturate(N.y * 0.5 + 0.5) * 0.25;
    half3 selfIllum = albedo * m1.a * _SelfIllumScale;
    return half4(diffuse + specular + rim + selfIllum, 1);
}

struct DepthVaryings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

DepthVaryings DotaDepthVert(Attributes input)
{
    DepthVaryings o;
    UNITY_SETUP_INSTANCE_ID(input);
    float3 position = input.positionOS.xyz;
    #if defined(DOTA_VAT)
        float3 normal = input.normalOS;
        FetchVat(input.vertexID, position, normal);
    #endif
    o.positionCS = TransformObjectToHClip(position);
    o.uv = TRANSFORM_TEX(input.uv, _ColorMap);
    return o;
}

half DotaDepthFrag(DepthVaryings i) : SV_Target
{
    #if defined(_ALPHATEST_ON)
        clip(SAMPLE_TEXTURE2D(_ColorMap, sampler_ColorMap, i.uv).a - _Cutoff);
    #endif
    return 0;
}

#endif
