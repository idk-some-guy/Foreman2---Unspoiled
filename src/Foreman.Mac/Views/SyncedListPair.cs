using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Foreman.Mac.Views {
    //Ports SyncListView's Buddy-paired mirroring (upstream Controls/SyncListView.cs) for
    //PresetComparatorWindow's Left/Right list pair. WinForms traps WM_VSCROLL/WM_MOUSEWHEEL/
    //WM_MOUSEHWHEEL on the ListView's WndProc and copies TopItem's index onto Buddy; Avalonia has
    //no such message, so this mirrors the paired ListBox's ScrollViewer vertical offset instead,
    //once each list's template has realized one. Selection sync ports the "if different" check
    //verbatim - the same short-circuit that lets upstream wire both directions without a
    //reentrancy flag (setting Buddy.SelectedIndex to a value it already holds raises no further
    //event, so the ping-pong terminates on its own).
    internal sealed class SyncedListPair {
        private readonly ListBox left;
        private readonly ListBox right;
        private ScrollViewer? leftScroll;
        private ScrollViewer? rightScroll;
        private bool syncingScroll;

        public SyncedListPair(ListBox left, ListBox right) {
            this.left = left;
            this.right = right;

            left.TemplateApplied += (_, e) => {
                leftScroll = e.NameScope.Find<ScrollViewer>("PART_ScrollViewer");
                if (leftScroll is not null)
                    leftScroll.PropertyChanged += (_, args) => {
                        if (args.Property == ScrollViewer.OffsetProperty)
                            MirrorScroll(leftScroll, rightScroll);
                    };
            };
            right.TemplateApplied += (_, e) => {
                rightScroll = e.NameScope.Find<ScrollViewer>("PART_ScrollViewer");
                if (rightScroll is not null)
                    rightScroll.PropertyChanged += (_, args) => {
                        if (args.Property == ScrollViewer.OffsetProperty)
                            MirrorScroll(rightScroll, leftScroll);
                    };
            };

            left.SelectionChanged += (_, _) => MirrorSelection(left, right);
            right.SelectionChanged += (_, _) => MirrorSelection(right, left);
        }

        private void MirrorScroll(ScrollViewer? source, ScrollViewer? buddy) {
            if (syncingScroll || source is null || buddy is null || buddy.Offset.Y == source.Offset.Y)
                return;
            syncingScroll = true;
            buddy.Offset = new Vector(buddy.Offset.X, source.Offset.Y);
            syncingScroll = false;
        }

        private static void MirrorSelection(ListBox source, ListBox buddy) {
            if (buddy.SelectedIndex != source.SelectedIndex)
                buddy.SelectedIndex = source.SelectedIndex;
        }

        //Test-only seams (see ShapePropertiesWindow's equivalent comment for the convention).
        internal ScrollViewer? LeftScrollViewer => leftScroll;
        internal ScrollViewer? RightScrollViewer => rightScroll;
    }
}
