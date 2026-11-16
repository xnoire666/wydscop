using System.ComponentModel;
using System.Windows.Media;

namespace Wydscop.Search;

public enum ResultKind
{
    App,
    File,
    Folder,

    /// <summary>Not a thing to open but an action in the list, like "Show more".</summary>
    Command,

    /// <summary>A section heading in the list ("Recent", "Pinned"); can't be highlighted or opened.</summary>
    Header,
}

/// <summary>One row in the results list. Providers (apps, files... later settings, calculator) create these.</summary>
public sealed class SearchResult : INotifyPropertyChanged
{
    private ImageSource? _icon;

    public SearchResult(string id, ResultKind kind, string title, string subtitle, Action execute, Action? reveal = null)
    {
        Id = id;
        Kind = kind;
        Title = title;
        Subtitle = subtitle;
        Execute = execute;
        Reveal = reveal;
    }

    /// <summary>Stable key, used for usage ranking (app id or full path).</summary>
    public string Id { get; }

    public ResultKind Kind { get; }

    /// <summary>Folders get a › to look inside them from the search bar.</summary>
    public bool IsFolder => Kind == ResultKind.Folder;

    /// <summary>A row in the Recent list: gets a ✕ to remove it from the list.</summary>
    public bool IsRecent { get; init; }

    /// <summary>An action row such as "Show more"; Enter or click runs it instead of opening something.</summary>
    public bool IsCommand => Kind == ResultKind.Command;

    /// <summary>A section heading ("Recent", "Pinned").</summary>
    public bool IsHeader => Kind == ResultKind.Header;

    /// <summary>Things that can be pinned: apps, files and folders (not headings or "Show more").</summary>
    public bool CanPin => Kind is ResultKind.App or ResultKind.File or ResultKind.Folder;

    private bool _isPinned;

    /// <summary>Pinned to the empty bar: its row shows a red pin.</summary>
    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (_isPinned == value) return;
            _isPinned = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPinned)));
        }
    }

    private bool _isMarked;

    /// <summary>Ctrl+clicked: one of several items Enter opens together. Its row is tinted.</summary>
    public bool IsMarked
    {
        get => _isMarked;
        set
        {
            if (_isMarked == value) return;
            _isMarked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMarked)));
        }
    }

    public static SearchResult Heading(string title) =>
        new("wydscop:heading:" + title, ResultKind.Header, title, string.Empty, () => { });

    private string? _headerAction;

    /// <summary>
    /// Clickable text on the right of a heading, like "Clear all" on Recent. Null for plain headings.
    /// Changes text in place (e.g. to ask for a second click), so it notifies.
    /// </summary>
    public string? HeaderAction
    {
        get => _headerAction;
        set
        {
            if (_headerAction == value) return;
            _headerAction = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HeaderAction)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasHeaderAction)));
        }
    }

    public bool HasHeaderAction => _headerAction is not null;

    /// <summary>
    /// A copy for the Recent list. App results are shared with normal search, so the ✕ flag must not be
    /// set on them directly. The copy follows the original's icon if it is still loading.
    /// </summary>
    public SearchResult AsRecent()
    {
        var copy = new SearchResult(Id, Kind, Title, Subtitle, Execute, Reveal) { IsRecent = true, Icon = Icon, IsPinned = IsPinned };
        copy.SetDate(_date, _dateIsCreated);
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Icon)) copy.Icon = Icon;
            if (e.PropertyName == nameof(DateText)) copy.SetDate(_date, _dateIsCreated);
        };
        return copy;
    }

    // ---------- Date (modified, or created when there's no modified date) ----------

    private DateTime? _date;
    private bool _dateIsCreated;

    /// <summary>When it was last modified, or created if it has no modified date (UTC). Null when unknown.</summary>
    public DateTime? Date => _date;

    /// <summary>Uses the modified date when there is one, otherwise the creation date.</summary>
    public void SetDates(DateTime modifiedUtc, DateTime createdUtc)
    {
        if (IsReal(modifiedUtc)) SetDate(modifiedUtc, isCreated: false);
        else if (IsReal(createdUtc)) SetDate(createdUtc, isCreated: true);
    }

    private void SetDate(DateTime? date, bool isCreated)
    {
        _date = date;
        _dateIsCreated = isCreated;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DateText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DateToolTip)));
    }

    private static bool IsReal(DateTime d) => d.Year > 1980 && d < DateTime.UtcNow.AddYears(1);

    /// <summary>"Today 3:41 PM", "Yesterday 9:05 AM", "12 Nov 3:41 PM", or "12 Nov 2024 3:41 PM".</summary>
    public string DateText
    {
        get
        {
            if (_date is not { } utc) return string.Empty;
            var local = utc.ToLocalTime();
            var today = DateTime.Today;
            string time = local.ToString("t");
            if (local.Date == today) return $"Today {time}";
            if (local.Date == today.AddDays(-1)) return $"Yesterday {time}";
            return local.ToString(local.Year == today.Year ? "d MMM" : "d MMM yyyy") + " " + time;
        }
    }

    /// <summary>"Modified Monday, 17 November 2026 3:41 PM" (or "Created …").</summary>
    public string? DateToolTip =>
        _date is { } utc ? (_dateIsCreated ? "Created " : "Modified ") + utc.ToLocalTime().ToString("f") : null;

    public string Title { get; }

    /// <summary>"Application", or the folder a file is in.</summary>
    public string Subtitle { get; }

    /// <summary>Enter / click.</summary>
    public Action Execute { get; }

    /// <summary>Ctrl+Enter: show it in File Explorer. Null when that doesn't apply.</summary>
    public Action? Reveal { get; }

    /// <summary>How closely this matches the current query (Matcher score); the list is ordered by this, then newest date.</summary>
    internal double Score { get; set; }

    internal string? IconPath { get; set; }

    internal string? IconKey { get; set; }

    /// <summary>Loaded in the background; the row updates when it arrives. Must be frozen.</summary>
    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            _icon = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
