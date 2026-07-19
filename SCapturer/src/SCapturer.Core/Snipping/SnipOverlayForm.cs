using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace SCapturer.Core.Snipping;

internal sealed class SnipOverlayForm : Form
{
    private static readonly Color OverlayColor = Color.FromArgb(118, 0, 0, 0);
    private static readonly Color BorderColor = Color.White;
    private static readonly Color LabelBackground = Color.FromArgb(228, 24, 24, 24);

    private readonly Bitmap _desktopFrame;
    private readonly Bitmap _dimmedFrame;

    private Point _anchor;
    private Rectangle _selection;
    private bool _dragging;
    private bool _cancelRequested;

    public SnipOverlayForm(Bitmap desktopFrame, Rectangle virtualDesktopBounds)
    {
        ArgumentNullException.ThrowIfNull(desktopFrame);

        _desktopFrame = desktopFrame;
        _dimmedFrame = CreateDimmedFrame(desktopFrame);

        AutoScaleMode = AutoScaleMode.None;
        Bounds = virtualDesktopBounds;
        Cursor = Cursors.Cross;
        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.None;
        KeyPreview = true;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;

        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint,
            true);
    }

    public Rectangle SelectedRegion { get; private set; }

    public void RequestCancel()
    {
        _cancelRequested = true;

        if (!IsHandleCreated || IsDisposed)
        {
            return;
        }

        try
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)CancelSelection);
            }
            else
            {
                CancelSelection();
            }
        }
        catch (InvalidOperationException)
        {
            // The overlay is already closing.
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        if (_cancelRequested)
        {
            CancelSelection();
            return;
        }

        Activate();
        BringToFront();
        Focus();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var clip = Rectangle.Intersect(ClientRectangle, e.ClipRectangle);
        if (clip.Width <= 0 || clip.Height <= 0)
        {
            return;
        }

        e.Graphics.DrawImage(
            _dimmedFrame,
            clip,
            clip,
            GraphicsUnit.Pixel);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (_selection.Width <= 0 || _selection.Height <= 0)
        {
            return;
        }

        e.Graphics.DrawImage(
            _desktopFrame,
            _selection,
            _selection,
            GraphicsUnit.Pixel);

        using var shadowPen = new Pen(Color.FromArgb(180, 0, 0, 0), 3);
        using var borderPen = new Pen(BorderColor, 1);

        var border = _selection;
        border.Width = Math.Max(1, border.Width - 1);
        border.Height = Math.Max(1, border.Height - 1);

        e.Graphics.DrawRectangle(shadowPen, border);
        e.Graphics.DrawRectangle(borderPen, border);

        DrawSizeLabel(e.Graphics, _selection);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button == MouseButtons.Right)
        {
            CancelSelection();
            return;
        }

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        _anchor = ClampToClient(e.Location);
        _dragging = true;
        Capture = true;
        UpdateSelection(e.Location);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_dragging)
        {
            UpdateSelection(e.Location);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button != MouseButtons.Left || !_dragging)
        {
            return;
        }

        UpdateSelection(e.Location);
        _dragging = false;
        Capture = false;

        if (_selection.Width < 2 || _selection.Height < 2)
        {
            CancelSelection();
            return;
        }

        SelectedRegion = _selection;
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            CancelSelection();
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Capture = false;
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _dimmedFrame.Dispose();
        }

        base.Dispose(disposing);
    }

    private void UpdateSelection(Point currentPoint)
    {
        var previousSelection = _selection;
        var previousLabel = GetLabelBounds(previousSelection);

        var current = ClampToClient(currentPoint);
        _selection = NormalizeRectangle(_anchor, current);

        var dirty = UnionNonEmpty(
            Inflate(previousSelection, 5),
            Inflate(_selection, 5),
            Inflate(previousLabel, 3),
            Inflate(GetLabelBounds(_selection), 3));

        if (!dirty.IsEmpty)
        {
            Invalidate(Rectangle.Intersect(ClientRectangle, dirty));
        }
    }

    private void DrawSizeLabel(Graphics graphics, Rectangle selection)
    {
        var text = $"{selection.Width} × {selection.Height}";
        var bounds = GetLabelBounds(selection);

        using var background = new SolidBrush(LabelBackground);
        graphics.FillRectangle(background, bounds);

        TextRenderer.DrawText(
            graphics,
            text,
            Font,
            bounds,
            Color.White,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding |
            TextFormatFlags.SingleLine);
    }

    private Rectangle GetLabelBounds(Rectangle selection)
    {
        if (selection.Width <= 0 || selection.Height <= 0)
        {
            return Rectangle.Empty;
        }

        var text = $"{selection.Width} × {selection.Height}";
        var measured = TextRenderer.MeasureText(
            text,
            Font,
            System.Drawing.Size.Empty,
            TextFormatFlags.NoPadding |
            TextFormatFlags.SingleLine);

        const int horizontalPadding = 12;
        const int verticalPadding = 6;
        const int offset = 8;

        var width = measured.Width + horizontalPadding;
        var height = measured.Height + verticalPadding;
        var x = selection.Right - width;
        var y = selection.Bottom + offset;

        if (y + height > ClientSize.Height)
        {
            y = selection.Bottom - height - offset;
        }

        if (y < 0)
        {
            y = Math.Clamp(selection.Top + offset, 0, Math.Max(0, ClientSize.Height - height));
        }

        x = Math.Clamp(x, 0, Math.Max(0, ClientSize.Width - width));

        return new Rectangle(x, y, width, height);
    }

    private void CancelSelection()
    {
        SelectedRegion = Rectangle.Empty;
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private Point ClampToClient(Point point)
    {
        return new Point(
            Math.Clamp(point.X, 0, Math.Max(0, ClientSize.Width - 1)),
            Math.Clamp(point.Y, 0, Math.Max(0, ClientSize.Height - 1)));
    }

    private static Rectangle NormalizeRectangle(Point first, Point second)
    {
        var left = Math.Min(first.X, second.X);
        var top = Math.Min(first.Y, second.Y);
        var right = Math.Max(first.X, second.X);
        var bottom = Math.Max(first.Y, second.Y);

        return Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }

    private static Bitmap CreateDimmedFrame(Bitmap source)
    {
        var dimmed = new Bitmap(
            source.Width,
            source.Height,
            PixelFormat.Format32bppPArgb);

        using var graphics = Graphics.FromImage(dimmed);
        graphics.DrawImageUnscaled(source, 0, 0);

        using var overlayBrush = new SolidBrush(OverlayColor);
        graphics.FillRectangle(
            overlayBrush,
            0,
            0,
            dimmed.Width,
            dimmed.Height);

        return dimmed;
    }

    private static Rectangle Inflate(Rectangle rectangle, int amount)
    {
        if (rectangle.IsEmpty)
        {
            return Rectangle.Empty;
        }

        rectangle.Inflate(amount, amount);
        return rectangle;
    }

    private static Rectangle UnionNonEmpty(params Rectangle[] rectangles)
    {
        var result = Rectangle.Empty;

        foreach (var rectangle in rectangles)
        {
            if (rectangle.IsEmpty)
            {
                continue;
            }

            result = result.IsEmpty
                ? rectangle
                : Rectangle.Union(result, rectangle);
        }

        return result;
    }
}
