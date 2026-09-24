using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Foreman.Mac.Kit;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Services;
using Foreman.Mac.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace Foreman.Mac.Views {
    //MainWindow keeps one instance alive for the app's lifetime; closing here hides rather than destroys it.
    public sealed partial class AddBrowserWindow : Window, IThemeAware {
        private static readonly string[] FilterLabels = ["Show unavailable", "Show hidden", "Ignore assembler"];

        private readonly Segmented modeSwitch;
        private readonly SearchField searchBox;
        private readonly PopUp qualityPopUp;
        private readonly MenuButton filterButton;
        private readonly Border sidebar;
        private readonly ItemsControl groupsList;
        private readonly ChooserGrid grid;
        private readonly PopUp rolePopUp;
        private readonly Button addButton;
        private readonly StatePanel loadingPanel;
        private readonly StatePanel emptyPanel;
        private readonly StatePanel noResultsPanel;
        private readonly StatePanel catalogueErrorPanel;
        private readonly StatePanel searchErrorPanel;
        private readonly WindowMinSizeScale minSizeScale;
        private AddBrowserViewModel? subscribedVm;
        private Dictionary<AddCandidate, ChooserItem> itemsByCandidate = [];
        private readonly Dictionary<ChooserItem, BrowserCell> cellsByItem = [];
        private int lastPointerClickCount = 1;
        private StatePanel? retryFrom;
        private IDisposable? loadingHold;
        private bool syncingFilters;

        //Parameterless so the XAML runtime loader can find it (AVLN3001) - the caller assigns DataContext
        //separately and OnDataContextChanged below picks it up.
        public AddBrowserWindow() {
            InitializeComponent();

            modeSwitch = this.FindControl<Segmented>("ModeSwitch")!;
            searchBox = this.FindControl<SearchField>("SearchBox")!;
            qualityPopUp = this.FindControl<PopUp>("QualityPopUp")!;
            filterButton = this.FindControl<MenuButton>("FilterButton")!;
            sidebar = this.FindControl<Border>("Sidebar")!;
            groupsList = this.FindControl<ItemsControl>("GroupsList")!;
            grid = this.FindControl<ChooserGrid>("Grid")!;
            rolePopUp = this.FindControl<PopUp>("RolePopUp")!;
            Chip chip = this.FindControl<Chip>("ConstraintChip")!;

            modeSwitch.Items.AddRange(["Items", "Recipes"]);
            modeSwitch.PropertyChanged += (_, e) => {
                if (e.Property == Segmented.SelectedIndexProperty && Vm is AddBrowserViewModel vm)
                    vm.Segment = modeSwitch.SelectedIndex == 1 ? BrowserSegment.Recipes : BrowserSegment.Items;
            };
            searchBox.MoveDownRequested += (_, _) => grid.Focus(NavigationMethod.Tab);
            ChipHost.SetFocusFallback(chip, searchBox);
            chip.Removed += (_, _) => Vm?.ClearChip();
            qualityPopUp.Command = new RelayCommand<string>(name => Vm?.SelectQuality(name));
            rolePopUp.Items.AddRange(AddBrowserViewModel.Roles.Select(r => r.Label));
            rolePopUp.Command = new RelayCommand<string>(label => Vm?.SelectRole(label));
            filterButton.PropertyChanged += (_, e) => {
                if (e.Property == MenuButton.DisplayLabelProperty)
                    OnFilterMenuToggled();
            };

            Button close = KitButton("Close", ButtonRowRole.Cancel);
            close.Click += (_, _) => Hide();
            addButton = KitButton("Add", ButtonRowRole.Default);
            addButton.Click += (_, _) => Vm?.Add();
            ButtonRow footerButtons = this.FindControl<ButtonRow>("FooterButtons")!;
            footerButtons.Items.Add(close);
            footerButtons.Items.Add(addButton);

            loadingPanel = NewStatePanel(StatePanelShape.Loading, "Loading objects", "Reading the current preset's items and recipes.");
            emptyPanel = NewStatePanel(StatePanelShape.Empty, "Nothing to browse yet",
                "This preset holds no items or recipes; import one from a Factorio install to fill it.");
            emptyPanel.Buttons = Row(ActionButton("Import a Preset…", ButtonRowRole.Default, () => Vm?.RequestImportPreset()));
            noResultsPanel = NewStatePanel(StatePanelShape.NoResults, null, null);
            catalogueErrorPanel = NewStatePanel(StatePanelShape.Error, "Couldn’t read this preset", "Some of its items or recipes are damaged, so they can’t be listed.");
            catalogueErrorPanel.Buttons = Row(ActionButton("Open Presets…", ButtonRowRole.Other, () => Vm?.RequestImportPreset()),
                ActionButton("Try Again", ButtonRowRole.Default, () => RetryFrom(catalogueErrorPanel)));
            searchErrorPanel = NewStatePanel(StatePanelShape.Error, "Couldn’t search this preset", "Browsing by category still works; clear the search to use it.");
            searchErrorPanel.Buttons = Row(ActionButton("Clear Search", ButtonRowRole.Other, () => Vm?.ClearSearch()),
                ActionButton("Try Again", ButtonRowRole.Default, () => RetryFrom(searchErrorPanel)));

            grid.PropertyChanged += (_, e) => {
                if (e.Property == ChooserGrid.SelectedItemProperty && grid.SelectedItem is { } item && cellsByItem.TryGetValue(item, out BrowserCell? cell))
                    Vm?.SelectCell(cell);
            };
            grid.Activated += (_, item) => {
                if (Vm is not AddBrowserViewModel vm || !cellsByItem.TryGetValue(item, out BrowserCell? cell))
                    return;
                vm.SelectCell(cell);
                vm.Add();
            };
            groupsList.AddHandler(KeyDownEvent, OnSidebarKeyDown);

            AddHandler(KeyDownEvent, OnRoleShortcut, RoutingStrategies.Tunnel);
            AddHandler(PointerPressedEvent, OnAnyPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
            AddHandler(PointerReleasedEvent, OnAnyPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
            DoubleTapped += OnDoubleTapped;
            DataContextChanged += OnDataContextChanged;
            groupsList.ContainerPrepared += OnGroupContainerPrepared;

            minSizeScale = new WindowMinSizeScale(this);

            //IsVisible, not Closed, is this window's real open/shut signal (MainWindow hides rather than
            //closes it); Closed below is just the backstop for a standalone instance.
            PropertyChanged += OnIsVisibleChanged;
            Closed += (_, _) => AppearanceHub.Current?.Unsubscribe(this);
        }

        private AddBrowserViewModel? Vm => DataContext as AddBrowserViewModel;

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private static ControlTheme KitTheme(string key) {
            Application.Current!.TryGetResource(key, null, out object? theme);
            return theme as ControlTheme ?? throw new InvalidOperationException($"AddBrowserWindow: kit theme '{key}' not found.");
        }

        private static Button KitButton(string label, ButtonRowRole role) {
            var button = new Button { Theme = KitTheme("Kit.Button"), Content = label };
            ButtonRow.SetRole(button, role);
            return button;
        }

        private static Button ActionButton(string label, ButtonRowRole role, Action onClick) {
            Button button = KitButton(label, role);
            button.Click += (_, _) => onClick();
            return button;
        }

        private static ButtonRow Row(params Button[] buttons) {
            var row = new ButtonRow { Theme = KitTheme("Kit.ButtonRow") };
            foreach (Button button in buttons)
                row.Items.Add(button);
            return row;
        }

        private static StatePanel NewStatePanel(StatePanelShape shape, string? headline, string? sentence) =>
            new() { Theme = KitTheme("Kit.StatePanel"), Shape = shape, Headline = headline, Sentence = sentence, MovesFocusOnAppear = false };

        private void OnIsVisibleChanged(object? sender, AvaloniaPropertyChangedEventArgs e) {
            if (e.Property != IsVisibleProperty)
                return;
            if (IsVisible) {
                AppearanceHub.Current?.Subscribe(this);
                //An appearance change made while hidden never reached this window - resync now rather
                //than wait for the next unrelated notification.
                RefreshTheme();
            } else {
                RememberSize();
                AppearanceHub.Current?.Unsubscribe(this);
            }
        }

        //The sidebar's measured width is the one thing this window computes in code, off live text metrics
        //that a scale change can move; everything else is DynamicResource-bound kit chrome.
        public void RefreshTheme() {
            minSizeScale.Apply();
            UpdateSidebarWidth();
        }

        private void RememberSize() {
            if (Vm is not AddBrowserViewModel vm || ClientSize.Width <= 0 || ClientSize.Height <= 0)
                return;
            vm.WindowWidth = ClientSize.Width / WindowScaler.CurrentScale;
            vm.WindowHeight = ClientSize.Height / WindowScaler.CurrentScale;
        }

        private void OnDataContextChanged(object? sender, EventArgs e) {
            if (subscribedVm is not null) {
                subscribedVm.PropertyChanged -= OnVmPropertyChanged;
                ((INotifyCollectionChanged)subscribedVm.Groups).CollectionChanged -= OnGroupsChanged;
            }
            subscribedVm = Vm;
            if (subscribedVm is not AddBrowserViewModel vm)
                return;
            Width = vm.WindowWidth * WindowScaler.CurrentScale;
            Height = vm.WindowHeight * WindowScaler.CurrentScale;
            vm.PropertyChanged += OnVmPropertyChanged;
            ((INotifyCollectionChanged)vm.Groups).CollectionChanged += OnGroupsChanged;
            qualityPopUp.Items.Clear();
            qualityPopUp.ItemIcons.Clear();
            qualityPopUp.Items.AddRange(vm.Qualities.Select(q => q.FriendlyName));
            qualityPopUp.ItemIcons.AddRange(vm.QualityIcons);
            modeSwitch.SelectedIndex = vm.Segment == BrowserSegment.Recipes ? 1 : 0;
            SyncQuality(vm);
            SyncRoles(vm);
            SyncFilterItems(vm);
            SyncGrid(vm);
            SyncState(vm);
            SyncChrome(vm);
            UpdateSidebarWidth();
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e) {
            if (Vm is not AddBrowserViewModel vm)
                return;
            switch (e.PropertyName) {
                case nameof(AddBrowserViewModel.Segment):
                    modeSwitch.SelectedIndex = vm.Segment == BrowserSegment.Recipes ? 1 : 0;
                    break;
                case nameof(AddBrowserViewModel.Sections):
                    SyncGrid(vm);
                    break;
                case nameof(AddBrowserViewModel.SelectedCell):
                    SyncGridSelection(vm);
                    break;
                case nameof(AddBrowserViewModel.DisabledRoles):
                case nameof(AddBrowserViewModel.RolesEnabled):
                case nameof(AddBrowserViewModel.SelectedRole):
                    SyncRoles(vm);
                    break;
                case nameof(AddBrowserViewModel.Quality):
                    SyncQuality(vm);
                    break;
                case nameof(AddBrowserViewModel.ShowUnavailable):
                case nameof(AddBrowserViewModel.ShowHidden):
                case nameof(AddBrowserViewModel.IgnoreAssembler):
                    SyncFilterItems(vm);
                    break;
                case nameof(AddBrowserViewModel.State):
                case nameof(AddBrowserViewModel.Failure):
                    SyncState(vm);
                    SyncChrome(vm);
                    break;
                case nameof(AddBrowserViewModel.NoResultsHeadline):
                case nameof(AddBrowserViewModel.NoResultsSentence):
                case nameof(AddBrowserViewModel.HiddenMatchCount):
                    SyncState(vm);
                    break;
                case nameof(AddBrowserViewModel.CanAdd):
                    SyncChrome(vm);
                    break;
            }
        }

        private void SyncQuality(AddBrowserViewModel vm) {
            qualityPopUp.SelectedItem = vm.Quality?.FriendlyName;
            qualityPopUp.Icon = vm.QualityIcon;
        }

        private void SyncRoles(AddBrowserViewModel vm) {
            IReadOnlyList<string> disabled = vm.DisabledRoles;
            if (!rolePopUp.DisabledItems.SequenceEqual(disabled)) {
                rolePopUp.DisabledItems.Clear();
                rolePopUp.DisabledItems.AddRange(disabled);
            }
            rolePopUp.SelectedItem = vm.SelectedRole.Label;
            SyncChrome(vm);
        }

        //A catalogue failure leaves only Close live; a search failure only takes Add away.
        private void SyncChrome(AddBrowserViewModel vm) {
            bool catalogueFailed = vm.Failure == BrowserFailure.Catalogue;
            sidebar.IsEnabled = !catalogueFailed;
            searchBox.IsEnabled = !catalogueFailed;
            qualityPopUp.IsEnabled = !catalogueFailed;
            filterButton.IsEnabled = !catalogueFailed;
            rolePopUp.IsEnabled = !catalogueFailed && vm.RolesEnabled;
            addButton.IsEnabled = vm.Failure == BrowserFailure.None && vm.CanAdd;
        }

        private void SyncFilterItems(AddBrowserViewModel vm) {
            bool[] state = [vm.ShowUnavailable, vm.ShowHidden, vm.IgnoreAssembler];
            if (syncingFilters || filterButton.Items.Select(i => i.IsChecked).SequenceEqual(state))
                return;
            syncingFilters = true;
            filterButton.Items.Clear();
            filterButton.Items.AddRange(FilterLabels.Select((label, i) => new MenuButtonItem { Text = label, IsDefault = false, IsChecked = state[i] }));
            syncingFilters = false;
        }

        //Every toggle and the Reset entry change the button's count label, the one change MenuButton raises.
        private void OnFilterMenuToggled() {
            if (syncingFilters || Vm is not AddBrowserViewModel vm || filterButton.Items.Count != FilterLabels.Length)
                return;
            bool[] picked = [.. filterButton.Items.Select(i => i.IsChecked)];
            syncingFilters = true;
            vm.ShowUnavailable = picked[0];
            vm.ShowHidden = picked[1];
            vm.IgnoreAssembler = picked[2];
            syncingFilters = false;
        }

        private void RetryFrom(StatePanel panel) {
            retryFrom = panel;
            Vm?.Retry();
        }

        private void SyncState(AddBrowserViewModel vm) {
            if (vm.State == BrowserState.NoResults)
                SyncNoResults(vm);
            StatePanel? error = vm.Failure == BrowserFailure.Catalogue ? catalogueErrorPanel : searchErrorPanel;
            if (vm.State == BrowserState.Error)
                error.MovesFocusOnAppear = vm.ErrorTakesFocus;
            StatePanel? next = vm.State switch {
                BrowserState.Loading => loadingPanel,
                BrowserState.Empty => emptyPanel,
                BrowserState.NoResults => noResultsPanel,
                BrowserState.Error => error,
                _ => null,
            };
            //A retry keeps its error panel in place for the loading grace, so a quick repeat failure never blanks it.
            if (next == loadingPanel && retryFrom is not null && ReferenceEquals(grid.StateContent, retryFrom)) {
                loadingHold ??= DispatcherTimer.RunOnce(ShowHeldLoading, Motion.StatePanelDelay);
                return;
            }
            loadingHold?.Dispose();
            loadingHold = null;
            if (vm.State != BrowserState.Loading) {
                if (retryFrom is not null && ReferenceEquals(next, retryFrom) && ReferenceEquals(grid.StateContent, retryFrom))
                    retryFrom.Reannounce();
                retryFrom = null;
            }
            if (!ReferenceEquals(grid.StateContent, next)) {
                if (next == loadingPanel)
                    loadingPanel.LoadingDelay = Motion.StatePanelDelay;
                grid.StateContent = next;
            }
        }

        private void ShowHeldLoading() {
            loadingHold = null;
            if (Vm?.State != BrowserState.Loading)
                return;
            loadingPanel.LoadingDelay = TimeSpan.Zero;
            grid.StateContent = loadingPanel;
        }

        private void SyncNoResults(AddBrowserViewModel vm) {
            noResultsPanel.Headline = vm.NoResultsHeadline;
            noResultsPanel.Sentence = vm.NoResultsSentence;
            noResultsPanel.MovesFocusOnAppear = vm.NoResultsTakesFocus;
            IReadOnlyList<NoResultsAction> actions = vm.NoResultsActions;
            string[] labels = [.. actions.Select(NoResultsLabel)];
            if (noResultsPanel.Buttons is not null && noResultsPanel.Buttons.Items.OfType<Button>().Select(b => b.Content as string).SequenceEqual(labels))
                return;
            noResultsPanel.Buttons = Row([.. actions.Select((action, i) =>
                ActionButton(NoResultsLabel(action), i == actions.Count - 1 ? ButtonRowRole.Default : ButtonRowRole.Other, () => RunNoResultsAction(action)))]);
        }

        private static string NoResultsLabel(NoResultsAction action) => action switch {
            NoResultsAction.ClearSearch => "Clear Search",
            NoResultsAction.ShowAll => "Show All",
            NoResultsAction.RemoveConstraint => "Remove Constraint",
            _ => "Show Item",
        };

        private void RunNoResultsAction(NoResultsAction action) {
            if (Vm is not AddBrowserViewModel vm)
                return;
            switch (action) {
                case NoResultsAction.ClearSearch:
                    vm.ClearSearch();
                    break;
                case NoResultsAction.ShowAll:
                    bool search = vm.IsSearchNoResults;
                    vm.ShowAll();
                    if (search)
                        searchBox.Focus(NavigationMethod.Tab);
                    else
                        grid.Focus(NavigationMethod.Tab);
                    break;
                case NoResultsAction.RemoveConstraint:
                    vm.ClearChip();
                    searchBox.Focus(NavigationMethod.Tab);
                    break;
                case NoResultsAction.ShowItem:
                    vm.ShowLinkItem();
                    grid.Focus(NavigationMethod.Tab);
                    break;
            }
        }

        //A search reads "Group · Subgroup" through the grid's own heading runs; browsing, a link or the
        //origins picker hand it the section's title whole.
        private void SyncGrid(AddBrowserViewModel vm) {
            string query = vm.SearchText.Trim();
            bool splitHeadings = query.Length > 0 && vm.Sections.All(s => s.Group is not null);
            grid.Query = splitHeadings ? query : null;

            var nextItems = new Dictionary<AddCandidate, ChooserItem>();
            cellsByItem.Clear();
            var fresh = new List<ChooserSection>();
            foreach (BrowserSection section in vm.Sections) {
                var chooserSection = new ChooserSection {
                    Group = splitHeadings ? section.Group : null,
                    Subgroup = splitHeadings ? section.Subgroup : section.Title,
                };
                foreach (BrowserCell cell in section.Cells) {
                    if (!itemsByCandidate.TryGetValue(cell.Candidate, out ChooserItem? item))
                        item = new ChooserItem();
                    item.Icon = cell.Icon;
                    item.Name = cell.Label;
                    item.IsAvailable = cell.IsAvailable;
                    item.IsIconMissing = cell.IconMissing;
                    nextItems[cell.Candidate] = item;
                    cellsByItem[item] = cell;
                    chooserSection.Items.Add(item);
                }
                fresh.Add(chooserSection);
            }
            itemsByCandidate = nextItems;

            //Add before remove: the grid fades out only the items missing from its next pass, so clearing first would drop every leaving fade.
            int stale = grid.Sections.Count;
            grid.Sections.AddRange(fresh);
            grid.Sections.RemoveRange(0, stale);
            SyncGridSelection(vm);
        }

        private void SyncGridSelection(AddBrowserViewModel vm) =>
            grid.SelectedItem = vm.SelectedCell is { } cell && itemsByCandidate.TryGetValue(cell.Candidate, out ChooserItem? item) ? item : null;

        private void OnGroupsChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateSidebarWidth();

        //A prepared container's own content (the label inside) isn't realised until it's loaded.
        private void OnGroupContainerPrepared(object? sender, ContainerPreparedEventArgs e) {
            if (e.Container.IsLoaded) {
                UpdateSidebarWidth();
                return;
            }
            e.Container.Loaded += OnGroupContainerLoaded;
        }

        private void OnGroupContainerLoaded(object? sender, RoutedEventArgs e) {
            ((Control)sender!).Loaded -= OnGroupContainerLoaded;
            UpdateSidebarWidth();
        }

        //Sizes to the widest realised row (Measure at infinity), floored at BrowserSidebar and capped at
        //BrowserSidebarMax; past the cap the row trims its own label and carries the full name as a tooltip.
        private void UpdateSidebarWidth() {
            if (Vm is null)
                return;
            double floor = Resource<double>("Foreman.Size.BrowserSidebar");
            double cap = Resource<double>("Foreman.Size.BrowserSidebarMax");

            double widestRow = 0;
            foreach (SidebarRow row in GroupRows())
                widestRow = Math.Max(widestRow, MeasuredWidth(row));

            double width = Math.Min(cap, Math.Max(floor, widestRow + sidebar.Padding.Left + sidebar.Padding.Right + sidebar.BorderThickness.Right));
            if (sidebar.Width != width)
                sidebar.Width = width;
        }

        private IEnumerable<SidebarRow> GroupRows() => groupsList.GetVisualDescendants().OfType<SidebarRow>();

        private static double MeasuredWidth(SidebarRow row) {
            row.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return row.DesiredSize.Width;
        }

        private static T Resource<T>(string key) {
            Application app = Application.Current!;
            return app.TryGetResource(key, app.ActualThemeVariant, out object? value) && value is T typed
                ? typed
                : throw new InvalidOperationException($"AddBrowserWindow: resource '{key}' not found.");
        }

        //Stashed for OnAnyPointerReleased below - the release carries no ClickCount of its own.
        private void OnAnyPointerPressed(object? sender, PointerPressedEventArgs e) {
            lastPointerClickCount = e.ClickCount;
            if (Vm is AddBrowserViewModel vm && (e.Source as Visual)?.FindAncestorOfType<SidebarRow>(includeSelf: true) is { DataContext: BrowserGroup group } row) {
                vm.SelectGroup(group.Group);
                FocusGroupRow(vm.Groups.ToList().FindIndex(g => g.IsSelected), NavigationMethod.Pointer);
            }
        }

        //A recipe has no role to choose, so one click places it; the cell has already selected itself by now.
        private void OnAnyPointerReleased(object? sender, PointerReleasedEventArgs e) {
            if (e.InitialPressMouseButton != MouseButton.Left || lastPointerClickCount != 1 || !IsOnCell(e.Source))
                return;
            if (Vm is { Segment: BrowserSegment.Recipes, SelectedCell.Candidate.Kind: AddKind.Recipe } vm)
                vm.PlaceSelectedRecipe();
        }

        //A Recipes cell already placed on its first press - ignore the DoubleTapped that follows.
        private void OnDoubleTapped(object? sender, TappedEventArgs e) {
            if (Vm is not AddBrowserViewModel vm || !IsOnCell(e.Source) || vm.SelectedCell is null)
                return;
            if (vm.Segment == BrowserSegment.Recipes && vm.SelectedCell.Candidate.Kind == AddKind.Recipe)
                return;
            vm.Add();
        }

        private static bool IsOnCell(object? source) => (source as Visual)?.FindAncestorOfType<ChooserCell>(includeSelf: true) is not null;

        private void OnSidebarKeyDown(object? sender, KeyEventArgs e) {
            if (Vm is not AddBrowserViewModel vm || e.Key is not (Key.Up or Key.Down) || vm.Groups.Count == 0)
                return;
            int current = vm.Groups.ToList().FindIndex(g => g.IsSelected);
            int next = Math.Clamp(current + (e.Key == Key.Down ? 1 : -1), 0, vm.Groups.Count - 1);
            e.Handled = true;
            if (next == current)
                return;
            vm.SelectGroup(vm.Groups[next].Group);
            FocusGroupRow(next, NavigationMethod.Directional);
        }

        //Selecting a group rebuilds the rows, so the new row is focused once the list has realised it.
        private void FocusGroupRow(int index, NavigationMethod method) =>
            Dispatcher.UIThread.Post(() => (groupsList.ContainerFromIndex(index)?.FindDescendantOfType<SidebarRow>(includeSelf: true))?.Focus(method), DispatcherPriority.Loaded);

        //⌘W/Esc both hide rather than close - MainWindow owns one instance for the app's lifetime.
        protected override void OnKeyDown(KeyEventArgs e) {
            bool primary = e.KeyModifiers.HasFlag(PlatformModifiers.Primary);
            if (e.Key == Key.Escape || (e.Key == Key.W && primary)) {
                e.Handled = true;
                Hide();
                return;
            }
            if (primary && e.Key == Key.F) {
                e.Handled = true;
                searchBox.Focus(NavigationMethod.Tab);
                searchBox.SelectAll();
                return;
            }
            base.OnKeyDown(e);
        }

        //Tunnel, so a ⌘ digit never reaches the grid: ⌘1…⌘7 are the roles' fixed positions, the rest do nothing.
        private void OnRoleShortcut(object? sender, KeyEventArgs e) {
            if (!e.KeyModifiers.HasFlag(PlatformModifiers.Primary) || e.Key is < Key.D1 or > Key.D9)
                return;
            e.Handled = true;
            if (Vm is { Segment: BrowserSegment.Items } vm)
                vm.PickRole(e.Key - Key.D1);
        }

        //Owner-close and app shutdown cascade a Closing here first - only a direct close of this window
        //(the red button, Cmd+W's own Hide() already short-circuits before this) should cancel-and-hide.
        protected override void OnClosing(WindowClosingEventArgs e) {
            if (e.CloseReason == WindowCloseReason.WindowClosing) {
                e.Cancel = true;
                Hide();
            } else {
                RememberSize();
            }
            base.OnClosing(e);
        }
    }
}
