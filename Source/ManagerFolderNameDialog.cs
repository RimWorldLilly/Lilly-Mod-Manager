
using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace Lilly.IntegratedModManager
{
    internal sealed class ManagerFolderNameDialog : Window
    {
        private readonly string title;
        private readonly Func<string, string, bool> acceptAction;

        private readonly bool allowColorSelection;

        private string inputName;
        private string inputColor;

        private bool focusNameOnNextDraw = true;

        public override Vector2 InitialSize
        {
            get
            {
return allowColorSelection
    ? new Vector2(440f, 480f)
    : new Vector2(420f, 160f);
            }
        }

        // 기존 이름 변경 및 폴더 생성 기능과의 호환성 유지
        public ManagerFolderNameDialog(
            string title,
            string initialName,
            Func<string, bool> acceptAction)
        {
            this.title = title;
            inputName = initialName ?? "";
            inputColor = ManagerTagColors.RandomHex();

            this.acceptAction = (name, color) =>
                acceptAction(name);

            allowColorSelection = false;

            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnCancel = true;
            closeOnAccept = false;
        }

        // 색상 선택 기능이 포함된 생성자
        public ManagerFolderNameDialog(
            string title,
            string initialName,
            string initialColor,
            Func<string, string, bool> acceptAction)
        {
            this.title = title;
            inputName = initialName ?? "";

inputColor = ManagerTagColors.IsValidHex(initialColor)
    ? initialColor
    : null;

            this.acceptAction = acceptAction;
            allowColorSelection = true;

            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnCancel = true;
            closeOnAccept = false;
        }

        public override void DoWindowContents(Rect rect)
        {
            Event e = Event.current;

            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Return ||
                    e.keyCode == KeyCode.KeypadEnter)
                {
                    TryAccept();
                    e.Use();
                    return;
                }

                if (e.keyCode == KeyCode.Escape)
                {
                    Close();
                    e.Use();
                    return;
                }
            }

            Text.Font = GameFont.Medium;
            Widgets.Label(
                new Rect(0f, 0f, rect.width, 32f), title);

            Text.Font = GameFont.Small;

            GUI.SetNextControlName("LillyModManagerNameInput");

            inputName = Widgets.TextField(
                new Rect(0f, 42f, rect.width, 32f),
                inputName);

            if (focusNameOnNextDraw)
            {
                GUI.FocusControl("LillyModManagerNameInput");
                focusNameOnNextDraw = false;
            }

            // 카테고리 색상도 공통 색상 선택 창을 사용한다.
            if (allowColorSelection)
            {
                if (Widgets.ButtonText(new Rect(0f, 84f, 92f, 28f), "색상 없음"))
                    inputColor = null;

                if (Widgets.ButtonText(
                    new Rect(98f, 84f, 112f, 28f),
                    ManagerTranslation.Get("LMM_0192")))
                {
                    Find.WindowStack.Add(new ManagerColorPickerDialog(
                        ManagerTranslation.Get("LMM_0192"),
                        inputColor,
                        colorHex => inputColor = colorHex));
                }

                if (ManagerTagColors.IsValidHex(inputColor))
                {
                    Rect preview = new Rect(218f, 84f, 42f, 28f);
                    Widgets.DrawBoxSolid(preview, ManagerTagColors.Background(inputColor));
                    Widgets.DrawBox(preview);
                    Widgets.Label(new Rect(268f, 84f, rect.width - 268f, 28f), "#" + inputColor);
                }
            }

            float buttonWidth = 120f;
            float buttonY = 380f;

            if (Widgets.ButtonText(
                new Rect(
                    rect.width / 2f - buttonWidth - 5f,
                    buttonY,
                    buttonWidth,
                    32f),
                ManagerTranslation.Get("LMM_0074")))
            {
                Close();
            }

            if (Widgets.ButtonText(
                new Rect(
                    rect.width / 2f + 5f,
                    buttonY,
                    buttonWidth,
                    32f),
                ManagerTranslation.Get("LMM_0118")))
            {
                TryAccept();
            }
        }

        private void TryAccept()
        {
            string trimmedName = inputName.Trim();

            if (trimmedName.NullOrEmpty())
            {
                Messages.Message(
                    ManagerTranslation.Get("LMM_0064"),
                    MessageTypeDefOf.RejectInput);

                return;
            }

            if (acceptAction(trimmedName, inputColor))
                Close();
        }
    }
}
