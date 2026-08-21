using System.CommandLine;
using MdPipe.Core.Services;

namespace MdPipe.Cli.Commands;

/// <summary>
/// Lists what MdPipe can convert, asked of the engine installed on this machine rather than read
/// from a list kept by hand.
/// </summary>
public static class FormatsCommand
{
    public static Command Build(FormatCatalogProvider formats)
    {
        var command = new Command("formats", "List the file types MdPipe can convert");

        command.SetAction(_ =>
        {
            var catalog = formats.Get();

            Console.WriteLine(catalog.IsBaseline
                ? "Formats MdPipe ships knowing about. Run 'mdpipe setup' to read them from the engine itself:"
                : $"Formats MarkItDown {catalog.EngineVersion} can read on this machine:");
            Console.WriteLine();

            foreach (var row in catalog.Extensions.Chunk(8))
                Console.WriteLine("  " + string.Join("  ", row));

            Console.WriteLine();
            Console.WriteLine($"  {catalog.Extensions.Count} formats.");

            if (catalog.Converters.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Handled by:");
                foreach (var converter in catalog.Converters.OrderBy(c => c.Name, StringComparer.Ordinal))
                    Console.WriteLine($"  {converter.Name,-22} {string.Join(" ", converter.Extensions)}");
            }

            Console.WriteLine();
            Console.WriteLine("Naming a file directly converts it whatever its extension. When scanning a folder,");
            Console.WriteLine("add --all-files to try everything and let the engine decide by content.");

            return 0;
        });

        return command;
    }
}
