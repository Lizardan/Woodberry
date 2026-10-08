// Порт Assets/2DLighting/Shaders/2DLighting.cginc из SardineFish/Unity2DLighting.
//
// Карта теней хранит ОСВЕЩЁННОСТЬ: 1 = свет дошёл, 0 = перекрыто.
// Поэтому семплирование обёрнуто в (1 - value).
#ifndef WOODBERRY_U2DLIGHTING_INCLUDED
#define WOODBERRY_U2DLIGHTING_INCLUDED

sampler2D _ShadowMap;

// Доля света, остающаяся в тени. Тень, погашенная в ноль, читается не как
// тень, а как дыра в геометрии: силуэт предмета пропадает совсем, и вместо
// объёма на экране чёрный клин. Ноль здесь был бы «физически честным», но
// в 2D-картинке без глобального освещения он не работает.
float _ShadowFloor;

#define SAMPLE_SHADOW_2D(uv) (1 - tex2D(_ShadowMap, (uv).xy).r)

#define APPLY_SHADOW_2D(color, uv) ((color) * lerp(_ShadowFloor, 1.0, SAMPLE_SHADOW_2D(uv)))

// Свет — это плавный градиент на сотни пикселей, а кадр на выходе
// восьмибитный. Без шума градиент раскладывается в видимые полосы, и в тёмной
// сцене они читаются как дефект отрисовки, а не как свет. Interleaved gradient
// noise стоит три инструкции и, в отличие от белого шума, не даёт заметного
// «кипения» на статичной картинке.
float DitherNoise(float2 pixelPos)
{
    return frac(52.9829189 * frac(dot(pixelPos, float2(0.06711056, 0.00583715))));
}

#define DITHER_OFFSET(pixelPos) ((DitherNoise(pixelPos) - 0.5) * (1.0 / 255.0))

#endif // WOODBERRY_U2DLIGHTING_INCLUDED
