using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Lilly.ModManager.Desktop;

internal sealed class TagSelectionDialog : Form
{
    private readonly IList<TagData> tags;
    private readonly Func<TagData, bool> isAssigned;
    private readonly Action<TagData, bool> onToggle;
    private readonly Func<string, TagData?> onCreate;
    private readonly Panel tagList = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly TextBox newTagName = new() { Dock = DockStyle.Fill };
    private bool rebuilding;

    public TagSelectionDialog(
        IList<TagData> tags,
        Func<TagData, bool> isAssigned,
        Action<TagData, bool> onToggle,
        Func<string, TagData?> onCreate)
    {
        this.tags = tags;
        this.isAssigned = isAssigned;
        this.onToggle = onToggle;
        this.onCreate = onCreate;

        Text = "태그 편집";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = Color.FromArgb(27, 30, 36);
        ForeColor = Color.Gainsboro;
        Font = new Font("Segoe UI", 9f);
        ClientSize = new Size(440, 520);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(10),
            BackColor = BackColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72f));

        var heading = new Panel { Dock = DockStyle.Fill };
        heading.Controls.Add(new Label
        {
            Text = "선택한 모드에 지정할 태그를 선택하세요.",
            Dock = DockStyle.Top,
            Height = 24,
            ForeColor = Color.Gainsboro
        });
        heading.Controls.Add(new Label
        {
            Text = "체크 상태는 선택된 모든 모드에 공통으로 지정된 경우에 표시됩니다.",
            Dock = DockStyle.Bottom,
            Height = 20,
            ForeColor = Color.Silver,
            Font = new Font("Segoe UI", 8f)
        });

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(0, 7, 0, 0)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92f));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        var addButton = new Button { Text = "태그 추가", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat };
        var closeButton = new Button { Text = "닫기", Dock = DockStyle.Right, Width = 92, FlatStyle = FlatStyle.Flat };
        footer.Controls.Add(newTagName, 0, 0);
        footer.Controls.Add(addButton, 1, 0);
        footer.Controls.Add(closeButton, 1, 1);

        root.Controls.Add(heading, 0, 0);
        root.Controls.Add(tagList, 0, 1);
        root.Controls.Add(footer, 0, 2);
        Controls.Add(root);

        addButton.Click += (_, _) => CreateTag();
        newTagName.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            CreateTag();
            e.SuppressKeyPress = true;
        };
        closeButton.Click += (_, _) => Close();
        AcceptButton = addButton;
        CancelButton = closeButton;

        tagList.Resize += (_, _) =>
        {
            int rowWidth = Math.Max(300, tagList.ClientSize.Width -
                (tagList.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0) - 4);
            foreach (Panel row in tagList.Controls.OfType<Panel>())
            {
                row.Width = rowWidth;
                ManagerChipLabel? chip = row.Controls.OfType<ManagerChipLabel>().FirstOrDefault();
                if (chip != null) chip.Width = row.Width - 46;
            }
        };

        RebuildTagRows();
    }

    private void RebuildTagRows()
    {
        rebuilding = true;
        tagList.SuspendLayout();
        foreach (Control old in tagList.Controls.Cast<Control>().ToArray())
        {
            tagList.Controls.Remove(old);
            old.Dispose();
        }

        List<TagData> visibleTags = tags.Where(tag => tag != null && !tag.IsSystem).ToList();
        if (visibleTags.Count == 0)
        {
            tagList.Controls.Add(new Label
            {
                Text = "등록된 사용자 태그가 없습니다.",
                ForeColor = Color.Silver,
                AutoSize = true,
                Location = new Point(8, 10)
            });
        }

        int rowIndex = 0;
        foreach (TagData tag in visibleTags)
        {
            var row = new Panel
            {
                Left = 0,
                Top = rowIndex * 38,
                Width = Math.Max(300, tagList.ClientSize.Width -
                    (tagList.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0) - 4),
                Height = 36,
                BackColor = rowIndex % 2 == 0
                    ? Color.FromArgb(19, 21, 25)
                    : Color.FromArgb(31, 33, 38)
            };
            var check = new CheckBox
            {
                Left = 7,
                Top = 6,
                Width = 24,
                Height = 24,
                Checked = isAssigned(tag),
                BackColor = Color.Transparent
            };
            Color background = ColorPickerForm.ParseColor(tag.ColorHex, Color.FromArgb(89, 102, 115));
            var chip = new ManagerChipLabel
            {
                Left = 38,
                Top = 4,
                Width = row.Width - 46,
                Height = 28,
                Text = tag.Name,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 6, 0),
                BackColor = background,
                ForeColor = ColorPickerForm.GetContrastingText(background),
                BorderColor = Color.White,
                AutoEllipsis = true,
                Cursor = Cursors.Hand
            };

            check.CheckedChanged += (_, _) =>
            {
                if (!rebuilding) onToggle(tag, check.Checked);
            };
            chip.Click += (_, _) => check.Checked = !check.Checked;
            row.Controls.Add(check);
            row.Controls.Add(chip);
            tagList.Controls.Add(row);
            rowIndex++;
        }

        tagList.ResumeLayout(true);
        rebuilding = false;
    }

    private void CreateTag()
    {
        string name = newTagName.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show(this, "태그 이름을 입력하세요.", "태그 추가",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            newTagName.Focus();
            return;
        }

        TagData? tag = onCreate(name);
        if (tag == null)
        {
            MessageBox.Show(this, "태그를 추가하지 못했습니다.", "태그 추가",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        onToggle(tag, true);
        newTagName.Clear();
        RebuildTagRows();
        newTagName.Focus();
    }
}
