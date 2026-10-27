namespace FluxTick.Core.Application;

/// <summary>
/// The error of a rate source that answered, but with no rates the app can use.
/// </summary>
public sealed class ExchangeRateException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExchangeRateException"/> class.
    /// </summary>
    /// <param name="message">
    /// What was wrong with the answer.
    /// </param>
    public ExchangeRateException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ExchangeRateException"/> class.
    /// </summary>
    /// <param name="message">
    /// What was wrong with the answer.
    /// </param>
    /// <param name="innerException">
    /// The error that made the answer unusable.
    /// </param>
    public ExchangeRateException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
