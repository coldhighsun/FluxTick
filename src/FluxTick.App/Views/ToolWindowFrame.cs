using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace FluxTick.App.Views;

/// <summary>
/// The look of a <see cref="ToolWindow"/>: a rounded, themed frame with a header (icon, title, close button)
/// that drags the window, and a grip that resizes it. It replaces the system title bar so the window matches
/// the main window. The control template is declared in <c>Theme.axaml</c>.
/// </summary>
public sealed class ToolWindowFrame : ContentControl
{
    /// <summary>
    /// Identifies the <see cref="Title"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<ToolWindowFrame, string?>(nameof(Title));

    /// <summary>
    /// Initializes static members of the <see cref="ToolWindowFrame"/> class.
    /// </summary>
    static ToolWindowFrame()
    {
        // The frame is a control with its own automation peer, so the title becomes its accessible name there,
        // instead of on a layout container that assistive technology would skip.
        TitleProperty.Changed.AddClassHandler<ToolWindowFrame>((frame, e) => AutomationProperties.SetName(frame, e.NewValue as string));
    }

    /// <summary>
    /// Gets or sets the title shown in the header.
    /// </summary>
    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <inheritdoc />
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (e.NameScope.Find<Border>("PART_Header") is { } header)
        {
            header.PointerPressed += OnHeaderPressed;
        }

        if (e.NameScope.Find<Button>("PART_Close") is { } close)
        {
            close.Click += OnCloseClicked;
        }

        if (e.NameScope.Find<Border>("PART_Grip") is { } grip)
        {
            grip.PointerPressed += OnGripPressed;
        }
    }

    /// <summary>
    /// Starts dragging the window by its header.
    /// </summary>
    /// <param name="sender">
    /// The header.
    /// </param>
    /// <param name="e">
    /// The pointer event data.
    /// </param>
    private void OnHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window window && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            window.BeginMoveDrag(e);
        }
    }

    /// <summary>
    /// Closes the window.
    /// </summary>
    /// <param name="sender">
    /// The close button.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnCloseClicked(object? sender, RoutedEventArgs e) => (TopLevel.GetTopLevel(this) as Window)?.Close();

    /// <summary>
    /// Starts resizing from the bottom-right grip. The width is fixed, so only the height changes.
    /// </summary>
    /// <param name="sender">
    /// The grip.
    /// </param>
    /// <param name="e">
    /// The pointer event data.
    /// </param>
    private void OnGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window window && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            window.BeginResizeDrag(WindowEdge.SouthEast, e);
        }
    }
}
