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
        private readonly bool showNameInput;
        private readonly ManagerColorPickerControl colorPicker;

        private string inputName;
        private bool focusNameOnNextDraw = true;

        public override Vector2 InitialSize
        {
            get
            {
                if (!allowColorSelection)
                    return new Vector2(420f, 160f);

                return showNameInput
                    ? new Vector2(520f, 560f)
                    : new Vector2(520f, 520f);
            }
        }

        // 기존 이름 변경 및 폴더 생성 기능과의 호환성 유지
        public ManagerFolderNameDialog(
            string title,
            string initialName,
            Func<string, bool> acceptAction)
        {
            this.title = title;
            inputName = initialName ?? string.Empty;
            this.acceptAction = (name, color) => acceptAction(name);
            allowColorSelection = false;
            showNameInput = true;
            colorPicker = null;

            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnCancel = true;
            closeOnAccept = false;
        }

        // 이름과 색상을 한 창에서 함께 편집한다.
        public ManagerFolderNameDialog(
            string title,
            string initialName,
            string initialColor,
            Func<string, string, bool> acceptAction)
        {
            this.title = title;
            inputName = initialName ?? string.Empty;
            this.acceptAction = acceptAction;
            allowColorSelection = true;
            showNameInput = true;
            colorPicker = new ManagerColorPickerControl(initialColor);

            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnCancel = true;
            closeOnAccept = false;
        }

        // 모드 배경색처럼 이름 편집이 필요 없는 경우에도 동일한 편집 창 안에 색상 컨트롤을 표시한다.
        public ManagerFolderNameDialog(
            string title,
            string initialColor,
            Action<string> acceptColorAction,
            bool showOnlyColorControls)
        {
            this.title = title;
            inputName = string.Empty;
            this.acceptAction = (name, color) =>
            {
                if (acceptColorAction != null)
                    acceptColorAction(color);
                return true;
            };
            allowColorSelection = true;
            showNameInput = !showOnlyColorControls;
            colorPicker = new ManagerColorPickerControl(initialColor);

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
            Widgets.Label(new Rect(0f, 0f, rect.width, 32f), title);
            Text.Font = GameFont.Small;

            float pickerY = 42f;
            if (showNameInput)
            {
                GUI.SetNextControlName("LillyModManagerNameInput");
                inputName = Widgets.TextField(
                    new Rect(0f, 42f, rect.width, 32f),
                    inputName);

                if (focusNameOnNextDraw)
                {
                    GUI.FocusControl("LillyModManagerNameInput");
                    focusNameOnNextDraw = false;
                }

                pickerY = 82f;
            }

            if (allowColorSelection && colorPicker != null)
                colorPicker.Draw(new Rect(0f, pickerY, rect.width, 340f));

            float buttonWidth = 120f;
            float buttonY = rect.height - 36f;

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
            string trimmedName = showNameInput
                ? (inputName ?? string.Empty).Trim()
                : string.Empty;

            if (showNameInput && trimmedName.NullOrEmpty())
            {
                Messages.Message(
                    ManagerTranslation.Get("LMM_0064"),
                    MessageTypeDefOf.RejectInput);
                return;
            }

            string selectedColor = inputColor;
            if (colorPicker != null)
            {
                if (colorPicker.HasInvalidInput)
                {
                    Messages.Message(
                        ManagerTranslation.Get("LMM_0193") + " / " +
                        ManagerTranslation.Get("LMM_0194"),
                        MessageTypeDefOf.RejectInput);
                    return;
                }

                if (colorPicker.HasChanged)
                    selectedColor = colorPicker.ColorHex;
            }

            if (acceptAction(trimmedName, selectedColor))
                Close();
        }
    }
}
