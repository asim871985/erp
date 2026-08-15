using System.Diagnostics;
using ErpApp.Data;

namespace ErpApp.Forms;

/// <summary>
/// Tools > Data Backup. Shells out to the PostgreSQL <c>pg_dump</c> command-line tool (must be
/// on PATH — it ships alongside every PostgreSQL server/client install) to produce a plain-SQL
/// backup file. Password is passed via the PGPASSWORD environment variable so it never appears
/// on the visible command line.
/// </summary>
public class DataBackupForm : AppFormBase
{
    private readonly TextBox txtPath = new() { ReadOnly = true };
    private readonly TextBox txtLog = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9) };
    private readonly Label lblStatus = new() { AutoSize = true };
    private readonly Button btnRun = new() { Text = "Run Backup", Enabled = false };

    private string? chosenPath;

    public DataBackupForm()
    {
        Text = "Data Backup";
        Width = 650;
        Height = 480;
        StartPosition = FormStartPosition.CenterParent;

        BuildLayout();
    }

    private void BuildLayout()
    {
        var info = new Label
        {
            Text = "Creates a full SQL backup of the current database using pg_dump.\n" +
                   "Requires the PostgreSQL command-line tools (pg_dump) to be installed and on PATH.",
            Dock = DockStyle.Top,
            Height = 45,
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
        using var sfd = new SaveFileDialog
        {
            Filter = "SQL backup files (*.sql)|*.sql",
            FileName = $"erp_backup_{DateTime.Now:yyyyMMdd_HHmmss}.sql"
        };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;

        chosenPath = sfd.FileName;
        txtPath.Text = chosenPath;
        btnRun.Enabled = true;
    }

    private void BtnRun_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(chosenPath)) return;
        btnRun.Enabled = false;
        lblStatus.Text = "Running backup...";
        lblStatus.ForeColor = Color.DimGray;
        txtLog.Clear();

        try
        {
            var (host, port, db, user, password) = DbHelper.GetConnectionParts();

            var psi = new ProcessStartInfo
            {
                FileName = "pg_dump",
                Arguments = $"-h {host} -p {port} -U {user} -d {db} -f \"{chosenPath}\" --no-owner --no-privileges",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            psi.Environment["PGPASSWORD"] = password;

            using var process = Process.Start(psi);
            if (process == null) throw new Exception("Could not start pg_dump.");

            string stderr = process.StandardError.ReadToEnd();
            string stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            txtLog.Text = string.IsNullOrWhiteSpace(stderr) ? "(no output)" : stderr;
            if (!string.IsNullOrWhiteSpace(stdout)) txtLog.Text += "\r\n" + stdout;

            if (process.ExitCode == 0)
            {
                lblStatus.Text = "\u2713 Backup completed: " + chosenPath;
                lblStatus.ForeColor = Color.SeaGreen;
                DbHelper.LogAction($"Data Backup: Created {chosenPath}");
            }
            else
            {
                lblStatus.Text = $"\u26A0 pg_dump exited with code {process.ExitCode}. See log below.";
                lblStatus.ForeColor = Color.Firebrick;
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            lblStatus.Text = "\u26A0 pg_dump was not found on PATH.";
            lblStatus.ForeColor = Color.Firebrick;
            txtLog.Text = "Install the PostgreSQL command-line tools and make sure the folder containing " +
                           "pg_dump.exe (e.g. C:\\Program Files\\PostgreSQL\\<version>\\bin) is on your system PATH, " +
                           "then try again.";
        }
        catch (Exception ex)
        {
            lblStatus.Text = "\u26A0 Backup failed.";
            lblStatus.ForeColor = Color.Firebrick;
            txtLog.Text = ex.Message;
        }
        finally
        {
            btnRun.Enabled = true;
        }
    }
}
