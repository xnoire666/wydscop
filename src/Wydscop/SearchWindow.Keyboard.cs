using System.Windows.Input;
using Wydscop.Search;

namespace Wydscop;

public partial class SearchWindow
{
    // ---------- Keyboard ----------

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                // Rows marked with Ctrl+click: Esc unmarks them first.
                // Inside a folder: back out to your search results first.
                // Pinned: Esc clears the search instead of hiding (unpin, or Alt+Space, to hide).
                if (_marked.Count > 0) ClearMarks();
                else if (InFolder)
                {
                    while (_folderStack.Count > 1)
                    {
                        _folderStack.Pop();
                        if (_scrollStack.Count > 1) _scrollStack.Pop(); // keep the search's own scroll position
                    }
                    LeaveFolder();
                }
                else if (IsPinned) SearchBox.Clear();
                else HideBar();
                break;

            // Right on a highlighted folder (with the cursor at the end of the text): look inside it.
            case Key.Right when ResultsList.SelectedItem is SearchResult { IsFolder: true } folder
                                && SearchBox.SelectionLength == 0 && SearchBox.CaretIndex == SearchBox.Text.Length:
                EnterFolder(folder);
                break;

            // Ctrl+Space: mark or unmark the highlighted result (like Ctrl+click); Enter opens all marked.
            case Key.Space when Keyboard.Modifiers == ModifierKeys.Control:
                if (ResultsList.SelectedItem is SearchResult toMark) ToggleMark(toMark);
                break;

            // Ctrl+P: pin or unpin the highlighted result.
            case Key.P when Keyboard.Modifiers == ModifierKeys.Control:
                if (ResultsList.SelectedItem is SearchResult toPin) TogglePin(toPin);
                break;

            // Delete on a recent item (empty bar): remove it from the recent list.
            case Key.Delete when ShowingRecent && ResultsList.SelectedItem is not null:
                RemoveSelectedRecent();
                break;

            // Left (cursor at the start of the text), or Backspace with no text: back out one folder.
            case Key.Left when InFolder && SearchBox.SelectionLength == 0 && SearchBox.CaretIndex == 0:
            case Key.Back when InFolder && SearchBox.Text.Length == 0:
                LeaveFolder();
                break;
            case Key.Down:
                MoveSelection(+1);
                break;
            case Key.Up:
                MoveSelection(-1);
                break;
            case Key.PageDown:
                MoveSelection(+VisibleRows);
                break;
            case Key.PageUp:
                MoveSelection(-VisibleRows);
                break;
            case Key.Enter:
                OpenSelected(reveal: Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
                break;
            default:
                base.OnPreviewKeyDown(e);
                return;
        }
        e.Handled = true;
    }
}
