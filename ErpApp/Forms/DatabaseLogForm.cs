using System.Data;
using ErpApp.Data;

namespace ErpApp.Forms;

/// <summary>Tools > Database Log. Read-only browser for the database_log table, filterable by date/user/text.</summary>
public class DatabaseLogForm : AppFormBase
{
    private readonly DateTimePicker dtFrom = new() { Format = DateTimePickerFormat.Short };
    private readonly DateTimePicker dtTo = new() { Format = DateTimePickerFormat.Short };
    private readonly ComboBox cboUser = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox txtSearch = new() { Width = 220 };
    private readonly DataGridView grid = new();
    private readonly Label lblCount = new() { AutoSize = true, ForeColor = Color.Gray };

    public DatabaseLogForm()
    {
        Text = "Database Log";
        Width = 950;
        Height = 600;
        StartPosition = FormStartPosition.CenterParent;

        BuildLayout();
        LoadUsers();
        RunReport();
    }

    private void BuildLayout()
    {
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 45, Padding = new Padding(8) };
        top.Controls.Add(new Label { Text = "From", AutoSize = true, Padding = new Padding(0, 8, 5, 0) });
        dtFrom.Value = DateTime.Today.AddDays(-7);
        top.Controls.Add(dtFrom);
        top.Controls.Add(new Label { Text = "To", AutoSize = true, Padding = new Padding(10, 8, 5, 0) });
        dtTo.Value = DateTime.Today;
        top.Controls.Add(dtTo);
        top.Controls.Add(new Label { Text = "User", AutoSize = true, Padding = new Padding(10, 8, 5, 0) });
        cboUser.Width = 150;
        top.Controls.Add(cboUser);
        top.Controls.Add(new Label { Text = "Search", AutoSize = true, Padding = new Padding(10, 8, 5, 0) });
        top.Controls.Add(txtSearch);
        var btnSearch = new Button { Text = "Search" };
        btnSearch.Click += (s, e) => RunReport();
        top.Controls.Add(btnSearch);
        var btnExport = new Button { Text = "Export" };
        btnExport.Click += BtnExport_Click;
        top.Controls.Add(btnExport);

        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        lblCount.Dock = DockStyle.Bottom;
        lblCount.Padding = new Padding(8, 4, 0, 4);

        Controls.Add(grid);
        Controls.Add(lblCount);
        Controls.Add(top);
    }

    private void LoadUsers()
    {
        try
        {
            var users = DbHelper.ExecuteQuery("SELECT DISTINCT username FROM database_log WHERE username IS NOT NULL ORDER BY username");
            cboUser.Items.Add("All");
            foreach (DataRow r in users.Rows) cboUser.Items.Add(r["username"].ToString()!);
            cboUser.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not load users: " + ex.Message);
        }
    }

    private void RunReport()
    {
        try
        {
            string sql = @"
                SELECT log_time AS ""Time"", username AS ""User"", action AS ""Action""
                FROM database_log
                WHERE log_time::date BETWEEN @from AND @to";
            var pars = new Dictionary<string, object?> { ["from"] = dtFrom.Value.Date, ["to"] = dtTo.Value.Date };

            if (cboUser.SelectedItem?.ToString() is string u && u != "All")
            {
                sql += " AND username=@u";
                pars["u"] = u;
            }
            if (!string.IsNullOrWhiteSpace(txtSearch.Text))
            {
                sql += " AND action ILIKE @s";
                pars["s"] = $"%{txtSearch.Text.Trim()}%";
            }
            sql += " ORDER BY log_time DESC LIMIT 2000";

            var table = DbHelper.ExecuteQuery(sql, pars);
            grid.DataSource = table;
            lblCount.Text = $"{table.Rows.Count} log entries" + (table.Rows.Count == 2000 ? " (showing most recent 2000 — narrow the date range for more)" : "");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not load log: " + ex.Message);
        }
    }

    private void BtnExport_Click(object? sender, EventArgs e)
    {
        if (grid.DataSource is not DataTable table) return;
        using var sfd = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = "database_log.csv" };
        if (sfd.ShowDialog() != DialogResult.OK) return;

        using var writer = new StreamWriter(sfd.FileName);
        writer.WriteLine(string.Join(",", table.Columns.Cast<DataColumn>().Select(c => c.ColumnName)));
        foreach (DataRow row in table.Rows)
            writer.WriteLine(string.Join(",", row.ItemArray.Select(v => $"\"{v}\"")));
        MessageBox.Show("Exported to " + sfd.FileName);
    }
}
