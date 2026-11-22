using System;
using UnityEngine;
using Verse;

namespace Lilly.IntegratedModManager
{
    // 기존 편집 창 안에 직접 그리는 팔레트 및 사용자 지정 색상 입력 컨트롤.
    // 별도의 Window를 생성하지 않는다.
    internal sealed class ManagerColorPickerControl
    {
        private string colorHex;
        private string hexInput;
        private string redInput;
        private string greenInput;
        private string blueInput;

        internal string ColorHex
        {
            get { return colorHex; }
        }

        internal bool HasChanged { get; private set; }

        internal bool HasInvalidInput { get; private set; }

        internal ManagerColorPickerControl(string initialColor)
        {
            Reset(initialColor);
        }

        internal void Reset(string initialColor)
        {
            colorHex = Normalize(initialColor);
            hexInput = colorHex ?? string.Empty;
            UpdateRgbInputs(colorHex);
            HasChanged = false;
            HasInvalidInput = false;
        }

        internal void SetColor(string value)
        {
            string normalized = Normalize(value);
            if (normalized == null)
                return;

            colorHex = normalized;
            hexInput = normalized;
            UpdateRgbInputs(normalized);
            HasChanged = true;
            HasInvalidInput = false;
        }

        internal void Clear()
        {
            colorHex = null;
            hexInput = string.Empty;
            redInput = string.Empty;
            greenInput = string.Empty;
            blueInput = string.Empty;
            HasChanged = true;
            HasInvalidInput = false;
        }

        internal void Draw(Rect rect)
        {
            Widgets.Label(
                new Rect(rect.x, rect.y, rect.width, 22f),
                ManagerTranslation.Get("LMM_0041"));

            const int columns = 8;
            const float swatchHeight = 25f;
            const float gap = 3f;
            float swatchWidth = (rect.width - gap * (columns - 1)) / columns;
            int rows = Mathf.CeilToInt(ManagerTagColors.Palette.Length / (float)columns);
            float gridY = rect.y + 24f;

            for (int i = 0; i < ManagerTagColors.Palette.Length; i++)
            {
                int column = i % columns;
                int row = i / columns;
                Rect swatch = new Rect(
                    rect.x + column * (swatchWidth + gap),
                    gridY + row * (swatchHeight + gap),
                    swatchWidth,
                    swatchHeight);
                string paletteColor = ManagerTagColors.Palette[i];

                Widgets.DrawBoxSolid(swatch, ManagerTagColors.Background(paletteColor));
                if (string.Equals(colorHex, paletteColor, StringComparison.OrdinalIgnoreCase))
                    Widgets.DrawBox(swatch, 2);

                GameFont oldFont = Text.Font;
                Color oldColor = GUI.color;
                Text.Font = GameFont.Tiny;
                GUI.color = ManagerTagColors.Foreground(paletteColor);
                Widgets.Label(swatch, "Aa");
                GUI.color = oldColor;
                Text.Font = oldFont;

                if (Widgets.ButtonInvisible(swatch))
                    SetColor(paletteColor);
            }

            float gridBottom = gridY + rows * (swatchHeight + gap) - gap;
            float customY = gridBottom + 4f;
            Widgets.Label(
                new Rect(rect.x, customY, rect.width, 20f),
                ManagerTranslation.Get("LMM_0192"));

            float previewY = customY + 20f;
            Rect preview = new Rect(rect.x, previewY, 48f, 26f);
            Color previewColor = colorHex == null
                ? new Color(0.22f, 0.22f, 0.22f, 1f)
                : ManagerTagColors.Background(colorHex);
            Widgets.DrawBoxSolid(preview, previewColor);
            Widgets.DrawBox(preview);
            Color previousGuiColor = GUI.color;
            GUI.color = colorHex == null ? Color.white : ManagerTagColors.Foreground(colorHex);
            Widgets.Label(preview, colorHex == null ? "-" : "Aa");
            GUI.color = previousGuiColor;

            Widgets.Label(
                new Rect(rect.x + 56f, previewY, 100f, 26f),
                colorHex == null ? ManagerTranslation.Get("LMM_0192") : "#" + colorHex);

            float clearWidth = 94f;
            float randomWidth = 104f;
            float actionGap = 5f;
            float actionX = rect.xMax - clearWidth - randomWidth - actionGap;
            if (Widgets.ButtonText(
                new Rect(actionX, previewY, clearWidth, 26f),
                "색상 없음"))
                Clear();

            if (Widgets.ButtonText(
                new Rect(rect.xMax - randomWidth, previewY, randomWidth, 26f),
                ManagerTranslation.Get("LMM_0066")))
                SetColor(ManagerTagColors.RandomHex());

            float inputY = previewY + 30f;
            const float labelWidth = 78f;
            Widgets.Label(
                new Rect(rect.x, inputY, labelWidth, 24f),
                ManagerTranslation.Get("LMM_0193"));
            string editedHex = Widgets.TextField(
                new Rect(rect.x + labelWidth, inputY, rect.width - labelWidth, 24f),
                hexInput);
            if (editedHex != hexInput)
            {
                hexInput = editedHex;
                HasChanged = true;
                Color parsed;
                if (TryParseHex(hexInput, out parsed))
                {
                    colorHex = ColorUtility.ToHtmlStringRGB(parsed);
                    UpdateRgbInputs(parsed);
                    HasInvalidInput = false;
                }
                else
                {
                    HasInvalidInput = true;
                }
            }

            float rgbY = inputY + 27f;
            Widgets.Label(
                new Rect(rect.x, rgbY, labelWidth, 24f),
                ManagerTranslation.Get("LMM_0194"));
            float rgbGap = 4f;
            float rgbWidth = (rect.width - labelWidth - rgbGap * 2f) / 3f;
            string newRed = Widgets.TextField(
                new Rect(rect.x + labelWidth, rgbY, rgbWidth, 24f),
                redInput);
            string newGreen = Widgets.TextField(
                new Rect(rect.x + labelWidth + rgbWidth + rgbGap, rgbY, rgbWidth, 24f),
                greenInput);
            string newBlue = Widgets.TextField(
                new Rect(rect.x + labelWidth + (rgbWidth + rgbGap) * 2f, rgbY, rgbWidth, 24f),
                blueInput);

            if (newRed != redInput || newGreen != greenInput || newBlue != blueInput)
            {
                redInput = newRed;
                greenInput = newGreen;
                blueInput = newBlue;
                HasChanged = true;

                int red;
                int green;
                int blue;
                if (TryParseRgb(out red, out green, out blue))
                {
                    colorHex = ColorUtility.ToHtmlStringRGB(
                        new Color32((byte)red, (byte)green, (byte)blue, 255));
                    hexInput = colorHex;
                    HasInvalidInput = false;
                }
                else
                {
                    HasInvalidInput = true;
                }
            }
        }

        private bool TryParseRgb(out int red, out int green, out int blue)
        {
            bool parsedRed = int.TryParse(redInput, out red);
            bool parsedGreen = int.TryParse(greenInput, out green);
            bool parsedBlue = int.TryParse(blueInput, out blue);
            return parsedRed && parsedGreen && parsedBlue &&
                red >= 0 && red <= 255 &&
                green >= 0 && green <= 255 &&
                blue >= 0 && blue <= 255;
        }

        private void UpdateRgbInputs(string value)
        {
            Color color;
            if (value == null ||
                !ColorUtility.TryParseHtmlString("#" + value.TrimStart('#'), out color))
            {
                redInput = string.Empty;
                greenInput = string.Empty;
                blueInput = string.Empty;
                return;
            }

            UpdateRgbInputs(color);
        }

        private void UpdateRgbInputs(Color color)
        {
            redInput = Mathf.RoundToInt(color.r * 255f).ToString();
            greenInput = Mathf.RoundToInt(color.g * 255f).ToString();
            blueInput = Mathf.RoundToInt(color.b * 255f).ToString();
        }

        private static string Normalize(string value)
        {
            if (!ManagerTagColors.IsValidHex(value))
                return null;

            Color color;
            return ColorUtility.TryParseHtmlString("#" + value.Trim().TrimStart('#'), out color)
                ? ColorUtility.ToHtmlStringRGB(color)
                : null;
        }

        private static bool TryParseHex(string value, out Color color)
        {
            string trimmed = value == null
                ? string.Empty
                : value.Trim().TrimStart('#');
            color = Color.clear;
            return ManagerTagColors.IsValidHex(trimmed) &&
                ColorUtility.TryParseHtmlString("#" + trimmed, out color);
        }
    }
}
