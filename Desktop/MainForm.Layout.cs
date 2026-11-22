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
    private readonly List<ModEntry> visibleMods = [];
    private readonly HashSet<string> selectedPackageIds = new(StringComparer.OrdinalIgnoreCase);
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
        details.LinkClicked += (_, e) =>
        {
            if (!Uri.TryCreate(e.LinkText, UriKind.Absolute, out Uri? uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "페이지 열기 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
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
        AddButton(actions, "폴더 바로가기", OpenQuickFolderMoveDialog);
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


    private void OpenQuickFolderMoveDialog()
    {
        if (store == null) return;
        List<string> selectedIds = SelectedIds();
        if (selectedIds.Count == 0)
        {
            MessageBox.Show(this, "폴더를 이동할 모드를 먼저 선택하세요.", "폴더 바로가기");
            return;
        }

        var columns = new List<QuickFolderMoveColumn>();
        if (sidebarTagMode)
        {
            foreach (TagData tag in store.Tags)
            {
                var targets = new List<QuickFolderMoveTarget>();
                foreach (FolderData folder in tag.Folders)
                {
                    int count = store.OrderedMods.Count(mod =>
                        folder.ModIds.Contains(mod.PackageId, StringComparer.OrdinalIgnoreCase));
                    string key = "tag:" + tag.Id + ":" + folder.Id;
                    bool isCurrent = selectedIds.All(id =>
                        folder.ModIds.Contains(id, StringComparer.OrdinalIgnoreCase));
                    targets.Add(new QuickFolderMoveTarget(
                        $"{folder.Name} ({count})",
                        key,
                        isCurrent,
                        () =>
                        {
                            store.AssignToTagFolder(selectedIds, tag, folder);
                            PersistManagerSettings("빠른 태그 폴더 이동");
                            RefreshSidebar();
                            RefreshGameLikeList();
                            SetStatus($"{selectedIds.Count}개 모드를 '{tag.Name}/{folder.Name}'으로 이동했습니다.");
                        }));
                }
                columns.Add(new QuickFolderMoveColumn(tag.Name, targets));
            }
        }
        else
        {
            foreach (CategoryData category in store.Categories)
            {
                var targets = new List<QuickFolderMoveTarget>();
                foreach (FolderData folder in category.Folders)
                {
                    int count = store.OrderedMods.Count(mod =>
                        folder.ModIds.Contains(mod.PackageId, StringComparer.OrdinalIgnoreCase));
                    string key = "category:" + category.Id + ":" + folder.Id;
                    bool isCurrent = selectedIds.All(id =>
                        folder.ModIds.Contains(id, StringComparer.OrdinalIgnoreCase));
                    targets.Add(new QuickFolderMoveTarget(
                        $"{folder.Name} ({count})",
                        key,
                        isCurrent,
                        () =>
                        {
                            store.AssignToCategoryFolder(selectedIds, category, folder);
                            PersistManagerSettings("빠른 카테고리 폴더 이동");
                            RefreshSidebar();
                            RefreshGameLikeList();
                            SetStatus($"{selectedIds.Count}개 모드를 '{category.Name}/{folder.Name}'으로 이동했습니다.");
                        }));
                }
                columns.Add(new QuickFolderMoveColumn(category.Name, targets));
            }
        }

        if (columns.Count == 0) return;
        using var dialog = new QuickFolderMoveForm(
            columns,
            store.FavoriteFolderKeys,
            store.RecentFolderKeys,
            () => PersistManagerSettings("폴더 바로가기 즐겨찾기/최근 기록"));
        dialog.ShowDialog(this);
    }

    private void MainFormKeyDown(object? sender, KeyEventArgs e)
    {
        if (!modRows.ContainsFocus) return;

        if (e.Control && e.KeyCode == Keys.A)
        {
            selectedPackageIds.Clear();
            foreach (ModEntry mod in visibleMods)
                selectedPackageIds.Add(mod.PackageId);
            primarySelectedPackageId = visibleMods.FirstOrDefault()?.PackageId;
            selectionAnchorRow = visibleMods.Count > 0 ? 0 : -1;
            UpdateModRowSelection();
            UpdateDetails();
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down || e.KeyCode == Keys.Home || e.KeyCode == Keys.End)
        {
            if (visibleMods.Count == 0) return;
            int current = visibleMods.FindIndex(mod =>
                mod.PackageId.Equals(primarySelectedPackageId, StringComparison.OrdinalIgnoreCase));
            if (current < 0) current = 0;
            int target = e.KeyCode switch
            {
                Keys.Home => 0,
                Keys.End => visibleMods.Count - 1,
                Keys.Up => Math.Max(0, current - 1),
                _ => Math.Min(visibleMods.Count - 1, current + 1)
            };
            ModEntry mod = visibleMods[target];
            SelectModRow(mod, new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
            Control? rowControl = modRows.Controls.Cast<Control>()
                .FirstOrDefault(control => control is Panel panel &&
                    panel.Tag is ModEntry item &&
                    item.PackageId.Equals(mod.PackageId, StringComparison.OrdinalIgnoreCase));
            if (rowControl != null) modRows.ScrollControlIntoView(rowControl);
            e.Handled = true;
            e.SuppressKeyPress = true;
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
        row.DragEnter += CategoryNavigationDragEnter;
        select.DragEnter += CategoryNavigationDragEnter;
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
        static double Linearize(byte channel)
        {
            double value = channel / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        double luminance = 0.2126 * Linearize(color.R) +
            0.7152 * Linearize(color.G) + 0.0722 * Linearize(color.B);
        return luminance > 0.179 ? Color.Black : Color.White;
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

        string[] preserveSelection = SelectedIds().ToArray();
        visibleMods.Clear();
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

        var visibleIds = new HashSet<string>(
            visibleMods.Select(mod => mod.PackageId), StringComparer.OrdinalIgnoreCase);
        selectedPackageIds.Clear();
        selectedPackageIds.UnionWith(preserveSelection.Where(visibleIds.Contains));
        if (primarySelectedPackageId != null && !visibleIds.Contains(primarySelectedPackageId))
            primarySelectedPackageId = null;
        if (primarySelectedPackageId != null &&
            !selectedPackageIds.Contains(primarySelectedPackageId))
            primarySelectedPackageId = null;
        if (primarySelectedPackageId == null)
        {
            ModEntry? firstSelected = visibleMods.FirstOrDefault(mod => selectedPackageIds.Contains(mod.PackageId));
            ModEntry? firstVisible = firstSelected ?? visibleMods.FirstOrDefault();
            if (firstVisible != null)
            {
                selectedPackageIds.Add(firstVisible.PackageId);
                primarySelectedPackageId = firstVisible.PackageId;
            }
        }
        selectionAnchorRow = primarySelectedPackageId == null
            ? -1
            : visibleMods.FindIndex(mod => mod.PackageId.Equals(primarySelectedPackageId, StringComparison.OrdinalIgnoreCase));
        modRows.ResumeLayout();
        ResizeModRows();
        UpdateModRowSelection();
        UpdateDetails();
        rebuildingRows = false;
    }


    private void SetFolderEnabled(FolderData folder, List<ModEntry> members, bool enabled)
    {
        if (store == null) return;
        foreach (ModEntry mod in members)
        {
            if (mod.Official) continue;
            mod.Active = enabled;
            store.GetState(mod.PackageId).Enabled = enabled;
        }
        RefreshSidebar();
        RefreshGameLikeList();
        SetStatus($"'{folder.Name}' 폴더의 모드를 {(enabled ? "활성화" : "비활성화")}할 예정입니다.");
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
        List<ModEntry> eligibleMembers = members.Where(mod => !mod.Official).ToList();
        int activeEligible = eligibleMembers.Count(mod => mod.Active);
        CheckState folderState = eligibleMembers.Count == 0 || activeEligible == 0
            ? CheckState.Unchecked
            : activeEligible == eligibleMembers.Count ? CheckState.Checked : CheckState.Indeterminate;
        var enabled = new CheckBox
        {
            AutoCheck = false,
            ThreeState = true,
            CheckState = folderState,
            Left = 34,
            Top = 4,
            Width = 20,
            Height = 22,
            Enabled = eligibleMembers.Count > 0,
            BackColor = Color.Transparent
        };
        enabled.Click += (_, _) => SetFolderEnabled(
            folder, eligibleMembers, enabled.CheckState != CheckState.Checked);
        int active = members.Count(m => m.Active || m.Official);
        var title = new Label
        {
            Text = $"{folder.Name} ({active}/{members.Count})",
            Left = 60,
            Top = 4,
            Width = Math.Max(100, row.Width - 68),
            Height = 24,
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold)
        };
        title.MouseDown += (_, e) => HandleFolderHeaderMouseDown(folder, isTagFolder, category, title, e);
        title.MouseMove += HandleFolderHeaderMouseMove;
        row.AllowDrop = true;
        title.AllowDrop = true;
        row.DragEnter += FolderDragEnter;
        title.DragEnter += FolderDragEnter;
        row.DragDrop += (_, e) => DropOnFolder(folder, isTagFolder, category, e);
        title.DragDrop += (_, e) => DropOnFolder(folder, isTagFolder, category, e);
        row.Controls.Add(fold);
        row.Controls.Add(enabled);
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
        enabled.ContextMenuStrip = menu;
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
        visibleMods.Add(mod);
        int rowIndex = visibleMods.Count - 1;

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
        List<TagData> assignedTags = state.TagIds
            .Select(id => store.Tags.FirstOrDefault(tag => tag.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            .Where(tag => tag != null).Cast<TagData>().ToList();
        var meta = new FlowLayoutPanel
        {
            Top = 3,
            Height = 25,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = false,
            AutoScroll = false,
            Padding = Padding.Empty,
            Margin = Padding.Empty,
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand
        };
        foreach (TagData tag in assignedTags)
        {
            Color chipColor = TryParseColor(tag.ColorHex, out Color parsedTagColor)
                ? parsedTagColor : Color.FromArgb(55, 63, 74);
            var chip = new Label
            {
                Text = tag.Name,
                AutoSize = true,
                MaximumSize = new Size(145, 22),
                Height = 22,
                Margin = new Padding(2, 1, 2, 1),
                Padding = new Padding(5, 1, 5, 1),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = chipColor,
                ForeColor = ColorForBackground(null, chipColor),
                TextAlign = ContentAlignment.MiddleCenter,
                AutoEllipsis = true,
                Cursor = Cursors.Hand
            };
            rowToolTip.SetToolTip(chip, tag.Name);
            meta.Controls.Add(chip);
        }
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
        MouseEventHandler rowMouseDown = (source, e) => HandleModRowMouseDown(mod, source, e);
        MouseEventHandler rowMouseMove = HandleModRowMouseMove;
        marker.MouseDown += rowMouseDown;
        marker.MouseMove += rowMouseMove;
        foreach (Control chip in meta.Controls)
        {
            chip.MouseDown += rowMouseDown;
            chip.MouseMove += rowMouseMove;
        }
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
            if (!selectedPackageIds.Contains(mod.PackageId))
                SelectModRow(mod, e, false);
            Control sourceControl = source as Control ?? row;
            contextMenuAnchor = row;
            contextMenuPoint = row.PointToClient(sourceControl.PointToScreen(e.Location));
            GridMouseUp(this, e);
            contextMenuAnchor = null;
        };
        foreach (Control chip in meta.Controls) chip.MouseUp += rightClick;
        marker.MouseUp += rightClick;
        row.MouseDown += rowMouseDown;
        name.MouseDown += rowMouseDown;
        meta.MouseDown += rowMouseDown;
        row.MouseMove += rowMouseMove;
        name.MouseMove += rowMouseMove;
        meta.MouseMove += rowMouseMove;
        row.MouseUp += rightClick;
        name.MouseUp += rightClick;
        meta.MouseUp += rightClick;
        check.MouseUp += rightClick;

        if (selectedPackageIds.Contains(mod.PackageId))
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
        PersistManagerSettings("카테고리 순서 변경");
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
        PersistManagerSettings("폴더 순서 변경");
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

    private void CategoryNavigationDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = ReadDraggedIds(e.Data).Count > 0 ||
            (e.Data != null && e.Data.GetDataPresent(CategoryDragDataFormat))
            ? DragDropEffects.Move : DragDropEffects.None;
    }

    private void ModRowDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = ReadDraggedIds(e.Data).Count > 0 ? DragDropEffects.Move : DragDropEffects.None;
    }

    private void DropOnCategory(CategoryData category, DragEventArgs e)
    {
        if (store == null) return;
        if (e.Data != null && e.Data.GetDataPresent(CategoryDragDataFormat))
        {
            string? sourceId = e.Data.GetData(CategoryDragDataFormat) as string;
            if (!string.IsNullOrWhiteSpace(sourceId)) ReorderCategory(sourceId, category);
            return;
        }
        List<string> ids = ReadDraggedIds(e.Data);
        if (ids.Count == 0) return;
        store.AssignCategory(ids, category.Id);
        PersistManagerSettings("카테고리 이동");
        RefreshGrid();
        SetStatus($"선택한 모드 {ids.Count}개를 '{category.Name}' 카테고리로 이동했습니다.");
    }

    private void DropOnTag(TagData tag, DragEventArgs e)
    {
        if (store == null || tag.IsSystem) return;
        List<string> ids = ReadDraggedIds(e.Data);
        if (ids.Count == 0) return;
        store.AssignTag(ids, tag, true);
        PersistManagerSettings("태그 지정");
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
        PersistManagerSettings("폴더 이동");
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
        int index = visibleMods.FindIndex(item =>
            item.PackageId.Equals(mod.PackageId, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return;

        bool control = applyModifierSelection && (ModifierKeys & Keys.Control) == Keys.Control;
        bool shift = applyModifierSelection && (ModifierKeys & Keys.Shift) == Keys.Shift;
        var selected = new HashSet<string>(selectedPackageIds, StringComparer.OrdinalIgnoreCase);

        if (shift && selectionAnchorRow >= 0 && selectionAnchorRow < visibleMods.Count)
        {
            if (!control) selected.Clear();
            int start = Math.Min(selectionAnchorRow, index);
            int end = Math.Max(selectionAnchorRow, index);
            for (int i = start; i <= end; i++)
                selected.Add(visibleMods[i].PackageId);
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

        selectedPackageIds.Clear();
        selectedPackageIds.UnionWith(selected);
        primarySelectedPackageId = selected.Contains(mod.PackageId)
            ? mod.PackageId
            : visibleMods.FirstOrDefault(item => selected.Contains(item.PackageId))?.PackageId;
        modRows.Focus();
        UpdateModRowSelection();
        UpdateDetails();
    }

    private void UpdateModRowSelection()
    {
        foreach (Control control in modRows.Controls)
        {
            if (control is not Panel panel || panel.Tag is not ModEntry mod) continue;
            panel.BackColor = selectedPackageIds.Contains(mod.PackageId)
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
        return visibleMods.FirstOrDefault(mod => selectedPackageIds.Contains(mod.PackageId));
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

internal sealed class QuickFolderMoveColumn
{
    public string Name { get; }
    public List<QuickFolderMoveTarget> Targets { get; }

    public QuickFolderMoveColumn(string name, List<QuickFolderMoveTarget> targets)
    {
        Name = name;
        Targets = targets;
    }
}

internal sealed class QuickFolderMoveTarget
{
    public string Title { get; }
    public string Key { get; }
    public bool IsCurrent { get; }
    public Action Move { get; }

    public QuickFolderMoveTarget(string title, string key, bool isCurrent, Action move)
    {
        Title = title;
        Key = key;
        IsCurrent = isCurrent;
        Move = move;
    }
}

internal sealed class QuickFolderMoveForm : Form
{
    private readonly List<QuickFolderMoveColumn> columns;
    private readonly List<string> favorites;
    private readonly List<string> recents;
    private readonly Action savePreferences;
    private readonly FlowLayoutPanel columnsHost = new()
    {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents = false,
        Padding = new Padding(8),
        BackColor = Color.FromArgb(23, 26, 31)
    };
    private readonly TextBox search = new()
    {
        Width = 320,
        PlaceholderText = "폴더 이름 검색"
    };

    public QuickFolderMoveForm(
        List<QuickFolderMoveColumn> columns,
        List<string> favorites,
        List<string> recents,
        Action savePreferences)
    {
        this.columns = columns ?? new List<QuickFolderMoveColumn>();
        this.favorites = favorites ?? new List<string>();
        this.recents = recents ?? new List<string>();
        this.savePreferences = savePreferences ?? (() => { });

        Text = "폴더 바로가기";
        Width = 1120;
        Height = 650;
        MinimumSize = new Size(760, 440);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(23, 26, 31);
        ForeColor = Color.Gainsboro;

        var header = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 44,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(10, 7, 8, 4),
            BackColor = Color.FromArgb(35, 39, 46)
        };
        header.Controls.Add(new Label
        {
            Text = "검색",
            AutoSize = true,
            ForeColor = Color.Silver,
            Margin = new Padding(1, 7, 6, 0)
        });
        header.Controls.Add(search);
        var close = new Button
        {
            Text = "닫기",
            Width = 80,
            Height = 28,
            Anchor = AnchorStyles.Right | AnchorStyles.Top,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(54, 63, 74),
            ForeColor = Color.White,
            Margin = new Padding(12, 0, 0, 0)
        };
        close.Click += (_, _) => Close();
        header.Controls.Add(close);

        Controls.Add(columnsHost);
        Controls.Add(header);
        search.TextChanged += (_, _) => RebuildColumns();
        columnsHost.SizeChanged += (_, _) =>
        {
            int height = Math.Max(280, columnsHost.ClientSize.Height - 22);
            foreach (Control column in columnsHost.Controls) column.Height = height;
        };
        Shown += (_, _) => RebuildColumns();
    }

    private void RebuildColumns()
    {
        columnsHost.SuspendLayout();
        foreach (Control old in columnsHost.Controls.Cast<Control>().ToArray()) old.Dispose();
        columnsHost.Controls.Clear();

        string query = search.Text.Trim();
        foreach (QuickFolderMoveColumn column in columns)
        {
            var visibleTargets = column.Targets
                .Where(target => query.Length == 0 ||
                    target.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                .OrderByDescending(target => favorites.Contains(target.Key, StringComparer.OrdinalIgnoreCase))
                .ThenBy(target =>
                {
                    int index = recents.FindIndex(key =>
                        key.Equals(target.Key, StringComparison.OrdinalIgnoreCase));
                    return index < 0 ? int.MaxValue : index;
                })
                .ToList();

            var panel = new Panel
            {
                Width = 300,
                Height = Math.Max(280, columnsHost.ClientSize.Height - 22),
                Margin = new Padding(4),
                BackColor = Color.FromArgb(30, 34, 41),
                BorderStyle = BorderStyle.FixedSingle
            };
            var heading = new Label
            {
                Text = column.Name,
                Dock = DockStyle.Top,
                Height = 34,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(45, 51, 60),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(9, 0, 0, 0)
            };
            var list = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(5),
                BackColor = Color.FromArgb(30, 34, 41)
            };
            panel.Controls.Add(list);
            panel.Controls.Add(heading);

            foreach (QuickFolderMoveTarget target in visibleTargets)
                AddTargetRow(list, target);

            if (visibleTargets.Count == 0)
            {
                list.Controls.Add(new Label
                {
                    Text = query.Length == 0 ? "폴더가 없습니다." : "검색 결과가 없습니다.",
                    Width = 260,
                    Height = 42,
                    ForeColor = Color.Silver,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Margin = new Padding(4, 8, 4, 2)
                });
            }

            columnsHost.Controls.Add(panel);
        }

        columnsHost.ResumeLayout();
    }

    private void AddTargetRow(FlowLayoutPanel list, QuickFolderMoveTarget target)
    {
        var row = new Panel
        {
            Width = 268,
            Height = 33,
            Margin = new Padding(1, 1, 1, 2),
            BackColor = target.IsCurrent
                ? Color.FromArgb(45, 78, 96)
                : Color.FromArgb(38, 43, 51)
        };
        var choose = new Button
        {
            Text = target.Title,
            Left = 2,
            Top = 1,
            Width = 219,
            Height = 30,
            TextAlign = ContentAlignment.MiddleLeft,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.Transparent,
            ForeColor = Color.Gainsboro,
            Padding = new Padding(7, 0, 0, 0),
            FlatAppearance = { BorderSize = 0, MouseOverBackColor = Color.FromArgb(58, 66, 77) }
        };
        var favorite = new Button
        {
            Text = favorites.Contains(target.Key, StringComparer.OrdinalIgnoreCase) ? "★" : "☆",
            Left = 223,
            Top = 1,
            Width = 42,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.Transparent,
            ForeColor = Color.Gold,
            FlatAppearance = { BorderSize = 0 }
        };
        choose.Click += (_, _) =>
        {
            recents.RemoveAll(key => key.Equals(target.Key, StringComparison.OrdinalIgnoreCase));
            recents.Insert(0, target.Key);
            if (recents.Count > 10) recents.RemoveRange(10, recents.Count - 10);
            try
            {
                savePreferences();
                target.Move();
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "폴더 이동 저장 실패",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        favorite.Click += (_, _) =>
        {
            if (favorites.RemoveAll(key => key.Equals(target.Key, StringComparison.OrdinalIgnoreCase)) == 0)
                favorites.Add(target.Key);
            try
            {
                savePreferences();
                RebuildColumns();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "즐겨찾기 저장 실패",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        row.Controls.Add(choose);
        row.Controls.Add(favorite);
        list.Controls.Add(row);
    }
}
