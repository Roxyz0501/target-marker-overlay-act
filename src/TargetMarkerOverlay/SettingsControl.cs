using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TargetMarkerOverlay
{
    public sealed class SettingsControl : UserControl
    {
        private const string SupportUrl = "https://ko-fi.com/roxyz0501";
        private readonly PluginSettings settings;
        private readonly MarkerStateTracker tracker;
        private bool loading = true;
        private volatile bool stopped;
        private int hoveredTabIndex = -1;
        private readonly CheckBox enabled = Check("Enabled");
        private readonly CheckBox hideEmpty = Check("HideEmpty");
        private readonly CheckBox showName = Check("ShowName");
        private readonly CheckBox anonymous = Check("Anonymous");
        private readonly CheckBox locked = Check("Locked");
        private readonly TrackBar opacity = Slider(20);
        private readonly Label opacityValue = new Label { AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly TrackBar backgroundOpacity = Slider(0);
        private readonly Label backgroundOpacityValue = new Label { AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly CheckBox echoToggle = Check("EchoToggle");
        private readonly TextBox echoText = new TextBox { Width = 200, MaxLength = 100, Anchor = AnchorStyles.Left };
        private readonly Label echoHint = new Label { AutoSize = true, ForeColor = Color.FromArgb(105, 115, 130), Anchor = AnchorStyles.Left };
        private readonly ComboBox languageInput = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180, Anchor = AnchorStyles.Left };
        private readonly CheckBox checkUpdatesOnStartup = Check("CheckStartup");
        private readonly ComboBox sortMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private readonly DataGridView markerGrid = Grid();
        private readonly DataGridView roleGrid = Grid();
        private readonly DataGridView jobGrid = Grid();
        private readonly Label activity = new Label { AutoSize = false, Dock = DockStyle.Fill, ForeColor = Color.FromArgb(75, 92, 115), Tag = "ActivityWaiting" };
        private TabControl tabs;
        private TabPage displayPage, sortPage, supportPage, updatePage;
        private Label overlayOpacityLabel, backgroundOpacityLabel, echoTitleLabel, echoTextLabel, sortPrimaryLabel, markerHeader, roleHeader, jobHeader, headerSubtitle;
        private Label supportTitleLabel, supportDescriptionLabel, supportAssuranceLabel, supportStatusLabel;
        private Button supportButton, clearButton;
        private Label resizeHint;
        private Label updateTitleLabel, updateCurrentLabel, updateLatestLabel, updateStatusLabel, updateNotesLabel;
        private Button checkNowButton, updateNowButton, laterButton;
        private UpdateCheckResult lastUpdateResult;
        private string updateInstallKey, updateInstallDetail;
        private bool updateInstallError;
        public event EventHandler SettingsChanged;
        public event EventHandler CheckForUpdatesRequested;
        public event EventHandler InstallUpdateRequested;
        public event EventHandler PostponeUpdateRequested;

        public SettingsControl(PluginSettings settings, MarkerStateTracker tracker)
        {
            this.settings = settings;
            this.tracker = tracker;
            Dock = DockStyle.Fill;
            Font = new Font("Yu Gothic UI", 9f);
            BackColor = Color.FromArgb(244, 247, 251);
            Padding = new Padding(14);

            tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = new Font("Yu Gothic UI Semibold", 9f),
                DrawMode = TabDrawMode.OwnerDrawFixed,
                Padding = new Point(18, 6),
            };
            tabs.TabPages.Add(BuildOverlayTab());
            tabs.TabPages.Add(BuildSortTab());
            tabs.TabPages.Add(BuildUpdateTab());
            tabs.TabPages.Add(BuildSupportTab());
            tabs.DrawItem += DrawTab;
            tabs.MouseMove += (s, e) => UpdateHoveredTab(tabs, e.Location);
            tabs.MouseLeave += (s, e) =>
            {
                if (hoveredTabIndex < 0) return;
                hoveredTabIndex = -1;
                tabs.Invalidate();
            };
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = BackColor };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(tabs, 0, 1);
            Controls.Add(root);
            ApplyVisualTheme();

            languageInput.Items.AddRange(new object[] { "English", "日本語", "简体中文", "한국어" });
            languageInput.SelectedIndex = LanguageIndex(settings.Language);
            enabled.Checked = settings.OverlayEnabled;
            hideEmpty.Checked = settings.HideWhenEmpty;
            showName.Checked = settings.ShowCharacterName;
            anonymous.Checked = settings.AnonymousMode;
            locked.Checked = settings.Locked;
            opacity.Value = settings.OpacityPercent;
            opacityValue.Text = settings.OpacityPercent + "%";
            backgroundOpacity.Value = settings.BackgroundOpacityPercent;
            backgroundOpacityValue.Text = settings.BackgroundOpacityPercent + "%";
            echoToggle.Checked = settings.EchoToggleEnabled;
            echoText.Text = settings.EchoToggleText ?? "TargetMarker";
            checkUpdatesOnStartup.Checked = settings.CheckUpdatesOnStartup;
            UpdateEchoHint();
            FillGrid(markerGrid, new[] { "Attack", "Bind", "Stop", "Shape" }, settings.MarkerPriorities);
            FillGrid(roleGrid, new[] { "Tank", "Healer", "Melee", "Ranged", "Caster", "Other" }, settings.RolePriorities);
            FillGrid(jobGrid, JobCatalog.All.Select(x => x.Abbreviation), settings.JobPriorities);

            enabled.CheckedChanged += Changed;
            hideEmpty.CheckedChanged += Changed;
            showName.CheckedChanged += Changed;
            anonymous.CheckedChanged += Changed;
            locked.CheckedChanged += Changed;
            opacity.ValueChanged += Changed;
            backgroundOpacity.ValueChanged += Changed;
            echoToggle.CheckedChanged += Changed;
            echoText.TextChanged += Changed;
            checkUpdatesOnStartup.CheckedChanged += Changed;
            languageInput.SelectedIndexChanged += LanguageChanged;
            sortMode.SelectedIndexChanged += Changed;
            markerGrid.CellValueChanged += GridChanged;
            roleGrid.CellValueChanged += GridChanged;
            jobGrid.CellValueChanged += GridChanged;
            tracker.Activity += TrackerOnActivity;
            ApplyLanguage();
            loading = false;
        }

        private TabPage BuildOverlayTab()
        {
            displayPage = new TabPage { BackColor = Color.White, Padding = new Padding(4), Tag = "Display" };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 16, Padding = new Padding(12), AutoScroll = true };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 235));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.Controls.Add(LocalizedLabel("Language"), 0, 0);
            layout.Controls.Add(languageInput, 1, 0);
            AddWide(layout, enabled, 1);
            AddWide(layout, hideEmpty, 2);
            AddWide(layout, showName, 3);
            AddWide(layout, anonymous, 4);
            AddWide(layout, locked, 5);
            overlayOpacityLabel = LocalizedLabel("OverlayOpacity");
            backgroundOpacityLabel = LocalizedLabel("BackgroundOpacity");
            layout.Controls.Add(overlayOpacityLabel, 0, 6);
            layout.Controls.Add(SliderPanel(opacity, opacityValue), 1, 6);
            layout.Controls.Add(backgroundOpacityLabel, 0, 7);
            layout.Controls.Add(SliderPanel(backgroundOpacity, backgroundOpacityValue), 1, 7);
            for (var i = 0; i < 6; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            echoTitleLabel = LocalizedLabel("EchoTitle");
            echoTitleLabel.Font = new Font("Yu Gothic UI Semibold", 10f, FontStyle.Bold);
            echoTitleLabel.ForeColor = Color.FromArgb(48, 61, 80);
            layout.Controls.Add(echoTitleLabel, 0, 8);
            layout.SetColumnSpan(echoTitleLabel, 2);
            AddWide(layout, echoToggle, 9);
            echoTextLabel = LocalizedLabel("EchoText");
            layout.Controls.Add(echoTextLabel, 0, 10);
            var echoPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty };
            echoPanel.Controls.Add(echoText);
            echoHint.Margin = new Padding(8, 5, 0, 0);
            echoPanel.Controls.Add(echoHint);
            layout.Controls.Add(echoPanel, 1, 10);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
            clearButton = new Button { AutoSize = true, Tag = "ClearDisplay" };
            StyleButton(clearButton, false);
            clearButton.Click += (s, e) => tracker.Clear(Localization.Get(settings.Language, "StateCleared"));
            buttons.Controls.Add(clearButton);
            layout.Controls.Add(buttons, 0, 11); layout.SetColumnSpan(buttons, 2);

            resizeHint = new Label
            {
                Dock = DockStyle.Fill, AutoSize = false,
                Tag = "ResizeHint",
                ForeColor = Color.FromArgb(80, 80, 88), Padding = new Padding(0, 8, 0, 0),
            };
            layout.Controls.Add(resizeHint, 0, 12); layout.SetColumnSpan(resizeHint, 2);
            layout.Controls.Add(activity, 0, 13); layout.SetColumnSpan(activity, 2);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            displayPage.Controls.Add(layout);
            return displayPage;
        }

        private TabPage BuildSortTab()
        {
            sortPage = new TabPage { BackColor = Color.White, Padding = new Padding(4), Tag = "Sort" };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3, Padding = new Padding(10) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            sortPrimaryLabel = LocalizedLabel("SortPrimary");
            layout.Controls.Add(sortPrimaryLabel, 0, 0);
            layout.Controls.Add(sortMode, 1, 0); layout.SetColumnSpan(sortMode, 2);
            markerHeader = Header("MarkerPriority"); roleHeader = Header("RolePriority"); jobHeader = Header("JobPriority");
            layout.Controls.Add(markerHeader, 0, 1);
            layout.Controls.Add(roleHeader, 1, 1);
            layout.Controls.Add(jobHeader, 2, 1);
            layout.Controls.Add(markerGrid, 0, 2);
            layout.Controls.Add(roleGrid, 1, 2);
            layout.Controls.Add(jobGrid, 2, 2);
            sortPage.Controls.Add(layout);
            return sortPage;
        }

        private TabPage BuildSupportTab()
        {
            supportPage = new TabPage { BackColor = Color.White, Padding = new Padding(20), Tag = "Support" };
            var card = new TableLayoutPanel
            {
                BackColor = Color.White,
                Dock = DockStyle.Top,
                Height = 330,
                ColumnCount = 1,
                RowCount = 6,
                Padding = new Padding(30, 26, 30, 24),
            };
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            supportTitleLabel = new Label
            {
                Tag = "SupportTitle",
                Dock = DockStyle.Fill,
                Font = new Font("Yu Gothic UI Semibold", 17f, FontStyle.Bold),
                ForeColor = Color.FromArgb(166, 83, 15),
                TextAlign = ContentAlignment.MiddleLeft,
            };
            supportDescriptionLabel = new Label
            {
                Tag = "SupportDescription",
                Dock = DockStyle.Fill,
                Font = new Font("Yu Gothic UI", 10f),
                ForeColor = Color.FromArgb(65, 70, 80),
                TextAlign = ContentAlignment.MiddleLeft,
            };
            supportAssuranceLabel = new Label
            {
                Tag = "SupportAssurance",
                Dock = DockStyle.Fill,
                Font = new Font("Yu Gothic UI Semibold", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(77, 90, 108),
                TextAlign = ContentAlignment.MiddleCenter,
            };
            supportButton = new Button
            {
                Tag = "SupportButton",
                Anchor = AnchorStyles.Left,
                Size = new Size(360, 50),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(202, 101, 20),
                ForeColor = Color.White,
                Font = new Font("Yu Gothic UI Semibold", 11f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
            };
            supportButton.FlatAppearance.BorderColor = Color.FromArgb(164, 79, 12);
            supportButton.FlatAppearance.BorderSize = 1;
            supportButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(224, 126, 31);
            supportButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(174, 83, 13);
            var urlLabel = new Label
            {
                Text = SupportUrl,
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(145, 98, 45),
                TextAlign = ContentAlignment.MiddleCenter,
            };
            supportStatusLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(79, 94, 112),
                TextAlign = ContentAlignment.TopCenter,
            };
            supportButton.Click += (s, e) => OpenSupportLink(supportStatusLabel);
            card.Paint += (s, e) =>
            {
                using (var pen = new Pen(Color.FromArgb(224, 173, 91), 2f))
                    e.Graphics.DrawRectangle(pen, 1, 1, card.Width - 3, card.Height - 3);
            };
            card.Controls.Add(supportTitleLabel, 0, 0);
            card.Controls.Add(supportDescriptionLabel, 0, 1);
            card.Controls.Add(supportAssuranceLabel, 0, 2);
            card.Controls.Add(supportButton, 0, 3);
            card.Controls.Add(urlLabel, 0, 4);
            card.Controls.Add(supportStatusLabel, 0, 5);
            supportPage.Controls.Add(card);
            return supportPage;
        }

        private TabPage BuildUpdateTab()
        {
            updatePage = new TabPage { BackColor = Color.White, Padding = new Padding(20), Tag = "Update" };
            var card = new TableLayoutPanel { BackColor = Color.White, Dock = DockStyle.Top, Height = 390, ColumnCount = 1, RowCount = 8, Padding = new Padding(28, 22, 28, 20) };
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            updateTitleLabel = new Label { Tag = "UpdateTitle", Dock = DockStyle.Fill, Font = new Font("Yu Gothic UI Semibold", 15f, FontStyle.Bold), ForeColor = Color.FromArgb(38, 53, 73), TextAlign = ContentAlignment.MiddleLeft };
            checkUpdatesOnStartup.Dock = DockStyle.Fill;
            updateCurrentLabel = new Label { Dock = DockStyle.Fill, ForeColor = Color.FromArgb(75, 88, 107), TextAlign = ContentAlignment.MiddleLeft };
            updateLatestLabel = new Label { Dock = DockStyle.Fill, ForeColor = Color.FromArgb(75, 88, 107), TextAlign = ContentAlignment.MiddleLeft };
            updateStatusLabel = new Label { Dock = DockStyle.Fill, ForeColor = Color.FromArgb(58, 83, 112), TextAlign = ContentAlignment.MiddleLeft };
            updateNotesLabel = new Label { Dock = DockStyle.Fill, ForeColor = Color.FromArgb(72, 78, 89), AutoEllipsis = true };
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            checkNowButton = new Button { Tag = "CheckNow", AutoSize = true }; StyleButton(checkNowButton, false);
            updateNowButton = new Button { Tag = "UpdateNow", AutoSize = true, Enabled = false }; StyleButton(updateNowButton, true);
            laterButton = new Button { Tag = "Later", AutoSize = true, Enabled = false }; StyleButton(laterButton, false);
            checkNowButton.Click += (s, e) => CheckForUpdatesRequested?.Invoke(this, EventArgs.Empty);
            updateNowButton.Click += (s, e) => InstallUpdateRequested?.Invoke(this, EventArgs.Empty);
            laterButton.Click += (s, e) => PostponeUpdateRequested?.Invoke(this, EventArgs.Empty);
            actions.Controls.Add(checkNowButton); actions.Controls.Add(updateNowButton); actions.Controls.Add(laterButton);
            card.Controls.Add(updateTitleLabel, 0, 0);
            card.Controls.Add(checkUpdatesOnStartup, 0, 1);
            card.Controls.Add(updateCurrentLabel, 0, 2);
            card.Controls.Add(updateLatestLabel, 0, 3);
            card.Controls.Add(updateStatusLabel, 0, 4);
            card.Controls.Add(new Label { Tag = "ReleaseNotes", Dock = DockStyle.Fill, Font = new Font("Yu Gothic UI Semibold", 9f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }, 0, 5);
            card.Controls.Add(updateNotesLabel, 0, 6);
            card.Controls.Add(actions, 0, 7);
            updatePage.Controls.Add(card);
            return updatePage;
        }

        private void OpenSupportLink(Label status)
        {
            try
            {
                var uri = new Uri(SupportUrl, UriKind.Absolute);
                if (uri.Scheme != Uri.UriSchemeHttps || !string.Equals(uri.Host, "ko-fi.com", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(Localization.Get(settings.Language, "SafeLinkError"));
                Process.Start(new ProcessStartInfo { FileName = uri.AbsoluteUri, UseShellExecute = true });
                status.ForeColor = Color.FromArgb(52, 126, 78);
                status.Text = Localization.Get(settings.Language, "LinkOpened");
            }
            catch (Exception ex)
            {
                status.ForeColor = Color.FromArgb(176, 67, 52);
                status.Text = Localization.Get(settings.Language, "LinkFailed", ex.Message);
            }
        }

        private void DrawTab(object sender, DrawItemEventArgs e)
        {
            var tabs = (TabControl)sender;
            var support = tabs.TabPages[e.Index] == supportPage;
            var selected = e.Index == tabs.SelectedIndex;
            var hovered = e.Index == hoveredTabIndex;
            var background = selected ? Color.White : Color.FromArgb(240, 242, 245);
            var foreground = Color.FromArgb(42, 53, 69);
            if (support)
            {
                background = selected ? Color.FromArgb(255, 245, 226) : hovered ? Color.FromArgb(255, 238, 207) : Color.FromArgb(250, 231, 197);
                foreground = selected ? Color.FromArgb(137, 62, 7) : hovered ? Color.FromArgb(157, 72, 8) : Color.FromArgb(174, 84, 13);
            }
            else if (hovered && !selected) background = Color.FromArgb(247, 248, 250);
            using (var brush = new SolidBrush(background)) e.Graphics.FillRectangle(brush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, tabs.Font, e.Bounds, foreground,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            if (support && selected)
            {
                using (var pen = new Pen(Color.FromArgb(213, 137, 32), 2f))
                    e.Graphics.DrawLine(pen, e.Bounds.Left + 4, e.Bounds.Bottom - 2, e.Bounds.Right - 4, e.Bounds.Bottom - 2);
            }
        }

        private void UpdateHoveredTab(TabControl tabs, Point location)
        {
            var next = -1;
            for (var index = 0; index < tabs.TabCount; index++)
                if (tabs.GetTabRect(index).Contains(location)) { next = index; break; }
            if (next == hoveredTabIndex) return;
            hoveredTabIndex = next;
            tabs.Invalidate();
        }

        private Control BuildHeader()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(18, 11, 18, 8), Margin = new Padding(0, 0, 0, 10) };
            panel.Paint += (s, e) =>
            {
                using (var pen = new Pen(Color.FromArgb(202, 165, 87), 2f))
                    e.Graphics.DrawLine(pen, 0, panel.Height - 2, panel.Width, panel.Height - 2);
            };
            var title = new Label
            {
                Text = "Target Marker Overlay",
                Font = new Font("Yu Gothic UI Semibold", 17f, FontStyle.Bold),
                ForeColor = Color.FromArgb(34, 44, 61),
                AutoSize = true,
                Location = new Point(18, 9),
            };
            headerSubtitle = new Label
            {
                Tag = "HeaderSubtitle",
                Font = new Font("Yu Gothic UI", 8.5f),
                ForeColor = Color.FromArgb(103, 116, 137),
                AutoSize = true,
                Location = new Point(20, 43),
            };
            panel.Controls.Add(title);
            panel.Controls.Add(headerSubtitle);
            return panel;
        }

        private void ApplyVisualTheme()
        {
            foreach (var check in new[] { enabled, hideEmpty, showName, anonymous, locked, echoToggle })
                check.ForeColor = Color.FromArgb(48, 61, 80);
            opacityValue.ForeColor = Color.FromArgb(71, 84, 103);
            backgroundOpacityValue.ForeColor = Color.FromArgb(71, 84, 103);
            activity.ForeColor = Color.FromArgb(42, 109, 158);
            activity.Font = new Font("Yu Gothic UI Semibold", 8.5f);
            StyleGrid(markerGrid);
            StyleGrid(roleGrid);
            StyleGrid(jobGrid);
        }

        private static void StyleGrid(DataGridView grid)
        {
            grid.BorderStyle = BorderStyle.None;
            grid.BackgroundColor = Color.White;
            grid.GridColor = Color.FromArgb(224, 229, 237);
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(43, 54, 72);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(244, 229, 194);
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Yu Gothic UI Semibold", 8.5f, FontStyle.Bold);
            grid.ColumnHeadersHeight = 31;
            grid.DefaultCellStyle.BackColor = Color.White;
            grid.DefaultCellStyle.ForeColor = Color.FromArgb(50, 63, 82);
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(224, 233, 245);
            grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(34, 48, 68);
            grid.RowTemplate.Height = 27;
        }

        private static void StyleButton(Button button, bool primary)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = primary ? Color.FromArgb(164, 127, 58) : Color.FromArgb(171, 181, 196);
            button.BackColor = primary ? Color.FromArgb(48, 55, 69) : Color.White;
            button.ForeColor = primary ? Color.FromArgb(244, 225, 183) : Color.FromArgb(63, 76, 95);
            button.Font = new Font("Yu Gothic UI Semibold", 8.5f, FontStyle.Bold);
            button.Padding = new Padding(9, 3, 9, 3);
            button.Cursor = Cursors.Hand;
        }

        private void Changed(object sender, EventArgs e)
        {
            if (loading || stopped) return;
            lock (settings)
            {
                if (stopped) return;
                settings.OverlayEnabled = enabled.Checked;
                settings.HideWhenEmpty = hideEmpty.Checked;
                settings.ShowCharacterName = showName.Checked;
                settings.AnonymousMode = anonymous.Checked;
                settings.Locked = locked.Checked;
                settings.OpacityPercent = opacity.Value;
                settings.BackgroundOpacityPercent = backgroundOpacity.Value;
                settings.EchoToggleEnabled = echoToggle.Checked;
                settings.EchoToggleText = echoText.Text.Trim();
                settings.CheckUpdatesOnStartup = checkUpdatesOnStartup.Checked;
                settings.SortMode = (SortMode)Math.Max(0, sortMode.SelectedIndex);
            }
            opacityValue.Text = opacity.Value + "%";
            backgroundOpacityValue.Text = backgroundOpacity.Value + "%";
            UpdateEchoHint();
            RaiseChanged();
        }

        public void SetOverlayEnabled(bool value)
        {
            if (stopped) return;
            if (InvokeRequired) { BeginInvoke(new Action<bool>(SetOverlayEnabled), value); return; }
            loading = true;
            enabled.Checked = value;
            loading = false;
        }

        private void UpdateEchoHint()
        {
            echoHint.Text = Localization.Get(settings.Language, "EchoExample", string.IsNullOrWhiteSpace(echoText.Text) ? "TargetMarker" : echoText.Text.Trim());
        }

        private void LanguageChanged(object sender, EventArgs e)
        {
            if (loading || stopped) return;
            lock (settings)
            {
                if (stopped) return;
                settings.Language = LanguageCode(languageInput.SelectedIndex);
            }
            tracker.Language = settings.Language;
            ApplyLanguage();
            RaiseChanged();
        }

        public void ApplyLanguage()
        {
            var previousLoading = loading;
            loading = true;
            lock (settings) settings.Language = Localization.Normalize(settings.Language);
            ApplyLocalizedText(this);
            var selected = sortMode.Items.Count == 0 ? (int)settings.SortMode : Math.Max(0, sortMode.SelectedIndex);
            sortMode.Items.Clear();
            sortMode.Items.AddRange(new object[]
            {
                Localization.Get(settings.Language, "SortMarker"),
                Localization.Get(settings.Language, "SortRole"),
                Localization.Get(settings.Language, "SortJob")
            });
            sortMode.SelectedIndex = Math.Min(selected, sortMode.Items.Count - 1);
            markerGrid.Columns[0].HeaderText = Localization.Get(settings.Language, "ColumnItem");
            markerGrid.Columns[1].HeaderText = Localization.Get(settings.Language, "ColumnPriority");
            roleGrid.Columns[0].HeaderText = Localization.Get(settings.Language, "ColumnItem");
            roleGrid.Columns[1].HeaderText = Localization.Get(settings.Language, "ColumnPriority");
            jobGrid.Columns[0].HeaderText = Localization.Get(settings.Language, "ColumnItem");
            jobGrid.Columns[1].HeaderText = Localization.Get(settings.Language, "ColumnPriority");
            LocalizeGridKeys(markerGrid);
            LocalizeGridKeys(roleGrid);
            markerHeader.Text = Localization.Get(settings.Language, "LowerFirst", Localization.Get(settings.Language, "MarkerPriority"));
            roleHeader.Text = Localization.Get(settings.Language, "LowerFirst", Localization.Get(settings.Language, "RolePriority"));
            jobHeader.Text = Localization.Get(settings.Language, "LowerFirst", Localization.Get(settings.Language, "JobPriority"));
            updateCurrentLabel.Text = Localization.Get(settings.Language, "CurrentVersion", GitHubUpdateService.CurrentVersion);
            if (!string.IsNullOrWhiteSpace(updateInstallKey)) RenderInstallState();
            else if (lastUpdateResult != null) RenderUpdateResult();
            else updateStatusLabel.Text = Localization.Get(settings.Language, UpdateConfiguration.IsConfigured ? "UpdateChecking" : "UpdateRepoMissing");
            supportStatusLabel.Text = "";
            UpdateEchoHint();
            tabs.Invalidate();
            loading = previousLoading;
        }

        private void ApplyLocalizedText(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                var key = control.Tag as string;
                if (!string.IsNullOrWhiteSpace(key)) control.Text = Localization.Get(settings.Language, key);
                ApplyLocalizedText(control);
            }
        }

        public void ShowUpdateChecking()
        {
            updateStatusLabel.ForeColor = Color.FromArgb(58, 83, 112);
            updateStatusLabel.Text = Localization.Get(settings.Language, "UpdateChecking");
            checkNowButton.Enabled = false; updateNowButton.Enabled = false; laterButton.Enabled = false;
        }

        public void ShowUpdateResult(UpdateCheckResult result)
        {
            lastUpdateResult = result;
            updateInstallKey = null;
            RenderUpdateResult();
        }

        private void RenderUpdateResult()
        {
            var result = lastUpdateResult;
            checkNowButton.Enabled = true;
            updateNowButton.Enabled = result != null && result.UpdateAvailable && result.Release?.Package != null && result.Release.HashManifest != null;
            laterButton.Enabled = result != null && result.UpdateAvailable;
            updateLatestLabel.Text = result?.Release == null ? "" : Localization.Get(settings.Language, "LatestVersion", result.Release.Version);
            updateNotesLabel.Text = result?.Release?.Notes ?? "";
            if (result == null || !result.IsConfigured) updateStatusLabel.Text = Localization.Get(settings.Language, "UpdateRepoMissing");
            else if (!string.IsNullOrWhiteSpace(result.Error)) updateStatusLabel.Text = Localization.Get(settings.Language, "UpdateFailed", result.Error);
            else if (result.UpdateAvailable) updateStatusLabel.Text = Localization.Get(settings.Language, "UpdateAvailable", GitHubUpdateService.CurrentVersion, result.Release.Version);
            else updateStatusLabel.Text = Localization.Get(settings.Language, "UpdateNone");
            updateStatusLabel.ForeColor = result != null && result.UpdateAvailable ? Color.FromArgb(181, 91, 20) : Color.FromArgb(58, 83, 112);
        }

        public void ShowUpdateInstallState(string key, string detail = null, bool error = false)
        {
            updateInstallKey = key; updateInstallDetail = detail; updateInstallError = error;
            RenderInstallState();
        }

        private void RenderInstallState()
        {
            updateStatusLabel.Text = Localization.Get(settings.Language, updateInstallKey, updateInstallDetail ?? "");
            updateStatusLabel.ForeColor = updateInstallError ? Color.FromArgb(176, 67, 52) : Color.FromArgb(52, 126, 78);
            checkNowButton.Enabled = !string.Equals(updateInstallKey, "UpdateDownloading", StringComparison.Ordinal);
            updateNowButton.Enabled = false;
        }

        private void GridChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (loading || stopped || e.RowIndex < 0 || e.ColumnIndex != 1) return;
            lock (settings)
            {
                if (stopped) return;
                settings.MarkerPriorities = ReadGrid(markerGrid);
                settings.RolePriorities = ReadGrid(roleGrid);
                settings.JobPriorities = ReadGrid(jobGrid);
            }
            RaiseChanged();
        }

        public void StopAcceptingInput()
        {
            lock (settings) stopped = true;
        }

        private void RaiseChanged() => SettingsChanged?.Invoke(this, EventArgs.Empty);

        private void TrackerOnActivity(object sender, string text)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(() => { if (!IsDisposed && !Disposing) activity.Text = DateTime.Now.ToString("HH:mm:ss") + "  " + text; })); }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
                return;
            }
            activity.Text = DateTime.Now.ToString("HH:mm:ss") + "  " + text;
        }

        private static void FillGrid(DataGridView grid, IEnumerable<string> keys, List<PriorityEntry> values)
        {
            foreach (var key in keys)
            {
                var entry = values.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
                var index = grid.Rows.Add(key, entry?.Value ?? 9999);
                grid.Rows[index].Tag = key;
            }
        }

        private static List<PriorityEntry> ReadGrid(DataGridView grid)
        {
            var result = new List<PriorityEntry>();
            foreach (DataGridViewRow row in grid.Rows)
            {
                int value;
                int.TryParse(Convert.ToString(row.Cells[1].Value), out value);
                result.Add(new PriorityEntry(Convert.ToString(row.Tag ?? row.Cells[0].Value), value));
            }
            return result;
        }

        private static DataGridView Grid()
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.CellSelect, BackgroundColor = SystemColors.Window,
            };
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Item", ReadOnly = true, FillWeight = 62 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Priority", FillWeight = 38, ValueType = typeof(int) });
            return grid;
        }

        private static CheckBox Check(string key) => new CheckBox { Tag = key, AutoSize = true, Anchor = AnchorStyles.Left };
        private static Label LocalizedLabel(string key) => new Label { Tag = key, AutoSize = true, Anchor = AnchorStyles.Left };
        private static TrackBar Slider(int minimum) => new TrackBar { Minimum = minimum, Maximum = 100, TickFrequency = 10, Width = 190, Height = 38, Anchor = AnchorStyles.Left };
        private static Control SliderPanel(TrackBar slider, Label value)
        {
            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty };
            value.Margin = new Padding(4, 10, 0, 0);
            panel.Controls.Add(slider);
            panel.Controls.Add(value);
            return panel;
        }
        private static Label Header(string key) => new Label { Tag = key, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        private static void AddWide(TableLayoutPanel layout, Control control, int row) { layout.Controls.Add(control, 0, row); layout.SetColumnSpan(control, 2); }

        private static int LanguageIndex(string language)
        {
            switch (Localization.Normalize(language)) { case Localization.Japanese: return 1; case Localization.Chinese: return 2; case Localization.Korean: return 3; default: return 0; }
        }

        private static string LanguageCode(int index)
        {
            switch (index) { case 1: return Localization.Japanese; case 2: return Localization.Chinese; case 3: return Localization.Korean; default: return Localization.English; }
        }

        private void LocalizeGridKeys(DataGridView grid)
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                var key = Convert.ToString(row.Tag);
                if (!string.IsNullOrWhiteSpace(key)) row.Cells[0].Value = Localization.Get(settings.Language, key);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                tracker.Activity -= TrackerOnActivity;
            }
            base.Dispose(disposing);
        }
    }
}
