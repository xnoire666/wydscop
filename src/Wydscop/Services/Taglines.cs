namespace Wydscop.Services;

/// <summary>
/// The rotating placeholder lines in the empty search bar. Shuffled, and each line is shown once
/// before any repeats. Keep them short (about 35 characters) so they fit the default bar width.
/// </summary>
internal sealed class Taglines
{
    private static readonly string[] All =
    [
        "What are we looking for, bestie?",
        "Spill it, what's the search?",
        "No cap, I'll find it",
        "Lowkey lost something? Type it",
        "It's giving… search era",
        "Main character search energy",
        "Find it fr fr",
        "Say less, just type",
        "Your files are in their hiding era",
        "Slay your search",
        "No thoughts, just search",
        "Real ones search here",
        "Lost an app? Not on my watch",
        "This search hits different",
        "POV: you find it on the first try",
        "Understood the assignment. Type it",
        "Vibe check: what are we finding?",
        "Delulu? No. Searching? Yes.",
        "Low effort, high results",
        "Bet. What do you need?",
        "It's the finding for me",
        "Gatekeeping files? Not today",
        "Manifesting that one file",
        "Search so fast it's lowkey rude",
    ];

    private readonly Random _random = new();
    private readonly List<string> _order = new();
    private int _index = -1;

    public Taglines() => Shuffle();

    /// <summary>The line currently shown.</summary>
    public string Current => _order[Math.Max(_index, 0)];

    /// <summary>Moves to the next line; reshuffles after every line has had its turn.</summary>
    public string Next()
    {
        _index++;
        if (_index >= _order.Count)
        {
            string last = _order[^1];
            Shuffle();
            if (_order[0] == last) (_order[0], _order[^1]) = (_order[^1], _order[0]); // no back-to-back repeat
            _index = 0;
        }
        return _order[_index];
    }

    private void Shuffle()
    {
        _order.Clear();
        _order.AddRange(All);
        for (int i = _order.Count - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (_order[i], _order[j]) = (_order[j], _order[i]);
        }
    }
}
