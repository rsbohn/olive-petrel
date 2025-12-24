using System.Globalization;

namespace OlivePetrel;

/// <summary>
/// Interactive help system for Olive Petrel.
/// Provides a shell-like interface for browsing help topics with shortcuts, search, and navigation features.
/// </summary>
public class HelpSystem
{
    private readonly string _helpDirectory;
    private List<HelpTopic> _topics = new();
    private List<HelpTopic> _recentTopics = new();
    private readonly Random _random = new();

    public HelpSystem(string helpDirectory)
    {
        _helpDirectory = helpDirectory;
    }

    /// <summary>
    /// Starts the interactive help shell.
    /// </summary>
    public void StartHelpShell()
    {
        var helpDir = Path.GetFullPath(_helpDirectory);
        if (!Directory.Exists(helpDir))
        {
            Console.WriteLine($"Help directory not found: {helpDir}");
            return;
        }

        Console.WriteLine("✨ Welcome to the Olive Petrel help lounge! Type 'menu' to see navigation tricks.");
        _topics = LoadHelpTopics(helpDir);
        _recentTopics = new List<HelpTopic>();
        ShowMenu();

        while (true)
        {
            Console.Write("help> ");
            var line = Console.ReadLine();
            if (line is null)
            {
                break;
            }

            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("quit", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("back", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            var parts = SplitCommand(trimmed);
            if (parts.Count == 0)
            {
                continue;
            }

            var command = parts[0].ToLowerInvariant();
            switch (command)
            {
                case "list":
                case "ls":
                    ListTopics();
                    continue;
                case "menu":
                case "commands":
                case "?":
                    ShowMenu();
                    continue;
                case "search":
                case "find":
                    if (parts.Count < 2)
                    {
                        Console.WriteLine("Try: search <keyword>");
                        continue;
                    }

                    var searchText = string.Join(' ', parts.Skip(1));
                    SearchTopics(searchText);
                    continue;
                case "random":
                case "surprise":
                    if (_topics.Count == 0)
                    {
                        Console.WriteLine("No topics available yet.");
                        continue;
                    }

                    var pick = _topics[_random.Next(_topics.Count)];
                    Console.WriteLine($"🎲 Random pick: {pick.Title} ({pick.Name})");
                    DisplayTopic(pick);
                    TrackRecent(pick);
                    continue;
                case "recent":
                    ShowRecent();
                    continue;
                case "refresh":
                case "reload":
                    _topics = LoadHelpTopics(helpDir);
                    Console.WriteLine("Topics reloaded. Fresh as new paper tape.");
                    continue;
                case "q":
                case "quit":
                case "exit":
                case "back":
                    return;
            }

            var topic = ResolveTopic(parts[0]);
            if (topic is null)
            {
                ShowSuggestions(trimmed);
                continue;
            }

            DisplayTopic(topic);
            TrackRecent(topic);
        }
    }

    private List<HelpTopic> LoadHelpTopics(string helpDir)
    {
        var items = new List<HelpTopic>();
        var files = Directory.GetFiles(helpDir).OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
        var usedShortcuts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var lowerName = name.ToLowerInvariant();
            var shortcut = MakeShortcut(lowerName, usedShortcuts);
            usedShortcuts.Add(shortcut);
            var lines = LoadHelpLines(file);
            var (title, preview) = BuildTitleAndPreview(lines, lowerName);
            items.Add(new HelpTopic(lowerName, shortcut, file, title, lines, preview));
        }

        return items;
    }

    private static string MakeShortcut(string name, HashSet<string> used)
    {
        for (var len = 1; len <= name.Length; len++)
        {
            var candidate = name.Substring(0, len);
            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }

        var suffix = 2;
        while (true)
        {
            var candidate = name + suffix.ToString(CultureInfo.InvariantCulture);
            if (!used.Contains(candidate))
            {
                return candidate;
            }

            suffix++;
        }
    }

    private void ShowHelpShortcuts(List<HelpTopic> list)
    {
        if (list.Count == 0)
        {
            return;
        }

        var parts = list.Select(t => $"{t.Shortcut}:{t.Name}");
        var line = string.Join(' ', parts);
        Console.WriteLine($"{line} q:quit help search <word> random recent list menu");
    }

    private void ListTopics()
    {
        if (_topics.Count == 0)
        {
            Console.WriteLine("No help topics found.");
            return;
        }

        Console.WriteLine("Topics:");
        foreach (var t in _topics)
        {
            var previewText = string.IsNullOrWhiteSpace(t.Preview) ? string.Empty : $" - {t.Preview}";
            Console.WriteLine($"  {t.Shortcut.PadRight(8)} {t.Title} [{t.Name}]{previewText}");
        }
    }

    private void ShowMenu()
    {
        Console.WriteLine("Type a shortcut to jump to a topic, or try these:");
        Console.WriteLine("  list           Show all topics with previews");
        Console.WriteLine("  search <term>  Search topics by keyword");
        Console.WriteLine("  random         Let the lounge pick for you");
        Console.WriteLine("  recent         See what you opened lately");
        Console.WriteLine("  refresh        Reload topics from disk");
        Console.WriteLine("  exit           Return to the emulator");
    }

    private void SearchTopics(string term)
    {
        var matches = _topics
            .Select(t => new
            {
                Topic = t,
                Snippet = FindSnippet(t.Lines, term)
            })
            .Where(m => m.Snippet is not null || TopicMatches(m.Topic, term))
            .ToList();

        static bool TopicMatches(HelpTopic t, string term) =>
            t.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            t.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            t.Shortcut.Contains(term, StringComparison.OrdinalIgnoreCase);

        if (matches.Count == 0)
        {
            Console.WriteLine("No matches yet. Try another word?");
            return;
        }

        Console.WriteLine($"Found {matches.Count} topic(s):");
        foreach (var match in matches)
        {
            var snippet = match.Snippet ?? string.Empty;
            var trail = string.IsNullOrWhiteSpace(snippet) ? string.Empty : $" → {snippet}";
            Console.WriteLine($"  {match.Topic.Title} ({match.Topic.Shortcut}){trail}");
        }
    }

    private void ShowRecent()
    {
        if (_recentTopics.Count == 0)
        {
            Console.WriteLine("No recent topics yet. Open one and they'll appear here!");
            return;
        }

        Console.WriteLine("Recently opened:");
        foreach (var topic in _recentTopics)
        {
            Console.WriteLine($"  {topic.Title} ({topic.Shortcut})");
        }
    }

    private HelpTopic? ResolveTopic(string topic)
    {
        var lower = topic.ToLowerInvariant();
        var exact = _topics.FirstOrDefault(t => t.Name == lower);
        if (exact is not null)
        {
            return exact;
        }

        var exactShortcut = _topics.FirstOrDefault(t => t.Shortcut == lower);
        if (exactShortcut is not null)
        {
            return exactShortcut;
        }

        var nameMatches = _topics.Where(t => t.Name.StartsWith(lower, StringComparison.OrdinalIgnoreCase)).ToList();
        if (nameMatches.Count == 1)
        {
            return nameMatches[0];
        }

        var shortcutMatches = _topics.Where(t => t.Shortcut.StartsWith(lower, StringComparison.OrdinalIgnoreCase)).ToList();
        if (shortcutMatches.Count == 1)
        {
            return shortcutMatches[0];
        }

        return null;
    }

    private void ShowSuggestions(string raw)
    {
        var candidates = _topics
            .Select(t => new { Topic = t, Score = Distance(raw, t.Name) })
            .OrderBy(t => t.Score)
            .ThenBy(t => t.Topic.Name, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        Console.WriteLine("Topic not found.");
        if (candidates.Count == 0)
        {
            Console.WriteLine("Try 'list' to see what's available.");
            return;
        }

        Console.WriteLine("Did you mean:");
        foreach (var item in candidates)
        {
            Console.WriteLine($"  {item.Topic.Name} ({item.Topic.Shortcut})");
        }
    }

    private void DisplayTopic(HelpTopic topic)
    {
        try
        {
            Console.WriteLine($"\n--- {topic.Title} [{topic.Name}] ---");
            foreach (var helpLine in topic.Lines)
            {
                Console.WriteLine(helpLine);
            }

            Console.WriteLine(new string('-', Math.Max(24, topic.Title.Length + topic.Name.Length + 6)));
            Console.WriteLine("Tip: type 'recent' to hop back or 'search <word>' to branch out.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to read help: {ex.Message}");
        }
    }

    private void TrackRecent(HelpTopic topic)
    {
        _recentTopics.RemoveAll(t => string.Equals(t.Name, topic.Name, StringComparison.OrdinalIgnoreCase));
        _recentTopics.Insert(0, topic);
        const int MaxRecent = 5;
        if (_recentTopics.Count > MaxRecent)
        {
            _recentTopics.RemoveRange(MaxRecent, _recentTopics.Count - MaxRecent);
        }
    }

    private static string[] LoadHelpLines(string path)
    {
        try
        {
            return File.ReadAllLines(path);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static (string Title, string Preview) BuildTitleAndPreview(string[] lines, string fallback)
    {
        if (lines.Length == 0)
        {
            return (fallback, string.Empty);
        }

        var title = lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? fallback;
        var preview = string.Empty;
        var seenTitle = false;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!seenTitle)
            {
                seenTitle = true;
                continue;
            }

            var trimmed = line.Trim();
            if (IsUnderline(trimmed) ||
                string.Equals(trimmed, title, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            preview = Truncate(trimmed, 60);
            break;
        }

        return (title, preview);
    }

    private static bool IsUnderline(string line) =>
        line.Length > 0 && line.All(ch => ch == '=' || ch == '-' || ch == '~');

    private static string? FindSnippet(IEnumerable<string> lines, string term)
    {
        foreach (var line in lines)
        {
            if (line.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                return Truncate(line.Trim(), 80);
            }
        }

        return null;
    }

    private static string Truncate(string text, int max)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max)
        {
            return text;
        }

        return text.Substring(0, max - 1) + "…";
    }

    private static int Distance(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var n = a.Length;
        var m = b.Length;
        var dp = new int[n + 1, m + 1];
        for (var i = 0; i <= n; i++)
        {
            dp[i, 0] = i;
        }

        for (var j = 0; j <= m; j++)
        {
            dp[0, j] = j;
        }

        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var cost = char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;
                dp[i, j] = Math.Min(
                    Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                    dp[i - 1, j - 1] + cost);
            }
        }

        return dp[n, m];
    }

    private static List<string> SplitCommand(string command)
    {
        var parts = new List<string>();
        var current = string.Empty;
        var inQuote = false;

        foreach (var ch in command)
        {
            if (ch == '"')
            {
                inQuote = !inQuote;
            }
            else if (char.IsWhiteSpace(ch) && !inQuote)
            {
                if (current.Length > 0)
                {
                    parts.Add(current);
                    current = string.Empty;
                }
            }
            else
            {
                current += ch;
            }
        }

        if (current.Length > 0)
        {
            parts.Add(current);
        }

        return parts;
    }

    private sealed class HelpTopic
    {
        public HelpTopic(string name, string shortcut, string path, string title, string[] lines, string preview)
        {
            Name = name;
            Shortcut = shortcut;
            Path = path;
            Title = title;
            Lines = lines;
            Preview = preview;
        }

        public string Name { get; }
        public string Shortcut { get; }
        public string Path { get; }
        public string Title { get; }
        public string[] Lines { get; }
        public string Preview { get; }
    }
}
