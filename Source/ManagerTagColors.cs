using UnityEngine;

namespace Lilly.IntegratedModManager
{
    internal static class ManagerTagColors
    {
        internal static readonly string[] Palette =
        {
            // 빨강 / 분홍
            "EF5350", "E57373", "C62828", "FF8A80",
            "F06292", "EC407A", "AD1457", "F8BBD0",

            // 보라
            "AB47BC", "BA68C8", "8E24AA", "E1BEE7",
            "7E57C2", "9575CD", "5E35B1", "D1C4E9",

            // 파랑
            "5C6BC0", "7986CB", "3949AB", "C5CAE9",
            "42A5F5", "64B5F6", "1565C0", "BBDEFB",
            "26C6DA", "4FC3F7", "00838F", "B2EBF2",

            // 초록
            "26A69A", "4DB6AC", "00796B", "B2DFDB",
            "66BB6A", "81C784", "2E7D32", "C8E6C9",

            // 노랑 / 주황
            "D4E157", "AED581", "827717", "F0F4C3",
            "FFCA28", "FFD54F", "F9A825", "FFF9C4",
            "FFA726", "FFB74D", "EF6C00", "FFE0B2",

            // 갈색 / 회색
            "8D6E63", "A1887F", "5D4037", "D7CCC8",
            "78909C", "90A4AE", "455A64", "CFD8DC"
        };

        internal static string RandomHex()
        {
            return Palette[Random.Range(0, Palette.Length)];
        }

        internal static bool IsValidHex(string value)
        {
            Color ignored;
            return !string.IsNullOrEmpty(value) &&
                ColorUtility.TryParseHtmlString("#" + value.TrimStart('#'), out ignored);
        }

        internal static Color Background(string value)
        {
            Color color;
            return ColorUtility.TryParseHtmlString(
                "#" + (value ?? string.Empty).TrimStart('#'), out color)
                ? color
                : new Color(0.35f, 0.4f, 0.45f, 1f);
        }

        // WCAG 상대 휘도 공식. 팔레트 정렬/선택과 글자 대비 판단에 공통으로 사용한다.
        internal static float RelativeLuminance(Color color)
        {
            return 0.2126f * Linear(color.r) +
                0.7152f * Linear(color.g) +
                0.0722f * Linear(color.b);
        }

        internal static Color Foreground(string value)
        {
            return Foreground(Background(value));
        }

        internal static Color Foreground(Color background)
        {
            // 검정과 흰색 중 더 높은 대비를 제공하는 글자색을 선택한다.
            return RelativeLuminance(background) > 0.179f
                ? Color.black
                : Color.white;
        }

        private static float Linear(float channel)
        {
            return channel <= 0.04045f
                ? channel / 12.92f
                : Mathf.Pow((channel + 0.055f) / 1.055f, 2.4f);
        }
    }
}
