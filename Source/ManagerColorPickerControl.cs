using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Lilly.IntegratedModManager
{
    // 기존 편집 창 안에서 림월드 기본 HSV 휠과 색상 팔레트를 사용하는 컨트롤.
    internal sealed class ManagerColorPickerControl
    {
        private const int PickerMode = 0;
        private const int PresetMode = 1;

        private static readonly List<Color> PresetColors = BuildPresetColors();

        private string colorHex;
        private string hexInput;
        private string redInput;
        private string greenInput;
        private string blueInput;
        private int selectedMode;
        private bool colorWheelDragging;

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
            selectedMode = PickerMode;
            colorWheelDragging = false;
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

            // 사용자 지정 색상 영역은 탭 콘텐츠보다 위에 고정해 탭 전환 시 위치가 바뀌지 않게 한다.
            float customColorBottom = DrawColorDetails(rect, rect.y + 22f);
            float tabY = customColorBottom + 5f;
            float tabGap = 4f;
            float tabWidth = (rect.width - tabGap) / 2f;
            DrawModeButton(
                new Rect(rect.x, tabY, tabWidth, 24f),
                ManagerTranslation.Get("LMM_0197"),
                PickerMode);
            DrawModeButton(
                new Rect(rect.x + tabWidth + tabGap, tabY, tabWidth, 24f),
                ManagerTranslation.Get("LMM_0198"),
                PresetMode);

            float contentY = tabY + 29f;
            if (selectedMode == PickerMode)
                DrawNativeColorWheel(rect, contentY);
            else
                DrawNativePresetPalette(rect, contentY);
        }

        private float DrawNativeColorWheel(Rect rect, float y)
        {
            float wheelSize = Mathf.Min(190f, rect.width * 0.43f);
            Rect wheelRect = new Rect(rect.x + 3f, y, wheelSize, wheelSize);

            // 색상이 지정되지 않은 경우에만 임시 기본색으로 휠을 표시한다.
            Color wheelColor = colorHex == null
                ? Color.HSVToRGB(0f, 0.72f, 0.9f)
                : ManagerTagColors.Background(colorHex);
            Color previousColor = wheelColor;
            float hue;
            float saturation;
            float value;
            Color.RGBToHSV(wheelColor, out hue, out saturation, out value);

            // RimWorld 기본 HSV 색상 휠. 클릭 후 드래그도 기본 위젯이 처리한다.
            Widgets.HSVColorWheel(
                wheelRect,
                ref wheelColor,
                ref colorWheelDragging,
                value);

            if (!SameColor(previousColor, wheelColor))
                SetColor(ColorUtility.ToHtmlStringRGB(wheelColor));

            float controlX = wheelRect.xMax + 12f;
            float controlWidth = Mathf.Max(80f, rect.xMax - controlX);
            Widgets.Label(
                new Rect(controlX, y + 12f, controlWidth, 24f),
                ManagerTranslation.Get("LMM_0201"));

            float updatedValue = Widgets.HorizontalSlider(
                new Rect(controlX, y + 38f, controlWidth, 18f),
                value,
                0f,
                1f,
                false,
                null,
                "0%",
                "100%");

            if (Mathf.Abs(updatedValue - value) > 0.0001f)
            {
                Color.RGBToHSV(wheelColor, out hue, out saturation, out value);
                wheelColor = Color.HSVToRGB(hue, saturation, updatedValue);
                SetColor(ColorUtility.ToHtmlStringRGB(wheelColor));
            }

            Widgets.Label(
                new Rect(controlX, y + 64f, controlWidth, 24f),
                "H: " + Mathf.RoundToInt(hue * 360f) + "°");
            Widgets.Label(
                new Rect(controlX, y + 88f, controlWidth, 24f),
                "S: " + Mathf.RoundToInt(saturation * 100f) + "%");
            Widgets.Label(
                new Rect(controlX, y + 112f, controlWidth, 24f),
                "V: " + Mathf.RoundToInt(updatedValue * 100f) + "%");

            return y + wheelSize;
        }

        private float DrawNativePresetPalette(Rect rect, float y)
        {
            Color selected = colorHex == null
                ? new Color(0f, 0f, 0f, 0f)
                : ManagerTagColors.Background(colorHex);
            float height;
            bool changed = Widgets.ColorSelector(
                new Rect(rect.x, y, rect.width, 150f),
                ref selected,
                PresetColors,
                out height,
                null,
                22,
                2);

            if (changed)
                SetColor(ColorUtility.ToHtmlStringRGB(selected));

            return y + height;
        }

        private float DrawColorDetails(Rect rect, float y)
        {
            Widgets.Label(
                new Rect(rect.x, y, rect.width, 18f),
                ManagerTranslation.Get("LMM_0192"));

            float previewY = y + 18f;
            Rect preview = new Rect(rect.x, previewY, 48f, 26f);
            Color previewColor = colorHex == null
                ? new Color(0.22f, 0.22f, 0.22f, 1f)
                : ManagerTagColors.Background(colorHex);
            Widgets.DrawBoxSolid(preview, previewColor);
            Widgets.DrawBox(preview);

            Color previousGuiColor = GUI.color;
            TextAnchor previousAnchor = Text.Anchor;
            GUI.color = colorHex == null
                ? Color.white
                : ManagerTagColors.Foreground(colorHex);
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
                    UpdateRgbInputs(parsed);
                    HasInvalidInput = false;
                }
                else
                {
                    HasInvalidInput = true;
                }
            }

            return rgbY + 24f;
        }

        private void DrawModeButton(Rect rect, string label, int mode)
        {
            bool selected = selectedMode == mode;
            Color previousColor = GUI.color;
            if (selected)
                GUI.color = new Color(0.88f, 0.72f, 0.34f, 1f);

            if (Widgets.ButtonText(rect, label))
            {
                selectedMode = mode;
                if (mode != PickerMode)
                    colorWheelDragging = false;
            }

            GUI.color = previousColor;
            if (selected)
                Widgets.DrawBox(rect, 2);
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

        private static List<Color> BuildPresetColors()
        {
            List<Color> result = new List<Color>();
            for (int i = 0; i < ManagerTagColors.Palette.Length; i++)
                result.Add(ManagerTagColors.Background(ManagerTagColors.Palette[i]));
            return result;
        }

        private static bool SameColor(Color left, Color right)
        {
            return Mathf.Abs(left.r - right.r) < 0.0001f &&
                Mathf.Abs(left.g - right.g) < 0.0001f &&
                Mathf.Abs(left.b - right.b) < 0.0001f &&
                Mathf.Abs(left.a - right.a) < 0.0001f;
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
