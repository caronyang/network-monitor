using NetworkMonitor.Core;

namespace NetworkMonitor.UI;

/// <summary>
/// Small modal question dialog: process monitoring cannot run without elevation -
/// offers to restart the app as administrator right now.
/// </summary>
public sealed class AdminPromptDialog : Form, ILocalizable
{
    private readonly Label _body = new();
    private readonly Button _yes = new();
    private readonly Button _no = new();

    public AdminPromptDialog()
    {
        SuspendLayout();

        Name = "AdminPrompt";
        Text = Loc.T("proc.name");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(420, 132);
        BackColor = Color.White;
        Font = new Font("Segoe UI", 9f);
        TopMost = true;

        _body.SetBounds(14, 14, 392, 58);
        _body.Text = Loc.T("proc.adminBody");
        _body.ForeColor = Color.FromArgb(45, 62, 80);

        _yes.SetBounds(238, 88, 170, 30);
        _yes.Text = Loc.T("proc.adminYes");
        _yes.FlatStyle = FlatStyle.Flat;
        _yes.BackColor = Color.FromArgb(48, 120, 198);
        _yes.ForeColor = Color.White;
        _yes.FlatAppearance.BorderSize = 0;
        _yes.Cursor = Cursors.Hand;
        _yes.DialogResult = DialogResult.Yes;

        _no.SetBounds(134, 88, 96, 30);
        _no.Text = Loc.T("common.later");
        _no.FlatStyle = FlatStyle.Flat;
        _no.FlatAppearance.BorderColor = Color.FromArgb(214, 222, 232);
        _no.ForeColor = Color.FromArgb(45, 62, 80);
        _no.Cursor = Cursors.Hand;
        _no.DialogResult = DialogResult.Cancel;

        Controls.Add(_body);
        Controls.Add(_yes);
        Controls.Add(_no);
        CancelButton = _no;
        AcceptButton = _yes;

        ApplyLocalization();
        ResumeLayout(false);
    }

    public void ApplyLocalization()
    {
        Text = Loc.T("proc.name");
        _body.Text = Loc.T("proc.adminBody");
        _yes.Text = Loc.T("proc.adminYes");
        _no.Text = Loc.T("common.later");
    }
}
