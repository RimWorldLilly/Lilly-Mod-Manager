using UnityEngine;

namespace Lilly.IntegratedModManager
{
    internal static class ManagerTagColors
    {
        internal static readonly string[] Palette =
        {
            "E57373", "F06292", "BA68C8", "9575CD", "7986CB", "64B5F6",
            "4FC3F7", "4DB6AC", "81C784", "AED581", "DCE775", "FFD54F",
            "FFB74D", "A1887F", "90A4AE", "5C8D89", "6C8EBF", "9673A6"
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
            return ColorUtility.TryParseHtmlString("#" + (value ?? string.Empty).TrimStart('#'), out color)
                ? color
                : new Color(0.35f, 0.4f, 0.45f, 1f);
        }

        internal static Color Foreground(string value)
        {
            Color background = Background(value);
            float luminance = 0.2126f * Linear(background.r) +
                0.7152f * Linear(background.g) + 0.0722f * Linear(background.b);
            return luminance > 0.179f ? Color.black : Color.white;
        }

        private static float Linear(float channel)
        {
            return channel <= 0.04045f
                ? channel / 12.92f
                : Mathf.Pow((channel + 0.055f) / 1.055f, 2.4f);
        }
    }
}
