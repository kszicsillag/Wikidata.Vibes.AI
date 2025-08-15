using Spectre.Console;
using System.Collections.Generic;
using System.Text.Json;

namespace Wikidata.AlignWithMCP.SemanticKernel;

public static class ConsoleHelpers
{
    /// <summary>
    /// Prints a KernelArguments or dictionary as a Spectre.Console table.
    /// Complex or multi-line values will be JSON-formatted and indented.
    /// </summary>
    public static void PrintArgumentsTable(IDictionary<string, object?>? arguments, string? title = null)
    {
        if (arguments is null || arguments.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No arguments.[/]");
            return;
        }

        var table = new Table();
        if (title != null)
        {
            table.Title = new TableTitle(title);
        }
        
        table.AddColumn("[yellow]Argument[/]");
        table.AddColumn("[green]Value[/]");

        if (title != null)
        {
            table.AddRow("Title", title);
        }
        foreach (var kvp in arguments)
        {
            table.AddRow(kvp.Key, kvp.Value?.ToString() ?? "[grey]NULL[/]");
        }

        AnsiConsole.Write(table);
    }

    public static void PrintSimpleMessage(string message)
    {
        AnsiConsole.WriteLine(message);
    }

    public static void PrintGreetings(string message)
    {
        AnsiConsole.Write(new FigletText(message).LeftJustified());
    }
}