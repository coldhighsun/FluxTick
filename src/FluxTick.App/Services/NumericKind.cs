namespace FluxTick.App.Services;

/// <summary>
/// The kind of number a limited text box accepts.
/// </summary>
public enum NumericKind
{
    /// <summary>
    /// A non-negative real number written with the decimal separator of the culture or the point.
    /// </summary>
    Real,

    /// <summary>
    /// A non-negative number of seconds, written with the point.
    /// </summary>
    Seconds,

    /// <summary>
    /// A non-negative whole number.
    /// </summary>
    WholeNumber,

    /// <summary>
    /// A whole number that may be negative.
    /// </summary>
    SignedWholeNumber,
}
