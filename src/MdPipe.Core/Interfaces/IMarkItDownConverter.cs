using MdPipe.Core.Models;

namespace MdPipe.Core.Interfaces;

public interface IMarkItDownConverter
{
    Task<ConversionResult> ConvertAsync(ConversionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts a whole batch, yielding each result as it lands so callers can show progress.
    /// Results come back in the order the requests were given.
    /// </summary>
    IAsyncEnumerable<ConversionResult> ConvertManyAsync(
        IReadOnlyList<ConversionRequest> requests, CancellationToken cancellationToken = default);
}
