namespace ErpApp.Forms;

/// <summary>
/// Base class for every form in the app. Makes the Enter key move focus to the next
/// control in tab order, the way Tab does, instead of the WinForms default (which does
/// nothing unless a control specifically handles it or the form has an AcceptButton).
///
/// Enter is left alone (does its normal thing) for:
/// - Multiline text boxes (Remarks/Description/Narration fields) — Enter inserts a newline.
/// - Buttons — Enter/Space already clicks a focused button; don't fight that. This also means
///   tabbing focus onto a button via Enter does NOT auto-click it — a deliberate choice, since
///   auto-clicking whatever happens to be next in tab order is one keystroke away from
///   triggering the wrong button (e.g. "Exit" instead of "Save") depending on button order.
///   Reaching a button this way just focuses it; press Enter/Space again (or click) to activate.
/// - DataGridView — Enter already commits the cell and moves down/across; grid navigation
///   should not be hijacked into tabbing out of the grid entirely.
/// </summary>
public class AppFormBase : Form
{
    protected AppFormBase()
    {
        KeyPreview = true;
        KeyDown += AppFormBase_KeyDown;
    }

    private void AppFormBase_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter) return;

        var active = ActiveControl;
        if (active == null) return;
        if (active is TextBoxBase { Multiline: true }) return;
        if (active is Button) return;
        if (active is DataGridView) return;

        e.Handled = true;
        e.SuppressKeyPress = true; // stop the default "ding"
        SelectNextControl(active, true, true, true, true);
    }
}
