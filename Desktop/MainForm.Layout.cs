using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Lilly.ModManager.Desktop;

internal sealed partial class MainForm
{
    private readonly SplitContainer outerSplit = new()
    {
        Dock = DockStyle.Fill,
        Orientation = Orientation.Vertical,
        SplitterDistance = 245,
        Panel1MinSize = 190,
        Panel2MinSize = 700,
        BackColor = Color.FromArgb(23, 26, 31)
    };
    private readonly SplitContainer contentSplit = new()
    {
        Dock = DockStyle.Fill,
        Orientation = Orientation.Vertical,
        SplitterDistance = 430,
        Panel1MinSize = 300,
        Panel2MinSize = 320,
        BackColor = Color.FromArgb(23, 26, 31)
    };
    private readonly FlowLayoutPanel sidebarItems = new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true,
        Padding = new Padding(4),
        BackColor = Color.FromArgb(27, 30, 36)
    };
    private readonly FlowLayoutPanel modRows = new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true,
        Padding = new Padding(7),
        BackColor = Color.FromArgb(25, 28, 34)
    };
    private readonly Label sidebarHeading = new()
    {
        Text = "카테고리",
        Dock = DockStyle.Top,
        Height = 28,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Color.White,
        Font = new Font("Segoe UI", 10f, FontStyle.Bold),
        Padding = new Padding(8, 0, 0, 0)
    };
    private readonly Label modListHeading = new()
    {
        Text = "모드 목록",
        Dock = DockStyle.Top,
        Height = 34,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Color.White,
        Font = new Font("Segoe UI", 10f, FontStyle.Bold),
        Padding = new Padding(10, 0, 0, 0)
    };
    private readonly Label detailHeading = new()
    {
        Text = "모드 정보",
        Dock = DockStyle.Top,
        Height = 38,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Color.White,
        Font = new Font("Segoe UI", 11f, FontStyle.Bold),
        Padding = new Padding(12, 0, 0, 0),
        BackColor = Color.FromArgb(35, 39, 46)
    };
    private readonly Label detailTitle = new()
    {
        Text = "모드를 선택하세요",
        Dock = DockStyle.Top,
        Height = 32,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Color.Gainsboro,
        Font = new Font("Segoe UI", 10f, FontStyle.Bold),
        Padding = new Padding(10, 0, 0, 0),
        BackColor = Color.FromArgb(29, 33, 40)
    };
    private readonly PictureBox previewImage = new()
    {
        Dock = DockStyle.Top,
        Height = 190,
        SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = Color.FromArgb(22, 24, 29),
        BorderStyle = BorderStyle.FixedSingle
    };
    private Image? currentPreviewImage;
    private readonly ToolTip rowToolTip = new()
    {
        AutoPopDelay = 12000,
        InitialDelay = 450,
        ReshowDelay = 120
    };
    private readonly Button categoryModeButton = new() { Text = "카테고리", Width = 94, Height = 28 };
    private readonly Button tagModeButton = new() { Text = "태그", Width = 70, Height = 28 };
    private readonly Button navAddButton = new() { Text = "+", Width = 30, Height = 28 };
    private readonly Button navRenameButton = new() { Text = "이름", Width = 48, Height = 28 };
    private readonly Button navDeleteButton = new() { Text = "삭제", Width = 48, Height = 28 };
    private readonly FlowLayoutPanel navActions = new()
    {
        Dock = DockStyle.Top,
        Height = 34,
        Padding = new Padding(5, 2, 3, 2),
        WrapContents = false,
        BackColor = Color.FromArgb(32, 36, 43)
    };
    private readonly FlowLayoutPanel navModeTabs = new()
    {
        Dock = DockStyle.Top,
        Height = 34,
        Padding = new Padding(5, 2, 3, 2),
        WrapContents = false,
        BackColor = Color.FromArgb(32, 36, 43)
    };
    private bool sidebarTagMode;
    private bool rebuildingNavigation;
    private bool rebuildingRows;
    private string? sidebarSelectedCategoryId;
    private string? sidebarSelectedTagId;
    private string? primarySelectedPackageId;
    private int selectionAnchorRow = -1;
    private Control? contextMenuAnchor;
    private Point contextMenuPoint;
    private const string ModDragDataFormat = "Lilly.ModManager.Desktop.PackageIds";
    private const string CategoryDragDataFormat = "Lilly.ModManager.Desktop.Category";
    private const string FolderDragDataFormat = "Lilly.ModManager.Desktop.Folder";
    private string? dragCandidateCategoryId;
    private string? dragCandidateFolderPayload;
    private string? dragCandidatePackageId;
    private Point dragStartScreenPoint;

    private void BuildGameLikeUi()
    {
        Text = "Lilly - Mod Manager";
        Width = 1500;
        Height = 920;
        MinimumSize = new Size(1120, 700);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(23, 26, 31);
        ForeColor = Color.Gainsboro;
        Icon = LoadIcon();

        // Hidden model grid is retained only as a selection/data adapter for existing actions.
        // It is never added to the visible layout.
        grid.Visible = false;
        grid.Size = new Size(1, 1);
        grid.Location = new Point(-10, -10);
        grid.Columns.Clear();
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "active", HeaderText = "활성" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = "이름" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "category", HeaderText = "카테고리" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "folder", HeaderText = "폴더" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "tags", HeaderText = "태그" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "version", HeaderText = "버전" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "source", HeaderText = "출처" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "issue", HeaderText = "검사" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "package", HeaderText = "PackageId" });
        grid.MultiSelect = true;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.SelectionChanged += (_, _) => UpdateDetails();

        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(9, 6, 5, 4),
            BackColor = Color.FromArgb(35, 39, 46),
            AutoScroll = true
        };
        AddToolbarLabel(top, "검색");
        search.Width = 390;
        top.Controls.Add(search);
        AddToolbarLabel(top, "상태");
        activeFilter.Width = 95;
        activeFilter.Items.Clear();
        activeFilter.Items.AddRange(new object[] { "모두", "활성", "비활성" });
        activeFilter.SelectedIndex = 0;
        top.Controls.Add(activeFilter);
        AddToolbarLabel(top, "설치 위치");
        sourceFilter.Width = 115;
        sourceFilter.Items.Clear();
        sourceFilter.Items.AddRange(new object[] { "전체", "로컬", "창작마당", "공식" });
        sourceFilter.SelectedIndex = 0;
        top.Controls.Add(sourceFilter);

        var sidebar = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(27, 30, 36) };
        navModeTabs.Controls.Add(categoryModeButton);
        navModeTabs.Controls.Add(tagModeButton);
        navActions.Controls.Add(navAddButton);
        navActions.Controls.Add(navRenameButton);
        navActions.Controls.Add(navDeleteButton);
        sidebar.Controls.Add(sidebarItems);
        sidebar.Controls.Add(navActions);
        sidebar.Controls.Add(navModeTabs);
        sidebar.Controls.Add(sidebarHeading);

        var listPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(25, 28, 34) };
        listPanel.Controls.Add(modRows);
        listPanel.Controls.Add(modListHeading);

        var infoPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(28, 31, 37) };
        details.Dock = DockStyle.Fill;
        details.ReadOnly = true;
        details.BackColor = Color.FromArgb(28, 31, 37);
        details.ForeColor = Color.Gainsboro;
        details.BorderStyle = BorderStyle.None;
        details.Font = new Font("Segoe UI", 9.5f);
        details.DetectUrls = true;
        details.WordWrap = true;
        infoPanel.Controls.Add(details);
        infoPanel.Controls.Add(previewImage);
        infoPanel.Controls.Add(detailTitle);
        infoPanel.Controls.Add(detailHeading);

        contentSplit.Panel1.Controls.Add(listPanel);
        contentSplit.Panel2.Controls.Add(infoPanel);
        outerSplit.Panel1.Controls.Add(sidebar);
        outerSplit.Panel2.Controls.Add(contentSplit);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoScroll = true,
            Padding = new Padding(7, 4, 5, 2),
            BackColor = Color.FromArgb(35, 39, 46)
        };
        AddButton(actions, "새로고침", Reload);
        AddButton(actions, "변경 취소", DiscardChanges);
        AddButton(actions, "적용 / 저장", Apply);
        AddButton(actions, "프리셋 저장", SavePreset);
        AddButton(actions, "프리셋 불러오기", LoadPreset);
        AddButton(actions, "자동 정렬", AutoSort);
        AddButton(actions, "위로", () => MoveSelected(-1));
        AddButton(actions, "아래로", () => MoveSelected(1));
        AddButton(actions, "폴더 만들기", CreateFolderForSelected);
        AddButton(actions, "태그 폴더 만들기", CreateTagFolderForSelected);
        AddButton(actions, "카테고리 색상", PickCategoryColor);
        AddButton(actions, "태그 색상", PickTagColor);
        AddButton(actions, "모드 색상", PickModColor);
        AddButton(actions, "모드 색상 해제", ClearModColor);
        AddButton(actions, "경로 설정", ChoosePaths);
        AddButton(actions, "게임 실행", LaunchGame);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.FromArgb(23, 26, 31),
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82f));
        root.Controls.Add(top, 0, 0);
        root.Controls.Add(outerSplit, 0, 1);
        root.Controls.Add(actions, 0, 2);
        status.Dock = DockStyle.Bottom;
        Controls.Add(root);
        Controls.Add(status);

        categoryModeButton.Click += (_, _) => { sidebarTagMode = false; RefreshSidebar(); RefreshGameLikeList(); };
        tagModeButton.Click += (_, _) => { sidebarTagMode = true; if (string.IsNullOrEmpty(sidebarSelectedTagId)) sidebarSelectedTagId = store?.Tags.FirstOrDefault()?.Id; RefreshSidebar(); RefreshGameLikeList(); };
        navAddButton.Click += (_, _) => { if (sidebarTagMode) AddTag(); else AddCategory(); };
        navRenameButton.Click += (_, _) => { if (sidebarTagMode) RenameTag(); else RenameCategory(); };
        navDeleteButton.Click += (_, _) => { if (sidebarTagMode) DeleteTag(); else DeleteCategory(); };
        search.TextChanged += (_, _) => { RefreshSidebar(); RefreshGameLikeList(); };
        activeFilter.SelectedIndexChanged += (_, _) => { RefreshSidebar(); RefreshGameLikeList(); };
        sourceFilter.SelectedIndexChanged += (_, _) => { RefreshSidebar(); RefreshGameLikeList(); };
        modRows.SizeChanged += (_, _) => ResizeModRows();
        sidebarItems.SizeChanged += (_, _) => ResizeSidebarRows();
        KeyPreview = true;
        KeyDown += MainFormKeyDown;
        Shown += (_, _) =>
        {
            try { outerSplit.SplitterDistance = Math.Max(205, (int)(ClientSize.Width * 0.17)); } catch { }
            try { contentSplit.SplitterDistance = Math.Max(330, (int)(contentSplit.Width * 0.36)); } catch { }
        };
        RefreshSidebar();
    }

    private void MainFormKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.A && modRows.ContainsFocus)
        {
            grid.ClearSelection();
            foreach (DataGridViewRow row in grid.Rows) row.Selected = true;
            primarySelectedPackageId = grid.Rows.Count > 0
                ? (grid.Rows[0].Tag as ModEntry)?.PackageId
                : null;
            UpdateModRowSelection();
            UpdateDetails();
            e.Handled = true;
        }
    }

    private void RefreshSidebar()
    {
        if (store == null || sidebarItems.IsDisposed) return;
        rebuildingNavigation = true;
        sidebarItems.SuspendLayout();
        foreach (Control oldControl in sidebarItems.Controls.Cast<Control>().ToArray()) oldControl.Dispose();
        sidebarItems.Controls.Clear();

        categoryModeButton.BackColor = sidebarTagMode ? Color.FromArgb(48, 54, 63) : Color.FromArgb(65, 91, 107);
        tagModeButton.BackColor = sidebarTagMode ? Color.FromArgb(65, 91, 107) : Color.FromArgb(48, 54, 63);
        categoryModeButton.ForeColor = tagModeButton.ForeColor = Color.White;
        navRenameButton.Enabled = sidebarTagMode
            ? store.Tags.Any(t => t.Id == sidebarSelectedTagId && !t.IsSystem)
            : store.Categories.Any(c => c.Id == sidebarSelectedCategoryId);
        navDeleteButton.Enabled = navRenameButton.Enabled && (sidebarTagMode
            ? store.Tags.Any(t => t.Id == sidebarSelectedTagId && !t.IsSystem)
            : store.Categories.Any(c => c.Id == sidebarSelectedCategoryId && !new[] { "before", "core", "normal", "after" }.Contains(c.Id)));

        sidebarHeading.Text = sidebarTagMode ? "태그 목록" : "카테고리 목록";
        if (!sidebarTagMode)
        {
            AddNavigationRow("전체 모드", $"{store.OrderedMods.Count}", null,
                sidebarSelectedCategoryId == null,
                () => SelectAllModsView());

            foreach (CategoryData category in store.Categories)
                AddCategoryNavigationRow(category);
        }
        else
        {
            foreach (TagData tag in store.Tags)
                AddTagNavigationRow(tag);
        }

        sidebarItems.ResumeLayout();
        rebuildingNavigation = false;
    }

    private void AddCategoryNavigationRow(CategoryData category)
    {
        int total = store!.OrderedMods.Count(m => category.ModIds.Contains(m.PackageId, StringComparer.OrdinalIgnoreCase));
        int active = store.OrderedMods.Count(m => category.ModIds.Contains(m.PackageId, StringComparer.OrdinalIgnoreCase) && (m.Active || m.Official));
        Panel row = CreateNavigationPanel(category.Id == sidebarSelectedCategoryId, category.ColorHex);
        var enabled = new CheckBox
        {
            AutoSize = false,
            Width = 22,
            Height = 24,
            Left = 5,
            Top = 4,
            Checked = category.Enabled,
            BackColor = Color.Transparent
        };
        var select = CreateNavigationButton($"({active}/{total}) {category.Name}");
        select.Left = 31;
        select.Width = Math.Max(90, sidebarItems.ClientSize.Width - 48);
        select.Click += (_, _) => SelectCategoryView(category);
        select.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                dragCandidateCategoryId = category.Id;
                dragStartScreenPoint = select.PointToScreen(e.Location);
            }
        };
        select.MouseMove += HandleCategoryMouseMove;
        row.AllowDrop = true;
        select.AllowDrop = true;
        row.DragEnter += NavigationDragEnter;
        select.DragEnter += NavigationDragEnter;
        row.DragDrop += (_, e) => DropOnCategory(category, e);
        select.DragDrop += (_, e) => DropOnCategory(category, e);
        row.DragDrop += (_, e) => DropOnCategory(category, e);
        select.DragDrop += (_, e) => DropOnCategory(category, e);
        enabled.CheckedChanged += (_, _) =>
        {
            if (rebuildingNavigation) return;
            SetCategoryEnabled(category, enabled.Checked);
        };
        select.ForeColor = ColorForBackground(null, row.BackColor);
        row.Controls.Add(enabled);
        row.Controls.Add(select);
        row.ContextMenuStrip = CreateCategoryContextMenu(category);
        select.ContextMenuStrip = row.ContextMenuStrip;
        AddNavigationRowControl(row);
    }

    private void AddTagNavigationRow(TagData tag)
    {
        List<ModEntry> members = store!.OrderedMods
            .Where(mod => tag.Id == "untagged"
                ? store.GetState(mod.PackageId).TagIds.Count == 0
                : store.GetState(mod.PackageId).TagIds.Contains(tag.Id, StringComparer.OrdinalIgnoreCase))
            .ToList();
        int active = members.Count(m => m.Active || m.Official);
        CheckState state = active == 0 ? CheckState.Unchecked :
            active == members.Count ? CheckState.Checked : CheckState.Indeterminate;

        Panel row = CreateNavigationPanel(tag.Id == sidebarSelectedTagId, tag.ColorHex);
        var enabled = new CheckBox
        {
            AutoCheck = false,
            ThreeState = true,
            CheckState = state,
            Width = 22,
            Height = 24,
            Left = 5,
            Top = 4,
            BackColor = Color.Transparent
        };
        var select = CreateNavigationButton($"({active}/{members.Count}) {tag.Name}");
        select.Left = 31;
        select.Width = Math.Max(90, sidebarItems.ClientSize.Width - 48);
        select.Click += (_, _) => SelectTagView(tag);
        row.AllowDrop = true;
        select.AllowDrop = true;
        row.DragEnter += NavigationDragEnter;
        select.DragEnter += NavigationDragEnter;
        row.DragDrop += (_, e) => DropOnTag(tag, e);
        select.DragDrop += (_, e) => DropOnTag(tag, e);
        enabled.Click += (_, _) => SetTagEnabled(tag, enabled.CheckState != CheckState.Checked);
        select.ForeColor = ColorForBackground(null, row.BackColor);
        row.Controls.Add(enabled);
        row.Controls.Add(select);
        if (!tag.IsSystem)
        {
            row.ContextMenuStrip = CreateTagContextMenu(tag);
            select.ContextMenuStrip = row.ContextMenuStrip;
        }
        AddNavigationRowControl(row);
    }

    private Panel CreateNavigationPanel(bool selected, string? colorHex)
    {
        Color back = selected ? Color.FromArgb(45, 78, 96) : Color.FromArgb(31, 35, 42);
        if (!selected && TryParseColor(colorHex, out Color custom)) back = custom;
        return new Panel
        {
            Width = Math.Max(170, sidebarItems.ClientSize.Width - 12),
            Height = 32,
            Margin = new Padding(1, 1, 2, 1),
            BackColor = back,
            BorderStyle = selected ? BorderStyle.FixedSingle : BorderStyle.None
        };
    }

    private Button CreateNavigationButton(string text)
    {
        return new Button
        {
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.Transparent,
            ForeColor = ColorForBackground((Control?)null, Color.FromArgb(31, 35, 42)),
            Font = new Font("Segoe UI", 8.8f, FontStyle.Regular),
            Height = 30,
            Top = 1,
            FlatAppearance = { BorderSize = 0, MouseOverBackColor = Color.FromArgb(58, 66, 77) },
            Padding = new Padding(3, 0, 0, 0)
        };
    }

    private static Color ColorForBackground(Control? control, Color fallback)
    {
        Color color = fallback;
        if (control != null) color = control.BackColor;
        double luminance = (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255.0;
        return luminance > 0.52 ? Color.Black : Color.White;
    }

    private static bool TryParseColor(string? hex, out Color color)
    {
        color = Color.Empty;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        try { color = ColorTranslator.FromHtml(hex); return true; }
        catch { return false; }
    }

    private void AddNavigationRow(string title, string count, string? colorHex, bool selected, Action onClick)
    {
        Panel row = CreateNavigationPanel(selected, colorHex);
        var button = CreateNavigationButton($"{title}  ({count})");
        button.Left = 7;
        button.Width = Math.Max(140, sidebarItems.ClientSize.Width - 22);
        button.Click += (_, _) => onClick();
        button.ForeColor = ColorForBackground(null, row.BackColor);
        row.Controls.Add(button);
        AddNavigationRowControl(row);
    }

    private void AddNavigationRowControl(Control row)
    {
        row.Width = Math.Max(170, sidebarItems.ClientSize.Width - 12);
        sidebarItems.Controls.Add(row);
    }

    private void ResizeSidebarRows()
    {
        int width = Math.Max(170, sidebarItems.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 12);
        foreach (Control control in sidebarItems.Controls)
        {
            control.Width = width;
            foreach (Control child in control.Controls)
                if (child is Button) child.Width = Math.Max(90, width - child.Left - 6);
        }
    }

    private ContextMenuStrip CreateCategoryContextMenu(CategoryData category)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("이름 변경", null, (_, _) => { SelectCategoryView(category); RenameCategory(); });
        menu.Items.Add("색상 변경", null, (_, _) => { SelectCategoryView(category); PickCategoryColor(); });
        if (!new[] { "before", "core", "normal", "after" }.Contains(category.Id))
            menu.Items.Add("삭제", null, (_, _) => { SelectCategoryView(category); DeleteCategory(); });
        return menu;
    }

    private ContextMenuStrip CreateTagContextMenu(TagData tag)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("이름 변경", null, (_, _) => { SelectTagView(tag); RenameTag(); });
        menu.Items.Add("색상 변경", null, (_, _) => { SelectTagView(tag); PickTagColor(); });
        menu.Items.Add("삭제", null, (_, _) => { SelectTagView(tag); DeleteTag(); });
        return menu;
    }

    private void SelectAllModsView()
    {
        sidebarTagMode = false;
        sidebarSelectedCategoryId = null;
        categoryFilter.SelectedItem = "전체";
        sidebarSelectedTagId = null;
        RefreshSidebar();
        RefreshGameLikeList();
    }

    private void SelectCategoryView(CategoryData category)
    {
        sidebarTagMode = false;
        sidebarSelectedCategoryId = category.Id;
        sidebarSelectedTagId = null;
        categoryFilter.SelectedItem = category.Name;
        tagFilter.SelectedItem = "모든 태그";
        RefreshSidebar();
        RefreshGameLikeList();
    }

    private void SelectTagView(TagData tag)
    {
        sidebarTagMode = true;
        sidebarSelectedTagId = tag.Id;
        tagFilter.SelectedItem = tag.IsSystem ? "모든 태그" : tag.Name;
        RefreshSidebar();
        RefreshGameLikeList();
    }

    private void SetCategoryEnabled(CategoryData category, bool enabled)
    {
        if (store == null) return;
        category.Enabled = enabled;
        foreach (ModEntry mod in store.OrderedMods.Where(m => category.ModIds.Contains(m.PackageId, StringComparer.OrdinalIgnoreCase)))
        {
            if (mod.Official) continue;
            mod.Active = enabled;
            store.GetState(mod.PackageId).Enabled = enabled;
        }
        RefreshSidebar();
        RefreshGameLikeList();
        SetStatus($"'{category.Name}' 카테고리의 모드를 {(enabled ? "활성화" : "비활성화")}할 예정입니다.");
    }

    private void SetTagEnabled(TagData tag, bool enabled)
    {
        if (store == null) return;
        foreach (ModEntry mod in store.OrderedMods)
        {
            ModState state = store.GetState(mod.PackageId);
            bool belongs = tag.Id == "untagged"
                ? state.TagIds.Count == 0
                : state.TagIds.Contains(tag.Id, StringComparer.OrdinalIgnoreCase);
            if (!belongs || mod.Official) continue;
            mod.Active = enabled;
            state.Enabled = enabled;
        }
        RefreshSidebar();
        RefreshGameLikeList();
        SetStatus($"'{tag.Name}' 태그의 모드를 {(enabled ? "활성화" : "비활성화")}할 예정입니다.");
    }

    private void RefreshGameLikeList()
    {
        if (store == null || modRows.IsDisposed || rebuildingRows) return;
        rebuildingRows = true;
        store.RefreshIssues();
        string query = search.Text.Trim();
        string stateFilter = activeFilter.SelectedItem?.ToString() ?? "모두";
        string source = sourceFilter.SelectedItem?.ToString() ?? "전체";

        IEnumerable<ModEntry> candidates = store.OrderedMods;
        CategoryData? currentCategory = null;
        TagData? currentTag = null;
        if (sidebarTagMode)
        {
            currentTag = store.Tags.FirstOrDefault(t => t.Id == sidebarSelectedTagId);
            if (currentTag == null)
            {
                currentTag = store.Tags.FirstOrDefault();
                sidebarSelectedTagId = currentTag?.Id;
            }
            if (currentTag != null)
            {
                TagData selected = currentTag;
                candidates = candidates.Where(mod =>
                {
                    List<string> ids = store.GetState(mod.PackageId).TagIds;
                    return selected.Id == "untagged"
                        ? ids.Count == 0
                        : ids.Contains(selected.Id, StringComparer.OrdinalIgnoreCase);
                });
            }
        }
        else if (!string.IsNullOrEmpty(sidebarSelectedCategoryId))
        {
            currentCategory = store.Categories.FirstOrDefault(c => c.Id == sidebarSelectedCategoryId);
            if (currentCategory != null)
            {
                CategoryData selected = currentCategory;
                candidates = candidates.Where(mod => selected.ModIds.Contains(mod.PackageId, StringComparer.OrdinalIgnoreCase));
            }
        }

        List<ModEntry> filtered = candidates.Where(mod =>
        {
            if (stateFilter == "활성" && !mod.Active && !mod.Official) return false;
            if (stateFilter == "비활성" && (mod.Active || mod.Official)) return false;
            if (source == "로컬" && (mod.Workshop || mod.Official)) return false;
            if (source == "창작마당" && (!mod.Workshop || mod.Official)) return false;
            if (source == "공식" && !mod.Official) return false;
            if (query.Length == 0) return true;
            ModState state = store.GetState(mod.PackageId);
            string tags = string.Join(" ", state.TagIds.Select(id => store.Tags.FirstOrDefault(t => t.Id == id)?.Name));
            string haystack = string.Join(" ", mod.Name, state.DisplayName, mod.PackageId, mod.Author,
                mod.Description, mod.Version, currentCategory?.Name, currentTag?.Name, tags);
            return haystack.Contains(query, StringComparison.CurrentCultureIgnoreCase);
        }).ToList();

        string[] preserveSelection = grid.SelectedRows.Cast<DataGridViewRow>()
            .Select(row => (row.Tag as ModEntry)?.PackageId)
            .Where(id => !string.IsNullOrEmpty(id)).Cast<string>().ToArray();
        grid.Rows.Clear();
        modRows.SuspendLayout();
        foreach (Control oldControl in modRows.Controls.Cast<Control>().ToArray()) oldControl.Dispose();
        modRows.Controls.Clear();

        modListHeading.Text = sidebarTagMode
            ? $"태그 모드 목록 · {currentTag?.Name ?? "태그"} ({filtered.Count})"
            : currentCategory == null ? $"전체 모드 ({filtered.Count})" : $"{currentCategory.Name} ({filtered.Count})";

        if (!sidebarTagMode && currentCategory != null)
        {
            AddFolderGroups(filtered, currentCategory.Folders, false, currentCategory);
        }
        else if (sidebarTagMode && currentTag != null)
        {
            AddFolderGroups(filtered, currentTag.Folders, true, null);
        }
        else
        {
            foreach (ModEntry mod in filtered)
                AddModRow(mod);
        }

        if (filtered.Count == 0)
            AddMessageRow(query.Length == 0 ? "표시할 모드가 없습니다." : "검색 결과가 없습니다.");

        var visibleIds = grid.Rows.Cast<DataGridViewRow>()
            .Select(row => (row.Tag as ModEntry)?.PackageId)
            .Where(id => !string.IsNullOrEmpty(id)).Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nextSelection = preserveSelection.Where(visibleIds.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (primarySelectedPackageId != null && !visibleIds.Contains(primarySelectedPackageId))
            primarySelectedPackageId = null;
        grid.ClearSelection();
        foreach (DataGridViewRow row in grid.Rows)
            if (row.Tag is ModEntry mod && nextSelection.Contains(mod.PackageId)) row.Selected = true;
        if (primarySelectedPackageId != null && !nextSelection.Contains(primarySelectedPackageId))
            primarySelectedPackageId = null;
        if (primarySelectedPackageId == null && grid.Rows.Count > 0)
        {
            grid.Rows[0].Selected = true;
            primarySelectedPackageId = (grid.Rows[0].Tag as ModEntry)?.PackageId;
        }
        modRows.ResumeLayout();
        ResizeModRows();
        UpdateModRowSelection();
        UpdateDetails();
        rebuildingRows = false;
    }

    private void AddFolderGroups(List<ModEntry> filtered, List<FolderData> folders, bool isTagFolder, CategoryData? category)
    {
        var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (FolderData folder in folders)
        {
            List<ModEntry> members = filtered.Where(mod => folder.ModIds.Contains(mod.PackageId, StringComparer.OrdinalIgnoreCase)).ToList();
            if (members.Count == 0 && folder.Id != "uncategorized") continue;
            AddFolderHeader(folder, members, isTagFolder, category);
            foreach (ModEntry mod in members)
            {
                shown.Add(mod.PackageId);
                if (!folder.Collapsed) AddModRow(mod);
            }
        }
        foreach (ModEntry mod in filtered.Where(mod => !shown.Contains(mod.PackageId)))
            AddModRow(mod);
    }

    private void AddFolderHeader(FolderData folder, List<ModEntry> members, bool isTagFolder, CategoryData? category)
    {
        var row = new Panel
        {
            Height = 31,
            Width = Math.Max(250, modRows.ClientSize.Width - 28),
            Margin = new Padding(1, 5, 1, 1),
            BackColor = Color.FromArgb(48, 53, 62),
            Tag = folder
        };
        var fold = new Button
        {
            Text = folder.Collapsed ? "▶" : "▼",
            Width = 29,
            Height = 27,
            Left = 2,
            Top = 2,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(55, 62, 72),
            ForeColor = Color.White,
            FlatAppearance = { BorderSize = 0 }
        };
        fold.Click += (_, _) => { folder.Collapsed = !folder.Collapsed; RefreshGameLikeList(); };
        title.MouseDown += (_, e) => HandleFolderHeaderMouseDown(folder, isTagFolder, category, title, e);
        title.MouseMove += HandleFolderHeaderMouseMove;
        row.AllowDrop = true;
        title.AllowDrop = true;
        row.DragEnter += FolderDragEnter;
        title.DragEnter += FolderDragEnter;
        row.DragDrop += (_, e) => DropOnFolder(folder, isTagFolder, category, e);
        title.DragDrop += (_, e) => DropOnFolder(folder, isTagFolder, category, e);
        int active = members.Count(m => m.Active || m.Official);
        var title = new Label
        {
            Text = $"{folder.Name} ({active}/{members.Count})",
            Left = 37,
            Top = 4,
            Width = Math.Max(100, row.Width - 45),
            Height = 24,
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold)
        };
        row.Controls.Add(fold);
        row.Controls.Add(title);
        var menu = new ContextMenuStrip();
        menu.Items.Add("선택 모드를 이 폴더로 이동", null, (_, _) =>
        {
            List<string> ids = SelectedIds();
            if (ids.Count == 0 || store == null) return;
            if (isTagFolder)
            {
                TagData? tag = SelectedTag();
                if (tag != null) store.AssignToTagFolder(ids, tag, folder);
            }
            else if (category != null) store.AssignToCategoryFolder(ids, category, folder);
            RefreshSidebar();
            RefreshGameLikeList();
        });
        menu.Items.Add("선택 항목으로 새 폴더 만들기", null, (_, _) =>
        {
            if (isTagFolder) CreateTagFolderForSelected(); else CreateFolderForSelected();
        });
        menu.Items.Add("이 폴더 이름 변경", null, (_, _) =>
        {
            string? name = Prompt.Show(this, "새 폴더 이름", "폴더 이름 변경");
            if (string.IsNullOrWhiteSpace(name)) return;
            folder.Name = name.Trim();
            RefreshGameLikeList();
        });
        if (folder.Id != "uncategorized")
            menu.Items.Add("이 폴더 삭제", null, (_, _) =>
            {
                if (MessageBox.Show(this, "폴더를 삭제하고 포함된 모드를 '미분류'로 이동할까요?", "폴더 삭제", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                List<FolderData> ownerFolders = isTagFolder
                    ? store?.Tags.FirstOrDefault(tag => tag.Id == sidebarSelectedTagId)?.Folders ?? new List<FolderData>()
                    : category?.Folders ?? new List<FolderData>();
                FolderData? unfiled = ownerFolders.FirstOrDefault(item => item.Id == "uncategorized");
                if (unfiled == null) { unfiled = new FolderData(); ownerFolders.Insert(0, unfiled); }
                foreach (string id in folder.ModIds.ToList())
                    if (!unfiled.ModIds.Contains(id, StringComparer.OrdinalIgnoreCase)) unfiled.ModIds.Add(id);
                ownerFolders.Remove(folder);
                RefreshGameLikeList();
            });
        row.ContextMenuStrip = menu;
        title.ContextMenuStrip = menu;
        AddModListControl(row);
    }

    private void SelectFolderRepresentative(List<ModEntry> members)
    {
        if (members.Count == 0) return;
        SelectModRow(members[0], new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0), false);
    }

    private void AddModRow(ModEntry mod)
    {
        ModState state = store!.GetState(mod.PackageId);
        CategoryData? category = store.Categories.FirstOrDefault(c => c.ModIds.Contains(mod.PackageId, StringComparer.OrdinalIgnoreCase));
        string tagNamesForGrid = string.Join(", ", state.TagIds.Select(id => store.Tags.FirstOrDefault(t => t.Id == id)?.Name).Where(n => !string.IsNullOrWhiteSpace(n)));
        string folderNameForGrid = category?.Folders.FirstOrDefault(f => f.ModIds.Contains(mod.PackageId, StringComparer.OrdinalIgnoreCase))?.Name ?? "미분류";
        grid.Rows.Add(mod.Active || mod.Official,
            string.IsNullOrWhiteSpace(state.DisplayName) ? mod.Name : state.DisplayName,
            category?.Name ?? "미분류", folderNameForGrid, tagNamesForGrid, mod.Version,
            mod.Official ? "공식" : mod.Workshop ? "창작마당" : "로컬", mod.Issue, mod.PackageId);
        int rowIndex = grid.Rows.Count - 1;
        grid.Rows[rowIndex].Tag = mod;

        var row = new Panel
        {
            Height = 31,
            Width = Math.Max(250, modRows.ClientSize.Width - 28),
            Margin = new Padding(1, 0, 1, 1),
            BackColor = rowIndex % 2 == 0 ? Color.FromArgb(31, 35, 42) : Color.FromArgb(36, 40, 48),
            Tag = mod,
            Cursor = Cursors.Hand
        };
        var marker = new Panel
        {
            Dock = DockStyle.Left,
            Width = 4,
            BackColor = TryParseColor(state.ColorHex, out Color modColor)
                ? modColor
                : TryParseColor(category?.ColorHex, out Color categoryColor) ? categoryColor : Color.FromArgb(71, 82, 96)
        };
        var check = new CheckBox
        {
            Left = 8,
            Top = 5,
            Width = 20,
            Height = 22,
            Checked = mod.Active || mod.Official,
            Enabled = !mod.Official,
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand
        };
        var name = new Label
        {
            Left = 34,
            Top = 1,
            Width = 240,
            Height = 29,
            Text = string.IsNullOrWhiteSpace(state.DisplayName) ? mod.Name : state.DisplayName,
            ForeColor = mod.Issue.Length > 0 ? Color.Salmon : Color.Gainsboro,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Cursor = Cursors.Hand
        };
        List<string> tagNames = state.TagIds.Select(id => store.Tags.FirstOrDefault(t => t.Id == id)?.Name)
            .Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().ToList();
        var meta = new Label
        {
            Top = 1,
            Height = 29,
            Text = string.Join(" · ", tagNames),
            ForeColor = Color.Silver,
            TextAlign = ContentAlignment.MiddleRight,
            AutoEllipsis = true,
            Cursor = Cursors.Hand
        };
        row.AllowDrop = true;
        row.DragEnter += ModRowDragEnter;
        row.DragDrop += (_, e) => DropOnModRow(mod, e);
        row.Controls.Add(marker);
        row.Controls.Add(check);
        row.Controls.Add(name);
        row.Controls.Add(meta);
        row.Resize += (_, _) =>
        {
            name.Width = Math.Max(90, row.ClientSize.Width - 250);
            meta.Left = name.Right + 4;
            meta.Width = Math.Max(35, row.ClientSize.Width - meta.Left - 7);
        };
        name.Width = Math.Max(90, row.ClientSize.Width - 250);
        meta.Left = name.Right + 4;
        meta.Width = Math.Max(35, row.ClientSize.Width - meta.Left - 7);
        rowToolTip.SetToolTip(row, $"{mod.Name}\n{mod.PackageId}\n제작자: {mod.Author}\n버전: {mod.Version}\n{mod.Issue}");
        rowToolTip.SetToolTip(name, $"{mod.Name}\n{mod.PackageId}\n{mod.Description}");
        EventHandler selectHandler = (_, _) => SelectModRow(mod, new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
        row.Click += selectHandler;
        marker.Click += selectHandler;
        name.Click += selectHandler;
        meta.Click += selectHandler;
        check.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) SelectModRow(mod, e); };
        check.CheckedChanged += (_, _) =>
        {
            if (rebuildingRows || store == null || mod.Official) return;
            mod.Active = check.Checked;
            store.GetState(mod.PackageId).Enabled = mod.Active;
            SetStatus($"{mod.Name}: {(mod.Active ? "활성화" : "비활성화")} 예정 · '적용 / 저장'을 눌러 반영하세요.");
            RefreshSidebar();
        };
        MouseEventHandler rightClick = (source, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            int selectedIndex = grid.Rows.Cast<DataGridViewRow>().ToList()
                .FindIndex(item => (item.Tag as ModEntry)?.PackageId.Equals(mod.PackageId, StringComparison.OrdinalIgnoreCase) == true);
            if (selectedIndex >= 0 && !grid.Rows[selectedIndex].Selected)
                SelectModRow(mod, e, false);
            Control sourceControl = source as Control ?? row;
            contextMenuAnchor = row;
            contextMenuPoint = row.PointToClient(sourceControl.PointToScreen(e.Location));
            GridMouseUp(grid, e);
            contextMenuAnchor = null;
        };
        row.MouseDown += (source, e) => HandleModRowMouseDown(mod, source, e);
        name.MouseDown += (source, e) => HandleModRowMouseDown(mod, source, e);
        meta.MouseDown += (source, e) => HandleModRowMouseDown(mod, source, e);
        row.MouseMove += HandleModRowMouseMove;
        name.MouseMove += HandleModRowMouseMove;
        meta.MouseMove += HandleModRowMouseMove;
        row.MouseUp += rightClick;
        name.MouseUp += rightClick;
        meta.MouseUp += rightClick;
        check.MouseUp += rightClick;

        if (grid.Rows[rowIndex].Selected)
            row.BackColor = Color.FromArgb(44, 91, 112);
        AddModListControl(row);
    }



    private void HandleCategoryMouseMove(object? sender, MouseEventArgs e)
    {
        if (dragCandidateCategoryId == null || sender is not Control source ||
            (Control.MouseButtons & MouseButtons.Left) != MouseButtons.Left)
            return;
        Point current = source.PointToScreen(e.Location);
        Size threshold = SystemInformation.DragSize;
        if (Math.Abs(current.X - dragStartScreenPoint.X) < threshold.Width / 2 &&
            Math.Abs(current.Y - dragStartScreenPoint.Y) < threshold.Height / 2)
            return;
        string id = dragCandidateCategoryId;
        dragCandidateCategoryId = null;
        var data = new DataObject();
        data.SetData(CategoryDragDataFormat, id);
        source.DoDragDrop(data, DragDropEffects.Move);
    }

    private void ReorderCategory(string sourceId, CategoryData target)
    {
        if (store == null || sourceId.Equals(target.Id, StringComparison.OrdinalIgnoreCase)) return;
        int sourceIndex = store.Categories.FindIndex(item => item.Id.Equals(sourceId, StringComparison.OrdinalIgnoreCase));
        int targetIndex = store.Categories.IndexOf(target);
        if (sourceIndex < 0 || targetIndex < 0) return;
        CategoryData moving = store.Categories[sourceIndex];
        store.Categories.RemoveAt(sourceIndex);
        targetIndex = store.Categories.IndexOf(target);
        store.Categories.Insert(targetIndex, moving);
        RefreshSidebar();
        RefreshGameLikeList();
        SetStatus($"'{moving.Name}' 카테고리 순서를 변경했습니다.");
    }

    private void HandleFolderHeaderMouseDown(FolderData folder, bool isTagFolder,
        CategoryData? category, Control source, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        string ownerId = isTagFolder ? sidebarSelectedTagId ?? string.Empty : category?.Id ?? string.Empty;
        if (ownerId.Length == 0) return;
        dragCandidateFolderPayload = (isTagFolder ? "T" : "C") + "\n" + ownerId + "\n" + folder.Id;
        dragStartScreenPoint = source.PointToScreen(e.Location);
    }

    private void HandleFolderHeaderMouseMove(object? sender, MouseEventArgs e)
    {
        if (dragCandidateFolderPayload == null || sender is not Control source ||
            (Control.MouseButtons & MouseButtons.Left) != MouseButtons.Left)
            return;
        Point current = source.PointToScreen(e.Location);
        Size threshold = SystemInformation.DragSize;
        if (Math.Abs(current.X - dragStartScreenPoint.X) < threshold.Width / 2 &&
            Math.Abs(current.Y - dragStartScreenPoint.Y) < threshold.Height / 2)
            return;
        string payload = dragCandidateFolderPayload;
        dragCandidateFolderPayload = null;
        var data = new DataObject();
        data.SetData(FolderDragDataFormat, payload);
        source.DoDragDrop(data, DragDropEffects.Move);
    }

    private static (bool IsTag, string OwnerId, string FolderId)? ReadFolderDrag(IDataObject? data)
    {
        if (data == null || !data.GetDataPresent(FolderDragDataFormat)) return null;
        string? raw = data.GetData(FolderDragDataFormat) as string;
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string[] parts = raw.Split('\n');
        if (parts.Length != 3 || (parts[0] != "T" && parts[0] != "C")) return null;
        return (parts[0] == "T", parts[1], parts[2]);
    }

    private void FolderDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = ReadFolderDrag(e.Data).HasValue || ReadDraggedIds(e.Data).Count > 0
            ? DragDropEffects.Move : DragDropEffects.None;
    }

    private void ReorderFolder((bool IsTag, string OwnerId, string FolderId) source, FolderData target,
        bool targetIsTag, CategoryData? targetCategory)
    {
        if (store == null || source.IsTag != targetIsTag) return;
        string targetOwnerId = targetIsTag ? sidebarSelectedTagId ?? string.Empty : targetCategory?.Id ?? string.Empty;
        if (!source.OwnerId.Equals(targetOwnerId, StringComparison.OrdinalIgnoreCase)) return;

        List<FolderData>? folders = targetIsTag
            ? store.Tags.FirstOrDefault(tag => tag.Id == targetOwnerId)?.Folders
            : targetCategory?.Folders;
        if (folders == null) return;
        FolderData? moving = folders.FirstOrDefault(folder =>
            folder.Id.Equals(source.FolderId, StringComparison.OrdinalIgnoreCase));
        if (moving == null || moving == target) return;

        int targetIndex = folders.IndexOf(target);
        folders.Remove(moving);
        targetIndex = folders.IndexOf(target);
        folders.Insert(targetIndex, moving);
        RefreshGameLikeList();
        SetStatus($"'{moving.Name}' 폴더 순서를 변경했습니다.");
    }

    private void HandleModRowMouseDown(ModEntry mod, object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        SelectModRow(mod, e);
        if (sender is Control control)
        {
            dragCandidatePackageId = mod.PackageId;
            dragStartScreenPoint = control.PointToScreen(e.Location);
        }
    }

    private void HandleModRowMouseMove(object? sender, MouseEventArgs e)
    {
        if (dragCandidatePackageId == null || sender is not Control source ||
            (Control.MouseButtons & MouseButtons.Left) != MouseButtons.Left)
            return;

        Point current = source.PointToScreen(e.Location);
        Size threshold = SystemInformation.DragSize;
        if (Math.Abs(current.X - dragStartScreenPoint.X) < threshold.Width / 2 &&
            Math.Abs(current.Y - dragStartScreenPoint.Y) < threshold.Height / 2)
            return;

        if (!SelectedIds().Contains(dragCandidatePackageId, StringComparer.OrdinalIgnoreCase))
        {
            ModEntry? candidate = store?.OrderedMods.FirstOrDefault(mod => mod.PackageId == dragCandidatePackageId);
            if (candidate != null) SelectModRow(candidate, e, false);
        }

        string payload = string.Join("\n", SelectedIds());
        if (payload.Length == 0)
        {
            dragCandidatePackageId = null;
            return;
        }

        var data = new DataObject();
        data.SetData(ModDragDataFormat, payload);
        dragCandidatePackageId = null;
        source.DoDragDrop(data, DragDropEffects.Move);
    }

    private static List<string> ReadDraggedIds(IDataObject? data)
    {
        if (data == null || !data.GetDataPresent(ModDragDataFormat)) return new List<string>();
        string? payload = data.GetData(ModDragDataFormat) as string;
        return string.IsNullOrWhiteSpace(payload)
            ? new List<string>()
            : payload.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void NavigationDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = ReadDraggedIds(e.Data).Count > 0 ? DragDropEffects.Move : DragDropEffects.None;
    }

    private void ModRowDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = ReadDraggedIds(e.Data).Count > 0 ? DragDropEffects.Move : DragDropEffects.None;
    }

    private void DropOnCategory(CategoryData category, DragEventArgs e)
    {
        if (store == null) return;
        List<string> ids = ReadDraggedIds(e.Data);
        if (ids.Count == 0) return;
        store.AssignCategory(ids, category.Id);
        RefreshGrid();
        SetStatus($"선택한 모드 {ids.Count}개를 '{category.Name}' 카테고리로 이동했습니다.");
    }

    private void DropOnTag(TagData tag, DragEventArgs e)
    {
        if (store == null || tag.IsSystem) return;
        List<string> ids = ReadDraggedIds(e.Data);
        if (ids.Count == 0) return;
        store.AssignTag(ids, tag, true);
        RefreshGrid();
        SetStatus($"선택한 모드 {ids.Count}개에 '{tag.Name}' 태그를 지정했습니다.");
    }

    private void DropOnFolder(FolderData folder, bool isTagFolder, CategoryData? category, DragEventArgs e)
    {
        if (store == null) return;
        var draggedFolder = ReadFolderDrag(e.Data);
        if (draggedFolder.HasValue)
        {
            ReorderFolder(draggedFolder.Value, folder, isTagFolder, category);
            return;
        }
        List<string> ids = ReadDraggedIds(e.Data);
        if (ids.Count == 0) return;
        if (isTagFolder)
        {
            TagData? tag = SelectedTag();
            if (tag == null) return;
            store.AssignToTagFolder(ids, tag, folder);
        }
        else
        {
            if (category == null) return;
            store.AssignToCategoryFolder(ids, category, folder);
        }
        RefreshGrid();
        SetStatus($"선택한 모드 {ids.Count}개를 '{folder.Name}' 폴더로 이동했습니다.");
    }

    private void DropOnModRow(ModEntry target, DragEventArgs e)
    {
        if (store == null) return;
        List<string> ids = ReadDraggedIds(e.Data);
        var movingIds = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        if (movingIds.Count == 0 || movingIds.Contains(target.PackageId)) return;

        List<ModEntry> moving = store.OrderedMods
            .Where(mod => movingIds.Contains(mod.PackageId) && !mod.Official)
            .ToList();
        if (moving.Count == 0) return;

        store.OrderedMods.RemoveAll(mod => movingIds.Contains(mod.PackageId) && !mod.Official);
        int targetIndex = store.OrderedMods.IndexOf(target);
        if (targetIndex < 0) targetIndex = store.OrderedMods.Count;
        store.OrderedMods.InsertRange(targetIndex, moving);
        RefreshGrid();
        SetStatus($"{moving.Count}개 모드의 불러오기 순서를 변경했습니다.");
    }

    private void SelectModRow(ModEntry mod, MouseEventArgs e, bool applyModifierSelection = true)
    {
        int index = grid.Rows.Cast<DataGridViewRow>()
            .ToList().FindIndex(row => (row.Tag as ModEntry)?.PackageId.Equals(mod.PackageId, StringComparison.OrdinalIgnoreCase) == true);
        if (index < 0) return;

        bool control = applyModifierSelection && (ModifierKeys & Keys.Control) == Keys.Control;
        bool shift = applyModifierSelection && (ModifierKeys & Keys.Shift) == Keys.Shift;
        var selected = grid.SelectedRows.Cast<DataGridViewRow>()
            .Select(row => (row.Tag as ModEntry)?.PackageId)
            .Where(id => !string.IsNullOrEmpty(id)).Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (shift && selectionAnchorRow >= 0)
        {
            if (!control) selected.Clear();
            int start = Math.Min(selectionAnchorRow, index);
            int end = Math.Max(selectionAnchorRow, index);
            for (int i = start; i <= end && i < grid.Rows.Count; i++)
                if (grid.Rows[i].Tag is ModEntry rangeMod) selected.Add(rangeMod.PackageId);
        }
        else if (control)
        {
            if (!selected.Add(mod.PackageId)) selected.Remove(mod.PackageId);
            selectionAnchorRow = index;
        }
        else
        {
            selected.Clear();
            selected.Add(mod.PackageId);
            selectionAnchorRow = index;
        }

        grid.ClearSelection();
        for (int i = 0; i < grid.Rows.Count; i++)
            if (grid.Rows[i].Tag is ModEntry rowMod && selected.Contains(rowMod.PackageId))
                grid.Rows[i].Selected = true;

        primarySelectedPackageId = selected.Contains(mod.PackageId)
            ? mod.PackageId
            : grid.SelectedRows.Count > 0 ? (grid.SelectedRows[0].Tag as ModEntry)?.PackageId : null;
        modRows.Focus();
        UpdateModRowSelection();
        UpdateDetails();
    }

    private void UpdateModRowSelection()
    {
        var selected = grid.SelectedRows.Cast<DataGridViewRow>()
            .Select(row => (row.Tag as ModEntry)?.PackageId)
            .Where(id => !string.IsNullOrEmpty(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (Control control in modRows.Controls)
        {
            if (control is not Panel panel || panel.Tag is not ModEntry mod) continue;
            panel.BackColor = selected.Contains(mod.PackageId)
                ? Color.FromArgb(44, 91, 112)
                : modRows.Controls.GetChildIndex(panel) % 2 == 0
                    ? Color.FromArgb(31, 35, 42)
                    : Color.FromArgb(36, 40, 48);
        }
    }

    private void AddModListControl(Control control)
    {
        control.Width = Math.Max(250, modRows.ClientSize.Width - 28);
        modRows.Controls.Add(control);
    }

    private void AddMessageRow(string message)
    {
        modRows.Controls.Add(new Label
        {
            Text = message,
            Width = Math.Max(250, modRows.ClientSize.Width - 25),
            Height = 42,
            ForeColor = Color.Silver,
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(1, 12, 1, 1)
        });
    }

    private void ResizeModRows()
    {
        int width = Math.Max(250, modRows.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 18);
        foreach (Control control in modRows.Controls) control.Width = width;
    }

    private ModEntry? GetPrimarySelectedMod()
    {
        if (store == null) return null;
        if (!string.IsNullOrEmpty(primarySelectedPackageId))
        {
            ModEntry? primary = store.OrderedMods.FirstOrDefault(mod =>
                mod.PackageId.Equals(primarySelectedPackageId, StringComparison.OrdinalIgnoreCase));
            if (primary != null) return primary;
        }
        return grid.SelectedRows.Count > 0 ? grid.SelectedRows[0].Tag as ModEntry : null;
    }

    private void UpdateDetailsPane()
    {
        ModEntry? mod = GetPrimarySelectedMod();
        if (mod == null)
        {
            detailTitle.Text = "모드를 선택하세요";
            SetPreviewImage(null);
            return;
        }

        ModState state = store?.GetState(mod.PackageId) ?? new ModState();
        detailTitle.Text = string.IsNullOrWhiteSpace(state.DisplayName) ? mod.Name : state.DisplayName;
        string[] candidates =
        {
            Path.Combine(mod.Path, "About", "Preview.png"),
            Path.Combine(mod.Path, "About", "preview.png"),
            Path.Combine(mod.Path, "About", "Preview.jpg"),
            Path.Combine(mod.Path, "About", "preview.jpg"),
            Path.Combine(mod.Path, "About", "Preview.jpeg")
        };
        string? imagePath = candidates.FirstOrDefault(File.Exists);
        if (imagePath == null)
        {
            SetPreviewImage(null);
            return;
        }
        try
        {
            using var stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var image = Image.FromStream(stream);
            SetPreviewImage(new Bitmap(image));
        }
        catch { SetPreviewImage(null); }
    }

    private void SetPreviewImage(Image? image)
    {
        Image? old = currentPreviewImage;
        currentPreviewImage = image;
        previewImage.Image = image;
        if (old != null && !ReferenceEquals(old, image)) old.Dispose();
    }
}
