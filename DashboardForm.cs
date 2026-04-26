using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace TimeTracker2K;

internal sealed class DashboardForm : Form
{
    private const string RepositoryUrl = "https://github.com/frsksx/Time-Tracker-2K";
    private const string ProjectFileName = "TimeTracker2K.csproj";
    private const string BuildConfiguration = "Release";
    private const string DotNetSdkVersion = "9.0.203";
    private const string TargetFramework = "net9.0-windows";
    private const string RuntimeIdentifier = "win-x64";
    private const string PublishMode = "Self-contained single-file";
    private const string PublishOptions = "SelfContained=true; PublishSingleFile=true; IncludeNativeLibrariesForSelfExtract=true; EnableCompressionInSingleFile=true; Deterministic=true";
    private const string PublishCommand = "dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:EnableCompressionInSingleFile=true";
    private static readonly string[] CurrencyCodes = new[] { "EUR", "USD", "GBP", "CHF", "CAD", "AUD", "JPY", "SEK", "NOK", "DKK", "PLN" };

    private readonly DailyLogStore _store;
    private readonly Label _todayLabel;
    private readonly Label _dataPathLabel;
    private readonly TabControl _tabs;
    private readonly ListView _weeklyList;
    private readonly ListView _dailyList;
    private readonly Label _selectedWeekLabel;
    private readonly NumericUpDown _correctionHoursInput;
    private readonly NumericUpDown _standardWeeklyHoursInput;
    private readonly NumericUpDown _toleranceInput;
    private readonly NumericUpDown _hourlyRateInput;
    private readonly ComboBox _currencyInput;
    private readonly TextBox _backupFolderInput;
    private readonly ComboBox _backupIntervalInput;
    private readonly Label _settingsStatusLabel;
    private readonly System.Windows.Forms.Timer _foregroundRefreshTimer;
    private bool _allowClose;
    private bool _isRefreshingCorrectionControls;
    private DateOnly? _selectedWeekStart;

    public DashboardForm(DailyLogStore store, Icon appIcon)
    {
        _store = store;

        Text = "Time Tracker 2K";
        Icon = (Icon)appIcon.Clone();
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 560);
        Size = new Size(980, 660);
        ShowInTaskbar = true;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(14)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        var titleLabel = new Label
        {
            AutoSize = true,
            Font = new Font(Font.FontFamily, 16, FontStyle.Bold),
            Text = "Time Tracker 2K"
        };
        root.Controls.Add(titleLabel, 0, 0);

        _todayLabel = new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 6, 0, 12)
        };
        root.Controls.Add(_todayLabel, 0, 1);

        _tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(226, 230, 233),
            DrawMode = TabDrawMode.OwnerDrawFixed
        };
        _tabs.DrawItem += OnTabsDrawItem;
        root.Controls.Add(_tabs, 0, 2);

        _weeklyList = CreateHistoryList();
        _weeklyList.Columns.Add("Week Monday", 115);
        _weeklyList.Columns.Add("Time logged in", 125);
        _weeklyList.Columns.Add("Correction", 105);
        _weeklyList.Columns.Add("Work total", 105);
        _weeklyList.Columns.Add("Overtime", 135);
        _weeklyList.Columns.Add("Cumulative overtime", 150);
        _weeklyList.Columns.Add("Overtime worth", 125);
        _weeklyList.SelectedIndexChanged += (_, _) => UpdateCorrectionPanelFromSelection();

        _selectedWeekLabel = new Label
        {
            AutoSize = true,
            Font = new Font(Font.FontFamily, Font.Size, FontStyle.Bold),
            Text = "Selected week"
        };

        _correctionHoursInput = new NumericUpDown
        {
            DecimalPlaces = 2,
            Increment = 0.25M,
            Minimum = -200M,
            Maximum = 200M,
            Width = 80
        };
        _correctionHoursInput.ValueChanged += (_, _) => SaveSelectedWeekCorrection();

        var settings = _store.GetSettings();
        _standardWeeklyHoursInput = new NumericUpDown
        {
            DecimalPlaces = 2,
            Increment = 0.5M,
            Minimum = 1M,
            Maximum = 100M,
            Value = settings.StandardWeeklyHours,
            Width = 80
        };
        _standardWeeklyHoursInput.ValueChanged += (_, _) => SaveSettingsAndRefreshWeekly();

        _toleranceInput = new NumericUpDown
        {
            DecimalPlaces = 1,
            Increment = 0.5M,
            Minimum = 0M,
            Maximum = 100M,
            Value = settings.OvertimeTolerancePercent,
            Width = 70
        };
        _toleranceInput.ValueChanged += (_, _) => SaveSettingsAndRefreshWeekly();

        _hourlyRateInput = new NumericUpDown
        {
            DecimalPlaces = 2,
            Increment = 1M,
            Minimum = 0M,
            Maximum = 10000M,
            Value = settings.HourlyRate,
            Width = 90
        };
        _hourlyRateInput.ValueChanged += (_, _) => SaveSettingsAndRefreshWeekly();

        _currencyInput = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 90
        };
        _currencyInput.Items.AddRange(CurrencyCodes);
        _currencyInput.SelectedItem = CurrencyCodes.Contains(settings.CurrencyCode) ? settings.CurrencyCode : "EUR";
        _currencyInput.SelectedIndexChanged += (_, _) => SaveSettingsAndRefreshWeekly();

        _backupFolderInput = new TextBox
        {
            Text = settings.BackupFolderPath,
            Width = 470
        };
        _backupFolderInput.Leave += (_, _) => SaveSettingsAndRefreshWeekly();
        _backupFolderInput.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                SaveSettingsAndRefreshWeekly();
                e.SuppressKeyPress = true;
            }
        };

        _backupIntervalInput = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 90
        };
        _backupIntervalInput.Items.AddRange(new[] { DailyLogStore.DailyBackupInterval, DailyLogStore.WeeklyBackupInterval });
        _backupIntervalInput.SelectedItem = settings.BackupInterval == DailyLogStore.WeeklyBackupInterval
            ? DailyLogStore.WeeklyBackupInterval
            : DailyLogStore.DailyBackupInterval;
        _backupIntervalInput.SelectedIndexChanged += (_, _) => SaveSettingsAndRefreshWeekly();

        _settingsStatusLabel = new Label
        {
            AutoSize = true,
            Margin = new Padding(8, 6, 0, 0),
            Text = string.Empty
        };

        _foregroundRefreshTimer = new System.Windows.Forms.Timer
        {
            Interval = 10_000
        };
        _foregroundRefreshTimer.Tick += (_, _) => RefreshDataIfForeground();
        _foregroundRefreshTimer.Start();

        var weeklyPage = new TabPage("Weekly");
        weeklyPage.Controls.Add(CreateWeeklyPage());
        _tabs.TabPages.Add(weeklyPage);

        _dailyList = CreateHistoryList();
        _dailyList.Columns.Add("Date", 110);
        _dailyList.Columns.Add("On time", 120);
        _dailyList.Columns.Add("Time logged in", 130);
        _dailyList.Columns.Add("Away time", 120);
        _dailyList.Columns.Add("Time logged out", 130);
        var dailyPage = new TabPage("Daily");
        dailyPage.Controls.Add(_dailyList);
        _tabs.TabPages.Add(dailyPage);

        _tabs.TabPages.Add(CreateSettingsPage());
        _tabs.TabPages.Add(CreateInfoPage());

        var footer = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 12, 0, 0)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.Controls.Add(footer, 0, 3);

        _dataPathLabel = new Label
        {
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        footer.Controls.Add(_dataPathLabel, 0, 0);

        var buttonPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = Padding.Empty
        };
        footer.Controls.Add(buttonPanel, 1, 0);

        var copyButton = new Button
        {
            AutoSize = true,
            Text = "Copy Excel"
        };
        copyButton.Click += (_, _) => CopyHistoryToClipboard();
        buttonPanel.Controls.Add(copyButton);

        var folderButton = new Button
        {
            AutoSize = true,
            Text = "Folder"
        };
        folderButton.Click += (_, _) => OpenDataFolder();
        buttonPanel.Controls.Add(folderButton);

        var closeButton = new Button
        {
            AutoSize = true,
            Text = "Close"
        };
        closeButton.Click += (_, _) => Hide();
        buttonPanel.Controls.Add(closeButton);

        RefreshData();
    }

    public void AllowApplicationClose()
    {
        _allowClose = true;
    }

    public void RefreshData()
    {
        var currentSelection = _selectedWeekStart;
        var weeklyRows = BuildWeeklySummaries(
            _store.GetRetainedDaysWithFullOldestWeek(),
            _store.GetSettings(),
            _store.GetWeeklyCorrection);
        RefreshWeeklyList(weeklyRows, currentSelection);
        RefreshDailyList(_store.GetLastDays(DailyLogStore.DailyDisplayDays));

        var today = _store.GetTodaySummary();
        _todayLabel.Text = $"Today: first login {FormatFirstLogin(today.FirstLoginTime)} | on {ToHours(today.OnTime)} h | logged in {ToHours(today.LoggedIn)} h | away {ToHours(today.Away)} h | logged out {ToHours(today.LoggedOut)} h";
        _dataPathLabel.Text = _store.DataFilePath;
        UpdateCorrectionPanelFromSelection();
    }

    private void RefreshDataIfForeground()
    {
        if (Visible && Form.ActiveForm == this)
        {
            RefreshData();
        }
    }

    private Control CreateWeeklyPage()
    {
        var pageLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        pageLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        pageLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        pageLayout.Controls.Add(_weeklyList, 0, 0);
        pageLayout.Controls.Add(CreateWeeklyControls(), 0, 1);
        return pageLayout;
    }

    private Control CreateWeeklyControls()
    {
        var group = new GroupBox
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = "Weekly corrections"
        };

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(8)
        };
        group.Controls.Add(layout);

        layout.Controls.Add(_selectedWeekLabel);

        var correctionRow = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 3,
            Margin = new Padding(0, 6, 0, 0),
            Padding = Padding.Empty
        };
        correctionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        correctionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        correctionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.Controls.Add(correctionRow);

        var negativeButtons = CreateCorrectionButtonPanel("For non-work use");
        correctionRow.Controls.Add(negativeButtons, 0, 0);

        var minusWeekButton = new Button
        {
            AutoSize = true,
            Text = "- Week"
        };
        minusWeekButton.Click += (_, _) => AdjustSelectedWeekCorrection(-_standardWeeklyHoursInput.Value);
        ((FlowLayoutPanel)negativeButtons.Controls[1]).Controls.Add(minusWeekButton);

        var minusDayButton = new Button
        {
            AutoSize = true,
            Text = "- Day"
        };
        minusDayButton.Click += (_, _) => AdjustSelectedWeekCorrection(-GetStandardWorkdayHours());
        ((FlowLayoutPanel)negativeButtons.Controls[1]).Controls.Add(minusDayButton);

        var positiveButtons = CreateCorrectionButtonPanel("For time off");
        correctionRow.Controls.Add(positiveButtons, 1, 0);

        var plusDayButton = new Button
        {
            AutoSize = true,
            Text = "+ Day"
        };
        plusDayButton.Click += (_, _) => AdjustSelectedWeekCorrection(GetStandardWorkdayHours());
        ((FlowLayoutPanel)positiveButtons.Controls[1]).Controls.Add(plusDayButton);

        var plusWeekButton = new Button
        {
            AutoSize = true,
            Text = "+ Week"
        };
        plusWeekButton.Click += (_, _) => AdjustSelectedWeekCorrection(_standardWeeklyHoursInput.Value);
        ((FlowLayoutPanel)positiveButtons.Controls[1]).Controls.Add(plusWeekButton);

        var inputRow = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 3,
            Margin = new Padding(16, 22, 0, 0),
            Padding = Padding.Empty
        };
        inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        inputRow.Controls.Add(CreateMiddleLabel("Correction"), 0, 0);
        _correctionHoursInput.Margin = new Padding(8, 0, 8, 0);
        inputRow.Controls.Add(_correctionHoursInput, 1, 0);

        var clearButton = new Button
        {
            AutoSize = true,
            Text = "Clear week"
        };
        clearButton.Margin = Padding.Empty;
        clearButton.Click += (_, _) => SetSelectedWeekCorrectionHours(0);
        inputRow.Controls.Add(clearButton, 2, 0);
        correctionRow.Controls.Add(inputRow, 2, 0);

        return group;
    }

    private void RefreshDailyList(IReadOnlyList<DailySummary> days)
    {
        _dailyList.BeginUpdate();
        _dailyList.Items.Clear();

        foreach (var day in days)
        {
            var item = new ListViewItem(FormatDate(day.Date));
            item.SubItems.Add(ToHours(day.OnTime));
            item.SubItems.Add(ToHours(day.LoggedIn));
            item.SubItems.Add(ToHours(day.Away));
            item.SubItems.Add(ToHours(day.LoggedOut));
            item.ToolTipText = FormatDate(day.Date);
            if (day.Date == DateOnly.FromDateTime(DateTime.Today))
            {
                item.Font = new Font(_dailyList.Font, FontStyle.Bold);
            }

            _dailyList.Items.Add(item);
        }

        _dailyList.EndUpdate();
    }

    private void RefreshWeeklyList(IReadOnlyList<WeeklySummary> weeks, DateOnly? preferredSelection)
    {
        _weeklyList.BeginUpdate();
        _weeklyList.Items.Clear();

        ListViewItem? itemToSelect = null;
        ListViewItem? currentWeekItem = null;
        foreach (var week in weeks)
        {
            var item = new ListViewItem(FormatDate(week.Start))
            {
                Tag = week,
                ToolTipText = $"{FormatDate(week.Start)} - {FormatDate(week.End)}"
            };
            item.SubItems.Add(ToHours(week.LoggedIn));
            item.SubItems.Add(ToHours(week.Correction));
            item.SubItems.Add(ToHours(week.WorkTotal));
            item.SubItems.Add(ToHours(week.CountedOvertime));
            item.SubItems.Add(ToHours(week.CumulativeOvertime));
            item.SubItems.Add(ToMoney(week.OvertimeWorth, week.CurrencyCode));

            if (DateOnly.FromDateTime(DateTime.Today) >= week.Start && DateOnly.FromDateTime(DateTime.Today) <= week.End)
            {
                item.Font = new Font(_weeklyList.Font, FontStyle.Bold);
                currentWeekItem = item;
            }

            if (preferredSelection == week.Start)
            {
                itemToSelect = item;
            }

            _weeklyList.Items.Add(item);
        }

        itemToSelect ??= preferredSelection is null ? currentWeekItem : null;
        itemToSelect ??= currentWeekItem;
        itemToSelect ??= _weeklyList.Items.Count > 0 ? _weeklyList.Items[0] : null;
        if (itemToSelect is not null)
        {
            itemToSelect.Selected = true;
            itemToSelect.Focused = true;
            itemToSelect.EnsureVisible();
        }

        _weeklyList.EndUpdate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _foregroundRefreshTimer.Stop();
        _foregroundRefreshTimer.Dispose();
        base.OnFormClosed(e);
    }

    private void OpenDataFolder()
    {
        Directory.CreateDirectory(_store.AppDataDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = _store.AppDataDirectory,
            UseShellExecute = true
        });
    }

    private void OnTabsDrawItem(object? sender, DrawItemEventArgs e)
    {
        var selected = e.Index == _tabs.SelectedIndex;
        var page = _tabs.TabPages[e.Index];
        using var font = new Font(
            Font.FontFamily,
            Font.Size,
            selected ? FontStyle.Bold : FontStyle.Regular);

        var background = selected ? SystemBrushes.Window : new SolidBrush(Color.FromArgb(226, 230, 233));
        try
        {
            e.Graphics.FillRectangle(background, e.Bounds);
            TextRenderer.DrawText(
                e.Graphics,
                page.Text,
                font,
                e.Bounds,
                SystemColors.ControlText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
        finally
        {
            if (!selected)
            {
                background.Dispose();
            }
        }
    }

    private void OnHistoryListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.C)
        {
            CopyHistoryToClipboard();
            e.Handled = true;
        }
    }

    private void OnHistoryListMouseDoubleClick(object? sender, MouseEventArgs e)
    {
        if (sender is not ListView list)
        {
            return;
        }

        var hit = list.HitTest(e.Location);
        if (hit.SubItem is null)
        {
            return;
        }

        Clipboard.SetText(hit.SubItem.Text);
    }

    private void CopyHistoryToClipboard()
    {
        Clipboard.SetText(BuildExcelText());
    }

    private string BuildExcelText()
    {
        return _tabs.SelectedTab?.Text switch
        {
            "Daily" => BuildDailyExcelText(),
            "Info" => BuildInfoExcelText(),
            "Settings" => BuildSettingsExcelText(),
            _ => BuildWeeklyExcelText()
        };
    }

    private string BuildDailyExcelText()
    {
        var rows = _store.GetLastDays(DailyLogStore.DailyDisplayDays);
        var builder = new StringBuilder();
        builder.AppendLine("Date\tOn time\tTime logged in\tAway time\tTime logged out");
        foreach (var row in rows)
        {
            builder
                .Append(FormatDate(row.Date)).Append('\t')
                .Append(ToHours(row.OnTime)).Append('\t')
                .Append(ToHours(row.LoggedIn)).Append('\t')
                .Append(ToHours(row.Away)).Append('\t')
                .Append(ToHours(row.LoggedOut)).AppendLine();
        }

        return builder.ToString();
    }

    private string BuildWeeklyExcelText()
    {
        var settings = _store.GetSettings();
        var rows = BuildWeeklySummaries(
            _store.GetRetainedDaysWithFullOldestWeek(),
            settings,
            _store.GetWeeklyCorrection);
        var builder = new StringBuilder();
        builder.AppendLine($"Week Monday\tTime logged in\tCorrection\tWork total\tOvertime\tCumulative overtime\tOvertime worth ({settings.CurrencyCode})");
        foreach (var row in rows)
        {
            builder
                .Append(FormatDate(row.Start)).Append('\t')
                .Append(ToHours(row.LoggedIn)).Append('\t')
                .Append(ToHours(row.Correction)).Append('\t')
                .Append(ToHours(row.WorkTotal)).Append('\t')
                .Append(ToHours(row.CountedOvertime)).Append('\t')
                .Append(ToHours(row.CumulativeOvertime)).Append('\t')
                .Append(ToMoneyValue(row.OvertimeWorth)).AppendLine();
        }

        return builder.ToString();
    }

    private string BuildInfoExcelText()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Name\tValue");
        builder.AppendLine($"Version\t{GetAppVersion()}");
        builder.AppendLine("License\tMIT");
        builder.AppendLine($"GitHub repo\t{RepositoryUrl}");
        builder.AppendLine("Privacy\tDoes not track keystrokes, screenshots, websites, apps, or upload data. Data stays local.");
        builder.AppendLine("Visibility\tTray icon stays visible while the app is running. Exiting the tray app stops tracking.");
        builder.AppendLine($"Data file\t{_store.DataFilePath}");
        builder.AppendLine($"Project file\t{ProjectFileName}");
        builder.AppendLine($"SDK\t{DotNetSdkVersion} (pinned in global.json)");
        builder.AppendLine($"Configuration\t{BuildConfiguration}");
        builder.AppendLine($"Target framework\t{TargetFramework}");
        builder.AppendLine($"Runtime identifier\t{RuntimeIdentifier}");
        builder.AppendLine($"Publish mode\t{PublishMode}");
        builder.AppendLine($"Publish options\t{PublishOptions}");
        builder.AppendLine($"Publish command\t{PublishCommand}");
        builder.AppendLine("Same hash requirements\tSame source files, SDK/runtime packs, OS, and publish command.");
        return builder.ToString();
    }

    private string BuildSettingsExcelText()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Name\tValue");
        builder.AppendLine($"Standard weekly worktime\t{ToHours(TimeSpan.FromHours((double)_standardWeeklyHoursInput.Value))}");
        builder.AppendLine($"Overtime tolerance percent\t{_toleranceInput.Value.ToString("0.#", CultureInfo.GetCultureInfo("de-DE"))}");
        builder.AppendLine($"Hourly rate\t{_hourlyRateInput.Value.ToString("0.##", CultureInfo.GetCultureInfo("de-DE"))}");
        builder.AppendLine($"Currency\t{GetSelectedCurrencyCode()}");
        builder.AppendLine($"Backup folder\t{_backupFolderInput.Text}");
        builder.AppendLine($"Backup interval\t{GetSelectedBackupInterval()}");
        return builder.ToString();
    }

    private TabPage CreateSettingsPage()
    {
        var page = new TabPage("Settings");
        page.AutoScroll = true;
        var panel = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 11,
            Padding = new Padding(16)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddFixedRows(panel, 11);
        page.Controls.Add(panel);

        AddInfoHeader(panel, 0, "Standard week");
        panel.Controls.Add(CreateSettingsControlRow(_standardWeeklyHoursInput, CreateMiddleLabel("hour")), 1, 0);

        AddInfoHeader(panel, 1, "Overtime tolerance");
        panel.Controls.Add(CreateSettingsControlRow(_toleranceInput, CreateMiddleLabel("%")), 1, 1);

        AddInfoHeader(panel, 2, "Hourly rate");
        panel.Controls.Add(CreateSettingsControlRow(_hourlyRateInput), 1, 2);

        AddInfoHeader(panel, 3, "Currency");
        panel.Controls.Add(CreateSettingsControlRow(_currencyInput), 1, 3);

        AddInfoHeader(panel, 4, "Backup folder");
        var browseBackupButton = new Button
        {
            AutoSize = true,
            Text = "Browse"
        };
        browseBackupButton.Click += (_, _) => BrowseBackupFolder();
        var openBackupButton = new Button
        {
            AutoSize = true,
            Text = "Open"
        };
        openBackupButton.Click += (_, _) => OpenBackupFolder();
        panel.Controls.Add(CreateSettingsControlRow(_backupFolderInput, browseBackupButton, openBackupButton), 1, 4);

        AddInfoHeader(panel, 5, "Backup interval");
        panel.Controls.Add(CreateSettingsControlRow(_backupIntervalInput), 1, 5);

        AddInfoHeader(panel, 6, "Restore data");
        var restoreBackupButton = new Button
        {
            AutoSize = true,
            Text = "Restore backup"
        };
        restoreBackupButton.Click += (_, _) => RestoreBackup();
        panel.Controls.Add(CreateSettingsControlRow(restoreBackupButton), 1, 6);

        AddInfoHeader(panel, 7, "Backups");
        AddInfoValue(panel, 7, "The app updates one local backup file in this folder when the selected daily or weekly interval is due.", 700);

        AddInfoHeader(panel, 8, "Corrections");
        AddInfoValue(panel, 8, "Negative correction accounts for logged in while not working. Positive correction records time off or holidays.", 700);

        AddInfoHeader(panel, 9, "Overtime");
        AddInfoValue(panel, 9, "Weekly overtime is based on logged-in time after corrections. Overtime within the tolerance is not counted toward cumulative overtime.", 700);

        _settingsStatusLabel.Dock = DockStyle.Fill;
        _settingsStatusLabel.Margin = new Padding(0, 0, 0, 0);
        _settingsStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        panel.Controls.Add(_settingsStatusLabel, 1, 10);
        return page;
    }

    private TabPage CreateInfoPage()
    {
        var page = new TabPage("Info");
        page.AutoScroll = true;
        var panel = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 14,
            Padding = new Padding(16)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddFixedRows(panel, 14, 34);
        page.Controls.Add(panel);

        AddInfoRow(panel, 0, "App", "Time Tracker 2K");
        AddInfoRow(panel, 1, "Version", GetAppVersion());
        AddInfoRow(panel, 2, "License", "MIT");

        AddInfoHeader(panel, 3, "GitHub");
        var repoLink = new LinkLabel
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(0, 5, 0, 0),
            Text = RepositoryUrl
        };
        repoLink.TextAlign = ContentAlignment.TopLeft;
        repoLink.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo
        {
            FileName = RepositoryUrl,
            UseShellExecute = true
        });
        panel.Controls.Add(repoLink, 1, 3);

        AddInfoRow(panel, 4, "Privacy", "Does not track keystrokes, screenshots, websites, apps, or upload data.");
        AddInfoRow(panel, 5, "Data", "Stored locally only. No upload or network connection is used for tracking.");
        AddInfoRow(panel, 6, "Visibility", "The tray icon stays visible while the app is running. Exiting the tray app stops tracking.");
        AddInfoRow(panel, 7, "Project", ProjectFileName);
        AddInfoRow(panel, 8, "SDK", $"{DotNetSdkVersion} (global.json)");
        AddInfoRow(panel, 9, "Build", $"{BuildConfiguration}, {TargetFramework}");
        AddInfoRow(panel, 10, "Runtime", RuntimeIdentifier);
        AddInfoRow(panel, 11, "Publish", PublishMode);
        AddInfoRow(panel, 12, "Options", PublishOptions);
        AddInfoRow(panel, 13, "Command", PublishCommand);

        return page;
    }

    private void UpdateCorrectionPanelFromSelection()
    {
        if (_weeklyList.SelectedItems.Count == 0 || _weeklyList.SelectedItems[0].Tag is not WeeklySummary week)
        {
            _selectedWeekStart = null;
            _selectedWeekLabel.Text = "Select a week to add a correction.";
            _correctionHoursInput.Value = 0;
            _correctionHoursInput.Enabled = false;
            return;
        }

        _selectedWeekStart = week.Start;
        _selectedWeekLabel.Text = $"Selected week: {FormatDate(week.Start)}";
        _correctionHoursInput.Enabled = true;

        _isRefreshingCorrectionControls = true;
        _correctionHoursInput.Value = ClampNumericValue((decimal)_store.GetWeeklyCorrection(week.Start).TotalHours);
        _isRefreshingCorrectionControls = false;
    }

    private void SaveSelectedWeekCorrection()
    {
        if (_isRefreshingCorrectionControls || _selectedWeekStart is not { } weekStart)
        {
            return;
        }

        _store.SetWeeklyCorrection(weekStart, TimeSpan.FromHours((double)_correctionHoursInput.Value));
        RefreshData();
    }

    private void AdjustSelectedWeekCorrection(decimal hours)
    {
        if (_selectedWeekStart is null)
        {
            return;
        }

        SetSelectedWeekCorrectionHours(_correctionHoursInput.Value + hours);
    }

    private void SetSelectedWeekCorrectionHours(decimal hours)
    {
        _correctionHoursInput.Value = ClampNumericValue(hours);
    }

    private decimal GetStandardWorkdayHours()
    {
        return _standardWeeklyHoursInput.Value / 5M;
    }

    private void SaveSettingsAndRefreshWeekly()
    {
        if (_isRefreshingCorrectionControls)
        {
            return;
        }

        _store.UpdateSettings(
            _standardWeeklyHoursInput.Value,
            _toleranceInput.Value,
            _hourlyRateInput.Value,
            GetSelectedCurrencyCode(),
            _backupFolderInput.Text,
            GetSelectedBackupInterval());
        RefreshData();
    }

    private string GetSelectedCurrencyCode()
    {
        return _currencyInput.SelectedItem?.ToString() ?? "EUR";
    }

    private string GetSelectedBackupInterval()
    {
        return _backupIntervalInput.SelectedItem?.ToString() ?? DailyLogStore.DailyBackupInterval;
    }

    private void BrowseBackupFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select backup folder",
            SelectedPath = Directory.Exists(_backupFolderInput.Text) ? _backupFolderInput.Text : _store.AppDataDirectory,
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _backupFolderInput.Text = dialog.SelectedPath;
        SaveSettingsAndRefreshWeekly();
    }

    private void OpenBackupFolder()
    {
        var folder = _backupFolderInput.Text;
        if (string.IsNullOrWhiteSpace(folder))
        {
            folder = _store.AppDataDirectory;
        }

        try
        {
            folder = Environment.ExpandEnvironmentVariables(folder);
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }
        catch
        {
            MessageBox.Show(this, "Could not open the backup folder.", "Time Tracker 2K", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void RestoreBackup()
    {
        var backupFolder = _backupFolderInput.Text;
        if (string.IsNullOrWhiteSpace(backupFolder))
        {
            backupFolder = _store.AppDataDirectory;
        }

        backupFolder = Environment.ExpandEnvironmentVariables(backupFolder);
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "JSON backup (*.json)|*.json|All files (*.*)|*.*",
            InitialDirectory = Directory.Exists(backupFolder) ? backupFolder : _store.AppDataDirectory,
            Title = "Restore Time Tracker 2K backup"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            "Restore this backup now? The current data file will be copied to a safety file first.",
            "Restore backup",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);
        if (result != DialogResult.Yes)
        {
            return;
        }

        try
        {
            _store.RestoreFromBackup(dialog.FileName);
            RefreshData();
            MessageBox.Show(this, "Backup restored.", "Time Tracker 2K", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not restore backup: {ex.Message}", "Time Tracker 2K", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static decimal ClampNumericValue(decimal value)
    {
        return Math.Clamp(value, -200M, 200M);
    }

    private ListView CreateHistoryList()
    {
        var list = new ListView
        {
            Dock = DockStyle.Fill,
            FullRowSelect = true,
            GridLines = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            HideSelection = false,
            MultiSelect = false,
            ShowItemToolTips = true,
            View = View.Details
        };
        list.KeyDown += OnHistoryListKeyDown;
        list.MouseDoubleClick += OnHistoryListMouseDoubleClick;
        return list;
    }

    private static TableLayoutPanel CreateCorrectionButtonPanel(string title)
    {
        var panel = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 12, 0),
            Padding = Padding.Empty
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var label = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            Margin = Padding.Empty,
            Text = title,
            TextAlign = ContentAlignment.MiddleLeft
        };
        panel.Controls.Add(label, 0, 0);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            WrapContents = false
        };
        panel.Controls.Add(buttons, 0, 1);

        return panel;
    }

    private static TableLayoutPanel CreateSettingsControlRow(params Control[] controls)
    {
        var row = new TableLayoutPanel
        {
            ColumnCount = controls.Length,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(0, 4, 0, 0),
            RowCount = 1
        };

        foreach (var control in controls)
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            control.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            control.Margin = row.Controls.Count == 0 ? Padding.Empty : new Padding(8, 0, 0, 0);
            row.Controls.Add(control, row.Controls.Count, 0);
        }

        return row;
    }

    private static Label CreateInlineLabel(string text)
    {
        var size = TextRenderer.MeasureText(text, SystemFonts.DefaultFont);
        return new Label
        {
            AutoSize = false,
            Height = 26,
            Margin = new Padding(8, 0, 2, 0),
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
            Width = size.Width + 4
        };
    }

    private static Label CreateMiddleLabel(string text)
    {
        var size = TextRenderer.MeasureText(text, SystemFonts.DefaultFont);
        return new Label
        {
            AutoSize = false,
            Height = 26,
            Margin = Padding.Empty,
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
            Width = size.Width + 16
        };
    }

    private static void AddFixedRows(TableLayoutPanel panel, int count, int height = 36)
    {
        for (var i = 0; i < count; i++)
        {
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        }
    }

    private static void AddInfoRow(TableLayoutPanel panel, int row, string name, string value)
    {
        AddInfoHeader(panel, row, name);
        AddInfoValue(panel, row, value);
    }

    private static void AddInfoHeader(TableLayoutPanel panel, int row, string text)
    {
        var label = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            Margin = new Padding(0, 0, 12, 0),
            Padding = new Padding(0, 5, 0, 0),
            Text = text,
            TextAlign = ContentAlignment.TopLeft
        };

        panel.Controls.Add(label, 0, row);
    }

    private static void AddInfoValue(TableLayoutPanel panel, int row, string text, int maxWidth = 720)
    {
        var label = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(0, 5, 0, 0),
            Text = text,
            TextAlign = ContentAlignment.TopLeft
        };

        panel.Controls.Add(label, 1, row);
    }

    private static IReadOnlyList<WeeklySummary> BuildWeeklySummaries(
        IReadOnlyList<DailySummary> days,
        TrackerSettingsSnapshot settings,
        Func<DateOnly, TimeSpan> getWeeklyCorrection)
    {
        var currentWeekStart = GetWeekStart(DateOnly.FromDateTime(DateTime.Today));
        var target = TimeSpan.FromHours((double)settings.StandardWeeklyHours);
        var tolerance = TimeSpan.FromHours((double)(settings.StandardWeeklyHours * settings.OvertimeTolerancePercent / 100M));
        var chronological = days
            .GroupBy(day => GetWeekStart(day.Date))
            .Where(group => group.Key == currentWeekStart || group.Any(HasAnyTrackedValue) || getWeeklyCorrection(group.Key) != TimeSpan.Zero)
            .OrderBy(group => group.Key)
            .ToList();

        var cumulative = TimeSpan.Zero;
        var rows = new List<WeeklySummary>(chronological.Count);
        foreach (var group in chronological)
        {
            var loggedIn = TimeSpan.FromSeconds(group.Sum(day => day.LoggedIn.TotalSeconds));
            var correction = getWeeklyCorrection(group.Key);
            var workTotal = loggedIn + correction;
            if (workTotal < TimeSpan.Zero)
            {
                workTotal = TimeSpan.Zero;
            }
            var countedOvertime = workTotal - target - tolerance;
            if (countedOvertime < TimeSpan.Zero)
            {
                countedOvertime = TimeSpan.Zero;
            }

            cumulative += countedOvertime;
            var overtimeWorth = Math.Round((decimal)countedOvertime.TotalHours * settings.HourlyRate, 2);
            rows.Add(new WeeklySummary(
                group.Key,
                group.Key.AddDays(6),
                loggedIn,
                correction,
                workTotal,
                countedOvertime,
                overtimeWorth,
                settings.CurrencyCode,
                cumulative));
        }

        rows.Reverse();
        return rows;
    }

    private static bool HasAnyTrackedValue(DailySummary day)
    {
        return day.LoggedIn != TimeSpan.Zero
            || day.Away != TimeSpan.Zero
            || day.LoggedOut != TimeSpan.Zero
            || day.Correction != TimeSpan.Zero;
    }

    private static DateOnly GetWeekStart(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return date.AddDays(-offset);
    }

    private static string FormatDate(DateOnly date)
    {
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string ToHours(TimeSpan duration)
    {
        return Math.Round(duration.TotalHours, 2).ToString("0.##", CultureInfo.GetCultureInfo("de-DE"));
    }

    private static string FormatFirstLogin(string? firstLoginTime)
    {
        return string.IsNullOrWhiteSpace(firstLoginTime) ? "-" : firstLoginTime;
    }

    private static string ToMoney(decimal amount, string currencyCode)
    {
        return $"{ToMoneyValue(amount)} {currencyCode}";
    }

    private static string ToMoneyValue(decimal amount)
    {
        return amount.ToString("0.00", CultureInfo.GetCultureInfo("de-DE"));
    }

    private static string GetAppVersion()
    {
        return typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? typeof(Program).Assembly.GetName().Version?.ToString()
            ?? "unknown";
    }

    private sealed record WeeklySummary(
        DateOnly Start,
        DateOnly End,
        TimeSpan LoggedIn,
        TimeSpan Correction,
        TimeSpan WorkTotal,
        TimeSpan CountedOvertime,
        decimal OvertimeWorth,
        string CurrencyCode,
        TimeSpan CumulativeOvertime);
}
