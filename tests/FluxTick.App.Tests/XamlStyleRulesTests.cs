using System.Xml.Linq;

namespace FluxTick.App.Tests;

/// <summary>
/// Guards against values set on an element that styles are meant to change. A value on the element always beats a
/// style, so the style silently does nothing; this happened to the cursor of the resize grip and to the drop
/// highlight of the new group placeholder. The tests read the XAML files of the application.
/// </summary>
public class XamlStyleRulesTests
{
    /// <summary>
    /// Loads a XAML file of the application's views. The test is skipped when the sources are not next to the build
    /// output, for example when the tests run from a copy of it.
    /// </summary>
    /// <param name="name">
    /// The file name, for example <c>MainWindow.axaml</c>.
    /// </param>
    /// <returns>
    /// The parsed document.
    /// </returns>
    private static XDocument Load(string name) => SourceFiles.LoadXaml("Views", name);

    /// <summary>
    /// Finds the elements with a given name and a style class.
    /// </summary>
    /// <param name="document">
    /// The document to search.
    /// </param>
    /// <param name="element">
    /// The element name.
    /// </param>
    /// <param name="styleClass">
    /// The class the element must have.
    /// </param>
    /// <returns>
    /// The matching elements.
    /// </returns>
    private static List<XElement> WithClass(XDocument document, string element, string styleClass) =>
        [.. document.Descendants().Where(e => e.Name.LocalName == element
            && ((string?)e.Attribute("Classes") ?? string.Empty).Split(' ').Contains(styleClass))];

    /// <summary>
    /// Verifies the resize grip leaves its cursor to the styles, which change it in the list layout.
    /// </summary>
    [Fact]
    public void ResizeGrip_HasNoCursorOfItsOwn()
    {
        var grips = WithClass(Load("MainWindow.axaml"), "Border", "grip");

        grips.Should().ContainSingle().Which.Attribute("Cursor").Should().BeNull();
    }

    /// <summary>
    /// Verifies the outline of the new group placeholder leaves colour and thickness to the styles, which change
    /// them while a card is dragged over it.
    /// </summary>
    [Fact]
    public void NewGroupOutline_HasNoStrokeOfItsOwn()
    {
        var outlines = WithClass(Load("NewGroupPlaceholder.axaml"), "Rectangle", "outline");

        var outline = outlines.Should().ContainSingle().Subject;
        outline.Attribute("Stroke").Should().BeNull();
        outline.Attribute("StrokeThickness").Should().BeNull();
    }

    /// <summary>
    /// Verifies every icon button of the header has the narrow width on the element itself, which is the only way to
    /// be sure it beats the width of the icon style.
    /// </summary>
    [Fact]
    public void HeaderIconButtons_HaveTheirWidthOnTheElement()
    {
        var buttons = WithClass(Load("MainWindow.axaml"), "Button", "icon");

        buttons.Should().NotBeEmpty();
        buttons.Should().OnlyContain(b => (string?)b.Attribute("Width") == "24");
    }

    /// <summary>
    /// Verifies the two numbers of the roll of the price, in a row and in a card, carry a real translate transform, and
    /// that they share a clipped box. The roll animates the translation; a transform written as a string, such as
    /// "translate(0,0)", is another kind of transform, which the animation cannot change: it never ends and nothing
    /// moves. Without the clipping the numbers would drift out over their neighbours.
    /// </summary>
    /// <param name="view">
    /// The view file.
    /// </param>
    [Theory]
    [InlineData("AssetRowView.axaml")]
    [InlineData("AssetCardView.axaml")]
    public void PriceNumbers_HaveATranslateTransformElementAndAClippedBox(string view)
    {
        var document = Load(view);
        foreach (var name in new[] { "PriceText", "PriceOld" })
        {
            var price = document.Descendants()
                .Single(e => e.Name.LocalName == "TextBlock" && (string?)e.Attribute("Name") == name);

            price.Attribute("RenderTransform").Should().BeNull();
            price.Elements().Should().Contain(e => e.Name.LocalName == "TextBlock.RenderTransform"
                && e.Elements().Any(t => t.Name.LocalName == "TranslateTransform"));
        }

        var old = document.Descendants().Single(e => (string?)e.Attribute("Name") == "PriceOld");
        old.Parent!.Attribute("ClipToBounds")!.Value.Should().Be("True");
        old.Attribute("IsVisible")!.Value.Should().Be("False", "the old price takes no room while it is not rolling");
    }

    /// <summary>
    /// Verifies the window leaves its widths to WindowWidthRules, so that there is one place to change them.
    /// </summary>
    [Fact]
    public void MainWindow_DoesNotDefineItsOwnWidths()
    {
        var window = Load("MainWindow.axaml").Root!;

        window.Attribute("Width").Should().BeNull();
        window.Attribute("MinWidth").Should().BeNull();
        window.Attribute("MaxWidth").Should().BeNull();
    }
}
