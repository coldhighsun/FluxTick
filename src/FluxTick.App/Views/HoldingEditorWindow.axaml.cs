namespace FluxTick.App.Views;

/// <summary>
/// The window in which the user says what they hold of one instrument and what it cost.
/// </summary>
public sealed partial class HoldingEditorWindow : ToolWindow
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HoldingEditorWindow"/> class.
    /// </summary>
    public HoldingEditorWindow()
    {
        InitializeComponent();

        // The window is as tall as its form and cannot be resized.
        MinHeight = 0;
        Height = double.NaN;
        SizeToContent = Avalonia.Controls.SizeToContent.Height;
        CanResize = false;
    }
}
