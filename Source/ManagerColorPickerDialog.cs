using System;
using UnityEngine;
using Verse;

namespace Lilly.IntegratedModManager
{
    // 카테고리/태그 및 모드 배경색에서 공통으로 사용하는 색상 선택 창.
    internal sealed class ManagerColorPickerDialog : Window
    {
        private readonly Action<string> onColorSelected;
        private readonly string titleText;
        private string currentColor;
        private string hexInput;
        private string redInput;
        private string greenInput;
        private string blueInput;

        public override Vector2 InitialSize
        {
            get { return new Vector2(440f, 520f); }
        }

        internal ManagerColorPickerDialog(string title, string initialColor, Action<string> onColorSelected)
        {
            this.titleText = title;
            this.onColorSelected = onColorSelected;
            currentColor = Normalize(initialColor);
            if (currentColor == null)
                currentColor = "4A90E2";
            SyncInputs();

            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnCancel = true;
            closeOnAccept = false;
        }

        public override void DoWindowContents(Rect rect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, rect.width, 30f), titleText);
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, 32f, rect.width, 24f), ManagerTranslation.Get("LMM_0041"));

            const int columns = 8;
            const float swatchWidth = 44f;
            const float swatchHeight = 36f;
            const float gap = 5f;
            for (int i = 0; i < ManagerTagColors.Palette.Length; i++)
            {
                int column = i % columns;
                int row = i / columns;
                Rect swatch = new Rect(column * (swatchWidth + gap), 62f + row * (swatchHeight + gap),
                    swatchWidth, swatchHeight);
                string colorHex = ManagerTagColors.Palette[i];
                Widgets.DrawBoxSolid(swatch, ManagerTagColors.Background(colorHex));
                if (string.Equals(currentColor, colorHex, StringComparison.OrdinalIgnoreCase))
                    Widgets.DrawBox(swatch, 2);
                Color oldColor = GUI.color;
                GUI.color = ManagerTagColors.Foreground(colorHex);
                Widgets.Label(swatch, "Aa");
                GUI.color = oldColor;
                if (Widgets.ButtonInvisible(swatch))
                {
                    currentColor = colorHex;
                    SyncInputs();
                }
            }

            float customY = 62f + 7f * (swatchHeight + gap) + 2f;
            Widgets.Label(new Rect(0f, customY, rect.width, 24f), ManagerTranslation.Get("LMM_0192"));
            Rect preview = new Rect(0f, customY + 26f, 54f, 30f);
            Widgets.DrawBoxSolid(preview, ManagerTagColors.Background(currentColor));
            Widgets.DrawBox(preview);
            Widgets.Label(new Rect(62f, customY + 26f, 100f, 30f), "#" + currentColor);

            float inputY = customY + 62f;
            Widgets.Label(new Rect(0f, inputY, 90f, 24f), ManagerTranslation.Get("LMM_0193"));
            string editedHex = Widgets.TextField(new Rect(96f, inputY, 130f, 24f), hexInput);
            if (editedHex != hexInput)
            {
                hexInput = editedHex;
                Color parsed;
                if (ColorUtility.TryParseHtmlString("#" + hexInput.Trim().TrimStart('#'), out parsed))
                {
                    currentColor = ColorUtility.ToHtmlStringRGB(parsed);
                    UpdateRgbInputs(parsed);
                }
            }

            Widgets.Label(new Rect(0f, inputY + 30f, 90f, 24f), ManagerTranslation.Get("LMM_0194"));
            float rgbWidth = (rect.width - 110f) / 3f;
            string newRed = Widgets.TextField(new Rect(96f, inputY + 30f, rgbWidth, 24f), redInput);
            string newGreen = Widgets.TextField(new Rect(102f + rgbWidth, inputY + 30f, rgbWidth, 24f), greenInput);
            string newBlue = Widgets.TextField(new Rect(108f + rgbWidth * 2f, inputY + 30f, rgbWidth, 24f), blueInput);
            if (newRed != redInput || newGreen != greenInput || newBlue != blueInput)
            {
                redInput = newRed;
                greenInput = newGreen;
                blueInput = newBlue;
                int r, g, b;
                if (int.TryParse(redInput, out r) && int.TryParse(greenInput, out g) &&
                    int.TryParse(blueInput, out b) && r >= 0 && r <= 255 &&
                    g >= 0 && g <= 255 && b >= 0 && b <= 255)
                {
                    currentColor = ColorUtility.ToHtmlStringRGB(new Color32((byte)r, (byte)g, (byte)b, 255));
                    hexInput = currentColor;
                }
            }

            float buttonY = rect.height - 36f;
            if (Widgets.ButtonText(new Rect(0f, buttonY, 104f, 28f), ManagerTranslation.Get("LMM_0066")))
            {
                currentColor = ManagerTagColors.RandomHex();
                SyncInputs();
            }
            if (Widgets.ButtonText(new Rect(rect.width - 216f, buttonY, 104f, 28f), ManagerTranslation.Get("LMM_0196")))
                Close();
            if (Widgets.ButtonText(new Rect(rect.width - 104f, buttonY, 104f, 28f), ManagerTranslation.Get("LMM_0195")))
            {
                int checkRed, checkGreen, checkBlue;
                bool validRgb = int.TryParse(redInput, out checkRed) &&
                    int.TryParse(greenInput, out checkGreen) &&
                    int.TryParse(blueInput, out checkBlue) &&
                    checkRed >= 0 && checkRed <= 255 &&
                    checkGreen >= 0 && checkGreen <= 255 &&
                    checkBlue >= 0 && checkBlue <= 255;
                if (!ManagerTagColors.IsValidHex(hexInput) || !validRgb)
                {
                    Messages.Message(ManagerTranslation.Get("LMM_0193") + " / " +
                        ManagerTranslation.Get("LMM_0194"), MessageTypeDefOf.RejectInput);
                    return;
                }
                if (onColorSelected != null)
                    onColorSelected(currentColor);
                Close();
            }
        }

        private void SyncInputs()
        {
            Color color = ManagerTagColors.Background(currentColor);
            hexInput = currentColor;
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
    }
}