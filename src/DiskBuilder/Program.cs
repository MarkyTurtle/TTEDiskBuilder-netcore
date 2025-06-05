using DiskCompiler.Commands;
using System.CommandLine;

namespace DiskCompiler
{
    internal class Program
    {
        static async Task<int> Main(string[] args)
        {
            var rootCommand = new RootCommand("Disk Compiler - A command line tool for compiling Amiga .ADF disk files.");
            CreateCommand.AddCreateCommand(rootCommand);

            return await rootCommand.InvokeAsync(args);

        }
    }
}
