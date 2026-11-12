using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.DataCaching;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.Views.Research;
using Foreman.Mac.Views.Sheets;
using Foreman.Models.Research;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests.Views.Research {
    public class QuickSyncTests {
        private static readonly DateTime SyncedAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private sealed class Rig : IDisposable {
            private readonly ShellFixture shell = ShellFixture.Open();
            private readonly string folder = Path.Combine(Path.GetTempPath(), "quick-sync-" + Guid.NewGuid().ToString("N"));

            public MainWindow Window => shell.Window;
            public ResearchSyncFlow Flow => Window.ResearchSync;
            public string SavesFolder => Path.Combine(folder, "saves");

            public Rig() => Directory.CreateDirectory(SavesFolder);

            public string StoreSave(string name, bool exists) {
                string path = Path.Combine(SavesFolder, name + ".zip");
                if (exists)
                    File.WriteAllBytes(path, []);
                shell.Graph.Research = ResearchStateMapper.FromSave(new SaveFileInfo { TicksPlayed = 100 }, path, SyncedAt);
                return path;
            }

            public ToolbarItem SyncItem => Window.Toolbar.Groups.SelectMany(g => g.Items).Single(i => i.Name == "SyncResearchButton");

            public NativeMenuItem FileItem(Func<NativeMenuItem, bool> match) {
                NativeMenu file = NativeMenu.GetMenu(Window)!.Items.OfType<NativeMenuItem>().Single(i => i.Header == "File").Menu!;
                return file.Items.OfType<NativeMenuItem>().Single(match);
            }

            public NativeMenuItem SyncMenuItem => FileItem(i => i.Header is "Sync Research" or "Sync Research…");
            public NativeMenuItem AnotherMenuItem => FileItem(i => i.Header == "Sync Research From Another Save…");

            public void Dispose() {
                shell.Dispose();
                try {
                    Directory.Delete(folder, true);
                } catch (IOException) {
                }
            }
        }

        [AvaloniaFact]
        public void Toolbar_ResearchGroupBetweenArrangeAndShare() {
            using var rig = new Rig();
            Dispatcher.UIThread.RunJobs();

            IReadOnlyList<ToolbarGroup> groups = rig.Window.Toolbar.Groups;
            Assert.Equal([ToolbarGroupRole.Document, ToolbarGroupRole.Add, ToolbarGroupRole.Arrange, ToolbarGroupRole.Research, ToolbarGroupRole.ShareView], groups.Select(g => g.Role));
            string[] names = [.. rig.Window.Toolbar.GetVisualDescendants().OfType<Control>().Select(AutomationProperties.GetName).OfType<string>()
                .Where(n => n is "Document" or "Add" or "Arrange" or "Research" or "Share").Distinct()];
            Assert.Equal(["Document", "Add", "Arrange", "Research", "Share"], names);

            ToolbarGroup research = groups.Single(g => g.Role == ToolbarGroupRole.Research);
            Assert.Equal(["Sync Research", "Research"], research.Items.Select(i => i.AutomationName));
            Assert.Equal("Sync", research.Items[0].Label);
            Assert.Equal("Sync Research", research.Items[0].OverflowLabel);
            Application.Current!.TryGetResource("Kit.Glyph.Sync.17", null, out object? glyph);
            Assert.Same(glyph, research.Items[0].Glyph);

            ToolbarGroup share = groups.Single(g => g.Role == ToolbarGroupRole.ShareView);
            Assert.Equal(["Export Image…", "Graph Summary", "View Options"], share.Items.Select(i => i.OverflowLabel));
        }

        [AvaloniaFact]
        public async Task QuickSync_Enablement() {
            using var rig = new Rig();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Sync Research…", rig.SyncMenuItem.Header);
            Assert.Equal(new KeyGesture(Key.R, PlatformModifiers.Primary), rig.SyncMenuItem.Gesture);
            Assert.Equal(new KeyGesture(Key.R, PlatformModifiers.Primary | KeyModifiers.Shift), rig.AnotherMenuItem.Gesture);
            Assert.True(rig.Window.Commands.SyncResearchCommand.CanExecute(null));
            Assert.True(rig.SyncItem.IsEnabled);

            rig.StoreSave("Megabase", exists: true);
            rig.Window.RefreshLibraryMenus();
            Assert.Equal("Sync Research", rig.SyncMenuItem.Header);

            var release = new TaskCompletionSource<ResearchSyncOutcome>();
            rig.Flow.SheetStub = _ => release.Task;
            Task run = rig.Flow.RunAsync(rig.Window, chooseAnother: false);

            Assert.True(rig.Flow.IsRunning);
            Assert.False(rig.Window.Commands.SyncResearchCommand.CanExecute(null));
            Assert.False(rig.Window.Commands.SyncResearchFromAnotherSaveCommand.CanExecute(null));
            Assert.False(rig.SyncItem.IsEnabled);
            Assert.False(rig.SyncMenuItem.IsEnabled);
            Assert.False(rig.AnotherMenuItem.IsEnabled);

            release.SetResult(ResearchSyncOutcome.Synced);
            await run;

            Assert.False(rig.Flow.IsRunning);
            Assert.True(rig.SyncItem.IsEnabled);
            Assert.True(rig.SyncMenuItem.IsEnabled);
            Assert.True(rig.AnotherMenuItem.IsEnabled);

            rig.Window.IsDocumentOpen = false;
            Assert.False(rig.Window.Commands.SyncResearchCommand.CanExecute(null));
            Assert.False(rig.SyncItem.IsEnabled);
            Assert.False(rig.SyncMenuItem.IsEnabled);
            Assert.False(rig.AnotherMenuItem.IsEnabled);
            rig.Window.IsDocumentOpen = true;
            Assert.True(rig.SyncMenuItem.IsEnabled);
        }

        [AvaloniaFact]
        public async Task QuickSync_RunningChanged_FiresOnBothEdges() {
            using var rig = new Rig();
            rig.StoreSave("Megabase", exists: true);
            var edges = new List<bool>();
            rig.Flow.RunningChanged += (_, _) => edges.Add(rig.Flow.IsRunning);
            rig.Flow.SheetStub = _ => Task.FromResult(ResearchSyncOutcome.Cancelled);

            await rig.Flow.RunAsync(rig.Window, chooseAnother: false);

            Assert.Equal([true, false], edges);
        }

        [AvaloniaFact]
        public async Task QuickSync_NoStoredSave_AsksForSave() {
            using var rig = new Rig();
            var events = new List<string>();
            rig.Flow.ChooseSavePath = _ => {
                events.Add("choose");
                return Task.FromResult<string?>(Path.Combine(rig.SavesFolder, "Chosen.zip"));
            };
            rig.Flow.SheetStub = async sheet => {
                events.Add("sheet " + sheet.SaveName);
                sheet.FirstSyncStub = _ => Task.FromResult(FirstSyncChoice.Clear);
                sheet.LoadPipelineStub = (_, _) => {
                    events.Add("load");
                    return new SaveFileReader.Result { Outcome = SaveFileLoadOutcome.Ok, SaveFileInfo = new SaveFileInfo { TicksPlayed = 100 } };
                };
                await sheet.RunAsync();
                return ResearchSyncOutcome.Synced;
            };

            await rig.Flow.RunAsync(rig.Window, chooseAnother: false);

            Assert.Equal(["choose", "sheet Chosen", "load"], events);
        }

        [AvaloniaFact]
        public async Task QuickSync_NoStoredSave_PanelCancelled_OpensNoSheet() {
            using var rig = new Rig();
            int sheets = 0;
            rig.Flow.ChooseSavePath = _ => Task.FromResult<string?>(null);
            rig.Flow.SheetStub = _ => { sheets++; return Task.FromResult(ResearchSyncOutcome.Cancelled); };

            await rig.Flow.RunAsync(rig.Window, chooseAnother: false);

            Assert.Equal(0, sheets);
            Assert.False(rig.Flow.IsRunning);
        }

        [AvaloniaFact]
        public async Task QuickSync_StoredSaveMissing_OpensMissingState() {
            using var rig = new Rig();
            rig.StoreSave("Gone", exists: false);
            bool? missing = null;
            bool asked = false;
            rig.Flow.ChooseSavePath = _ => { asked = true; return Task.FromResult<string?>(null); };
            rig.Flow.SheetStub = sheet => { missing = sheet.StoredSaveMissing; return Task.FromResult(ResearchSyncOutcome.Cancelled); };

            await rig.Flow.RunAsync(rig.Window, chooseAnother: false);

            Assert.True(missing);
            Assert.False(asked);
        }

        [AvaloniaFact]
        public async Task QuickSync_StoredSaveExists_OpensNormally() {
            using var rig = new Rig();
            rig.StoreSave("Megabase", exists: true);
            bool? missing = null;
            rig.Flow.SheetStub = sheet => { missing = sheet.StoredSaveMissing; return Task.FromResult(ResearchSyncOutcome.Cancelled); };

            await rig.Flow.RunAsync(rig.Window, chooseAnother: false);

            Assert.False(missing);
        }

        [AvaloniaFact]
        public void QuickSync_FromAnotherSave_AlwaysAsks() {
            using var rig = new Rig();
            rig.StoreSave("Megabase", exists: true);
            var starts = new List<string?>();
            rig.Flow.ChooseSavePath = start => {
                starts.Add(start);
                return new TaskCompletionSource<string?>().Task;
            };

            rig.Window.Commands.SyncResearchFromAnotherSaveCommand.Execute(null);

            Assert.Equal([rig.SavesFolder], starts);
        }

        [AvaloniaFact]
        public async Task QuickSync_ChooseAnotherSave_ReentersSheetWithChosenPath() {
            using var rig = new Rig();
            rig.StoreSave("Megabase", exists: false);
            string other = Path.Combine(rig.SavesFolder, "Other.zip");
            var starts = new List<string?>();
            var sheets = new List<(string Name, bool Missing)>();
            rig.Flow.ChooseSavePath = start => { starts.Add(start); return Task.FromResult<string?>(other); };
            rig.Flow.SheetStub = sheet => {
                sheets.Add((sheet.SaveName, sheet.StoredSaveMissing));
                return Task.FromResult(sheets.Count == 1 ? ResearchSyncOutcome.ChooseAnotherSave : ResearchSyncOutcome.Cancelled);
            };

            await rig.Flow.RunAsync(rig.Window, chooseAnother: false);

            Assert.Equal([rig.SavesFolder], starts);
            Assert.Equal([("Megabase", true), ("Other", false)], sheets);
            Assert.False(rig.Flow.IsRunning);

            starts.Clear();
            sheets.Clear();
            rig.Flow.ChooseSavePath = start => { starts.Add(start); return Task.FromResult<string?>(null); };
            rig.Flow.SheetStub = sheet => { sheets.Add((sheet.SaveName, sheet.StoredSaveMissing)); return Task.FromResult(ResearchSyncOutcome.ChooseAnotherSave); };

            await rig.Flow.RunAsync(rig.Window, chooseAnother: false);

            Assert.Equal([rig.SavesFolder], starts);
            Assert.Single(sheets);
            Assert.False(rig.Flow.IsRunning);
        }

        [AvaloniaFact]
        public async Task QuickSync_ShowResearchOutcome_InvokesTheAction() {
            using var rig = new Rig();
            rig.StoreSave("Megabase", exists: true);
            int shown = 0;
            rig.Flow.ShowResearch = () => shown++;
            rig.Flow.SheetStub = _ => Task.FromResult(ResearchSyncOutcome.ShowResearch);

            await rig.Flow.RunAsync(rig.Window, chooseAnother: false);

            Assert.Equal(1, shown);
        }

        [AvaloniaFact]
        public void QuickSync_Tooltip_NamesTheSave() {
            using var rig = new Rig();
            Assert.Equal("Sync research from a save (⌘R)", ToolTip.GetTip(rig.SyncItem));
            Assert.Equal("Asks for a save, then syncs research from it.", AutomationProperties.GetHelpText(rig.SyncItem));

            rig.StoreSave("Megabase", exists: true);

            Assert.Equal("Sync research from “Megabase” (⌘R)", ToolTip.GetTip(rig.SyncItem));
            Assert.Equal("Syncs research from “Megabase”.", AutomationProperties.GetHelpText(rig.SyncItem));
            Assert.Equal("⌘R", AutomationProperties.GetAcceleratorKey(rig.SyncItem));

            string longName = new('m', 60);
            rig.StoreSave(longName, exists: true);

            Assert.Equal($"Sync research from “{Foreman.Mac.Dialogs.MiddleTruncate(longName)}” (⌘R)", ToolTip.GetTip(rig.SyncItem));
            Assert.Equal($"Syncs research from “{longName}”.", AutomationProperties.GetHelpText(rig.SyncItem));
        }
    }
}
