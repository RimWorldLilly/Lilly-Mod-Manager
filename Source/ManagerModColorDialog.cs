
using System;
using UnityEngine;
using Verse;

namespace Lilly.IntegratedModManager
{
    internal sealed class ManagerModColorDialog : Window
    {
        private readonly Action<string> onColorSelected;
        private string currentColor;

        public override Vector2 InitialSize
        {
        get { return new Vector2(440f, 430f); }
        }

        internal ManagerModColorDialog(
            string initialColor,
            Action<string> onColorSelected)
        {
            currentColor = initialColor;
            this.onColorSelected = onColorSelected;

            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnCancel = true;
            closeOnAccept = false;
        }

        public override void DoWindowContents(Rect rect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(
                new Rect(0f, 0f, rect.width, 30f),
                "모드 배경색 선택");

            Text.Font = GameFont.Small;

const int columns = 8;
const float swatchWidth = 44f;
const float swatchHeight = 40f;
const float gap = 5f;

            for (int i = 0; i < ManagerTagColors.Palette.Length; i++)
            {
                int column = i % columns;
                int row = i / columns;

                Rect swatch = new Rect(
                    column * (swatchWidth + gap),
                    40f + row * (swatchHeight + gap),
                    swatchWidth,
                    swatchHeight);

                string colorHex = ManagerTagColors.Palette[i];

                Widgets.DrawBoxSolid(
                    swatch,
                    ManagerTagColors.Background(colorHex));

                if (string.Equals(
                    currentColor,
                    colorHex,
                    StringComparison.OrdinalIgnoreCase))
                {
                    Widgets.DrawBox(swatch, 2);
                }

                Color oldColor = GUI.color;
                GUI.color = ManagerTagColors.Foreground(colorHex);

                Widgets.Label(swatch, "Aa");

                GUI.color = oldColor;

                if (Widgets.ButtonInvisible(swatch))
                {
                    currentColor = colorHex;

                    if (onColorSelected != null)
                        onColorSelected(colorHex);

                    Close();
                }
            }

            if (Widgets.ButtonText(
new Rect(
    rect.width - 112f,
    rect.height - 42f,
    112f,
    28f),
                ManagerTranslation.Get("LMM_0066")))
            {
                string randomColor = ManagerTagColors.RandomHex();

                if (onColorSelected != null)
                    onColorSelected(randomColor);

                Close();
            }
        }
    }
}
