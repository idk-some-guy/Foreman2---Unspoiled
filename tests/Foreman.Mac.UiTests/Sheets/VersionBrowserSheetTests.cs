using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.DataCaching.Storage;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.Views.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests.Sheets {
    public class VersionBrowserSheetTests {
        private static readonly DateTime NowLocal = new(2026, 10, 28, 15, 0, 0, DateTimeKind.Local);

        private static GraphVersionRow Version(DateTime local, GraphVersionKind kind, int nodes) => new() {
            Id = Guid.NewGuid(), CreatedAt = local.ToUniversalTime(), Kind = kind, NodeCount = nodes,
        };

        private static List<GraphVersionRow> Versions() => [
            Version(new DateTime(2026, 10, 28, 14, 14, 0, DateTimeKind.Local), GraphVersionKind.Saved, 118),
            Version(new DateTime(2026, 10, 28, 11, 40, 0, DateTimeKind.Local), GraphVersionKind.Automatic, 104),
            Version(new DateTime(2026, 10, 27, 18, 2, 0, DateTimeKind.Local), GraphVersionKind.BeforeRestore, 96),
            Version(new DateTime(2026, 9, 21, 9, 15, 0, DateTimeKind.Local), GraphVersionKind.Automatic, 71),
        ];

        [AvaloniaFact]
        public async Task VersionBrowser_Wide460_NewestFirst_RestoreDisabledUntilARow_OnlyConfirmedDeletesTheRow() {
            List<GraphVersionRow> versions = Versions();
            var deleted = new List<Guid>();
            bool confirm = false;
            var sheet = new VersionBrowserSheet("Green Circuit Factory", versions, NowLocal) {
                ConfirmDeleteAsync = () => Task.FromResult(confirm),
                DeleteVersion = deleted.Add,
            };
            (Window window, SheetHost host, Task<Guid?> result) = SheetFixture.Present(sheet);

            Assert.Equal("Versions of “Green Circuit Factory”", sheet.Title);
            Assert.Equal(SheetWidthClass.Wide, sheet.WidthClass);
            Assert.Equal(SheetFixture.Token("Foreman.Metrics.SheetWidthWide"), SheetFixture.PanelOf(host, sheet).Bounds.Width, 0.5);
            Assert.Equal(versions.Select(v => v.Id), sheet.Rows.Select(r => r.Id));
            Assert.Equal(["Today, 2:14 PM", "Today, 11:40 AM", "Yesterday, 6:02 PM", "Sep 21, 9:15 AM"], sheet.Rows.Select(r => r.WhenText));
            Assert.Equal(["Saved", "Automatic", "Before Restore", "Automatic"], sheet.Rows.Select(r => r.KindText));
            Assert.Equal(["118", "104", "96", "71"], sheet.Rows.Select(r => r.NodesText));
            Assert.Equal(["Saved", "Kind", "Nodes"], sheet.HeaderRowControl.Children.OfType<TextBlock>().Select(t => t.Text));
            Assert.Equal(["Delete Version…", "Cancel", "Restore"], sheet.Buttons!.Items.Cast<Button>().Select(b => (string)b.Content!));
            Assert.Equal(ButtonRowRole.Helper, ButtonRow.GetRole(sheet.DeleteButtonControl));
            Assert.Same(sheet.RestoreButtonControl, sheet.DefaultButton);
            Assert.False(sheet.RestoreButtonControl.IsEnabled);
            Assert.Equal("Today, 2:14 PM, Saved, 118 nodes", AutomationProperties.GetName(sheet.VersionListControl.ContainerFromIndex(0)!));
            Assert.Equal("Yesterday, 6:02 PM, Before Restore, 96 nodes", AutomationProperties.GetName(sheet.VersionListControl.ContainerFromIndex(2)!));

            sheet.VersionListControl.SelectedIndex = 2;
            await sheet.DeleteSelectedAsync();
            Assert.Empty(deleted);
            Assert.Equal(4, sheet.Rows.Count);
            confirm = true;
            await sheet.DeleteSelectedAsync();
            Assert.Equal([versions[2].Id], deleted);
            Assert.Equal(3, sheet.Rows.Count);
            Assert.DoesNotContain(sheet.Rows, r => r.Id == versions[2].Id);
            Assert.False(sheet.RestoreButtonControl.IsEnabled);

            sheet.VersionListControl.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            Assert.True(sheet.RestoreButtonControl.IsEnabled);

            sheet.RestoreButtonControl.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(versions[1].Id, result.Result);
            window.Close();
        }

        [AvaloniaFact]
        public void VersionBrowser_ReturnRestoresTheSelectedRow_EscapeCompletesNull() {
            List<GraphVersionRow> versions = Versions();
            var restoring = new VersionBrowserSheet("Plant", versions, NowLocal);
            (Window window, _, Task<Guid?> restored) = SheetFixture.Present(restoring);
            restoring.VersionListControl.SelectedIndex = 3;
            Dispatcher.UIThread.RunJobs();

            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(versions[3].Id, restored.Result);
            window.Close();

            var leaving = new VersionBrowserSheet("Plant", versions, NowLocal);
            (Window other, _, Task<Guid?> left) = SheetFixture.Present(leaving);
            other.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Dispatcher.UIThread.RunJobs();
            Assert.Null(left.Result);
            other.Close();
        }

        [AvaloniaFact]
        public async Task Rename_SheetIsAlertWidth_SelectsTheName_KeysCompleteIt_TheTitleFollows() {
            using var library = new LibraryFixture();
            var window = new MainWindow();
            window.Show();
            window.DataCache = new Foreman.DataCaching.DataCache(filterRecipes: true);
            window.Library = library.Service;
            window.SaveVersion();
            string original = window.ViewModel.DocumentTitle;

            Task renaming = window.RenameGraphAsync();
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();
            SheetHost host = SheetHost.For(window);
            RenameGraphSheet sheet = Assert.Single(host.GetVisualDescendants().OfType<RenameGraphSheet>());
            Assert.Equal("Rename graph", sheet.Title);
            Assert.Equal(SheetWidthClass.Alert, sheet.WidthClass);
            Assert.Equal(SheetFixture.Token("Foreman.Metrics.SheetWidthAlert"), SheetFixture.PanelOf(host, sheet).Bounds.Width, 0.5);
            Assert.Equal(["Cancel", "Rename"], sheet.Buttons!.Items.Cast<Button>().Select(b => (string)b.Content!));
            Assert.Same(sheet.RenameButtonControl, sheet.DefaultButton);
            Assert.Equal(original, sheet.NameInputControl.Text);
            Assert.Equal(0, sheet.NameInputControl.SelectionStart);
            Assert.Equal(original.Length, sheet.NameInputControl.SelectionEnd);
            Assert.False(sheet.RenameButtonControl.IsEnabled);

            sheet.NameInputControl.Text = "   ";
            Assert.False(sheet.RenameButtonControl.IsEnabled);
            sheet.NameInputControl.Text = "Blue Science ";
            Assert.True(sheet.RenameButtonControl.IsEnabled);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();
            await renaming.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal("Blue Science", window.ViewModel.DocumentTitle);
            Assert.Equal("Blue Science", library.Store.Get(library.Service.OpenGraphId!.Value)!.Name);

            Task cancelling = window.RenameGraphAsync();
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();
            RenameGraphSheet second = Assert.Single(host.GetVisualDescendants().OfType<RenameGraphSheet>());
            second.NameInputControl.Text = "Never Applied";
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();
            await cancelling.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("Blue Science", window.ViewModel.DocumentTitle);
            window.Close();
        }
    }

    [Collection(ScaleTestGroup.Name)]
    public class VersionBrowserColumnTests {
        [AvaloniaTheory]
        [InlineData(1.0)]
        [InlineData(1.5)]
        public void Columns_AreTheRuledTokensTimesTheScale_AndKindReadsSecondary(double scale) {
            try {
                WindowScaler.SetScale(scale);
                var versions = new List<GraphVersionRow> {
                    new() { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow, Kind = GraphVersionKind.Saved, NodeCount = 9 },
                };
                var sheet = new VersionBrowserSheet("Plant", versions);
                (Window window, _, _) = SheetFixture.Present(sheet);

                Grid row = sheet.VersionListControl.GetVisualDescendants().OfType<Grid>().First(g => g.ColumnDefinitions.Count == 5);
                window.UpdateLayout();
                double[] widths = [.. row.ColumnDefinitions.Skip(1).Select(c => c.ActualWidth)];
                double[] expected = [SheetFixture.Token("Foreman.Space.Space2") * scale, SheetFixture.Token("Foreman.Size.VersionKindColumn") * scale,
                    SheetFixture.Token("Foreman.Space.Space2") * scale, SheetFixture.Token("Foreman.Size.VersionNodesColumn") * scale];
                for (int i = 0; i < expected.Length; i++)
                    Assert.Equal(expected[i], widths[i], 0.5);
                Assert.Equal(SheetFixture.Token("Foreman.Size.VersionHeaderHeight") * scale, sheet.HeaderRowControl.Bounds.Height, 0.5);

                Assert.True(Application.Current!.TryGetResource("Foreman.Color.TextSecondary", Application.Current.ActualThemeVariant, out object? secondary));
                TextBlock[] cells = [.. row.Children.OfType<TextBlock>()];
                Assert.Equal((Color)secondary!, Assert.IsAssignableFrom<ISolidColorBrush>(cells[1].Foreground).Color);
                Assert.NotEqual((Color)secondary!, Assert.IsAssignableFrom<ISolidColorBrush>(cells[0].Foreground).Color);
                window.Close();
            } finally {
                WindowScaler.SetScale(1.0);
            }
        }
    }
}
