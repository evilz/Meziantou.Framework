using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Meziantou.Framework.Globbing;
using Microsoft.AspNetCore.StaticFiles;

[assembly: InternalsVisibleTo("Meziantou.Framework.Html.Tool.Tests")]

namespace Meziantou.Framework.Html.Tool;

internal static class Program
{
    public static Task<int> Main(string[] args)
    {
        return MainImpl(args, console: null);
    }

    internal static Task<int> MainImpl(string[] args, IConsole? console)
    {
        var rootCommand = new RootCommand() { Name = "meziantou.html" }; // Name must match <ToolCommandName> in csproj
        AddReplaceValueCommand(rootCommand);
        AddAppendVersionCommand(rootCommand);
        InlineResourceCommand(rootCommand);
        return rootCommand.InvokeAsync(args, console);
    }

    private static void AddReplaceValueCommand(RootCommand rootCommand)
    {
        var singleFileOption = new Option<string>("--single-file", description: "Path of the file to update") { IsRequired = false };
        var filePatternOption = new Option<string>("--file-pattern", description: "Glob pattern to find files to update") { IsRequired = false };
        var rootDirectoryOption = new Option<string>("--root-directory", description: "Root directory for glob pattern") { IsRequired = false };
        var xpathOption = new Option<string>("--xpath", "XPath to the elements/attributes to replace") { IsRequired = true };
        var newValueOption = new Option<string>("--new-value", "New value for the elements/attributes") { IsRequired = true };

        var replaceValueCommand = new Command("replace-value")
        {
            Description = "Replace element/attribute values in an html file",
        };
        replaceValueCommand.AddOption(singleFileOption);
        replaceValueCommand.AddOption(filePatternOption);
        replaceValueCommand.AddOption(rootDirectoryOption);
        replaceValueCommand.AddOption(xpathOption);
        replaceValueCommand.AddOption(newValueOption);

        replaceValueCommand.SetHandler(
            (string? singleFile, string? filePattern, string? rootDirectory, string xpath, string newValue) => ReplaceValue(singleFile, filePattern, rootDirectory, xpath, newValue),
            singleFileOption, filePatternOption, rootDirectoryOption, xpathOption, newValueOption);

        rootCommand.AddCommand(replaceValueCommand);
    }

    private static async Task<int> ReplaceValue(string? filePath, string? globPattern, string? rootDirectory, string xpath, string newValue)
    {
        if (!string.IsNullOrEmpty(filePath))
        {
            await UpdateFileAsync(filePath, xpath, newValue);
        }

        if (!string.IsNullOrEmpty(globPattern))
        {
            if (!Glob.TryParse(globPattern, GlobOptions.None, out var glob))
            {
                await Console.Error.WriteLineAsync($"Glob pattern '{globPattern}' is invalid");
                return -1;
            }

            foreach (var file in glob.EnumerateFiles(string.IsNullOrEmpty(rootDirectory) ? Environment.CurrentDirectory : rootDirectory))
            {
                await UpdateFileAsync(file, xpath, newValue);
            }
        }

        return 0;

        static async Task UpdateFileAsync(string file, string xpath, string newValue)
        {
            var doc = new HtmlDocument();
            await using (var stream = File.OpenRead(file))
            {
                doc.Load(stream);
            }

            var count = 0;
            var nodes = doc.SelectNodes(xpath);
            foreach (var node in nodes)
            {
                node.Value = newValue;
                count++;
            }

            doc.Save(file, doc.DetectedEncoding ?? doc.StreamEncoding);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Updated {count} nodes in '{file}'"));
        }
    }

    private static void AddAppendVersionCommand(RootCommand rootCommand)
    {
        var singleFileOption = new Option<string>("--single-file", description: "Path of the file to update") { IsRequired = false };
        var filePatternOption = new Option<string>("--file-pattern", description: "Glob pattern to find files to update") { IsRequired = false };
        var rootDirectoryOption = new Option<string>("--root-directory", description: "Root directory for glob pattern") { IsRequired = false };

        var command = new Command("append-version")
        {
            Description = "Append version to style / script URLs",
        };
        command.AddOption(singleFileOption);
        command.AddOption(filePatternOption);
        command.AddOption(rootDirectoryOption);

        command.SetHandler(async (InvocationContext ctx) =>
        {
            var singleFile = ctx.ParseResult.GetValueForOption(singleFileOption);
            var filePattern = ctx.ParseResult.GetValueForOption(filePatternOption);
            var rootDirectory = ctx.ParseResult.GetValueForOption(rootDirectoryOption);
            if (!string.IsNullOrEmpty(singleFile))
            {
                await UpdateFileAsync(singleFile).ConfigureAwait(false);
            }

            if (!string.IsNullOrEmpty(filePattern))
            {
                if (!Glob.TryParse(filePattern, GlobOptions.None, out var glob))
                {
                    await Console.Error.WriteLineAsync($"Glob pattern '{filePattern}' is invalid");
                    ctx.ExitCode = -1;
                    return;
                }

                foreach (var f in glob.EnumerateFiles(string.IsNullOrEmpty(rootDirectory) ? Environment.CurrentDirectory : rootDirectory))
                {
                    await UpdateFileAsync(f).ConfigureAwait(false);
                }
            }

            return;

            static async Task UpdateFileAsync(string file)
            {
                var doc = new HtmlDocument();
                await using (var stream = File.OpenRead(file))
                {
                    doc.Load(stream);
                }

                var count = 0;
                var nodes = doc.SelectNodes("//@src|//@href|//@poster");
                foreach (var node in nodes)
                {
                    if (string.IsNullOrWhiteSpace(node.Value))
                        continue;

                    // Only consider relative path
                    if (node.Value.Contains("://", StringComparison.Ordinal) && node.Value.StartsWith("//", StringComparison.Ordinal))
                        continue;

                    string? uriPath = null;
                    string? uriQuery = null;
                    string? uriHash = null;

                    var hashIndex = node.Value.IndexOf('#', StringComparison.Ordinal);
                    var queryIndex = node.Value.IndexOf('?', StringComparison.Ordinal);
                    if (hashIndex >= 0 && queryIndex > hashIndex)
                    {
                        queryIndex = -1;
                    }

                    if (queryIndex >= 0 || hashIndex >= 0)
                    {
                        uriPath = node.Value[0..(queryIndex >= 0 ? queryIndex : hashIndex)];

                        if (queryIndex >= 0)
                        {
                            if (hashIndex >= 0)
                            {
                                uriQuery = node.Value[queryIndex..hashIndex];
                            }
                            else
                            {
                                uriQuery = node.Value[queryIndex..];
                            }
                        }

                        if (hashIndex >= 0)
                        {
                            uriHash = node.Value[hashIndex..];
                        }
                    }
                    else
                    {
                        uriPath = node.Value;
                    }

                    var parentFolder = Path.GetDirectoryName(file);
                    var assetPath = parentFolder is not null ? Path.Combine(parentFolder, uriPath) : uriPath;
                    if (!File.Exists(assetPath))
                        continue;

                    var bytes = await File.ReadAllBytesAsync(assetPath).ConfigureAwait(false);
#pragma warning disable CA1308 // Normalize strings to uppercase
                    var hash = Convert.ToHexString(SHA512.HashData(bytes))[0..6].ToLowerInvariant();
#pragma warning restore CA1308

                    if (uriQuery is null)
                    {
                        uriQuery = "?v=" + hash;
                    }
                    else
                    {
                        var index = uriQuery.IndexOf("&v=", StringComparison.Ordinal);
                        if (index < 0)
                        {
                            index = uriQuery.IndexOf("?v=", StringComparison.Ordinal);
                        }

                        if (index >= 0)
                        {
                            var endIndex = uriQuery.IndexOf('&', index + 1);
                            if (endIndex < 0)
                            {
                                uriQuery = uriQuery[0..index] + (index == 0 ? '?' : '&') + "v=" + hash;
                            }
                            else
                            {
                                uriQuery = uriQuery[0..index] + (index == 0 ? '?' : '&') + "v=" + hash + uriQuery[endIndex..];
                            }
                        }
                        else
                        {
                            uriQuery += "&v=" + hash;
                        }
                    }

                    node.Value = uriPath + uriQuery + uriHash;
                    count++;
                }

                if (count > 0)
                {
                    doc.Save(file, doc.DetectedEncoding ?? doc.StreamEncoding);
                }

                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Updated {count} nodes in '{file}'"));
            }
        });

        rootCommand.AddCommand(command);
    }

    private static void InlineResourceCommand(RootCommand rootCommand)
    {
        var singleFileOption = new Option<string>("--single-file", description: "Path of the file to update") { IsRequired = false };
        var filePatternOption = new Option<string>("--file-pattern", description: "Glob pattern to find files to update") { IsRequired = false };
        var rootDirectoryOption = new Option<string>("--root-directory", description: "Root directory for glob pattern") { IsRequired = false };
        var resourcePatternsOption = new Option<string[]>("--resource-patterns", description: "Files to inline") { IsRequired = true, AllowMultipleArgumentsPerToken = true, Arity = ArgumentArity.OneOrMore };

        var command = new Command("inline-resources")
        {
            Description = "Inline scripts, styles, and images",
        };
        command.AddOption(singleFileOption);
        command.AddOption(filePatternOption);
        command.AddOption(rootDirectoryOption);
        command.AddOption(resourcePatternsOption);

        command.SetHandler(async (InvocationContext ctx) =>
        {
            var singleFile = ctx.ParseResult.GetValueForOption(singleFileOption);
            var filePattern = ctx.ParseResult.GetValueForOption(filePatternOption);
            var rootDirectory = ctx.ParseResult.GetValueForOption(rootDirectoryOption);
            var resourcePatterns = ctx.ParseResult.GetValueForOption(resourcePatternsOption);
            if (!string.IsNullOrEmpty(singleFile))
            {
                await UpdateFileAsync(singleFile).ConfigureAwait(false);
            }

            if (!string.IsNullOrEmpty(filePattern))
            {
                if (!Glob.TryParse(filePattern, GlobOptions.None, out var glob))
                {
                    await Console.Error.WriteLineAsync($"Glob pattern '{filePattern}' is invalid");
                    ctx.ExitCode = -1;
                    return;
                }

                foreach (var f in glob.EnumerateFiles(string.IsNullOrEmpty(rootDirectory) ? Environment.CurrentDirectory : rootDirectory))
                {
                    await UpdateFileAsync(f).ConfigureAwait(false);
                }
            }

            return;

            static async Task UpdateFileAsync(string file)
            {
                var doc = new HtmlDocument();
                await using (var stream = File.OpenRead(file))
                {
                    doc.Load(stream);
                }

                var count = 0;
                var nodes = doc.SelectNodes("//@src|//@href|//@poster");
                foreach (var node in nodes)
                {
                    if (string.IsNullOrWhiteSpace(node.Value))
                        continue;

                    // Only consider relative path
                    if (node.Value.Contains("://", StringComparison.Ordinal) && node.Value.StartsWith("//", StringComparison.Ordinal))
                        continue;

                    string? uriPath = null;
                    string? uriQuery = null;
                    string? uriHash = null;

                    var hashIndex = node.Value.IndexOf('#', StringComparison.Ordinal);
                    var queryIndex = node.Value.IndexOf('?', StringComparison.Ordinal);
                    if (hashIndex >= 0 && queryIndex > hashIndex)
                    {
                        queryIndex = -1;
                    }

                    if (queryIndex >= 0 || hashIndex >= 0)
                    {
                        uriPath = node.Value[0..(queryIndex >= 0 ? queryIndex : hashIndex)];

                        if (queryIndex >= 0)
                        {
                            if (hashIndex >= 0)
                            {
                                uriQuery = node.Value[queryIndex..hashIndex];
                            }
                            else
                            {
                                uriQuery = node.Value[queryIndex..];
                            }
                        }

                        if (hashIndex >= 0)
                        {
                            uriHash = node.Value[hashIndex..];
                        }
                    }
                    else
                    {
                        uriPath = node.Value;
                    }

                    var parentFolder = Path.GetDirectoryName(file);
                    var assetPath = parentFolder is not null ? Path.Combine(parentFolder, uriPath) : uriPath;
                    if (!File.Exists(assetPath))
                        continue;

                    var element = node.ParentElement!;
                    if (string.Equals(element.Name, "SCRIPT", StringComparison.OrdinalIgnoreCase))
                    {
                        var text = await File.ReadAllTextAsync(assetPath).ConfigureAwait(false);
                        element.RemoveAttribute("src");
                        element.InnerText = text;

                    }
                    else if (string.Equals(element.Name, "LINK", StringComparison.OrdinalIgnoreCase))
                    {
                        var text = await File.ReadAllTextAsync(assetPath).ConfigureAwait(false);
                        element.Name = "style";
                        element.RemoveAttribute("href");
                        element.InnerText = text;
                    }
                    else
                    {
                        var bytes = await File.ReadAllBytesAsync(assetPath).ConfigureAwait(false);
                        var base64 = Convert.ToBase64String(bytes);
                        if (!new FileExtensionContentTypeProvider().TryGetContentType(assetPath, out var contentType))
                        {
                            contentType = "application/octet-stream";
                        }

                        var srcAttribute = $"data:{contentType};base64,{base64}";
                        node.Value = srcAttribute;
                    }

                    count++;
                }

                if (count > 0)
                {
                    doc.Save(file, doc.DetectedEncoding ?? doc.StreamEncoding);
                }

                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Updated {count} nodes in '{file}'"));
            }
        });

        rootCommand.AddCommand(command);
    }
}
