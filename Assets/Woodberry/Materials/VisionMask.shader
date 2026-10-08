// Геометрия обзора: заливает маску яркостью там, где игрок видит.
//
// Смешивание аддитивное (Blend One One), а не обычная прозрачность.
// Источников обзора несколько, их области пересекаются, и при обычном
// альфа-смешивании перекрытие дало бы шов. Сумма с последующим ограничением
// по единице в шейдере темноты такого шва не даёт.
//
// Спад яркости к краю считается ЗДЕСЬ, по интерполированному признаку
// расстояния, а не по горстке колец вершин.
//
// Почему так. Раньше меш строил 6 колец, и цвет вершины задавал яркость.
// Между кольцами яркость шла линейно, поэтому весь спад укладывался
// в шесть ступеней, а «мягко» он выглядел только рядом. Чем больше радиус
// обзора относительно размера кадра, тем шире каждая ступень на экране —
// и тем заметнее, что уход в темноту ступенчатый. Шаг по пикселю эти
// ступени убирает: количество колец больше не влияет на плавность.
Shader "Woodberry/VisionMask"
{
    Properties
    {
        // Насколько широкой должна быть зона ухода в темноту, в долях радиуса.
        // 0.3 — последние 30% пятна уходят в ноль. Спад собирается у самой
        // кромки, поэтому основная часть пятна горит ровно, а не тускнеет
        // от центра. Так граница читается мягкой, но пятно не превращается
        // в размытый пузырь.
        _FalloffWidth ("Falloff Width", Range(0.05, 1)) = 0.3
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Overlay"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "VisionMask"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite Off
            ZTest Always
            Cull Off
            Blend One One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _FalloffWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                // x — доля радиуса, на которой стоит вершина. Центр — 0,
                // внутреннее кольцо — 1 - _FalloffWidth, внешнее — 1.
                // Радиус, а не «пройденный путь»: у луча, упёршегося в стену,
                // вершина ложится на стену, и короткий луч дал бы долю 1
                // уже в глубине комнаты — рельеф света привязался бы
                // к планировке, а не к расстоянию от игрока.
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half radiusT : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.radiusT = input.color.r;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float t = saturate(input.radiusT);

                // Спад собирается У КРОМКИ, а не размазывается по всему
                // радиусу. Разница принципиальна: если гасить яркость
                // от центра, пятно выглядит тусклым размытым пузырём.
                // Здесь яркость держится ровно почти до конца, и в ноль
                // уходит последняя полоса шириной _FalloffWidth.
                float falloff = saturate((t - (1.0 - _FalloffWidth)) / max(0.0001, _FalloffWidth));

                // Спад идёт по ПЛАВНОЙ кривой, а не по степенной.
                //
                // Степень здесь работала против задачи: показатель 1.5
                // прижимал яркость к единице почти до самого края, и весь
                // переход собирался в последние проценты радиуса — то есть
                // снова в ступеньку, только сильнее. Сглаженная ступенька
                // (smoothstep) распределяет переход по всей ширине полосы:
                // медленный вход, быстрая середина, медленный выход.
                // Именно мягкий выход и читается как «уходит во тьму»,
                // а не как обрыв.
                falloff = falloff * falloff * (3.0 - 2.0 * falloff);

                half brightness = (half)(1.0 - falloff);
                return half4(brightness, brightness, brightness, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
