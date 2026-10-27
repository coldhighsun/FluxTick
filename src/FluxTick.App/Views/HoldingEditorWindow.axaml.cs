namespace FluxTick.App.Views;

/// <summary>
/// The window in which the user says what they hold of one instrument and what it cost.
/// </summary>
public sealed partial class HoldingEditorWindow : ToolWindow
{
    /// <summary>
    /// The width in device independent pixels. The numbers and the currency are short, so the window is narrower than
    /// the other tool windows.
    /// </summary>
    private const double NarrowWidth = 300;

    /// <summary>
    /// Initializes a new instance of the <see cref="HoldingEditorWindow"/> class.
    /// </summary>
    public HoldingEditorWindow()
        : base(NarrowWidth)
    {
        InitializeComponent();

        // The window is as tall as its form and cannot be resized.
        MinHeight = 0;
        Height = double.NaN;
        SizeToContent = Avalonia.Controls.SizeToContent.Height;
        CanResize = false;
    }
}
