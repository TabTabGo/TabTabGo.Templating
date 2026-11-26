namespace TabTabGo.Templating.TestKit;

/// <summary>Thrown by the golden helpers; the test framework reports it as a failure.</summary>
/// <param name="message">What differs.</param>
public sealed class GoldenMismatchException(string message) : Exception(message);
