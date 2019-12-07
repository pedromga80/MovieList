using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Resources;

using DynamicData;
using DynamicData.Binding;

using MovieList.Data.Models;

using ReactiveUI;

using static MovieList.Data.Constants;

namespace MovieList.ViewModels.Forms
{
    public abstract class TitledFormViewModelBase<TModel, TViewModel> : FormViewModelBase<TModel, TViewModel>
        where TModel : class
        where TViewModel : TitledFormViewModelBase<TModel, TViewModel>
    {
        private readonly ReadOnlyObservableCollection<TitleFormViewModel> titles;
        private readonly ReadOnlyObservableCollection<TitleFormViewModel> originalTitles;

        protected TitledFormViewModelBase(ResourceManager? resourceManager, IScheduler? scheduler = null)
            : base(resourceManager, scheduler)
        {
            this.TitlesSource = new SourceList<Title>();

            this.InitializeTitles(title => !title.IsOriginal, out this.titles);
            this.InitializeTitles(title => title.IsOriginal, out this.originalTitles);

            this.FormTitle = this.CreateFormTitle();

            var canAddTitle = this.Titles.ToObservableChangeSet()
                .Select(_ => this.Titles.Count < MaxTitleCount);

            var canAddOriginalTitle = this.OriginalTitles.ToObservableChangeSet()
                .Select(_ => this.OriginalTitles.Count < MaxTitleCount);

            this.AddTitle = ReactiveCommand.Create(() => this.OnAddTitle(false), canAddTitle);
            this.AddOriginalTitle = ReactiveCommand.Create(() => this.OnAddTitle(true), canAddOriginalTitle);
        }

        public IObservable<string> FormTitle { get; }

        public ReadOnlyObservableCollection<TitleFormViewModel> Titles
            => this.titles;

        public ReadOnlyObservableCollection<TitleFormViewModel> OriginalTitles
            => this.originalTitles;

        public ReactiveCommand<Unit, Unit> AddTitle { get; }
        public ReactiveCommand<Unit, Unit> AddOriginalTitle { get; }

        protected SourceList<Title> TitlesSource { get; }

        protected abstract IEnumerable<Title> ItemTitles { get; }

        protected abstract string NewItemKey { get; }

        protected override void EnableChangeTracking()
        {
            this.TrackChanges(this.IsCollectionChanged(
                vm => vm.Titles,
                vm => vm.ItemTitles.Where(title => !title.IsOriginal).ToList()));

            this.TrackChanges(this.IsCollectionChanged(
                vm => vm.OriginalTitles,
                vm => vm.ItemTitles.Where(title => title.IsOriginal).ToList()));

            this.TrackValidation(this.IsCollectionValid<TitleFormViewModel, Title>(this.Titles));
            this.TrackValidation(this.IsCollectionValid<TitleFormViewModel, Title>(this.OriginalTitles));

            base.EnableChangeTracking();
        }

        protected abstract void AttachTitle(Title title);

        private void InitializeTitles(
            Func<Title, bool> predicate,
            out ReadOnlyObservableCollection<TitleFormViewModel> titles)
        {
            var canDelete = this.TitlesSource.Connect()
                .Select(_ => this.TitlesSource.Items.Where(predicate).Count())
                .Select(count => count > MinTitleCount);

            this.TitlesSource.Connect()
                .Filter(predicate)
                .Sort(SortExpressionComparer<Title>.Ascending(title => title.Priority))
                .Transform(title => this.CreateTitleForm(title, canDelete))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Bind(out titles)
                .DisposeMany()
                .Subscribe();
        }

        private TitleFormViewModel CreateTitleForm(Title title, IObservable<bool> canDelete)
        {
            var titleForm = new TitleFormViewModel(title, canDelete, this.ResourceManager);

            titleForm.Delete
                .WhereNotNull()
                .Subscribe(deletedTitle =>
                {
                    this.TitlesSource.Remove(deletedTitle);

                    (!deletedTitle.IsOriginal ? this.Titles : this.OriginalTitles)
                        .Where(t => t.Priority > deletedTitle.Priority)
                        .ForEach(t => t.Priority--);
                });

            return titleForm;
        }

        private IObservable<string> CreateFormTitle()
            => this.Titles.ToObservableChangeSet()
                .AutoRefresh(vm => vm.Name)
                .AutoRefresh(vm => vm.Priority)
                .ToCollection()
                .Select(vms => vms.OrderBy(vm => vm.Priority).Select(vm => vm.Name).FirstOrDefault())
                .Select(title => this.IsNew && String.IsNullOrWhiteSpace(title)
                    ? this.ResourceManager.GetString(this.NewItemKey) ?? String.Empty
                    : title);

        private void OnAddTitle(bool isOriginal)
        {
            var title = new Title
            {
                IsOriginal = isOriginal,
                Priority = !isOriginal ? this.Titles.Count + 1 : this.OriginalTitles.Count + 1
            };

            this.AttachTitle(title);
            this.TitlesSource.Add(title);
        }
    }
}
