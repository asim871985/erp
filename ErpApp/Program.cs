using ErpApp.Data;
using ErpApp.Forms;

namespace ErpApp;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        try
        {
            AppConfig.Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to load appsettings.json:\n" + ex.Message,
                "Startup Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        using var login = new LoginForm();
        if (login.ShowDialog() != DialogResult.OK)
            return; // user cancelled/exited, or exceeded failed login attempts

        Application.Run(new MainForm());
    }
}
