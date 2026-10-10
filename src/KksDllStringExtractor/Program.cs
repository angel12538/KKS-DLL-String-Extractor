using System.Text;
using KksDllStringExtractor;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;
Environment.ExitCode = ConsoleApplication.Run(args, Console.In, Console.Out, Console.Error, AppContext.BaseDirectory);
