using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Wydscop.Services;

namespace Wydscop;

public partial class SearchWindow
{
    // ---------- Rotating placeholder ----------

    // Rotating placeholder lines in the empty bar.
    private static readonly TimeSpan TaglineInterval = TimeSpan.FromSeconds(30);
    private readonly Taglines _taglines = new();
    private readonly DispatcherTimer _taglineTimer = new() { Interval = TaglineInterval };

    /// <summary>The line to show in the empty bar right now (not inside a folder).</summary>
    private void ShowFreshTagline()
    {
        Placeholder.BeginAnimation(OpacityProperty, null);
        PlaceholderShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
        Placeholder.Text = _taglines.Next();
        _taglineTimer.Stop();
        _taglineTimer.Start();
    }

    /// <summary>Every few seconds: fade the line out, swap it, and slide the next one up into place.</summary>
    private void RotateTagline()
    {
        // Only while the empty bar is showing its placeholder.
        if (!IsVisible || InFolder || SearchBox.Text.Length > 0) return;

        string next = _taglines.Next();
        if (!SystemParameters.ClientAreaAnimation) // Windows animations turned off: just swap
        {
            Placeholder.Text = next;
            return;
        }

        var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(170));
        fadeOut.Completed += (_, _) =>
        {
            if (InFolder) // you stepped into a folder meanwhile; its "Search in …" stays
            {
                Placeholder.BeginAnimation(OpacityProperty, null);
                return;
            }

            Placeholder.Text = next;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            Placeholder.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
            PlaceholderShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
                new DoubleAnimation(7, 0, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
        };
        Placeholder.BeginAnimation(OpacityProperty, fadeOut);
    }
}
