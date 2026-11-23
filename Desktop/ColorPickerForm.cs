using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace Lilly.ModManager.Desktop;

internal sealed class ColorPickerForm : Form
{
    private static readonly string[] Palette =
    [
        "EF5350", "E57373", "C62828", "FF8A80",
        "F06292", "EC407A", "AD1457", "F8BBD0",
        "AB47BC", "BA68C8", "8E24AA", "E1BEE7",
        "7E57C2", "9575CD", "5E35B1", "D1C4E9",
        "5C6BC0", "7986CB", "3949AB", "C5CAE9",
        "42A5F5", "64B5F6", "1565C0", "BBDEFB",
        "26C6DA", "4FC3F7", "00838F", "B2EBF2",
        "26A69A", "4DB6AC", "00796B", "B2DFDB",
        "66BB6A", "81C784", "2E7D32", "C8E6C9",
        "D4E157", "AED581", "827717", "F0F4C3",
        "FFCA28", "FFD54F", "F9A825", "FFF9C4",
        "FFA726", "FFB74D", "EF6C00", "FFE0B2",
        "8D6E63", "A1887F", "5D4037", "D7CCC8",
        "78909C", "90A4AE", "455A64", "CFD8DC"
    ];

    private readonly HsvWheelControl wheel = new() { Size = new Size(224, 224), Margin = new Padding(8) };
    private readonly TrackBar brightness = new() { Minimum = 0, Maximum = 100, TickFrequency = 10, Value = 90, Orientation = Orientation.Vertical, Height = 210, Width = 48 };
    private readonly TextBox hexInput = new() { Width = 104, MaxLength = 7 };
    private readonly TextBox redInput = new() { Width = 48, MaxLength = 3 };
    private readonly TextBox greenInput = new() { Width = 48, MaxLength = 3 };
    private readonly TextBox blueInput = new() { Width = 48, MaxLength = 3 };
    private readonly Panel preview = new() { Width = 72, Height = 34, BorderStyle = BorderStyle.FixedSingle };
    private readonly Label hexLabel = new() { AutoSize = true, Text = "HEX", ForeColor = Color.Gainsboro, Margin = new Padding(4, 8, 2, 0) };
    private readonly Label rgbLabel = new() { AutoSize = true, Text = "RGB", ForeColor = Color.Gainsboro, Margin = new Padding(10, 8, 2, 0) };
    private readonly ToolTip paletteToolTip = new();

    private Color? selectedColor;
    private float hue;
    private float saturation = 0.72f;
    private float value = 0.9f;
    private bool updatingInputs;

    public Color? SelectedColor => selectedColor;

    private ColorPickerForm(string? initialHex)
    {
        Text = "색상 선택";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = Color.FromArgb(29, 32, 38);
        ForeColor = Color.Gainsboro;
        Font = new Font("Segoe UI", 9f);
        ClientSize = new Size(560, 610);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12),
            BackColor = BackColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));

        var details = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 6, 0, 0),
            BackColor = BackColor
        };
        details.Controls.Add(preview);
        details.Controls.Add(hexLabel);
        details.Controls.Add(hexInput);
        details.Controls.Add(rgbLabel);
        details.Controls.Add(redInput);
        details.Controls.Add(greenInput);
        details.Controls.Add(blueInput);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = BackColor,
            Padding = new Padding(0, 3, 0, 0)
        };
        AddAction(actions, "휘도 랜덤", RandomizeLuminance);
        AddAction(actions, "색만 랜덤", RandomizeColorOnly);
        AddAction(actions, "랜덤", () => SetColor(ParseColor(RandomPaletteHex(), Color.Gray)));
        AddAction(actions, "색상 없음", ClearColor);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var wheelPage = new TabPage("색상 휠") { BackColor = BackColor, ForeColor = ForeColor };
        var wheelLayout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(14, 12, 0, 0),
            BackColor = BackColor
        };
        wheelLayout.Controls.Add(wheel);
        var brightnessPanel = new Panel { Width = 110, Height = 236, Margin = new Padding(10, 8, 0, 0) };
        brightnessPanel.Controls.Add(new Label
        {
            Text = "밝기",
            Dock = DockStyle.Top,
            Height = 22,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = ForeColor
        });
        brightness.Top = 25;
        brightness.Left = 30;
        brightnessPanel.Controls.Add(brightness);
        wheelLayout.Controls.Add(brightnessPanel);
        wheelPage.Controls.Add(wheelLayout);

        var palettePage = new TabPage("팔레트") { BackColor = BackColor, ForeColor = ForeColor };
        var paletteGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 7 * 43 + 12,
            ColumnCount = 8,
            RowCount = 7,
            Padding = new Padding(10),
            BackColor = BackColor
        };
        for (int column = 0; column < 8; column++)
            paletteGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12.5f));
        for (int row = 0; row < 7; row++)
            paletteGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 43f));
        for (int index = 0; index < Palette.Length; index++)
        {
            string hex = Palette[index];
            int row = index / 8;
            int column = index % 8;
            Color color = ParseColor(hex, Color.Gray);
            var swatch = new Button
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(3),
                BackColor = color,
                ForeColor = GetContrastingText(color),
                Text = "Aa",
                FlatStyle = FlatStyle.Flat,
                Tag = hex,
                UseVisualStyleBackColor = false
            };
            swatch.FlatAppearance.BorderColor = Color.FromArgb(190, 190, 190);
            swatch.FlatAppearance.BorderSize = 1;
            swatch.Click += (_, _) => SetColor(color);
            paletteToolTip.SetToolTip(swatch, "#" + hex);
            paletteGrid.Controls.Add(swatch, column, row);
        }
        palettePage.Controls.Add(paletteGrid);
        tabs.TabPages.Add(wheelPage);
        tabs.TabPages.Add(palettePage);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = BackColor,
            Padding = new Padding(0, 5, 0, 0)
        };
        var ok = new Button { Text = "확인", Width = 100, Height = 30, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "취소", Width = 100, Height = 30, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);

        root.Controls.Add(details, 0, 0);
        root.Controls.Add(actions, 0, 1);
        root.Controls.Add(tabs, 0, 2);
        root.Controls.Add(buttons, 0, 3);
        Controls.Add(root);

        wheel.SelectionChanged += (_, _) =>
        {
            hue = wheel.Hue;
            saturation = wheel.Saturation;
            UpdateSelectedFromHsv();
        };
        brightness.ValueChanged += (_, _) =>
        {
            value = brightness.Value / 100f;
            UpdateSelectedFromHsv();
        };
        hexInput.TextChanged += (_, _) => UpdateFromHexInput();
        redInput.TextChanged += (_, _) => UpdateFromRgbInputs();
        greenInput.TextChanged += (_, _) => UpdateFromRgbInputs();
        blueInput.TextChanged += (_, _) => UpdateFromRgbInputs();

        AcceptButton = ok;
        CancelButton = cancel;
        ok.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };
        cancel.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        if (TryParseHex(initialHex, out Color initial))
            SetColor(initial);
        else
            ClearColor();
    }

    public static bool TryPickColor(IWin32Window owner, string? currentHex, out Color? selected)
    {
        using var dialog = new ColorPickerForm(currentHex);
        if (dialog.ShowDialog(owner) == DialogResult.OK)
        {
            selected = dialog.SelectedColor;
            return true;
        }

        selected = null;
        return false;
    }

    public static string RandomPaletteHex()
    {
        return Palette[Random.Shared.Next(Palette.Length)];
    }

    public static Color ParseColor(string? value, Color fallback)
    {
        if (TryParseHex(value, out Color parsed))
            return parsed;
        try
        {
            if (!string.IsNullOrWhiteSpace(value))
                return ColorTranslator.FromHtml(value.Trim());
        }
        catch
        {
            // Unknown values use the supplied fallback color.
        }
        return fallback;
    }

    public static Color GetContrastingText(Color background)
    {
        return RelativeLuminance(background) > 0.179f ? Color.Black : Color.White;
    }

    private static bool TryParseHex(string? text, out Color color)
    {
        string value = (text ?? "").Trim().TrimStart('#');
        color = Color.Empty;
        if (value.Length is not (6 or 8)) return false;
        if (!uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint parsed))
            return false;
        if (value.Length == 6)
        {
            color = Color.FromArgb((int)(parsed >> 16) & 255, (int)(parsed >> 8) & 255, (int)parsed & 255);
            return true;
        }
        color = Color.FromArgb((int)(parsed >> 24) & 255, (int)(parsed >> 16) & 255,
            (int)(parsed >> 8) & 255, (int)parsed & 255);
        return true;
    }

    private static string ToHex(Color color) =>
        color.R.ToString("X2", CultureInfo.InvariantCulture) +
        color.G.ToString("X2", CultureInfo.InvariantCulture) +
        color.B.ToString("X2", CultureInfo.InvariantCulture);

    private static float Linear(float channel)
    {
        float value = channel / 255f;
        return value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);
    }

    private static float RelativeLuminance(Color color) =>
        0.2126f * Linear(color.R) + 0.7152f * Linear(color.G) + 0.0722f * Linear(color.B);

    private static Color HsvToRgb(float h, float s, float v)
    {
        h = (h % 1f + 1f) % 1f;
        s = Math.Clamp(s, 0f, 1f);
        v = Math.Clamp(v, 0f, 1f);
        float chroma = v * s;
        float x = chroma * (1f - Math.Abs((h * 6f) % 2f - 1f));
        float m = v - chroma;
        (float r, float g, float b) = h switch
        {
            < 1f / 6f => (chroma, x, 0f),
            < 2f / 6f => (x, chroma, 0f),
            < 3f / 6f => (0f, chroma, x),
            < 4f / 6f => (0f, x, chroma),
            < 5f / 6f => (x, 0f, chroma),
            _ => (chroma, 0f, x)
        };
        return Color.FromArgb(255,
            (int)Math.Round((r + m) * 255f),
            (int)Math.Round((g + m) * 255f),
            (int)Math.Round((b + m) * 255f));
    }

    private static void RgbToHsv(Color color, out float h, out float s, out float v)
    {
        float r = color.R / 255f;
        float g = color.G / 255f;
        float b = color.B / 255f;
        float max = Math.Max(r, Math.Max(g, b));
        float min = Math.Min(r, Math.Min(g, b));
        float delta = max - min;
        h = 0f;
        if (delta > 0.00001f)
        {
            if (max == r) h = ((g - b) / delta) % 6f;
            else if (max == g) h = (b - r) / delta + 2f;
            else h = (r - g) / delta + 4f;
            h /= 6f;
            if (h < 0f) h += 1f;
        }
        s = max <= 0.00001f ? 0f : delta / max;
        v = max;
    }

    private void AddAction(FlowLayoutPanel panel, string text, Action action)
    {
        var button = new Button
        {
            Text = text,
            Width = 124,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(45, 50, 59),
            ForeColor = Color.Gainsboro,
            Margin = new Padding(2)
        };
        button.Click += (_, _) => action();
        panel.Controls.Add(button);
    }

    private void SetColor(Color color)
    {
        selectedColor = Color.FromArgb(255, color.R, color.G, color.B);
        RgbToHsv(selectedColor.Value, out hue, out saturation, out value);
        wheel.SetSelection(hue, saturation);
        brightness.Value = Math.Clamp((int)Math.Round(value * 100f), brightness.Minimum, brightness.Maximum);
        UpdateInputsAndPreview();
    }

    private void ClearColor()
    {
        selectedColor = null;
        preview.BackColor = Color.FromArgb(55, 59, 66);
        wheel.SetSelection(hue, saturation);
        updatingInputs = true;
        hexInput.Text = "";
        redInput.Text = "";
        greenInput.Text = "";
        blueInput.Text = "";
        updatingInputs = false;
    }

    private void UpdateSelectedFromHsv()
    {
        SetColor(HsvToRgb(hue, saturation, value));
    }

    private void UpdateFromHexInput()
    {
        if (updatingInputs) return;
        string value = hexInput.Text.Trim();
        if (value.Length > 0 && TryParseHex(value, out Color parsed))
            SetColor(parsed);
    }

    private void UpdateFromRgbInputs()
    {
        if (updatingInputs) return;
        if (byte.TryParse(redInput.Text, out byte red) &&
            byte.TryParse(greenInput.Text, out byte green) &&
            byte.TryParse(blueInput.Text, out byte blue))
            SetColor(Color.FromArgb(red, green, blue));
    }

    private void UpdateInputsAndPreview()
    {
        if (!selectedColor.HasValue) return;
        Color color = selectedColor.Value;
        preview.BackColor = color;
        updatingInputs = true;
        hexInput.Text = ToHex(color);
        redInput.Text = color.R.ToString(CultureInfo.InvariantCulture);
        greenInput.Text = color.G.ToString(CultureInfo.InvariantCulture);
        blueInput.Text = color.B.ToString(CultureInfo.InvariantCulture);
        updatingInputs = false;
    }

    private void RandomizeLuminance()
    {
        float target = (float)(Random.Shared.NextDouble() * 0.9 + 0.05);
        Color result = ColorAtLuminance(hue, saturation, target);
        SetColor(result);
    }

    private void RandomizeColorOnly()
    {
        float target = selectedColor.HasValue ? RelativeLuminance(selectedColor.Value) : 0.35f;
        float randomHue = (float)Random.Shared.NextDouble();
        float randomSaturation = (float)(Random.Shared.NextDouble() * 0.55 + 0.45);
        SetColor(ColorAtLuminance(randomHue, randomSaturation, target));
    }

    private static Color ColorAtLuminance(float requestedHue, float requestedSaturation, float targetLuminance)
    {
        targetLuminance = Math.Clamp(targetLuminance, 0f, 1f);
        if (targetLuminance <= 0.0001f) return Color.Black;
        if (targetLuminance >= 0.9999f) return Color.White;

        float saturation = Math.Clamp(requestedSaturation, 0f, 1f);
        if (RelativeLuminance(HsvToRgb(requestedHue, saturation, 1f)) < targetLuminance)
        {
            float lowSaturation = 0f;
            float highSaturation = saturation;
            for (int i = 0; i < 18; i++)
            {
                float middle = (lowSaturation + highSaturation) * 0.5f;
                if (RelativeLuminance(HsvToRgb(requestedHue, middle, 1f)) >= targetLuminance)
                    lowSaturation = middle;
                else
                    highSaturation = middle;
            }
            saturation = lowSaturation;
        }

        float lowValue = 0f;
        float highValue = 1f;
        for (int i = 0; i < 20; i++)
        {
            float middle = (lowValue + highValue) * 0.5f;
            if (RelativeLuminance(HsvToRgb(requestedHue, saturation, middle)) < targetLuminance)
                lowValue = middle;
            else
                highValue = middle;
        }
        return HsvToRgb(requestedHue, saturation, (lowValue + highValue) * 0.5f);
    }
}

internal sealed class HsvWheelControl : Control
{
    private Bitmap? wheelBitmap;
    private bool dragging;
    private float hue;
    private float saturation = 0.72f;

    public event EventHandler? SelectionChanged;
    public float Hue => hue;
    public float Saturation => saturation;

    public HsvWheelControl()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.FromArgb(29, 32, 38);
        RebuildBitmap();
    }

    public void SetSelection(float newHue, float newSaturation)
    {
        hue = (newHue % 1f + 1f) % 1f;
        saturation = Math.Clamp(newSaturation, 0f, 1f);
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        RebuildBitmap();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (wheelBitmap == null)
            RebuildBitmap();
        if (wheelBitmap != null)
        {
            Rectangle destination = GetWheelBounds();
            e.Graphics.DrawImage(wheelBitmap, destination);
            float radius = destination.Width / 2f;
            float angle = hue * MathF.PI * 2f;
            float markerRadius = saturation * radius;
            float x = destination.Left + radius + MathF.Cos(angle) * markerRadius;
            float y = destination.Top + radius + MathF.Sin(angle) * markerRadius;
            using var blackPen = new Pen(Color.Black, 3f);
            using var whitePen = new Pen(Color.White, 1f);
            e.Graphics.DrawEllipse(blackPen, x - 7f, y - 7f, 14f, 14f);
            e.Graphics.DrawEllipse(whitePen, x - 5f, y - 5f, 10f, 10f);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        dragging = true;
        Capture = true;
        UpdateFromMouse(e.Location);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragging && e.Button == MouseButtons.Left)
            UpdateFromMouse(e.Location);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        dragging = false;
        Capture = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            wheelBitmap?.Dispose();
            wheelBitmap = null;
        }
        base.Dispose(disposing);
    }

    private Rectangle GetWheelBounds()
    {
        int size = Math.Max(1, Math.Min(ClientSize.Width, ClientSize.Height) - 8);
        return new Rectangle((ClientSize.Width - size) / 2, (ClientSize.Height - size) / 2, size, size);
    }

    private void RebuildBitmap()
    {
        wheelBitmap?.Dispose();
        int size = Math.Max(1, Math.Min(ClientSize.Width, ClientSize.Height) - 8);
        if (size <= 1) return;
        wheelBitmap = new Bitmap(size, size);
        float radius = size / 2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - radius) / radius;
                float dy = (y + 0.5f - radius) / radius;
                float distance = MathF.Sqrt(dx * dx + dy * dy);
                if (distance > 1f)
                {
                    wheelBitmap.SetPixel(x, y, Color.Transparent);
                    continue;
                }
                float h = (MathF.Atan2(dy, dx) / (MathF.PI * 2f) + 1f) % 1f;
                float s = distance;
                wheelBitmap.SetPixel(x, y, HsvToRgb(h, s, 1f));
            }
        }
    }

    private void UpdateFromMouse(Point point)
    {
        Rectangle bounds = GetWheelBounds();
        float radius = bounds.Width / 2f;
        float dx = point.X - (bounds.Left + radius);
        float dy = point.Y - (bounds.Top + radius);
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        if (distance > radius)
        {
            float scale = radius / distance;
            dx *= scale;
            dy *= scale;
            distance = radius;
        }
        hue = (MathF.Atan2(dy, dx) / (MathF.PI * 2f) + 1f) % 1f;
        saturation = Math.Clamp(distance / radius, 0f, 1f);
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private static Color HsvToRgb(float h, float s, float v)
    {
        h = (h % 1f + 1f) % 1f;
        float chroma = v * s;
        float x = chroma * (1f - Math.Abs((h * 6f) % 2f - 1f));
        float m = v - chroma;
        (float r, float g, float b) = h switch
        {
            < 1f / 6f => (chroma, x, 0f),
            < 2f / 6f => (x, chroma, 0f),
            < 3f / 6f => (0f, chroma, x),
            < 4f / 6f => (0f, x, chroma),
            < 5f / 6f => (x, 0f, chroma),
            _ => (chroma, 0f, x)
        };
        return Color.FromArgb(255, (int)((r + m) * 255f), (int)((g + m) * 255f), (int)((b + m) * 255f));
    }
}
