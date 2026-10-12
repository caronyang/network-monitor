using System.Diagnostics;
using NetworkMonitor.Core;

namespace NetworkMonitor.UI;

/// <summary>
/// Modal list of the processes currently running (grouped by image name, system noise
/// filtered out). The user selects entries to add to the monitor list; the dialog refuses
/// selections that would exceed the hard cap of 5 monitored processes.
/// </summary>
public sealed class ProcessPickerDialog : Form, ILocalizable
{
    private readonly TextBox _search = new();
    private readonly Label _searchLabel = new();
    private readonly Label _hint = new();
    private readonly ListView _list = new();
    private readonly Button _btnAdd = new();
    private readonly Button _btnCancel = new();

    private readonly HashSet<string> _existing;

    private sealed record Entry(string Name, int Instances)
    {
        public string Display => $"{Name}.exe";
    }

    /// <summary>Canonical names the user added (lowercase, no extension).</summary>
    public List<string> SelectedNames { get; } = new();

    public ProcessPickerDialog(IEnumerable<string> existing)
    {
        _existing = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);

        SuspendLayout();

        Name = "ProcessPicker";
        Text = Loc.T("picker.title");
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(380, 420);
        Size = new Size(440, 520);
        ShowInTaskbar = false;
        BackColor = Color.White;
        KeyPreview = true;

        var top = new Panel { Dock = DockStyle.Top, Height = 76, Padding = new Padding(10, 8, 10, 6), BackColor = Color.FromArgb(247, 249, 252) };

        _searchLabel.AutoSize = true;
        _searchLabel.Text = Loc.T("picker.search");
        _searchLabel.Location = new System.Drawing.Point(0, 12);
        _searchLabel.Font = new Font("Segoe UI", 9f);

        _search.SetBounds(56, 8, 200, 26);
        _search.Font = new Font("Segoe UI", 9f);
        _search.TextChanged += (_, _) => RebuildList();

        _hint.AutoSize = false;
        _hint.SetBounds(0, 42, 410, 26);
        _hint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _hint.Text = Loc.T("picker.hint");
        _hint.ForeColor = Color.FromArgb(96, 110, 125);
        _hint.Font = new Font("Segoe UI", 9f);

        top.Controls.Add(_searchLabel);
        top.Controls.Add(_search);
        top.Controls.Add(_hint);

        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.MultiSelect = true;
        _list.HideSelection = false;
        _list.Font = new Font("Segoe UI", 9f);
        _list.BorderStyle = BorderStyle.None;
        _list.BackColor = Color.White;
        _list.Columns.Add(Loc.T("proc.name"), 260);
        _list.Columns.Add(Loc.T("picker.instances"), 90, HorizontalAlignment.Right);
        _list.DoubleClick += (_, _) => AcceptSelection();

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(10, 8, 10, 8), BackColor = Color.FromArgb(247, 249, 252) };

        StyleButton(_btnAdd, Loc.T("picker.add"));
        _btnAdd.Click += (_, _) => AcceptSelection();
        StyleButton(_btnCancel, Loc.T("common.cancel"));
        _btnCancel.Click += (_, _) => Close();

        var rightFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
        };
        rightFlow.Controls.Add(_btnAdd);    // first in RTL = rightmost
        rightFlow.Controls.Add(_btnCancel);
        bottom.Controls.Add(rightFlow);

        Controls.Add(_list);
        Controls.Add(top);
        Controls.Add(bottom);

        CancelButton = _btnCancel;
        AcceptButton = _btnAdd;

        RebuildList();
        ApplyLocalization();
        ResumeLayout(false);
    }

    private static void StyleButton(Button b, string text)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.BorderColor = Color.FromArgb(214, 222, 232);
        b.BackColor = Color.White;
        b.ForeColor = Color.FromArgb(45, 62, 80);
        b.Font = new Font("Segoe UI", 9f);
        b.AutoSize = true;
        b.Padding = new Padding(12, 4, 12, 4);
        b.Margin = new Padding(6, 0, 0, 0);
        b.Cursor = Cursors.Hand;
        b.Text = text;
    }

    public void ApplyLocalization()
    {
        Text = Loc.T("picker.title");
        _searchLabel.Text = Loc.T("picker.search");
        _hint.Text = Loc.T("picker.hint");
        _btnAdd.Text = Loc.T("picker.add");
        _btnCancel.Text = Loc.T("common.cancel");
        if (_list.Columns.Count >= 2)
        {
            _list.Columns[0].Text = Loc.T("proc.name");
            _list.Columns[1].Text = Loc.T("picker.instances");
        }
    }

    private void RebuildList()
    {
        string filter = _search.Text.Trim();
        _list.BeginUpdate();
        _list.Items.Clear();

        List<Entry> entries;
        try
        {
            entries = Process.GetProcesses()
                .Where(p => p.Id != 0 && p.Id != 4 && p.Id != Environment.ProcessId)
                .GroupBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase)
                .Select(g => new Entry(g.Key.ToLowerInvariant(), g.Count()))
                .OrderByDescending(e => e.Instances)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            entries = new List<Entry>();
        }

        foreach (var e in entries)
        {
            if (_existing.Contains(e.Name)) continue;
            if (filter.Length > 0 && !e.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;

            var item = new ListViewItem(e.Display) { Tag = e.Name };
            item.SubItems.Add(e.Instances.ToString());
            _list.Items.Add(item);
        }

        _list.EndUpdate();
    }

    private void AcceptSelection()
    {
        var picked = _list.SelectedItems.Cast<ListViewItem>()
            .Select(it => it.Tag as string)
            .Where(n => !string.IsNullOrEmpty(n))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (picked.Count == 0)
        {
            MessageBox.Show(this, Loc.T("picker.nothingNew"), Loc.T("picker.title"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_existing.Count + picked.Count > AppSettings.MaxProcessMonitors)
        {
            MessageBox.Show(this, Loc.T("proc.limit"), Loc.T("picker.title"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SelectedNames.AddRange(picked);
        DialogResult = DialogResult.OK;
        Close();
    }
}
