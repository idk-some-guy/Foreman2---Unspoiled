using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Services;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Foreman.Mac.Canvas.Panels {
    //Ports Controls/IRChooserPanel.cs's abstract base (docs/panels-reference.md §2/§9 step 2): filter row,
    //group/subgroup organization, the 4-color availability scheme, and paging. Positioning, click-outside-
    //closes, Escape-closes and focus suspension are already Task 1's FloatingPanelHost/GraphCanvasControl job
    //(reference §7) so PGViewer/viewer-bounds-debounce/CloseIfClickOutside/Leave-focus-timer have no port
    //here; DetachedFromVisualTree stands in for the WinForms Dispose() that used to fire PanelClosed, since
    //the host removes us from its Children directly rather than calling back into panel-specific close logic.
    //UpdateIRButtons drops the Task.Run/UIThread marshaling (a WinForms UI-responsiveness trick, not semantic
    //behavior) and runs synchronously, which is also what makes this testable without racing a dispatcher.
    public abstract class IRChooserPanel : Border, IViewportFittable, IPanelInitialFocus {
        public enum ChooserPanelCloseReason {
            RecipeSelected,
            ItemSelected,
            AltNodeSelected,
            RequiresItemSelection,
            Cancelled,
        }

        public event EventHandler<PanelChooserCloseEventArgs>? PanelClosed;

        protected static readonly Color IRButtonDefaultColor = Color.FromRgb(70, 70, 70);
        protected static readonly Color IRButtonHiddenColor = Color.FromRgb(120, 0, 0);
        protected static readonly Color IRButtonNoAssemblerColor = Color.FromRgb(100, 100, 0);
        protected static readonly Color IRButtonUnavailableColor = Color.FromRgb(170, 10, 160);
        private static readonly Color SelectedGroupButtonFillColor = Color.FromRgb(244, 164, 96);
        private static readonly Color GroupButtonFillColor = Color.FromRgb(105, 105, 105);
        private static readonly SolidColorBrush ChromeBackground = new(GroupButtonFillColor);

        protected ChooserPanelCloseReason PanelCloseReason { get; set; }
        private bool isClosing;

        private readonly List<KeyValuePair<IDataObjectBase, Color>?[]> filteredIRRowsList = [];
        protected int CurrentRow { get; private set; }

        protected List<IGroup>? SortedGroups { get; set; }
        protected IGroup? SelectedGroup { get; set; }
        private static IGroup? startingGroup;
        protected bool ShowUnavailable { get; }
        protected AppSettings Settings { get; }

        private readonly List<IconButton> groupButtons = [];
        private readonly Dictionary<IGroup, IconButton> groupButtonLinks = [];
        private readonly List<Button> footerButtons = [];
        private readonly StackPanel headerStack;

        protected abstract List<List<KeyValuePair<IDataObjectBase, Color>>> GetSubgroupList();
        protected abstract List<IGroup> GetSortedGroups();
        protected abstract void IRButtonMouseUp(IconButton button, PointerReleasedEventArgs e);

        protected TextBlock FilterLabel { get; }
        protected TextBox FilterTextBox { get; }
        protected CheckBox RecipeNameOnlyFilterCheckBox { get; }
        protected CheckBox IgnoreAssemblerCheckBox { get; }
        protected CheckBox ShowHiddenCheckBox { get; }
        protected QualityPicker QualityPicker { get; }
        protected CheckBox AsIngredientCheckBox { get; }
        protected CheckBox AsProductCheckBox { get; }
        protected CheckBox AsFuelCheckBox { get; }
        protected StackPanel RecipeRoleRow { get; }
        protected IconButton ItemIconPanel { get; }
        protected WrapPanel GroupsPanel { get; }
        protected ChooserIconGrid IconGrid { get; }
        protected StackPanel NodeOptionsRowA { get; }
        protected StackPanel NodeOptionsRowB { get; }
        protected Button AddSupplyButton { get; }
        protected Button AddPassthroughButton { get; }
        protected Button AddConsumerButton { get; }
        protected Button AddSpoilButton { get; }
        protected Button AddPlantButton { get; }
        protected Button AddUnspoilButton { get; }
        protected Button AddUnplantButton { get; }

        protected IRChooserPanel(AppSettings settings) {
            Settings = settings;
            ShowUnavailable = settings.ShowUnavailable;
            PanelCloseReason = ChooserPanelCloseReason.Cancelled;
            Background = Brushes.Black;
            Focusable = true;

            //Finding A1: upstream insets each header row from the panel edge via nested FlowLayoutPanel
            //Padding - IRChooserPanel.Designer.cs:88 (headerStack.Padding=(4,4,4,2)) wraps :103/:151/:189/:225
            //(filterRow/optionRow/QualityRow/recipeRoleRow.Padding=(4,2,4,2) each). Avalonia's StackPanel
            //carries no Padding of its own, so Margin on the same containers reproduces the same pixel inset.
            var rowMargin = new Thickness(4, 2, 4, 2);

            FilterLabel = new TextBlock { Text = "Filter:", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
            FilterTextBox = new TextBox { Width = ChooserLayout.FilterTextWidth, Background = Brushes.LightGray, Foreground = Brushes.Black };
            RecipeNameOnlyFilterCheckBox = new CheckBox { Content = "Recipe Only", Foreground = Brushes.White, IsVisible = false, IsChecked = settings.RecipeNameOnlyFilter };
            var filterRow = new StackPanel {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Margin = rowMargin,
                Children = { FilterLabel, FilterTextBox, RecipeNameOnlyFilterCheckBox },
            };

            IgnoreAssemblerCheckBox = new CheckBox { Content = "Ignore Assembler", Foreground = Brushes.White, IsChecked = settings.IgnoreAssemblerStatus };
            ShowHiddenCheckBox = new CheckBox { Content = "Show Hidden", Foreground = Brushes.White, IsChecked = settings.ShowHidden };
            var optionRow = new StackPanel {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Margin = rowMargin,
                Children = { IgnoreAssemblerCheckBox, ShowHiddenCheckBox },
            };

            QualityPicker = new QualityPicker { IsVisible = false, Margin = rowMargin };

            AsIngredientCheckBox = new CheckBox { Content = "Ingredient", Foreground = Brushes.White, IsChecked = true };
            AsProductCheckBox = new CheckBox { Content = "Product", Foreground = Brushes.White, IsChecked = true };
            AsFuelCheckBox = new CheckBox { Content = "Fuel", Foreground = Brushes.White, IsChecked = true };
            RecipeRoleRow = new StackPanel {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                IsVisible = false,
                Margin = rowMargin,
                Children = { AsIngredientCheckBox, AsProductCheckBox, AsFuelCheckBox },
            };

            ItemIconPanel = new IconButton {
                Width = ChooserLayout.ItemIconSize,
                Height = ChooserLayout.ItemIconSize,
                IsHitTestVisible = false,
                DrawBorderWhenEmpty = false,
                IsVisible = false,
            };

            headerStack = new StackPanel {
                Orientation = Orientation.Vertical,
                Spacing = 4,
                Margin = new Thickness(4, 4, 4, 2), //IRChooserPanel.Designer.cs:88's headerStack.Padding
                Children = { filterRow, optionRow, QualityPicker, RecipeRoleRow, ItemIconPanel },
            };

            GroupsPanel = new WrapPanel { Margin = new Thickness(4, 1, 4, 4) }; //Designer.cs:293's groupsPanel.Padding

            IconGrid = new ChooserIconGrid { HorizontalAlignment = HorizontalAlignment.Center };
            IconGrid.ApplyLayout(
                ChooserLayout.CellSize * ChooserIconGrid.VisibleRowCount,
                ChooserLayout.ChooserWidth,
                ChooserLayout.CellSize,
                ChooserLayout.MinCellSize,
                ChooserLayout.ScrollbarWidth);

            AddSupplyButton = FooterButton("Source");
            AddPassthroughButton = FooterButton("Pass-Through");
            AddConsumerButton = FooterButton("Output");
            NodeOptionsRowA = new StackPanel {
                Orientation = Orientation.Horizontal,
                Background = Brushes.Black,
                IsVisible = false,
                Children = { AddSupplyButton, AddPassthroughButton, AddConsumerButton },
            };

            AddUnspoilButton = FooterButton("UnSpoil");
            AddUnplantButton = FooterButton("UnPlant");
            AddSpoilButton = FooterButton("Spoil");
            AddPlantButton = FooterButton("Plant");
            NodeOptionsRowB = new StackPanel {
                Orientation = Orientation.Horizontal,
                Background = Brushes.Black,
                IsVisible = false,
                Children = { AddUnspoilButton, AddUnplantButton, AddSpoilButton, AddPlantButton },
            };

            Child = new StackPanel {
                Orientation = Orientation.Vertical,
                Background = ChromeBackground,
                Children = { headerStack, GroupsPanel, IconGrid, NodeOptionsRowA, NodeOptionsRowB },
            };

            footerButtons.AddRange([
                AddSupplyButton, AddPassthroughButton, AddConsumerButton,
                AddUnspoilButton, AddUnplantButton, AddSpoilButton, AddPlantButton,
            ]);

            FilterTextBox.TextChanged += (_, _) => UpdateIRButtons();
            IconGrid.WireMouseWheel(IconGridMouseWheel);
            IconGrid.ScrollBar.Scroll += ScrollBarScroll;
            foreach (IReadOnlyList<IconButton> column in IconGrid.Buttons)
                foreach (IconButton button in column)
                    button.Released += (sender, e) => IRButtonMouseUp((IconButton)sender!, e);

            DetachedFromVisualTree += (_, _) => ClosePanel(ChooserPanelCloseReason.Cancelled);
        }

        private static Button FooterButton(string text) =>
            new() { Content = text, MinHeight = ChooserLayout.FooterButtonHeight, IsVisible = false };

        //Ports upstream IRChooserPanel.Ui.cs's ApplyViewerBounds (docs/panels-reference.md §2/§9 step 2
        //review): the grid, group icons, and footer buttons all shrink together so the panel always fits the
        //host, down to ChooserLayout.MinCellSize/MinGroupIconSize/MinFooterButtonHeight. Where upstream binary-
        //searches grid height directly, this iterates cell size against the *ForCell functions and re-measures
        //the fixed-size chrome rows (header/groups/footer) each pass, converging the way upstream's own
        //pass-loop in ApplyViewerBounds does (fixed 8-pass cap, break early once the cell size stops moving).
        //Called from FloatingPanelHost.Reposition (all four of its call sites) via the IViewportFittable hook.
        public void FitToViewport(double maxWidth, double maxHeight) {
            int intMaxWidth = Math.Max(1, (int)maxWidth);
            int intMaxHeight = Math.Max(1, (int)maxHeight);

            Width = double.NaN;
            Height = double.NaN;
            //EditPanelViewportLayout.Apply remeasures us right after this with an unbounded (Infinity,
            //Infinity) constraint to find our "natural" size - GroupsPanel (a WrapPanel with no fixed size of
            //its own) never wraps under that, so without this it reports one huge single-row width regardless
            //of group count, and Apply clamps that down to the full available width instead of our real size.
            MaxWidth = intMaxWidth;
            MaxHeight = intMaxHeight;

            int cell = ChooserLayout.CellSize;
            for (int pass = 0; pass < 8; pass++) {
                ApplyCellDerivedSizes(cell);

                int chromeHeight = MeasureFixedChromeHeight(intMaxWidth);
                //Don't floor this at minGridHeight: ChooserIconGrid.ApplyLayout only enforces MinCellSize
                //when availableGridHeight >= minGridHeight, and relaxes below it otherwise (upstream's own
                //behavior) - flooring here would force the 18px floor even when the width budget can't fit
                //18px cells plus the scrollbar, which is exactly the case at the smallest viewport sizes.
                int availableGridHeight = Math.Max(1, intMaxHeight - chromeHeight);
                IconGrid.ApplyLayout(availableGridHeight, intMaxWidth, ChooserLayout.CellSize, ChooserLayout.MinCellSize, ChooserLayout.ScrollbarWidth);

                if (IconGrid.TargetCellSize == cell)
                    break;
                cell = IconGrid.TargetCellSize;
            }
        }

        private void ApplyCellDerivedSizes(int cell) {
            int groupSize = ChooserLayout.GroupIconSizeForCell(cell, ChooserLayout.GroupIconSize, ChooserLayout.MinGroupIconSize);
            foreach (IconButton groupButton in groupButtons) {
                groupButton.Width = groupSize;
                groupButton.Height = groupSize;
            }

            int footerHeight = ChooserLayout.FooterButtonHeightForCell(cell, ChooserLayout.FooterButtonHeight, ChooserLayout.MinFooterButtonHeight);
            double footerFont = ChooserLayout.FooterButtonFontSizeForCell(cell, ChooserLayout.CellSize, ChooserLayout.FooterButtonFontSize, ChooserLayout.MinFooterButtonFontSize);
            foreach (Button footerButton in footerButtons) {
                footerButton.MinHeight = footerHeight;
                footerButton.FontSize = footerFont;
            }
        }

        private int MeasureFixedChromeHeight(int maxWidth) {
            var constraint = new Size(maxWidth, double.PositiveInfinity);
            double height = 0;
            foreach (Control row in new Control[] { headerStack, GroupsPanel, NodeOptionsRowA, NodeOptionsRowB }) {
                if (!row.IsVisible)
                    continue;
                row.Measure(constraint);
                height += row.DesiredSize.Height;
            }
            return (int)Math.Ceiling(height);
        }

        //Mirrors upstream's Show(): build the group row from GetSortedGroups(), restore the last-selected
        //group (or "logistics" on first-ever show), and focus the filter box. Split from the constructor
        //because GetSortedGroups/GetSubgroupList are abstract and a subclass's own fields (e.g. RecipeChooser
        //Panel's KeyItem) aren't set until its constructor body runs.
        public void Initialize() {
            InitializeButtons();
            startingGroup ??= SortedGroups?.FirstOrDefault(g => g.Name == "logistics");
            SetSelectedGroup(null);

            ShowHiddenCheckBox.IsCheckedChanged += FilterCheckBoxChanged;
            IgnoreAssemblerCheckBox.IsCheckedChanged += FilterCheckBoxChanged;
        }

        //IPanelInitialFocus: FocusInitialControl() runs once FloatingPanelHost.Show has actually attached us
        //to the visual tree, unlike a Focus() call from here or from the constructor - both fire before
        //attachment and are silent no-ops.
        public void FocusInitialControl() => FilterTextBox.Focus();

        private void InitializeButtons() {
            SortedGroups = GetSortedGroups();
            GroupsPanel.Children.Clear();
            groupButtons.Clear();
            groupButtonLinks.Clear();

            foreach (IGroup group in SortedGroups) {
                var button = new IconButton {
                    Width = ChooserLayout.GroupIconSize,
                    Height = ChooserLayout.GroupIconSize,
                    DrawBorderWhenEmpty = false,
                };
                button.SetPopulated(group, GroupButtonFillColor);
                button.Released += GroupButtonReleased;
                groupButtons.Add(button);
                groupButtonLinks.Add(group, button);
                GroupsPanel.Children.Add(button);
            }
        }

        protected void UpdateIRButtons(int startRow = 0, bool scrollOnly = false) {
            if (!scrollOnly) {
                filteredIRRowsList.Clear();
                int currentRow = 0;
                foreach (List<KeyValuePair<IDataObjectBase, Color>> sgList in GetSubgroupList().Where(n => n.Count > 0)) {
                    filteredIRRowsList.Add(new KeyValuePair<IDataObjectBase, Color>?[ChooserIconGrid.ColumnCount]);
                    int currentColumn = 0;
                    foreach (KeyValuePair<IDataObjectBase, Color> kvp in sgList) {
                        if (currentColumn == ChooserIconGrid.ColumnCount) {
                            filteredIRRowsList.Add(new KeyValuePair<IDataObjectBase, Color>?[ChooserIconGrid.ColumnCount]);
                            currentColumn = 0;
                            currentRow++;
                        }
                        filteredIRRowsList[currentRow][currentColumn] = kvp;
                        currentColumn++;
                    }
                    currentRow++;
                }

                //Finding A2: upstream sets Maximum to (row count - 1) because WinForms' native VScrollBar
                //silently caps user interaction (thumb drag, track click, arrow keys) at Maximum-LargeChange+1
                //regardless of the raw Maximum value - a Win32 scrollbar quirk Avalonia's ScrollBar does not
                //share. Avalonia lets Value reach Maximum directly, so Maximum itself has to be that effective
                //bound (row count - visible rows) or the real control lets you scroll past the last row.
                int maxScrollOffset = Math.Max(0, filteredIRRowsList.Count - ChooserIconGrid.VisibleRowCount);
                IconGrid.ScrollBar.Maximum = maxScrollOffset;
                IconGrid.ScrollBar.IsEnabled = maxScrollOffset > 0;
            }

            CurrentRow = startRow;
            IconGrid.ScrollBar.Value = startRow;

            for (int column = 0; column < ChooserIconGrid.ColumnCount; column++) {
                for (int row = 0; row < ChooserIconGrid.VisibleRowCount; row++) {
                    KeyValuePair<IDataObjectBase, Color>? cell =
                        row + startRow < filteredIRRowsList.Count ? filteredIRRowsList[row + startRow][column] : null;
                    IconButton button = IconGrid.Buttons[column][row];
                    if (cell is KeyValuePair<IDataObjectBase, Color> kvp)
                        button.SetPopulated(kvp.Key, kvp.Value);
                    else
                        button.SetEmpty();
                }
            }
        }

        protected void SetSelectedGroup(IGroup? group, bool causeUpdate = true) {
            if (SortedGroups is not null && startingGroup is not null && (group is null || !SortedGroups.Contains(group))) {
                IGroup chosen = SortedGroups.Contains(startingGroup) ? startingGroup : SortedGroups[0];
                startingGroup = chosen;
                SelectedGroup = chosen;
                UpdateIRButtons();
            } else {
                foreach (IconButton groupButton in groupButtons)
                    if (groupButton.DataObject is IGroup g)
                        groupButton.SetFillColor(g == group ? SelectedGroupButtonFillColor : GroupButtonFillColor);
                if (SelectedGroup != group) {
                    startingGroup = group;
                    SelectedGroup = group;
                    if (causeUpdate)
                        UpdateIRButtons();
                }
            }
        }

        protected void UpdateGroupButton(IGroup group, bool enabled) => groupButtonLinks[group].IsEnabled = enabled;

        private void GroupButtonReleased(object? sender, PointerReleasedEventArgs e) {
            if (sender is IconButton { DataObject: IGroup group } && e.InitialPressMouseButton == MouseButton.Left)
                SetSelectedGroup(group);
        }

        private void ScrollBarScroll(object? sender, ScrollEventArgs e) {
            if ((int)e.NewValue != CurrentRow)
                UpdateIRButtons((int)e.NewValue, scrollOnly: true);
        }

        private void IconGridMouseWheel(object? sender, PointerWheelEventArgs e) {
            ScrollBar bar = IconGrid.ScrollBar;
            //Maximum is now already the effective scroll bound (see UpdateIRButtons), so a whole-row step
            //down is valid whenever Value hasn't reached it yet - no separate LargeChange arithmetic needed.
            if (e.Delta.Y < 0 && bar.Value < bar.Maximum) {
                bar.Value++;
                UpdateIRButtons((int)bar.Value, scrollOnly: true);
            } else if (e.Delta.Y > 0 && bar.Value > 0) {
                bar.Value--;
                UpdateIRButtons((int)bar.Value, scrollOnly: true);
            }
            e.Handled = true;
        }

        protected void FilterCheckBoxChanged(object? sender, RoutedEventArgs e) => UpdateIRButtons();

        protected void ClosePanel(ChooserPanelCloseReason reason) {
            if (isClosing)
                return;
            isClosing = true;
            PanelCloseReason = reason;
            PersistSettings();
            PanelClosed?.Invoke(this, new PanelChooserCloseEventArgs(reason));
        }

        //Real Avalonia Measure/Arrange (no Window or compositor needed - Layoutable works standalone), then a
        //plain SKCanvas walk of the resulting visual tree using each control's real, laid-out Bounds - the
        //same "no compositor needed" pattern GraphCanvasControl.Render(SKCanvas) uses, extended to cover the
        //whole panel (filter row, checkboxes, footer) rather than just the grid, per review: a PNG that only
        //proved the grid didn't prove the panel. Chrome widgets get simplified representations (a fill rect
        //plus their text) rather than pixel-perfect Avalonia rendering, since no real Skia backend is attached
        //offscreen; IconButton/ChooserIconGrid already know how to paint themselves exactly, so those are
        //deferred to PaintOnto and never recursed into.
        public void RenderOffscreen(SKCanvas canvas, int width, int height) {
            Measure(new Size(width, height));
            Arrange(new Rect(0, 0, width, height));

            using var backgroundPaint = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Fill };
            canvas.DrawRect(0, 0, width, height, backgroundPaint);

            PaintVisual(canvas, this, 0, 0);
        }

        private static void PaintVisual(SKCanvas canvas, Control control, float parentAbsX, float parentAbsY) {
            if (!control.IsVisible)
                return;

            Rect bounds = control.Bounds;
            float x = parentAbsX + (float)bounds.X;
            float y = parentAbsY + (float)bounds.Y;
            float w = (float)bounds.Width;
            float h = (float)bounds.Height;

            switch (control) {
                case IconButton iconButton:
                    iconButton.PaintOnto(canvas, new SKRect(x, y, x + w, y + h));
                    return;
                case ChooserIconGrid grid:
                    grid.PaintOnto(canvas, x, y);
                    return;
                case Border border:
                    FillRect(canvas, border.Background, x, y, w, h);
                    break;
                //These four draw a simplified stand-in for the whole control (no real Skia backend is
                //attached offscreen to render their own template), so - unlike Border/StackPanel/WrapPanel,
                //which only paint a fill and still need their children reached - painting recurses into
                //none of their internal template children, or the checkbox/button label doubles up with the
                //Content ContentPresenter's own TextBlock underneath it.
                case TextBox textBox:
                    FillRect(canvas, textBox.Background, x, y, w, h);
                    DrawText(canvas, textBox.Text, textBox.Foreground, x + 2, y, h);
                    return;
                case CheckBox checkBox:
                    DrawCheckbox(canvas, checkBox, x, y, h);
                    return;
                case ComboBox comboBox:
                    FillRect(canvas, Brushes.LightGray, x, y, w, h);
                    DrawText(canvas, (comboBox.SelectedItem as string) ?? "", Brushes.Black, x + 2, y, h);
                    return;
                case Button button:
                    FillRect(canvas, Brushes.DimGray, x, y, w, h);
                    DrawText(canvas, button.Content as string, Brushes.White, x + 4, y, h);
                    return;
                case TextBlock textBlock:
                    DrawText(canvas, textBlock.Text, textBlock.Foreground, x, y, h);
                    return;
            }

            foreach (Visual child in control.GetVisualChildren())
                if (child is Control childControl)
                    PaintVisual(canvas, childControl, x, y);
        }

        private static void FillRect(SKCanvas canvas, IBrush? brush, float x, float y, float w, float h) {
            if (brush is not ISolidColorBrush solid || w <= 0 || h <= 0)
                return;
            using var paint = new SKPaint { Color = ToSkColor(solid.Color), Style = SKPaintStyle.Fill };
            canvas.DrawRect(x, y, w, h, paint);
        }

        private static readonly SKFont ChromeFont = new() { Size = 11 };

        //Review finding 3: every RenderOffscreen text call vertically centers against its row's own
        //[rowY, rowY+rowHeight] band via the font's real ascent/descent metrics, rather than the old fixed
        //"rowY + rowHeight - 4" bottom-anchored baseline this used to compute (that offset put every label's
        //ink hard against the row's bottom edge instead of centered against its checkbox glyph/textbox/combo -
        //a RenderOffscreen text-walk bug, not a live-control layout one: the real chrome controls already
        //carry correct alignment (TextBlock.VerticalAlignment=Center in this file/QualityPicker.cs, and
        //CheckBox/ComboBox/Button center their own content within their bounds via their real Avalonia
        //template when actually rendered - RenderOffscreen never reaches that template, since no real Skia
        //backend is attached offscreen).
        private static void DrawText(SKCanvas canvas, string? text, IBrush? brush, float x, float rowY, float rowHeight) {
            if (string.IsNullOrEmpty(text))
                return;
            SKColor color = brush is ISolidColorBrush solid ? ToSkColor(solid.Color) : SKColors.White;
            using var paint = new SKPaint { Color = color, IsAntialias = true };
            SKFontMetrics metrics = ChromeFont.Metrics;
            float baseline = rowY + (rowHeight / 2) - ((metrics.Ascent + metrics.Descent) / 2);
            canvas.DrawText(text, x, baseline, ChromeFont, paint);
        }

        private static void DrawCheckbox(SKCanvas canvas, CheckBox checkBox, float x, float y, float h) {
            const float boxSize = 12f;
            float boxY = y + (h - boxSize) / 2;
            using (var boxPaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = false })
                canvas.DrawRect(x, boxY, boxSize, boxSize, boxPaint);
            if (checkBox.IsChecked == true) {
                using var fillPaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };
                canvas.DrawRect(x + 2, boxY + 2, boxSize - 4, boxSize - 4, fillPaint);
            }
            DrawText(canvas, checkBox.Content as string, checkBox.Foreground, x + boxSize + 4, y, h);
        }

        private static SKColor ToSkColor(Color c) => new(c.R, c.G, c.B, c.A);

        private void PersistSettings() {
            Settings.ShowHidden = ShowHiddenCheckBox.IsChecked ?? false;
            Settings.IgnoreAssemblerStatus = IgnoreAssemblerCheckBox.IsChecked ?? false;
            Settings.RecipeNameOnlyFilter = RecipeNameOnlyFilterCheckBox.IsChecked ?? false;
        }
    }

    public sealed class PanelChooserCloseEventArgs(IRChooserPanel.ChooserPanelCloseReason reason) : EventArgs {
        public IRChooserPanel.ChooserPanelCloseReason Reason { get; } = reason;
    }
}
