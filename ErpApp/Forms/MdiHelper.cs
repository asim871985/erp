namespace ErpApp.Forms;

/// <summary>
/// WinForms MDI child forms completely ignore <see cref="FormStartPosition.CenterParent"/> and
/// <see cref="FormStartPosition.CenterScreen"/> — the framework always cascades them from the
/// top-left of the MDI client area instead, regardless of what StartPosition is set to. Every
/// place in this app that opens a form as an MDI child should go through here instead of setting
/// <c>MdiParent</c> and calling <c>Show()</c> directly, so windows open centered like the rest of
/// the app's dialogs do.
/// </summary>
public static class MdiHelper
{
    /// <summary>Shows <paramref name="child"/> as an MDI child of <paramref name="mdiParent"/>, centered.</summary>
    public static void ShowCentered(Form? mdiParent, Form child)
    {
        child.MdiParent = mdiParent;
        child.Show();
        CenterInMdiClient(child);
    }

    /// <summary>Re-centers a form that's already an MDI child (e.g. after it's been shown/resized).</summary>
    public static void CenterInMdiClient(Form child)
    {
        // Once a form is an MDI child, WinForms reparents it under the MdiParent's internal
        // MdiClient control — child.Parent (not child.MdiParent) is that MdiClient, and its
        // ClientSize is the actual visible area to center within (excludes menu/status bars).
        if (child.Parent == null) return;

        var area = child.Parent.ClientSize;
        int x = Math.Max(0, (area.Width - child.Width) / 2);
        int y = Math.Max(0, (area.Height - child.Height) / 2);
        child.Location = new Point(x, y);
    }
}
