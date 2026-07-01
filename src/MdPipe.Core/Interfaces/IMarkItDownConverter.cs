using MdPipe.Core.Models;

namespace MdPipe.Core.Interfaces;

public interface IMarkItDownConverter
{
    Task<ConversionResult> ConvertAsync(ConversionRequest request, CancellationToken cancellationToken = default);
}
