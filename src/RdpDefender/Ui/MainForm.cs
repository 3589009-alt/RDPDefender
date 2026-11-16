using System.Drawing.Drawing2D;
using System.ServiceProcess;
using RdpDefender.Configuration;
using RdpDefender.Core;
using RdpDefender.Infrastructure;
using RdpDefender.Ipc;
using RdpDefender.Service;
using ServiceInstaller = RdpDefender.Service.ServiceInstaller;

namespace RdpDefender.Ui;

public sealed partial class MainForm : Form
{
    private readonly bool _preview;
    private readonly ControlClient _client;
    private readonly System.Windows.Forms.Timer _pollTimer;
    private readonly Font _cellBold;

    private GuardStatus? _status;
    private ServiceControllerStatus? _serviceStatus;
    private bool _connected;
    private bool _polling;
    private bool _busy;
    private bool _exiting;
    private bool _settingsLoaded;
    private bool _trayHintShown;
    private int _lastBlockCount = -1;
    private string _blocksSignature = "";
    private string _attemptsSignature = "";
    private string _journalSignature = "";
    private string _loadedWhitelist = "";
    private DateTime _lastServiceCheck = DateTime.MinValue;

    private StatusPill _statusPill = null!;
    private StatCard _cardActive = null!;
    private StatCard _cardAttempts = null!;
    private StatCard _cardTotal = null!;
    private StatCard _cardMode = null!;
    private readonly List<NavButton> _nav = new();
    private Panel _content = null!;
    private Panel[] _pages = Array.Empty<Panel>();
    private Panel _pageBlocks = null!;
    private Panel _pageAttempts = null!;
    private Panel _pageSuccess = null!;
    private Panel _pageJournal = null!;
    private Panel _pageSettings = null!;
    private Panel _pageService = null!;

    private DarkGrid _blocksGrid = null!;
    private DarkGrid _attemptsGrid = null!;
    private DarkGrid _successGrid = null!;
    private DarkGrid _journalGrid = null!;
    private AttackerDetailsPanel _blocksDetails = null!;
    private AttackerDetailsPanel _attemptsDetails = null!;
    private readonly WhoisLookup _whoisLookup = new();
    private CancellationTokenSource? _blocksWhoisCts;
    private CancellationTokenSource? _attemptsWhoisCts;
    private NumberInput _attemptsLimit = null!;
    private Label _attemptsInfo = null!;
    private Label _successInfo = null!;
    private string _successSignature = "";
    private AccentButton _btnUnblock = null!;

    private DarkScrollPanel _settingsScroll = null!;
    private TableLayoutPanel _settingsStack = null!;
    private SegmentedControl _cfgBlockingMethod = null!;
    private Label _blockingMethodHint = null!;
    private ToggleSwitch _cfgEnabled = null!;
    private ToggleSwitch _cfgDryRun = null!;
    private ToggleSwitch _cfgBlockAll = null!;
    private ToggleSwitch _cfgNotify = null!;
    private ToggleSwitch _cfgLogon3 = null!;
    private ToggleSwitch _cfgLogon10 = null!;
    private NumberInput _cfgPort = null!;
    private NumberInput _cfgThreshold = null!;
    private NumberInput _cfgWindow = null!;
    private NumberInput _cfgBlockMinutes = null!;
    private NumberInput _cfgPermanent = null!;
    private NumberInput _cfgRetention = null!;
    private NumberInput _cfgRecentAttempts = null!;
    private ToggleSwitch _cfgRdp261 = null!;
    private NumberInput _cfgRdp261Threshold = null!;
    private NumberInput _cfgRdp261Window = null!;
    private ToggleSwitch _cfgWhitelistSuccess = null!;
    private DarkInput _cfgWhitelist = null!;
    private DarkInput _cfgBlacklist = null!;
    private Label _saveState = null!;

    private Label _footerStatus = null!;
    private Label _footerInfo = null!;
    private Label _footerVersion = null!;

    private Label _svcState = null!;
    private Label _svcPath = null!;
    private Label _svcMonitor = null!;
    private Label _svcFirewall = null!;
    private Label _svcData = null!;
    private Label _svcVersion = null!;
    private Label _svcUpdateStatus = null!;
    private AccentButton _btnUpdate = null!;
    private UpdateInfo? _pendingUpdate;
    private bool _updateBusy;
    private Label _svcError = null!;
    private AccentButton _btnInstall = null!;
    private AccentButton _btnStart = null!;
    private AccentButton _btnStop = null!;
    private AccentButton _btnRestart = null!;
    private AccentButton _btnRemove = null!;

    private NotifyIcon? _tray;
    private ToolStripMenuItem? _trayToggle;

    public MainForm(bool preview = false)
    {
        _preview = preview;
        _client = new ControlClient(GuardPaths.PipeName);
        _pollTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _pollTimer.Tick += async (_, _) => await PollAsync();
        _cellBold = Theme.Font(9.25f, FontStyle.Bold);

        SuspendLayout();
        InitializeForm();
        BuildUi();
        ResumeLayout(true);

        if (preview)
        {
            LoadPreview();
        }
        else
        {
            InitializeTray();
            var serviceStatus = ServiceInstaller.GetStatus();
            UpdateServiceUi(serviceStatus);

            // Пока служба не запущена, показываем сохранённые на диске настройки.
            if (serviceStatus != ServiceControllerStatus.Running)
                LoadSettings(LoadConfigFromDisk());

            _pollTimer.Start();
            Shown += async (_, _) => await PollAsync();
        }
    }

    private void InitializeForm()
    {
        Text = "RDP Defender";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1180, 760);
        MinimumSize = new Size(1020, 680);
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Font(9.5f);
        Icon = AppIcon.Get(64);
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;
        KeyPreview = true;

        if (_preview)
        {
            // Для отрисовки макетов окно без системной рамки: снимок содержит только клиентскую область.
            FormBorderStyle = FormBorderStyle.None;
            MinimumSize = Size.Empty;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.EnableDarkTitleBar(Handle);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Invalidate(true);
        Update();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Bg,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildBody(), 0, 1);
        root.Controls.Add(BuildFooter(), 0, 2);
        Controls.Add(root);
    }

    private Panel BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Fill, BackColor = Theme.HeaderTop, Margin = new Padding(0) };
        header.Paint += PaintHeader;

        var icon = new PictureBox
        {
            Image = AppIcon.GetBitmap(54),
            Size = new Size(54, 54),
            Location = new Point(26, 21),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent
        };

        var titleFlow = new FlowLayoutPanel
        {
            Location = new Point(94, 13),
            Size = new Size(420, 40),
            BackColor = Color.Transparent,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };

        titleFlow.Controls.Add(new Label
        {
            Text = "RDP",
            AutoSize = true,
            Font = Theme.Font(21f, FontStyle.Bold),
            ForeColor = Theme.Text,
            Margin = new Padding(0, 0, 10, 0)
        });
        titleFlow.Controls.Add(new Label
        {
            Text = "DEFENDER",
            AutoSize = true,
            Font = Theme.Font(21f, FontStyle.Bold),
            ForeColor = Theme.Green,
            Margin = new Padding(0, 0, 0, 0)
        });

        var subtitle = new Label
        {
            Text = "Защита удалённого доступа от подбора паролей",
            AutoSize = true,
            Font = Theme.Font(9f),
            ForeColor = Theme.TextMuted,
            Location = new Point(96, 54),
            BackColor = Color.Transparent
        };

        _statusPill = new StatusPill
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(header.Width - 250, 31)
        };

        header.Controls.Add(icon);
        header.Controls.Add(titleFlow);
        header.Controls.Add(subtitle);
        header.Controls.Add(_statusPill);

        header.Resize += (_, _) =>
        {
            _statusPill.Left = header.Width - _statusPill.Width - 28;
            _statusPill.Top = (header.Height - _statusPill.Height) / 2;
        };

        return header;
    }

    private void PaintHeader(object? sender, PaintEventArgs e)
    {
        var header = (Panel)sender!;
        var rect = header.ClientRectangle;
        using (var gradient = new LinearGradientBrush(
                   new Rectangle(0, 0, Math.Max(1, rect.Width), Math.Max(1, rect.Height)),
                   Theme.HeaderTop, Theme.HeaderBottom, 0f))
        {
            e.Graphics.FillRectangle(gradient, rect);
        }

        var glowPath = new GraphicsPath();
        glowPath.AddEllipse(rect.Width - 430, -220, 560, 400);
        using (var glow = new PathGradientBrush(glowPath)
               {
                   CenterColor = Color.FromArgb(30, 53, 224, 138),
                   SurroundColors = new[] { Color.FromArgb(0, 53, 224, 138) }
               })
        {
            e.Graphics.FillPath(glow, glowPath);
        }
        glowPath.Dispose();

        using var line = new Pen(Theme.Border, 1f);
        e.Graphics.DrawLine(line, 0, header.Height - 1, header.Width, header.Height - 1);
    }

    private Control BuildBody()
    {
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Bg,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(24, 16, 24, 8),
            Margin = new Padding(0)
        };
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var stats = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        for (var i = 0; i < 4; i++)
            stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        stats.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        _cardActive = new StatCard { Caption = "Активные блокировки", Value = "0", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 14, 4) };
        _cardAttempts = new StatCard { Caption = "Неудачных входов", Value = "0", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 14, 4) };
        _cardTotal = new StatCard { Caption = "Всего блокировок", Value = "0", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 14, 4) };
        _cardMode = new StatCard { Caption = "Режим", Value = "—", Dock = DockStyle.Fill, Margin = new Padding(0) };

        _cardActive.Cursor = Cursors.Hand;
        _cardAttempts.Cursor = Cursors.Hand;
        _cardMode.Cursor = Cursors.Hand;
        _cardActive.Click += (_, _) => SelectPage(0);
        _cardAttempts.Click += (_, _) => SelectPage(1);
        _cardMode.Click += ToggleMode;

        stats.Controls.Add(_cardActive, 0, 0);
        stats.Controls.Add(_cardAttempts, 1, 0);
        stats.Controls.Add(_cardTotal, 2, 0);
        stats.Controls.Add(_cardMode, 3, 0);

        var nav = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0, 3, 0, 0)
        };

        _nav.Add(new NavButton { Text = "Защита", Active = true });
        _nav.Add(new NavButton { Text = "Попытки" });
        _nav.Add(new NavButton { Text = "Успешные" });
        _nav.Add(new NavButton { Text = "Журнал" });
        _nav.Add(new NavButton { Text = "Настройки" });
        _nav.Add(new NavButton { Text = "Служба" });
        foreach (var (button, index) in _nav.Select((button, index) => (button, index)))
        {
            var pageIndex = index;
            button.Click += (_, _) => SelectPage(pageIndex);
            nav.Controls.Add(button);
        }

        _content = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = new Padding(0) };

        _pageBlocks = BuildBlocksPage();
        _pageAttempts = BuildAttemptsPage();
        _pageSuccess = BuildSuccessPage();
        _pageJournal = BuildJournalPage();
        _pageSettings = BuildSettingsPage();
        _pageService = BuildServicePage();
        _pages = new[] { _pageBlocks, _pageAttempts, _pageSuccess, _pageJournal, _pageSettings, _pageService };
        foreach (var page in _pages)
        {
            page.Visible = false;
            _content.Controls.Add(page);
        }
        _pageBlocks.Visible = true;

        body.Controls.Add(stats, 0, 0);
        body.Controls.Add(nav, 0, 1);
        body.Controls.Add(_content, 0, 2);
        return body;
    }

    private Panel BuildBlocksPage()
    {
        var page = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Visible = false };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Bg,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 232));

        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Bg,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var left = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Bg,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0, 5, 0, 0)
        };

        var refresh = new AccentButton { Text = "Обновить", Kind = AccentKind.Secondary, Width = 104, Height = 34, Margin = new Padding(0, 0, 10, 0) };
        refresh.Click += async (_, _) => await PollAsync();

        _btnUnblock = new AccentButton { Text = "Разблокировать", Kind = AccentKind.Secondary, Width = 172, Height = 34, Margin = new Padding(0, 0, 10, 0), Enabled = false };
        _btnUnblock.Click += UnblockSelected;

        var unblockAll = new AccentButton { Text = "Снять все", Kind = AccentKind.Ghost, Width = 126, Height = 34, Margin = new Padding(0, 0, 8, 0) };
        unblockAll.Click += UnblockAll;

        left.Controls.Add(refresh);
        left.Controls.Add(_btnUnblock);
        left.Controls.Add(unblockAll);

        var right = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Bg,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0, 5, 0, 0)
        };
        var block = new AccentButton { Text = "Заблокировать IP", Kind = AccentKind.Primary, Width = 166, Height = 34, Margin = new Padding(0) };
        block.Click += BlockIp;
        right.Controls.Add(block);

        toolbar.Controls.Add(left, 0, 0);
        toolbar.Controls.Add(right, 1, 0);

        var gridHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(0, 10, 0, 0) };
        _blocksGrid = new DarkGrid { Dock = DockStyle.Fill };
        _blocksGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ip", HeaderText = "IP-АДРЕС", FillWeight = 16, SortMode = DataGridViewColumnSortMode.NotSortable });
        _blocksGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "blocked", HeaderText = "ЗАБЛОКИРОВАН", FillWeight = 18, SortMode = DataGridViewColumnSortMode.NotSortable });
        _blocksGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "expires", HeaderText = "ДЕЙСТВУЕТ ДО", FillWeight = 18, SortMode = DataGridViewColumnSortMode.NotSortable });
        _blocksGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "attempts", HeaderText = "ПОПЫТОК", FillWeight = 9, SortMode = DataGridViewColumnSortMode.NotSortable });
        _blocksGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "state", HeaderText = "СОСТОЯНИЕ", FillWeight = 12, SortMode = DataGridViewColumnSortMode.NotSortable });
        _blocksGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "reason", HeaderText = "ПРИЧИНА", FillWeight = 31, SortMode = DataGridViewColumnSortMode.NotSortable });
        _blocksGrid.CellFormatting += BlocksGridOnCellFormatting;
        _blocksGrid.SelectionChanged += (_, _) =>
        {
            _btnUnblock.Enabled = _blocksGrid.SelectedRows.Count > 0;
            ShowSelectedBlockDetails();
        };
        _blocksGrid.EmptyText = "Активных блокировок нет\r\nПод защитой всё спокойно";

        gridHost.Controls.Add(_blocksGrid);

        _blocksDetails = new AttackerDetailsPanel { Dock = DockStyle.Fill };
        _blocksDetails.RefreshRequested += (_, _) => _ = LoadBlockWhoisAsync(_blocksDetails.Ip, refresh: true);

        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(gridHost, 0, 1);
        layout.Controls.Add(_blocksDetails, 0, 2);
        page.Controls.Add(layout);
        return page;
    }

    private Panel BuildAttemptsPage()
    {
        var page = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Visible = false };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Bg,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 232));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Bg,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0, 5, 0, 0)
        };

        var refresh = new AccentButton
        {
            Text = "Обновить",
            Kind = AccentKind.Secondary,
            Width = 104,
            Height = 34,
            Margin = new Padding(0, 0, 16, 0)
        };
        refresh.Click += async (_, _) => await PollAsync();

        var showLabel = new Label
        {
            Text = "Показывать последние",
            AutoSize = true,
            ForeColor = Theme.TextMuted,
            Font = Theme.Font(9f),
            Margin = new Padding(0, 9, 8, 0)
        };

        _attemptsLimit = new NumberInput
        {
            Minimum = 20,
            Maximum = 100,
            Value = 50,
            Width = 92,
            Height = 34,
            Margin = new Padding(0, 0, 8, 0)
        };
        _attemptsLimit.ValueChanged += async (_, _) =>
        {
            if (_cfgRecentAttempts is not null)
                _cfgRecentAttempts.Value = _attemptsLimit.Value;
            await PollAsync();
        };

        var suffix = new Label
        {
            Text = "попыток",
            AutoSize = true,
            ForeColor = Theme.TextMuted,
            Font = Theme.Font(9f),
            Margin = new Padding(0, 9, 18, 0)
        };

        _attemptsInfo = new Label
        {
            Text = "Последние неудачные входы",
            AutoSize = true,
            ForeColor = Theme.TextDim,
            Font = Theme.Font(8.5f),
            Margin = new Padding(0, 10, 0, 0)
        };

        toolbar.Controls.Add(refresh);
        toolbar.Controls.Add(showLabel);
        toolbar.Controls.Add(_attemptsLimit);
        toolbar.Controls.Add(suffix);
        toolbar.Controls.Add(_attemptsInfo);

        var gridHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Bg,
            Padding = new Padding(0, 10, 0, 0)
        };

        _attemptsGrid = new DarkGrid { Dock = DockStyle.Fill };
        _attemptsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "ВРЕМЯ", FillWeight = 15, SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _attemptsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "IP-АДРЕС", FillWeight = 16, SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _attemptsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "ПОЛЬЗОВАТЕЛЬ", FillWeight = 19, SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _attemptsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "ТИП ВХОДА", FillWeight = 11, SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _attemptsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "РАБОЧАЯ СТАНЦИЯ", FillWeight = 16, SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _attemptsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "РЕЗУЛЬТАТ", FillWeight = 23, SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _attemptsGrid.CellFormatting += AttemptsGridOnCellFormatting;
        _attemptsGrid.SelectionChanged += (_, _) => ShowSelectedAttemptDetails();
        _attemptsGrid.EmptyText = "Неудачных попыток входа пока нет";

        gridHost.Controls.Add(_attemptsGrid);

        _attemptsDetails = new AttackerDetailsPanel { Dock = DockStyle.Fill };
        _attemptsDetails.RefreshRequested += (_, _) => _ = LoadAttemptWhoisAsync(_attemptsDetails.Ip, refresh: true);

        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(gridHost, 0, 1);
        layout.Controls.Add(_attemptsDetails, 0, 2);
        page.Controls.Add(layout);
        return page;
    }

    private Panel BuildSuccessPage()
    {
        var page = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Visible = false };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Bg,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Bg,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var left = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Bg,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0, 5, 0, 0)
        };

        var refresh = new AccentButton
        {
            Text = "Обновить",
            Kind = AccentKind.Secondary,
            Width = 104,
            Height = 34,
            Margin = new Padding(0, 0, 16, 0)
        };
        refresh.Click += async (_, _) => await PollAsync();

        _successInfo = new Label
        {
            Text = "Успешные RDP-подключения",
            AutoSize = true,
            ForeColor = Theme.TextDim,
            Font = Theme.Font(8.5f),
            Margin = new Padding(0, 10, 0, 0)
        };

        left.Controls.Add(refresh);
        left.Controls.Add(_successInfo);

        var settings = new AccentButton
        {
            Text = "Настройки",
            Kind = AccentKind.Ghost,
            Width = 130,
            Height = 34,
            Margin = new Padding(0)
        };
        settings.Click += (_, _) => SelectPage(4);

        toolbar.Controls.Add(left, 0, 0);
        toolbar.Controls.Add(settings, 1, 0);

        var gridHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Bg,
            Padding = new Padding(0, 10, 0, 0)
        };

        _successGrid = new DarkGrid { Dock = DockStyle.Fill };
        AddSuccessColumn("ip", "IP-АДРЕС", 18);
        AddSuccessColumn("first", "ПЕРВЫЙ ВХОД", 17);
        AddSuccessColumn("last", "ПОСЛЕДНИЙ ВХОД", 17);
        AddSuccessColumn("count", "ВХОДОВ", 8);
        AddSuccessColumn("source", "ИСТОЧНИК", 12);
        AddSuccessColumn("user", "ПОЛЬЗОВАТЕЛЬ", 14);
        AddSuccessColumn("station", "РАБОЧАЯ СТАНЦИЯ", 14);
        AddSuccessColumn("state", "ЗАЩИТА", 20);
        _successGrid.CellFormatting += SuccessGridOnCellFormatting;
        _successGrid.EmptyText = "Успешных RDP-подключений пока нет";

        gridHost.Controls.Add(_successGrid);
        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(gridHost, 0, 1);
        page.Controls.Add(layout);
        return page;
    }

    private void AddSuccessColumn(string name, string header, int weight)
    {
        _successGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            FillWeight = weight,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
    }

    private void SuccessGridOnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (_successGrid.Rows[e.RowIndex].Tag is not SuccessfulLogonView) return;
        var style = e.CellStyle;
        if (style is null) return;

        switch (e.ColumnIndex)
        {
            case 0:
                style.ForeColor = Theme.Green;
                style.Font = _cellBold;
                break;
            case 3:
                style.Font = _cellBold;
                break;
            case 7:
                style.Font = _cellBold;
                var state = _successGrid.Rows[e.RowIndex].Cells[7].Value?.ToString() ?? "";
                style.ForeColor = state switch
                {
                    "Защищён" or "Белый список" => Theme.Green,
                    "Чёрный список" or "Заблокирован" => Theme.Danger,
                    _ => Theme.Warn
                };
                break;
            default:
                style.ForeColor = Theme.TextMuted;
                break;
        }
    }

    private Panel BuildJournalPage()
    {
        var page = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Visible = false };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0, 5, 0, 0)
        };

        var refresh = new AccentButton { Text = "Обновить", Kind = AccentKind.Secondary, Width = 104, Height = 34, Margin = new Padding(0, 0, 8, 0) };
        refresh.Click += async (_, _) => await PollAsync();

        var openLog = new AccentButton { Text = "Открыть журнал", Kind = AccentKind.Secondary, Width = 148, Height = 34, Margin = new Padding(0, 0, 8, 0) };
        openLog.Click += (_, _) => OpenLatestLog();

        var openFolder = new AccentButton { Text = "Папка данных", Kind = AccentKind.Ghost, Width = 140, Height = 34, Margin = new Padding(0) };
        openFolder.Click += (_, _) => OpenDataFolder();

        toolbar.Controls.Add(refresh);
        toolbar.Controls.Add(openLog);
        toolbar.Controls.Add(openFolder);

        var gridHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(0, 10, 0, 0) };
        _journalGrid = new DarkGrid { Dock = DockStyle.Fill };
        _journalGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ВРЕМЯ", FillWeight = 14, SortMode = DataGridViewColumnSortMode.NotSortable });
        _journalGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "УРОВЕНЬ", FillWeight = 10, SortMode = DataGridViewColumnSortMode.NotSortable });
        _journalGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "СОБЫТИЕ", FillWeight = 76, SortMode = DataGridViewColumnSortMode.NotSortable });
        _journalGrid.CellFormatting += JournalGridOnCellFormatting;
        _journalGrid.EmptyText = "Журнал пока пуст";

        gridHost.Controls.Add(_journalGrid);

        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(gridHost, 0, 1);
        page.Controls.Add(layout);
        return page;
    }

    private Panel BuildSettingsPage()
    {
        var page = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Visible = false };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        _settingsStack = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            BackColor = Color.Transparent,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 0,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        _settingsStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddSettingCard(BuildBlockingMethodSection());
        AddSettingCard(BuildProtectionSection());
        AddSettingCard(BuildThresholdSection());
        AddSettingCard(BuildWhitelistSection());
        AddSettingCard(BuildBlacklistSection());
        AddSettingCard(BuildAdvancedSection());

        _settingsScroll = new DarkScrollPanel(_settingsStack)
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(0, 10, 0, 0)
        };
        _settingsScroll.EnableMouseWheel();
        _settingsScroll.Recalculate();

        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0, 10, 0, 0)
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 660));
        bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _saveState = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Theme.Green,
            Font = Theme.Font(9f),
            AutoEllipsis = true,
            Margin = new Padding(0)
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            WrapContents = false,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };

        var save = new AccentButton { Text = "Сохранить настройки", Kind = AccentKind.Primary, Width = 196, Height = 36, Margin = new Padding(8, 0, 0, 0) };
        save.Click += SaveSettings;

        var revert = new AccentButton { Text = "Отменить", Kind = AccentKind.Secondary, Width = 112, Height = 36, Margin = new Padding(8, 0, 0, 0) };
        revert.Click += (_, _) => RevertSettings();

        var reset = new AccentButton { Text = "По умолчанию", Kind = AccentKind.Secondary, Width = 150, Height = 36, Margin = new Padding(8, 0, 0, 0) };
        reset.Click += ResetSettings;

        var folder = new AccentButton { Text = "Папка данных", Kind = AccentKind.Ghost, Width = 140, Height = 36, Margin = new Padding(8, 0, 0, 0) };
        folder.Click += (_, _) => OpenDataFolder();

        buttons.Controls.Add(save);
        buttons.Controls.Add(revert);
        buttons.Controls.Add(reset);
        buttons.Controls.Add(folder);

        bottom.Controls.Add(_saveState, 0, 0);
        bottom.Controls.Add(buttons, 1, 0);

        layout.Controls.Add(_settingsScroll, 0, 0);
        layout.Controls.Add(bottom, 0, 1);
        page.Controls.Add(layout);
        return page;
    }

    private void AddSettingCard(Control card)
    {
        var row = _settingsStack.RowCount;
        _settingsStack.RowCount = row + 1;
        _settingsStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _settingsStack.Controls.Add(card, 0, row);
    }

    private SectionCard BuildBlockingMethodSection()
    {
        var card = new SectionCard("Способ блокировки", "Правила действуют только при запущенной службе");
        _cfgBlockingMethod = new SegmentedControl
        {
            Options = new[] { "Брандмауэр", "WinDivert", "Политики IP" },
            SelectedIndex = 1,
            Height = 40
        };

        card.AddFullRow(_cfgBlockingMethod, 48);

        _blockingMethodHint = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            ForeColor = Theme.TextMuted,
            Font = Theme.Font(8.5f),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0)
        };
        card.AddFullRow(_blockingMethodHint, 46);
        _cfgBlockingMethod.SelectedIndexChanged += (_, _) => UpdateBlockingMethodHint();
        UpdateBlockingMethodHint();

        card.AddHint(
            "При остановке службы выбранные правила удаляются, при следующем запуске — " +
            "восстанавливаются из сохранённого состояния. WinDivert использует драйвер WFP, " +
            "политики IP — локальную политику IPsec, которая создаётся и удаляется службой.", 44);
        card.FitToContent();
        return card;
    }

    private void UpdateBlockingMethodHint()
    {
        if (_blockingMethodHint is null) return;

        _blockingMethodHint.Text = _cfgBlockingMethod.SelectedIndex switch
        {
            0 => "Брандмауэр Windows: правила применяются сразу, но не работают при выключенных " +
                 "профилях брандмауэра. Уже открытые сессии могут сохраниться.",
            2 => "Политики IP: локальная IPsec-политика применяется с задержкой (опрос до 180 минут) " +
                 "и не рвёт уже открытые сессии. Для быстрой блокировки менее надёжна.",
            _ => "WinDivert: драйвер WFP отбрасывает пакеты, включая уже открытые соединения. " +
                 "Требует загружаемого подписанного драйвера; наиболее надёжный вариант."
        };
    }

    private SectionCard BuildProtectionSection()
    {
        var card = new SectionCard("Защита", "Основные переключатели");
        _cfgEnabled = new ToggleSwitch
        {
            Text = "Защита включена",
            Description = "Блокировать адреса после серии неудачных входов"
        };
        _cfgDryRun = new ToggleSwitch
        {
            Text = "Режим наблюдения",
            Description = "Записывать события, но не создавать правила блокировки"
        };
        card.AddFullRow(_cfgEnabled, 46);
        card.AddFullRow(_cfgDryRun, 46);
        card.FitToContent();
        return card;
    }

    private SectionCard BuildThresholdSection()
    {
        var card = new SectionCard("Пороги срабатывания", "Когда и насколько блокировать адрес");
        _cfgThreshold = new NumberInput { Minimum = 1, Maximum = 10_000, Value = 5 };
        _cfgWindow = new NumberInput { Minimum = 1, Maximum = 10_080, Value = 10 };
        _cfgBlockMinutes = new NumberInput { Minimum = 1, Maximum = 525_600, Value = 1440 };
        _cfgPermanent = new NumberInput { Minimum = 0, Maximum = 10_000, Value = 0 };

        card.AddRow("Неудачных попыток до блокировки", _cfgThreshold);
        card.AddRow("Окно наблюдения, минут", _cfgWindow);
        card.AddRow("Длительность блокировки, минут", _cfgBlockMinutes);
        card.AddRow("Навсегда после N блокировок", _cfgPermanent);
        card.AddHint("0 — не блокировать навсегда. 1440 минут = 24 часа.", 24);
        card.FitToContent();
        return card;
    }

    private SectionCard BuildWhitelistSection()
    {
        var card = new SectionCard("Белый список", "Адреса, которые никогда не блокируются");
        _cfgWhitelist = new DarkInput(multiline: true)
        {
            Height = 160,
            PlaceholderText = "192.168.1.0/24\r\n10.10.10.5"
        };

        var tools = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Surface,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };

        var addLocal = new AccentButton
        {
            Text = "Добавить локальные сети",
            Kind = AccentKind.Secondary,
            Width = 238,
            Height = 32,
            Margin = new Padding(0, 0, 8, 0)
        };
        addLocal.Click += (_, _) => MergeLocalNetworks();

        var clear = new AccentButton
        {
            Text = "Очистить",
            Kind = AccentKind.Ghost,
            Width = 108,
            Height = 32,
            Margin = new Padding(0)
        };
        clear.Click += (_, _) => _cfgWhitelist.Text = "";

        tools.Controls.Add(addLocal);
        tools.Controls.Add(clear);

        card.AddHint("По умолчанию здесь уже перечислены все локальные диапазоны: RFC1918, " +
                     "loopback, link-local, CGNAT, IPv6 ULA и link-local. Удалите нужные строки, " +
                     "если хотите блокировать атаки из локальной сети.", 40);
        card.AddFullRow(tools, 40);
        card.AddFullRow(_cfgWhitelist, 166);
        card.FitToContent();
        return card;
    }

    private SectionCard BuildBlacklistSection()
    {
        var card = new SectionCard("Постоянный чёрный список", "Блокировать всегда, независимо от попыток");
        _cfgBlacklist = new DarkInput(multiline: true)
        {
            Height = 88,
            PlaceholderText = "203.0.113.10\r\n198.51.100.0/24"
        };
        card.AddHint("Адреса из этого списка блокируются сразу после запуска службы.", 24);
        card.AddFullRow(_cfgBlacklist, 94);
        card.FitToContent();
        return card;
    }

    private SectionCard BuildAdvancedSection()
    {
        var card = new SectionCard("Дополнительно", "Типы событий и поведение");
        _cfgBlockAll = new ToggleSwitch
        {
            Text = "Блокировать весь трафик с адреса",
            Description = "Все входящие подключения, а не только RDP-порт"
        };
        _cfgNotify = new ToggleSwitch
        {
            Text = "Уведомлять о блокировках",
            Description = "Показывать всплывающее уведомление в трее"
        };
        _cfgLogon3 = new ToggleSwitch
        {
            Text = "Тип входа 3 — сетевой",
            Description = "Стандартные попытки подключения к RDP"
        };
        _cfgLogon10 = new ToggleSwitch
        {
            Text = "Тип входа 10 — удалённый рабочий стол",
            Description = "Интерактивные RDP-сеансы"
        };
        _cfgRetention = new NumberInput { Minimum = 1, Maximum = 3650, Value = 30 };
        _cfgPort = new NumberInput { Minimum = 1, Maximum = 65_535, Value = 3389 };
        _cfgRecentAttempts = new NumberInput { Minimum = 20, Maximum = 100, Value = 50 };
        _cfgRdp261 = new ToggleSwitch
        {
            Text = "Считать RDP-подключения (событие 261)",
            Description = "Блокировать IP при частых подключениях до входа в систему"
        };
        _cfgRdp261Threshold = new NumberInput { Minimum = 1, Maximum = 10_000, Value = 50 };
        _cfgRdp261Window = new NumberInput { Minimum = 1, Maximum = 1440, Value = 10 };
        _cfgWhitelistSuccess = new ToggleSwitch
        {
            Text = "Не блокировать успешные подключения",
            Description = "IP успешных RDP-входов не блокируются и показываются на вкладке «Успешные»"
        };
        _cfgRecentAttempts.ValueChanged += (_, _) =>
        {
            if (_attemptsLimit is not null && _attemptsLimit.Value != _cfgRecentAttempts.Value)
                _attemptsLimit.Value = _cfgRecentAttempts.Value;
        };

        card.AddFullRow(_cfgBlockAll, 46);
        card.AddFullRow(_cfgNotify, 46);
        card.AddFullRow(_cfgLogon3, 46);
        card.AddFullRow(_cfgLogon10, 46);
        card.AddRow("Порт RDP (если блокируется только он)", _cfgPort);
        card.AddRow("Хранить журнал, дней", _cfgRetention);
        card.AddRow("Показывать последних попыток", _cfgRecentAttempts);
        card.AddFullRow(_cfgRdp261, 46);
        card.AddRow("Порог RDP-подключений (261)", _cfgRdp261Threshold);
        card.AddRow("Окно RDP-подключений, минут", _cfgRdp261Window);
        card.AddFullRow(_cfgWhitelistSuccess, 46);
        card.FitToContent();
        return card;
    }

    private Panel BuildServicePage()
    {
        var page = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Visible = false };
        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            BackColor = Color.Transparent,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 0,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var statusCard = new SectionCard("Служба Windows", "Фоновая защита работает без входа в систему");
        _svcState = new Label
        {
            Text = "Проверка…",
            Font = Theme.Font(15f, FontStyle.Bold),
            ForeColor = Theme.Green,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0)
        };
        _svcPath = new Label
        {
            Text = "—",
            Font = Theme.Font(8.25f),
            ForeColor = Theme.TextMuted,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(0)
        };

        var serviceButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Surface,
            WrapContents = true,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        _btnInstall = new AccentButton { Text = "Установить службу", Kind = AccentKind.Primary, Width = 178, Height = 36, Margin = new Padding(0, 4, 8, 4) };
        _btnInstall.Click += InstallService;
        _btnStart = new AccentButton { Text = "Запустить", Kind = AccentKind.Secondary, Width = 112, Height = 36, Margin = new Padding(0, 4, 8, 4) };
        _btnStart.Click += StartService;
        _btnStop = new AccentButton { Text = "Остановить", Kind = AccentKind.Secondary, Width = 118, Height = 36, Margin = new Padding(0, 4, 8, 4) };
        _btnStop.Click += StopService;
        _btnRestart = new AccentButton { Text = "Перезапустить", Kind = AccentKind.Ghost, Width = 138, Height = 36, Margin = new Padding(0, 4, 8, 4) };
        _btnRestart.Click += RestartService;
        serviceButtons.Controls.Add(_btnInstall);
        serviceButtons.Controls.Add(_btnStart);
        serviceButtons.Controls.Add(_btnStop);
        serviceButtons.Controls.Add(_btnRestart);

        statusCard.AddFullRow(_svcState, 38);
        statusCard.AddFullRow(_svcPath, 24);
        statusCard.AddFullRow(serviceButtons, 50);
        statusCard.FitToContent();

        var diagCard = new SectionCard("Диагностика", "Состояние компонентов защиты");
        _svcMonitor = CreateValueLabel();
        _svcFirewall = CreateValueLabel();
        _svcData = CreateValueLabel();
        _svcVersion = CreateValueLabel();
        diagCard.AddRow("Мониторинг событий", _svcMonitor, 36);
        diagCard.AddRow("Способ блокировки", _svcFirewall, 36);
        diagCard.AddRow("Папка данных", _svcData, 36);
        diagCard.AddRow("Версия", _svcVersion, 36);
        _svcError = new Label
        {
            Text = "",
            Font = Theme.Font(8.5f),
            ForeColor = Theme.Warn,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(0)
        };
        diagCard.AddFullRow(_svcError, 28);
        diagCard.FitToContent();

        var updateCard = new SectionCard("Обновления", "Проверка новых версий на GitHub");
        _svcUpdateStatus = CreateValueLabel();
        _svcUpdateStatus.Text = $"Текущая версия {GuardPaths.Version}";
        _btnUpdate = new AccentButton
        {
            Text = "Проверить обновления",
            Kind = AccentKind.Secondary,
            Width = 220,
            Height = 36,
            Margin = new Padding(0, 4, 0, 0)
        };
        _btnUpdate.Click += UpdateButtonOnClick;
        var updateButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Surface,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        updateButtons.Controls.Add(_btnUpdate);
        updateCard.AddRow("Состояние", _svcUpdateStatus, 36);
        updateCard.AddFullRow(updateButtons, 48);
        updateCard.FitToContent();

        var dangerCard = new SectionCard("Обслуживание", "Удаление службы и очистка правил");
        var dangerText = new Label
        {
            Text = "При удалении службы все созданные ей правила брандмауэра будут сняты. " +
                   "Настройки и журналы можно сохранить.",
            Font = Theme.Font(9f),
            ForeColor = Theme.TextMuted,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0)
        };
        _btnRemove = new AccentButton { Text = "Удалить службу…", Kind = AccentKind.Danger, Width = 170, Height = 36, Margin = new Padding(0, 4, 0, 0) };
        _btnRemove.Click += RemoveService;
        var dangerButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Surface,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        dangerButtons.Controls.Add(_btnRemove);
        dangerCard.AddFullRow(dangerText, 28);
        dangerCard.AddFullRow(dangerButtons, 48);
        dangerCard.FitToContent();

        AddCardToStack(stack, statusCard);
        AddCardToStack(stack, diagCard);
        AddCardToStack(stack, updateCard);
        AddCardToStack(stack, dangerCard);

        var scroll = new DarkScrollPanel(stack)
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(0, 10, 0, 0)
        };
        scroll.EnableMouseWheel();
        scroll.Recalculate();

        page.Controls.Add(scroll);
        return page;
    }

    private static void AddCardToStack(TableLayoutPanel stack, Control card)
    {
        var row = stack.RowCount;
        stack.RowCount = row + 1;
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.Controls.Add(card, 0, row);
    }

    private static Label CreateValueLabel() => new()
    {
        Text = "—",
        Font = Theme.Font(9f),
        ForeColor = Theme.Text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true,
        Margin = new Padding(0)
    };

    private Panel BuildFooter()
    {
        var footer = new Panel { Dock = DockStyle.Fill, BackColor = Theme.HeaderTop, Margin = new Padding(0) };
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.BorderSoft);
            e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(24, 0, 24, 0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _footerStatus = new Label
        {
            Text = "●  Подключение…",
            Font = Theme.Font(8.25f),
            ForeColor = Theme.TextMuted,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0)
        };
        _footerInfo = new Label
        {
            Text = "",
            Font = Theme.Font(8.25f),
            ForeColor = Theme.TextDim,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = true,
            Margin = new Padding(0)
        };
        _footerVersion = new Label
        {
            Text = $"RDP Defender {GuardPaths.Version}",
            Font = Theme.Font(8.25f),
            ForeColor = Theme.TextDim,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            Margin = new Padding(0)
        };

        layout.Controls.Add(_footerStatus, 0, 0);
        layout.Controls.Add(_footerInfo, 1, 0);
        layout.Controls.Add(_footerVersion, 2, 0);
        footer.Controls.Add(layout);
        return footer;
    }

    private void SelectPage(int index)
    {
        if (index < 0 || index >= _pages.Length) return;

        for (var i = 0; i < _pages.Length; i++)
        {
            _pages[i].Visible = i == index;
            _nav[i].Active = i == index;
        }

        _pages[index].BringToFront();
        _content.Invalidate(true);
        _content.Update();
    }
}
