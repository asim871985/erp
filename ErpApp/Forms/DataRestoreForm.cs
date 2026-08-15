using System.Diagnostics;
using ErpApp.Data;

namespace ErpApp.Forms;

/// <summary>
/// Tools > Restore Backup. Shells out to the PostgreSQL <c>psql</c> command-line tool (must be
/// on PATH) to run a plain-SQL backup file against the current database. This does NOT drop the
/// database first — a restore of a full pg_dump file will recreate tables it dumped and insert
/// their data, which can fail with "already exists" errors on a non-empty database. It's meant
/// for restoring into a freshly created empty database.
/// </summary>
public class DataRestoreForm : AppFormBase
{
    private readonly TextBox txtPath = new() { ReadOnly = true };
    private readonly TextBox txtLog = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9) };
    private readonly Label lblStatus = new() { AutoSize = true };
    private readonly Button btnRun = new() { Text = "Run Restore", Enabled = false };

    private string? chosenPath;

    public DataRestoreForm()
    {
        Text = "Restore Backup";
        Width = 650;
        Height = 480;
        StartPosition = FormStartPosition.CenterParent;

        BuildLayout();
    }

    private void BuildLayout()
    {
        var info = new Label
        {
            Text = "Runs a .sql backup file against the current database using psql.\n" +
                   "Best used on a freshly created, empty database — restoring into a database that " +
                   "already has data can produce \"already exists\" errors for duplicate rows/tables.",
            Dock = DockStyle.Top,
            Height = 55,
            Padding = new Padding(10, 8, 10, 0),
            ForeColor = Color.DimGray
        };

        var pathPanel = new TableLayoutPanel { Dock = DockStyle.Top, Height = 45, ColumnCount = 2, Padding = new Padding(10) };
        pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 80));
        pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        txtPath.Dock = DockStyle.Fill;
        pathPanel.Controls.Add(txtPath, 0, 0);
        var btnBrowse = new Button { Text = "Choose File...", Dock = DockStyle.Fill };
        btnBrowse.Click += BtnBrowse_Click;
        pathPanel.Controls.Add(btnBrowse, 1, 0);

        var runPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(10, 0, 10, 0) };
        btnRun.Click += BtnRun_Click;
        runPanel.Controls.Add(btnRun);

        lblStatus.Dock = DockStyle.Top;
        lblStatus.Padding = new Padding(10, 4, 10, 4);

        txtLog.Dock = DockStyle.Fill;

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(10), FlowDirection = FlowDirection.RightToLeft };
        var btnClose = new Button { Text = "Close" };
        btnClose.Click += (s, e) => Close();
        btnPanel.Controls.Add(btnClose);

        Controls.Add(txtLog);
        Controls.Add(btnPanel);
        Controls.Add(lblStatus);
        Controls.Add(runPanel);
        Controls.Add(pathPanel);
        Controls.Add(info);
    }

    private void BtnBrowse_Click(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog { Filter = "SQL backup files (*.sql)|*.sql" };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;

        chosenPath = ofd.FileName;
        txtPath.Text = chosenPath;
        btnRun.Enabled = true;
    }

    private void BtnRun_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(chosenPath)) return;

        var (host, port, db, _, _) = DbHelper.GetConnectionParts();
        var confirm = MessageBox.Show(
            $"This will run \"{Path.GetFileName(chosenPath)}\" against database \"{db}\" on {host}:{port}.\n\n" +
            "This cannot be undone. Continue?",
            "Confirm Restore", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        btnRun.Enabled = false;
        lblStatus.Text = "Running restore...";
        lblStatus.ForeColor = Color.DimGray;
        txtLog.Clear();

        try
        {
            var (h, p, d, user, password) = DbHelper.GetConnectionParts();

            var psi = new ProcessStartInfo
            {
                FileName = "psql",
                Arguments = $"-h {h} -p {p} -U {user} -d {d} -f \"{chosenPath}\"",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            psi.Environment["PGPASSWORD"] = password;

            using var process = Process.Start(psi);
            if (process == null) throw new Exception("Could not start psql.");

            string stderr = process.StandardError.ReadToEnd();
            string stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            txtLog.Text = stdout;
            if (!string.IsNullOrWhiteSpace(stderr)) txtLog.Text += "\r\n--- stderr ---\r\n" + stderr;

            if (process.ExitCode == 0)
            {
                lblStatus.Text = "\u2713 Restore completed.";
                lblStatus.ForeColor = Color.SeaGreen;
                DbHelper.LogAction($"Data Restore: Ran {chosenPath}");
            }
            else
            {
                lblStatus.Text = $"\u26A0 psql exited with code {process.ExitCode} — check the log below (some errors, like duplicate rows, may be expected on a non-empty database).";
                lblStatus.ForeColor = Color.Firebrick;
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            lblStatus.Text = "\u26A0 psql was not found on PATH.";
            lblStatus.ForeColor = Color.Firebrick;
            txtLog.Text = "Install the PostgreSQL command-line tools and make sure the folder containing " +
                           "psql.exe (e.g. C:\\Program Files\\PostgreSQL\\<version>\\bin) is on your system PATH, " +
                           "then try again.";
        }
        catch (Exception ex)
        {
            lblStatus.Text = "\u26A0 Restore failed.";
            lblStatus.ForeColor = Color.Firebrick;
            txtLog.Text = ex.Message;
        }
        finally
        {
            btnRun.Enabled = true;
        }
    }
}
