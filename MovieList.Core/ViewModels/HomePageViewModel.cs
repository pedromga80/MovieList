using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;

using Akavache;

using DynamicData;
using DynamicData.Binding;

using MovieList.DialogModels;
using MovieList.Models;
using MovieList.Preferences;

using ReactiveUI;
using ReactiveUI.Fody.Helpers;

using Splat;

using static MovieList.Constants;

namespace MovieList.ViewModels
{
    public sealed class HomePageViewModel : ReactiveObject
    {
        private readonly IBlobCache store;
        private readonly ReadOnlyObservableCollection<RecentFileViewModel> recentFiles;
        private readonly SourceCache<RecentFileViewModel, string> recentFilesSource
            = new SourceCache<RecentFileViewModel, string>(vm => vm.File.Path);

        public HomePageViewModel(IBlobCache? store = null)
        {
            this.store = store ?? Locator.Current.GetService<IBlobCache>(StoreKey);

            this.recentFilesSource.Connect()
                .Sort(SortExpressionComparer<RecentFileViewModel>.Descending(vm => vm.File.Closed))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Bind(out this.recentFiles)
                .DisposeMany()
                .Subscribe();

            this.store.GetObject<UserPreferences>(PreferencesKey)
                .SelectMany(preferences => preferences.File.RecentFiles)
                .Select(file => new RecentFileViewModel(file, this))
                .Subscribe(recentFilesSource.AddOrUpdate);

            this.WhenAnyValue(vm => vm.RecentFiles.Count)
                .Select(count => count != 0)
                .ToPropertyEx(this, vm => vm.RecentFilesPresent);

            this.CreateFile = ReactiveCommand.CreateFromObservable(this.OnCreateFile);
            this.OpenFile = ReactiveCommand.CreateFromObservable<string?, string?>(this.OnOpenFile);
            this.OpenRecentFile = ReactiveCommand.CreateFromObservable<string, string?>(this.OnOpenRecentFile);

            var canRemoveSelectedRecentFiles = this.recentFilesSource.Connect()
                .AutoRefresh(file => file.IsSelected)
                .ToCollection()
                .Select(files => files.Any(file => file.IsSelected));

            this.RemoveSelectedRecentFiles = ReactiveCommand.CreateFromObservable(
                this.OnRemoveSelectedRecentFiles, canRemoveSelectedRecentFiles);

            this.AddRecentFile = ReactiveCommand.Create<RecentFile>(
                file => this.recentFilesSource.AddOrUpdate(new RecentFileViewModel(file, this)));

            this.RemoveRecentFile = ReactiveCommand.Create<RecentFile>(
                file => this.recentFilesSource.RemoveKey(file.Path));

            this.OpenRecentFile
                .WhereNotNull()
                .InvokeCommand(this.OpenFile);
        }

        public ReadOnlyObservableCollection<RecentFileViewModel> RecentFiles
            => this.recentFiles;

        public bool RecentFilesPresent { [ObservableAsProperty] get; }

        public ReactiveCommand<Unit, CreateFileModel?> CreateFile { get; }
        public ReactiveCommand<string?, string?> OpenFile { get; }
        public ReactiveCommand<string, string?> OpenRecentFile { get; }

        public ReactiveCommand<Unit, Unit> RemoveSelectedRecentFiles { get; }

        public ReactiveCommand<RecentFile, Unit> AddRecentFile { get; }
        public ReactiveCommand<RecentFile, Unit> RemoveRecentFile { get; }

        private IObservable<CreateFileModel?> OnCreateFile()
            => Dialog.SaveFile.Handle(String.Empty)
                .Do(_ => this.Log().Debug("Creating a new list"))
                .SelectNotNull(fileName => new CreateFileModel(fileName, Path.GetFileNameWithoutExtension(fileName)));

        private IObservable<string?> OnOpenFile(string? fileName)
        {
            this.Log().Debug(fileName is null ? "Opening a list" : $"Opening a list: {fileName}");
            return fileName != null ? Observable.Return(fileName) : Dialog.OpenFile.Handle(Unit.Default);
        }

        private IObservable<string?> OnOpenRecentFile(string fileName)
            => File.Exists(fileName)
                ? Observable.Return(fileName)
                : Dialog.Confirm.Handle(new ConfirmationModel("RemoveRecentFileQuesiton", "RemoveRecentFileTitle"))
                    .SelectMany(shouldRemoveFile => shouldRemoveFile
                        ? this.RemoveRecentFileEntry(fileName)
                        : Observable.Return(Unit.Default))
                    .Select(_ => (string?)null);

        private IObservable<Unit> RemoveRecentFileEntry(string fileName)
            => this.store.GetObject<UserPreferences>(PreferencesKey)
                .Eager()
                .Do(_ => this.Log().Debug($"Removing recent file: {fileName}"))
                .Do(_ => this.recentFilesSource.Remove(fileName))
                .Do(preferences => preferences.File.RecentFiles.RemoveAll(file => file.Path == fileName))
                .SelectMany(preferences => this.store.InsertObject(PreferencesKey, preferences).Eager());

        private IObservable<Unit> OnRemoveSelectedRecentFiles()
            => this.store.GetObject<UserPreferences>(PreferencesKey)
                .Eager()
                .Select(preferences => new
                {
                    Preferences = preferences,
                    FilesToRemove = this.recentFiles
                        .Where(file => file.IsSelected)
                        .ToList()
                })
                .Do(data =>
                {
                    string fileNames = data.FilesToRemove
                        .Select(file => file.File.Name)
                        .Aggregate((acc, file) => $"{acc}, {file}");

                    this.Log().Debug($"Removing recent files: {fileNames}");
                })
                .Do(data => this.recentFilesSource.Remove(data.FilesToRemove))
                .Do(data => data.Preferences.File.RecentFiles.RemoveMany(data.FilesToRemove.Select(file => file.File)))
                .SelectMany(data => this.store.InsertObject(PreferencesKey, data.Preferences).Eager());
    }
}
