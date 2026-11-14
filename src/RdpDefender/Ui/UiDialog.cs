using System.Drawing.Drawing2D;

namespace RdpDefender.Ui;

/// <summary>Модальные окна в стиле приложения вместо системного MessageBox.</summary>
public static class UiDialog
{
    public static void Info(IWin32Window? owner, string title, string message)
        => Show(owner, title, message, new[] { "Понятно" }, 0, Theme.Green);

    public static void Error(IWin32Window? owner, string title, string message)
        => Show(owner, title, message, new[] { "Закрыть" }, 0, Theme.Danger);

    public static bool Confirm(IWin32Window? owner, string title, string message)
        => Show(owner, title, message, new[] { "Отмена", "Да" }, 1, Theme.Green) == 1;

    public static int Choose(
        IWin32Window? owner,
        string title,
        string message,
        string[] buttons,
        int primaryIndex,
        int dangerIndex = -1)
        => Show(owner, title, message, buttons, primaryIndex, Theme.Green, dangerIndex);

    private static int Show(
        IWin32Window? owner,
        string title,
        string message,
        string[] buttons,
        int primaryIndex,
        Color accent,
        int dangerIndex = -1)
    {
        using var form = new DialogForm(title, message, buttons, primaryIndex, accent, dangerIndex);
        return owner is null ? form.ShowDialog() == DialogResult.OK ? form.SelectedIndex : -1
                             : form.ShowDialog(owner) == DialogResult.OK ? form.SelectedIndex : -1;
    }

    private sealed class DialogForm : Form
    {
        private readonly Color _accent;
        public int SelectedIndex { get; private set; } = -1;

        public DialogForm(string title, string message, string[] buttons, int primaryIndex, Color accent, int dangerIndex = -1)
        {
            _accent = accent;
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ShowIcon = false;
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            Font = Theme.Font(9.5f);

            var messageFont = Theme.Font(9.5f);
            var maxTextWidth = 470;
            var measured = TextRenderer.MeasureText(
                message, messageFont, new Size(maxTextWidth, 0), TextFormatFlags.WordBreak);

            var clientWidth = maxTextWidth + 56;
            var labelTop = 66;
            var buttonTop = labelTop + Math.Max(40, measured.Height) + 30;
            ClientSize = new Size(clientWidth, buttonTop + 58);

            var titleLabel = new Label
            {
                Text = title,
                Font = Theme.Font(13f, FontStyle.Bold),
                ForeColor = Theme.Text,
                Location = new Point(26, 18),
                AutoSize = true
            };

            var messageLabel = new Label
            {
                Text = message,
                Font = messageFont,
                ForeColor = Theme.TextMuted,
                Location = new Point(28, labelTop),
                Size = new Size(maxTextWidth, Math.Max(40, measured.Height + 4)),
                AutoSize = false
            };

            var buttonPanel = new Panel
            {
                Location = new Point(0, buttonTop),
                Size = new Size(clientWidth, 48),
                BackColor = Color.Transparent
            };

            var x = clientWidth - 24;
            for (var i = buttons.Length - 1; i >= 0; i--)
            {
                var isPrimary = i == primaryIndex;
                var button = new AccentButton
                {
                    Text = buttons[i],
                    Kind = isPrimary ? AccentKind.Primary : i == dangerIndex ? AccentKind.Danger : AccentKind.Secondary,
                    Size = new Size(TextRenderer.MeasureText(buttons[i], Theme.Font(9.25f, FontStyle.Bold)).Width + 40, 34),
                    Location = new Point(0, 4)
                };
                button.Left = x - button.Width;
                x = button.Left - 10;

                var index = i;
                button.Click += (_, _) =>
                {
                    SelectedIndex = index;
                    DialogResult = DialogResult.OK;
                    Close();
                };

                buttonPanel.Controls.Add(button);
                if (isPrimary) AcceptButton = button;
            }

            if (buttons.Length > 1)
            {
                var cancel = buttonPanel.Controls.OfType<AccentButton>().FirstOrDefault(b => b.Text == "Отмена");
                if (cancel is not null) CancelButton = cancel;
            }

            Controls.Add(titleLabel);
            Controls.Add(messageLabel);
            Controls.Add(buttonPanel);

            KeyPreview = true;
            KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape && CancelButton is not null)
                {
                    CancelButton.PerformClick();
                    e.Handled = true;
                }
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(_accent);
            e.Graphics.FillRectangle(brush, 0, 22, 3, 22);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.EnableDarkTitleBar(Handle);
        }
    }
}
