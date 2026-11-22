using System;
using UnityEngine;
using Verse;

namespace Lilly.IntegratedModManager
{
    // 기존 편집 창 안에 직접 그리는 색상 선택기.
    // 색상 선택기 / 기본 팔레트 / 상대 휘도 팔레트를 한 컨트롤에서 제공한다.
    internal sealed class ManagerColorPickerControl
    {
        private const int PickerMode = 0;
        private const int PresetMode = 1;
        private const int LuminanceMode = 2;
        private const int GridColumns = 16;
        private const int GridRows = 10;
        private const int PresetColumns = 12;

        private string colorHex;
        private string hexInput;
        private string redInput;
        private string greenInput;
        private string blueInput;
        private float selectedHue;
        private int selectedMode;

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
            if (colorHex != null)
            {
                Color initial = ManagerTagColors.Background(colorHex);
                float saturation;
                float value;
                ToHsv(initial, out selectedHue, out saturation, out value);
                if (saturation <= 0.0001f)
                    selectedHue = 0f;
            }
            else
            {
                selectedHue = 0f;
            }

            selectedMode = PickerMode;
            UpdateRgbInputs(colorHex);
            HasChanged = false;
            HasInvalidInput = false;
        }

        internal void SetColor(string value)
        {
            SetColor(value, true);
        }

        private void SetColor(string value, bool updateHue)
        {
            string normalized = Normalize(value);
            if (normalized == null)
                return;

            colorHex = normalized;
            hexInput = normalized;
            UpdateRgbInputs(normalized);
            if (updateHue)
            {
                float saturation;
                float brightness;
                float hue;
                ToHsv(ManagerTagColors.Background(normalized),
                    out hue, out saturation, out brightness);
                if (saturation > 0.0001f)
                    selectedHue = hue;
            }
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

            float tabY = rect.y + 22f;
            float tabGap = 4f;
            float tabWidth = (rect.width - tabGap * 2f) / 3f;
            DrawModeButton(
                new Rect(rect.x, tabY, tabWidth, 24f),
                ManagerTranslation.Get("LMM_0197"),
                PickerMode);
            DrawModeButton(
                new Rect(rect.x + tabWidth + tabGap, tabY, tabWidth, 24f),
                ManagerTranslation.Get("LMM_0198"),
                PresetMode);
            DrawModeButton(
                new Rect(rect.x + (tabWidth + tabGap) * 2f, tabY, tabWidth, 24f),
                ManagerTranslation.Get("LMM_0199"),
                LuminanceMode);

            float gridY;
            if (selectedMode == PresetMode)
            {
                gridY = tabY + 29f;
            }
            else
            {
                float hueY = tabY + 28f;
                DrawHueStrip(new Rect(rect.x, hueY, rect.width, 16f));
                gridY = hueY + 20f;
            }

            DrawColorGrid(rect, gridY);

            int visibleRows = selectedMode == PresetMode
                ? Mathf.CeilToInt(ManagerTagColors.Palette.Length / (float)PresetColumns)
                : GridRows;
            float cellHeight = selectedMode == PresetMode ? 30f : 14f;
            float cellGap = selectedMode == PresetMode ? 2f : 1.5f;
            float gridBottom = gridY + visibleRows * (cellHeight + cellGap) - cellGap;
            float customY = gridBottom + 3f;
            Widgets.Label(
                new Rect(rect.x, customY, rect.width, 18f),
                ManagerTranslation.Get("LMM_0192"));

            float previewY = customY + 18f;
            Rect preview = new Rect(rect.x, previewY, 48f, 26f);
            Color previewColor = colorHex == null
                ? new Color(0.22f, 0.22f, 0.22f, 1f)
                : ManagerTagColors.Background(colorHex);
            Widgets.DrawBoxSolid(preview, previewColor);
            Widgets.DrawBox(preview);
            Color previousGuiColor = GUI.color;
            GUI.color = colorHex == null
                ? Color.white
                : ManagerTagColors.Foreground(colorHex);
            TextAnchor previousAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(preview, colorHex == null ? "-" : "Aa");
            Text.Anchor = previousAnchor;
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

            float inputY = previewY + 29f;
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
                    float saturation;
                    float brightness;
                    float hue;
                    ToHsv(parsed, out hue, out saturation, out brightness);
                    if (saturation > 0.0001f)
                        selectedHue = hue;
                    HasInvalidInput = false;
                }
                else
                {
                    HasInvalidInput = true;
                }
            }

            float rgbY = inputY + 26f;
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
                    Color parsed = new Color32((byte)red, (byte)green, (byte)blue, 255);
                    colorHex = ColorUtility.ToHtmlStringRGB(parsed);
                    hexInput = colorHex;
                    float saturation;
                    float brightness;
                    float hue;
                    ToHsv(parsed, out hue, out saturation, out brightness);
                    if (saturation > 0.0001f)
                        selectedHue = hue;
                    HasInvalidInput = false;
                }
                else
                {
                    HasInvalidInput = true;
                }
            }
        }

        private void DrawModeButton(Rect rect, string label, int mode)
        {
            bool selected = selectedMode == mode;
            Color previousColor = GUI.color;
            if (selected)
                GUI.color = new Color(0.88f, 0.72f, 0.34f, 1f);

            if (Widgets.ButtonText(rect, label))
                selectedMode = mode;

            GUI.color = previousColor;
            if (selected)
                Widgets.DrawBox(rect, 2);
        }

        private void DrawHueStrip(Rect rect)
        {
            const int segments = 24;
            const float gap = 2f;
            float segmentWidth = (rect.width - gap * (segments - 1)) / segments;
            for (int i = 0; i < segments; i++)
            {
                float hue = i / (float)segments;
                Rect segment = new Rect(
                    rect.x + i * (segmentWidth + gap),
                    rect.y,
                    segmentWidth,
                    rect.height);
                Widgets.DrawBoxSolid(segment, ColorFromHsv(hue, 1f, 1f));
                if (Mathf.Abs(Mathf.DeltaAngle(selectedHue * 360f, hue * 360f)) < 7.5f)
                    Widgets.DrawBox(segment, 2);

                if (Widgets.ButtonInvisible(segment))
                    SelectHue(hue);
                TooltipHandler.TipRegion(segment,
                    "H " + Mathf.RoundToInt(hue * 360f) + "°");
            }
        }

        private void SelectHue(float hue)
        {
            selectedHue = Mathf.Repeat(hue, 1f);

            float saturation = 1f;
            float value = 1f;
            if (colorHex != null)
                ToHsv(ManagerTagColors.Background(colorHex),
                    out hue, out saturation, out value);

            // 무채색이나 검정을 선택한 상태에서도 색상 띠를 누르면 새 색조가 보이도록 한다.
            if (saturation <= 0.0001f)
                saturation = 1f;
            if (value <= 0.0001f)
                value = 1f;

            Color selected;
            if (selectedMode == LuminanceMode && colorHex != null)
            {
                float luminance = ManagerTagColors.RelativeLuminance(
                    ManagerTagColors.Background(colorHex));
                if (luminance <= 0.0001f)
                    luminance = 0.5f;
                selected = ColorAtLuminance(selectedHue, saturation, luminance);
            }
            else
            {
                selected = ColorFromHsv(selectedHue, saturation, value);
            }

            SetColor(ColorUtility.ToHtmlStringRGB(selected), false);
        }

        private void DrawColorGrid(Rect rect, float gridY)
        {
            int columns = selectedMode == PresetMode ? PresetColumns : GridColumns;
            int visibleRows = selectedMode == PresetMode
                ? Mathf.CeilToInt(ManagerTagColors.Palette.Length / (float)PresetColumns)
                : GridRows;
            float cellHeight = selectedMode == PresetMode ? 30f : 14f;
            float cellGap = selectedMode == PresetMode ? 2f : 1.5f;
            float cellWidth = (rect.width - cellGap * (columns - 1)) / columns;

            for (int row = 0; row < visibleRows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    string candidate;
                    if (selectedMode == PresetMode)
                    {
                        int index = row * columns + column;
                        if (index >= ManagerTagColors.Palette.Length)
                            continue;
                        candidate = ManagerTagColors.Palette[index];
                    }
                    else
                    {
                        float saturation = column / (float)(GridColumns - 1);
                        Color generated;
                        if (selectedMode == LuminanceMode)
                        {
                            float targetLuminance = 1f -
                                row / (float)(GridRows - 1) * 0.97f;
                            generated = ColorAtLuminance(
                                selectedHue, saturation, targetLuminance);
                        }
                        else
                        {
                            float value = 1f -
                                row / (float)(GridRows - 1) * 0.90f;
                            generated = ColorFromHsv(selectedHue, saturation, value);
                        }

                        candidate = ColorUtility.ToHtmlStringRGB(generated);
                    }

                    Rect swatch = new Rect(
                        rect.x + column * (cellWidth + cellGap),
                        gridY + row * (cellHeight + cellGap),
                        cellWidth,
                        cellHeight);
                    Widgets.DrawBoxSolid(swatch, ManagerTagColors.Background(candidate));

                    if (string.Equals(
                        colorHex, candidate, StringComparison.OrdinalIgnoreCase))
                        Widgets.DrawBox(swatch, 2);

                    if (Widgets.ButtonInvisible(swatch))
                        SetColor(candidate, selectedMode == PresetMode);

                    float luminanceValue = ManagerTagColors.RelativeLuminance(
                        ManagerTagColors.Background(candidate));
                    TooltipHandler.TipRegion(
                        swatch,
                        "#" + candidate + "  |  " +
                        ManagerTranslation.Get("LMM_0200") + ": " +
                        Mathf.RoundToInt(luminanceValue * 100f) + "%");
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

        private static Color ColorAtLuminance(float hue, float requestedSaturation, float target)
        {
            target = Mathf.Clamp01(target);
            if (target <= 0.0001f)
                return Color.black;
            if (target >= 0.9999f)
                return Color.white;

            float saturation = Mathf.Clamp01(requestedSaturation);
            if (ManagerTagColors.RelativeLuminance(
                ColorFromHsv(hue, saturation, 1f)) < target)
            {
                // 어두운 색상(특히 파랑)은 최대 채도에서 목표 휘도에 도달하지 못할 수 있다.
                // 목표 휘도를 보존하도록 필요한 만큼 채도를 낮춘다.
                float lowSaturation = 0f;
                float highSaturation = saturation;
                for (int i = 0; i < 18; i++)
                {
                    float middle = (lowSaturation + highSaturation) * 0.5f;
                    float luminance = ManagerTagColors.RelativeLuminance(
                        ColorFromHsv(hue, middle, 1f));
                    if (luminance >= target)
                        lowSaturation = middle;
                    else
                        highSaturation = middle;
                }
                saturation = lowSaturation;
            }

            float lowValue = 0f;
            float highValue = 1f;
            for (int i = 0; i < 20; i++)
            {
                float middle = (lowValue + highValue) * 0.5f;
                float luminance = ManagerTagColors.RelativeLuminance(
                    ColorFromHsv(hue, saturation, middle));
                if (luminance < target)
                    lowValue = middle;
                else
                    highValue = middle;
            }

            return ColorFromHsv(hue, saturation, (lowValue + highValue) * 0.5f);
        }

        private static Color ColorFromHsv(float hue, float saturation, float value)
        {
            hue = Mathf.Repeat(hue, 1f);
            saturation = Mathf.Clamp01(saturation);
            value = Mathf.Clamp01(value);

            float sector = hue * 6f;
            int index = Mathf.FloorToInt(sector);
            float fraction = sector - index;
            float p = value * (1f - saturation);
            float q = value * (1f - fraction * saturation);
            float t = value * (1f - (1f - fraction) * saturation);

            switch (index % 6)
            {
                case 0: return new Color(value, t, p, 1f);
                case 1: return new Color(q, value, p, 1f);
                case 2: return new Color(p, value, t, 1f);
                case 3: return new Color(p, q, value, 1f);
                case 4: return new Color(t, p, value, 1f);
                default: return new Color(value, p, q, 1f);
            }
        }

        private static void ToHsv(
            Color color,
            out float hue,
            out float saturation,
            out float value)
        {
            float maximum = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
            float minimum = Mathf.Min(color.r, Mathf.Min(color.g, color.b));
            float delta = maximum - minimum;

            value = maximum;
            saturation = maximum <= 0f ? 0f : delta / maximum;
            hue = 0f;

            if (delta <= 0.00001f)
                return;

            if (maximum == color.r)
                hue = (color.g - color.b) / delta;
            else if (maximum == color.g)
                hue = 2f + (color.b - color.r) / delta;
            else
                hue = 4f + (color.r - color.g) / delta;

            hue = Mathf.Repeat(hue / 6f, 1f);
        }
    }
}
