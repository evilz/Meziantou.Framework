namespace Meziantou.Framework;

public sealed class CommandLineParser
{
    private static readonly string[] HelpArguments = { "-?", "/?", "-help", "/help", "--help" };

    private readonly IDictionary<string, string> _namedArguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private readonly IDictionary<int, string> _positionArguments = new Dictionary<int, string>();

    public static CommandLineParser Current { get; } = ParseCurrent();

    private static CommandLineParser ParseCurrent()
    {
        var parser = new CommandLineParser();
        parser.Parse(Environment.GetCommandLineArgs());
        return parser;
    }

    public void Parse(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.IsNullOrEmpty(arg))
                continue;

            if (arg.All(c => c == ' '))
            {
                _positionArguments[i] = arg;
                continue;
            }

            arg = arg.TrimStart();
            if (string.IsNullOrEmpty(arg))
                continue;

            if (IsHelpArgument(arg))
            {
                HelpRequested = true;
                continue;
            }

            if (arg[0] == '-' || arg[0] == '/')
            {
                arg = arg[1..];
                var indexOfSeparator = arg.IndexOfAny(new[] { ':', '=' });

                var name = arg;
                var value = string.Empty;
                if (indexOfSeparator >= 0)
                {
                    name = arg[..indexOfSeparator].Trim();
                    value = arg[(indexOfSeparator + 1)..];
                }

                _namedArguments[name] = value;
            }

            _positionArguments[i] = arg;
        }
    }

    public bool HelpRequested { get; private set; }

    public bool HasArgument(string name)
    {
        return _namedArguments.ContainsKey(name);
    }

    public string? GetArgument(string name)
    {
        if (_namedArguments.TryGetValue(name, out var value))
            return value;

        return null;
    }

    public string? GetArgument(int position)
    {
        if (_positionArguments.TryGetValue(position, out var value))
            return value;

        return null;
    }

    private static bool IsHelpArgument(string arg)
    {
        return HelpArguments.Contains(arg, StringComparer.OrdinalIgnoreCase);
    }
}
