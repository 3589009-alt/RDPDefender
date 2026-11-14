namespace RdpDefender.Ui;

/// <summary>Таблица с тёмным оформлением и тёмными полосами прокрутки.</summary>
public sealed class DarkGrid : DataGridView
{
    private readonly Font _emptyFont = Theme.Font(10.5f);
    private string _emptyText = "";

    public string EmptyText
    {
        get => _emptyText;
        set
        {
            _emptyText = value ?? "";
            Invalidate();
        }
    }

    public Color EmptyTextColor { get; set; } = Theme.TextDim;

    public DarkGrid()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);

        BackgroundColor = Theme.Surface;
        BorderStyle = BorderStyle.None;
        EnableHeadersVisualStyles = false;
        ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        GridColor = Theme.BorderSoft;
        RowHeadersVisible = false;
        ColumnHeadersHeight = 38;
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        AllowUserToAddRows = false;
        AllowUserToDeleteRows = false;
        AllowUserToResizeRows = false;
        AllowUserToOrderColumns = false;
        ReadOnly = true;
        MultiSelect = true;
        SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        EditMode = DataGridViewEditMode.EditProgrammatically;
        ScrollBars = ScrollBars.Vertical;
        ShowCellErrors = false;
        ShowRowErrors = false;
        RowTemplate.Height = 34;
        ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText;

        ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(13, 19, 15),
            ForeColor = Theme.TextMuted,
            SelectionBackColor = Color.FromArgb(13, 19, 15),
            SelectionForeColor = Theme.TextMuted,
            Font = Theme.Font(8.25f, FontStyle.Bold),
            Alignment = DataGridViewContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 0, 0),
            WrapMode = DataGridViewTriState.False
        };

        DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            SelectionBackColor = Color.FromArgb(24, 58, 40),
            SelectionForeColor = Theme.Text,
            Font = Theme.Font(9.25f),
            Alignment = DataGridViewContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 0, 0),
            WrapMode = DataGridViewTriState.False
        };

        AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(11, 16, 12),
            ForeColor = Theme.Text,
            SelectionBackColor = Color.FromArgb(24, 58, 40),
            SelectionForeColor = Theme.Text
        };

        GridColor = Theme.BorderSoft;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.UseDarkScrollbars(this);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (Rows.Count > 0 || string.IsNullOrWhiteSpace(_emptyText))
            return;

        var top = ColumnHeadersHeight;
        var rect = new Rectangle(0, top, Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height - top));
        TextRenderer.DrawText(
            e.Graphics,
            _emptyText,
            _emptyFont,
            rect,
            EmptyTextColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _emptyFont.Dispose();
        base.Dispose(disposing);
    }
}
