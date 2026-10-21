#ifndef CARD_CITY_SCENERY_TONE
#define CARD_CITY_SCENERY_TONE

// Shared by the painted scenery and its masked video residents, before vertex tint/opacity.
// Compress the highlight shoulder, not the shadows: a black veil would crush the night detail.
half3 CardCitySceneryTone(half3 source, half amount)
{
    half3 rgb = source;
    #ifdef UNITY_COLORSPACE_GAMMA
    rgb = GammaToLinearSpace(rgb);
    #endif
    half peak = max(rgb.r, max(rgb.g, rgb.b));
    // A monotonic shoulder keeps low light intact and rolls off bright lamps/gold smoothly.
    // White reaches about .73 sRGB, versus .85 in the first, deliberately mild version.
    half excess = max(peak - 0.12, 0.0);
    half mapped = min(peak, 0.12) + excess / (1.0 + excess / 0.65);
    half3 soft = rgb * (mapped / max(peak, 0.0001));
    half luminance = dot(soft, half3(0.2126, 0.7152, 0.0722));
    soft = lerp(luminance.xxx, soft, 0.80);
    rgb = lerp(rgb, soft, amount);
    #ifdef UNITY_COLORSPACE_GAMMA
    rgb = LinearToGammaSpace(rgb);
    #endif
    return rgb;
}
#endif
