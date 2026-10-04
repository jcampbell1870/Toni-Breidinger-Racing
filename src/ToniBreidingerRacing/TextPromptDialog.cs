namespace ToniBreidingerRacing;

/// <summary>A small modal dialog used to enter the A1870 wallet address.</summary>
internal sealed class TextPromptDialog : Form
{
    private readonly TextBox _textBox;

    public TextPromptDialog(string title, string message, string initialValue, int maxLength)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(460, 140);

        var label = new Label { Text = message, Location = new Point(12, 12), Size = new Size(436, 44) };
        _textBox = new TextBox
        {
            Location = new Point(12, 62),
            Size = new Size(436, 23),
            MaxLength = maxLength,
            Text = initialValue,
        };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(292, 100), Size = new Size(75, 27) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(373, 100), Size = new Size(75, 27) };

        Controls.AddRange([label, _textBox, ok, cancel]);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public string Value => _textBox.Text;
}
