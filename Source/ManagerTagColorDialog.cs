using System;
using UnityEngine;
using Verse;

namespace Lilly.IntegratedModManager
{
    internal sealed class ManagerTagColorDialog : Window
    {
        private readonly ManagerTagData tag;
        private readonly Action<string> onColorSelected;

        public override Vector2 InitialSize
        {
            get { return new Vector2(440f, 250f); }
        }

        internal ManagerTagColorDialog(ManagerTagData tag, Action<string> onColorSelected)
        {
            this.tag = tag;
            this.onColorSelected = onColorSelected;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnCancel = true;
            closeOnAccept = false;
        }

        public override void DoWindowContents(Rect rect)
        {
            if (tag == null)
                return;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, rect.width, 30f), "태그 색상: " + tag.Name);
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, 32f, rect.width, 24f),
                "배경색을 선택하세요. 글자색은 배경 밝기에 맞춰 자동으로 정해집니다.");

            const int columns = 6;
            const float swatchWidth = 54f;
            const float swatchHeight = 48f;
            const float gap = 8f;
            for (int i = 0; i < ManagerTagColors.Palette.Length; i++)
            {
                int column = i % columns;
                int row = i / columns;
                Rect swatch = new Rect(column * (swatchWidth + gap), 66f + row * (swatchHeight + gap),
                    swatchWidth, swatchHeight);
                Widgets.DrawBoxSolid(swatch, ManagerTagColors.Background(ManagerTagColors.Palette[i]));
                if (string.Equals(tag.ColorHex, ManagerTagColors.Palette[i],
                    StringComparison.OrdinalIgnoreCase))
                    Widgets.DrawBox(swatch, 2);
                Color oldColor = GUI.color;
                GUI.color = ManagerTagColors.Foreground(ManagerTagColors.Palette[i]);
                Widgets.Label(swatch, "Aa");
                GUI.color = oldColor;
                if (Widgets.ButtonInvisible(swatch))
                {
                    if (onColorSelected != null)
                        onColorSelected(ManagerTagColors.Palette[i]);
                    Close();
                }
            }

            if (Widgets.ButtonText(new Rect(rect.width - 112f, rect.height - 32f, 112f, 28f), "임의 색상"))
            {
                if (onColorSelected != null)
                    onColorSelected(ManagerTagColors.RandomHex());
                Close();
            }
        }
    }
}
