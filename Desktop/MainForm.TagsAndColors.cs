using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Lilly.ModManager.Desktop;

internal sealed partial class MainForm
{
    private void OpenTagSelectionDialog(IEnumerable<string> packageIds)
    {
        if (store == null) return;
        List<string> ids = packageIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ids.Count == 0) return;

        using var dialog = new TagSelectionDialog(
            store.Tags,
            tag => ids.All(id => store.GetState(id).TagIds.Contains(tag.Id, StringComparer.OrdinalIgnoreCase)),
            (tag, add) =>
            {
                store.AssignTag(ids, tag, add);
                PersistManagerSettings("태그 지정");
                RefreshSidebar();
                RefreshGameLikeList();
            },
            name =>
            {
                TagData? existing = store.Tags.FirstOrDefault(tag =>
                    tag.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase));
                if (existing != null) return existing;

                var created = new TagData
                {
                    Id = "tag-" + Guid.NewGuid().ToString("N"),
                    Name = name,
                    ColorHex = ColorPickerForm.RandomPaletteHex(),
                    Folders = [new FolderData()]
                };
                store.Tags.Add(created);
                PersistManagerSettings("태그 추가");
                RefreshSidebar();
                return created;
            });

        dialog.ShowDialog(this);
    }

    private List<Label> CreateModWarningChips(ModEntry mod)
    {
        var warnings = new List<(string Label, Color Color, string Tooltip)>();
        if (mod.HasMissingRequiredMod)
        {
            string missing = string.Join(", ", mod.Required.Where(id =>
                !store!.OrderedMods.Any(item => item.PackageId.Equals(id, StringComparison.OrdinalIgnoreCase) &&
                    (item.Active || item.Official))));
            warnings.Add(("필수 누락", Color.FromArgb(255, 71, 71),
                "필수 모드가 없거나 비활성화되어 있습니다." +
                (missing.Length > 0 ? Environment.NewLine + missing : "")));
        }
        if (mod.HasActiveIncompatibleMod)
        {
            string conflicts = string.Join(", ", mod.IncompatibleWith.Where(id =>
                store!.OrderedMods.Any(item => item.PackageId.Equals(id, StringComparison.OrdinalIgnoreCase) &&
                    (item.Active || item.Official))));
            warnings.Add(("충돌", Color.FromArgb(255, 106, 66),
                "활성 모드와 충돌하는 모드입니다." +
                (conflicts.Length > 0 ? Environment.NewLine + conflicts : "")));
        }
        if (mod.HasLoadOrderIssue)
        {
            warnings.Add(("로드 순서", Color.FromArgb(255, 140, 61),
                "현재 활성 순서가 선후행 조건과 충돌합니다."));
        }
        if (mod.HasVersionMismatch)
        {
            warnings.Add(("버전 불일치", Color.FromArgb(255, 175, 56),
                "현재 게임 버전과 호환되는 항목이 지원 버전 목록에 없습니다." +
                (mod.SupportedVersions.Count > 0
                    ? Environment.NewLine + "지원 버전: " + string.Join(", ", mod.SupportedVersions)
                    : "")));
        }
        if (mod.HasOutdatedForGameVersion)
        {
            warnings.Add(("게임 버전 낮음", Color.FromArgb(255, 209, 51),
                "이 모드는 현재 게임 버전보다 낮은 버전만 지원합니다." +
                Environment.NewLine + "지원 버전: " + string.Join(", ", mod.SupportedVersions)));
        }
        if (mod.HasMissingFolder)
        {
            warnings.Add(("폴더 없음", Color.FromArgb(255, 71, 71),
                "모드 설치 폴더를 찾을 수 없습니다." +
                (string.IsNullOrWhiteSpace(mod.Path) ? "" : Environment.NewLine + mod.Path)));
        }

        var result = new List<Label>(warnings.Count);
        foreach ((string label, Color color, string tooltip) in warnings)
        {
            int width = Math.Clamp(TextRenderer.MeasureText(label, new Font("Segoe UI", 7.5f)).Width + 12, 34, 112);
            var chip = new ManagerChipLabel
            {
                Text = label,
                Width = width,
                Height = 22,
                Margin = new Padding(2, 1, 2, 1),
                Padding = new Padding(4, 0, 4, 0),
                BackColor = Color.Black,
                ForeColor = color,
                BorderColor = color,
                TextAlign = ContentAlignment.MiddleCenter,
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 7.5f),
                Tag = tooltip
            };
            rowToolTip.SetToolTip(chip, tooltip);
            result.Add(chip);
        }
        return result;
    }

    private Label CreateModTagChip(TagData tag)
    {
        Color background = ColorPickerForm.ParseColor(tag.ColorHex, Color.FromArgb(89, 102, 115));
        int width = Math.Clamp(TextRenderer.MeasureText(tag.Name, new Font("Segoe UI", 7.5f)).Width + 12, 34, 112);
        var chip = new ManagerChipLabel
        {
            Text = tag.Name,
            Width = width,
            Height = 22,
            Margin = new Padding(2, 1, 2, 1),
            Padding = new Padding(4, 0, 4, 0),
            BackColor = background,
            ForeColor = ColorPickerForm.GetContrastingText(background),
            BorderColor = Color.White,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = true,
            Font = new Font("Segoe UI", 7.5f)
        };
        rowToolTip.SetToolTip(chip, tag.Name);
        return chip;
    }

    private Label CreateTagOverflowChip()
    {
        return new ManagerChipLabel
        {
            Text = "…",
            Width = 24,
            Height = 22,
            Margin = new Padding(2, 1, 2, 1),
            BackColor = Color.FromArgb(61, 61, 61),
            ForeColor = Color.White,
            BorderColor = Color.White,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            Tag = "overflow",
            Visible = false
        };
    }
}

internal sealed class ManagerChipLabel : Label
{
    public Color BorderColor { get; set; } = Color.White;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(BorderColor);
        e.Graphics.DrawRectangle(pen, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
    }
}
