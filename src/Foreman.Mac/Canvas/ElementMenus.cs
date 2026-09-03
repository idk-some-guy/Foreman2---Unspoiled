using Avalonia.Controls;
using System;
using System.Collections.Generic;

namespace Foreman.Mac.Canvas {
    //Ports GraphElement.RightClickMenu's per-invocation build (reference §4's opening paragraph): upstream
    //rebuilds a shared ContextMenuStrip from scratch on every right-click rather than caching one per
    //element. This port has no equivalent persistent field - element classes build a plain MenuEntry list
    //(their own AddRClickMenuOptions-style contribution point) and this class turns it into a real Avalonia
    //ContextMenu on demand. Avalonia's ContextMenu already auto-closes on a leaf item click, so there's no
    //port-side equivalent needed for the Closing handler's ItemClicked cancel-and-self-close dance.
    public readonly record struct MenuEntry {
        public string? Caption { get; }
        public bool Enabled { get; }
        public Action? Invoke { get; }
        public bool IsDivider => Caption is null;

        //RecipeNodeElement's paste-options checkboxes (reference §4c): non-null only for a checkable entry.
        //The state lives outside this readonly struct so the same object is shared between the checkbox
        //entry that flips it and the "Paste selected options" entry that reads it back at click time -
        //mirrors upstream's WeakReference-to-live-ToolStripMenuItem read, without needing Avalonia's
        //MenuItem to still exist by then.
        public MenuCheckboxState? Checkbox { get; }

        private MenuEntry(string? caption, bool enabled, Action? invoke, MenuCheckboxState? checkbox) {
            Caption = caption;
            Enabled = enabled;
            Invoke = invoke;
            Checkbox = checkbox;
        }

        public static MenuEntry Item(string caption, Action invoke, bool enabled = true) => new(caption, enabled, invoke, null);
        public static MenuEntry Checkable(string caption, MenuCheckboxState state, bool enabled = true) => new(caption, enabled, null, state);
        public static readonly MenuEntry Divider = new(null, true, null, null);
    }

    //Session-lifetime remembered checkbox value (reference §4c's `OptionsCopyAssemblerDefault` etc.) - a
    //plain mutable cell rather than a bool field on MenuEntry itself, since MenuEntry is an immutable struct
    //and the checkbox's value needs to keep changing after the entry was handed out.
    public sealed class MenuCheckboxState(bool initialChecked) {
        public bool Checked { get; set; } = initialChecked;
    }

    public static class ElementMenus {
        //Ports upstream's explicit Invalidate() calls inside every menu handler (ProductionGraphViewer.cs
        //lines 488, 497, 819 and their BaseNodeElement/ItemTabElement/ErrorNoticeElement equivalents): a
        //menu item's click fires from Avalonia's own popup loop, after the pointer handler that would
        //otherwise repaint has already returned, so the caller-supplied afterInvoke runs once per click,
        //right after the entry's own action, to request that repaint explicitly.
        public static ContextMenu Build(IReadOnlyList<MenuEntry> entries, Action? afterInvoke = null) {
            var menu = new ContextMenu();
            foreach (MenuEntry entry in entries) {
                if (entry.IsDivider) {
                    menu.Items.Add(new Separator());
                    continue;
                }

                var item = new MenuItem { Header = entry.Caption, IsEnabled = entry.Enabled };
                if (entry.Checkbox is MenuCheckboxState checkboxState) {
                    //Ports the checkbox items' CheckOnClick=true (reference §4c): stays open across
                    //repeated toggles instead of closing the menu like a normal leaf item, so the user can
                    //flip several fields before clicking "Paste selected options".
                    item.ToggleType = MenuItemToggleType.CheckBox;
                    item.IsChecked = checkboxState.Checked;
                    item.StaysOpenOnClick = true;
                    item.Click += (_, _) => {
                        checkboxState.Checked = !checkboxState.Checked;
                        item.IsChecked = checkboxState.Checked;
                    };
                } else {
                    Action? invoke = entry.Invoke;
                    if (invoke is not null)
                        item.Click += (_, _) => {
                            invoke();
                            afterInvoke?.Invoke();
                        };
                }
                menu.Items.Add(item);
            }
            return menu;
        }
    }
}
