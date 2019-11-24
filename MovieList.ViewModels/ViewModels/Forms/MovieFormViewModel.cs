using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Resources;

using DynamicData;
using DynamicData.Binding;

using MovieList.Data.Models;

using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using ReactiveUI.Validation.Extensions;
using ReactiveUI.Validation.Helpers;

using Splat;

using static MovieList.Data.Constants;

namespace MovieList.ViewModels.Forms
{
    public sealed class MovieFormViewModel : ReactiveValidationObject<MovieFormViewModel>
    {
        private readonly ResourceManager resourceManager;
        private readonly IScheduler scheduler;

        private readonly SourceList<Title> titlesSource;

        private readonly ReadOnlyObservableCollection<TitleFormViewModel> titles;
        private readonly ReadOnlyObservableCollection<TitleFormViewModel> originalTitles;

        private readonly BehaviorSubject<bool> formChanged;

        public MovieFormViewModel(Movie movie, ResourceManager? resourceManager = null, IScheduler? scheduler = null)
        {
            this.Movie = movie;

            this.resourceManager = resourceManager ?? Locator.Current.GetService<ResourceManager>();
            this.scheduler = scheduler ?? Scheduler.Default;

            this.titlesSource = new SourceList<Title>();

            this.InitializeTitles(title => !title.IsOriginal, out this.titles);
            this.InitializeTitles(title => title.IsOriginal, out this.originalTitles);

            this.CopyProperties();

            this.FormTitle = this.CreateFormTitle();

            this.YearRule = this.CreateYearRule();
            this.ImdbLinkRule = this.CreateImdbLinkRule();
            this.PosterUrlRule = this.CreatePosterUrlRule();

            this.InitializeValueDependencies();

            this.formChanged = new BehaviorSubject<bool>(false);

            var canSave = new BehaviorSubject<bool>(false);
            var canDelete = new BehaviorSubject<bool>(false);

            this.AddTitle = ReactiveCommand.Create(() => { }, this.Titles.CanAddMore(MaxTitleCount));
            this.AddOriginalTitle = ReactiveCommand.Create(() => { }, this.OriginalTitles.CanAddMore(MaxTitleCount));

            this.Save = ReactiveCommand.Create(() => this.Movie, canSave);
            this.Cancel = ReactiveCommand.Create(() => { }, this.formChanged);
            this.Close = ReactiveCommand.Create(() => true);
            this.Delete = ReactiveCommand.Create<Movie?>(() => null, canDelete);

            Observable.Return(this.Movie.Id != default)
                .Merge(this.Save.Select(_ => true))
                .Subscribe(canDelete);

            this.InitializeChangeTracking(canSave);
        }

        public Movie Movie { get; }

        public IObservable<string> FormTitle { get; }

        public ReadOnlyObservableCollection<TitleFormViewModel> Titles
            => this.titles;

        public ReadOnlyObservableCollection<TitleFormViewModel> OriginalTitles
            => this.originalTitles;

        [Reactive]
        public string Year { get; set; } = String.Empty;

        [Reactive]
        public bool IsWatched { get; set; }

        [Reactive]
        public bool IsReleased { get; set; }

        [Reactive]
        public string? ImdbLink { get; set; }

        [Reactive]
        public string? PosterUrl { get; set; }

        public IObservable<bool> FormChanged
            => this.formChanged.AsObservable();

        public bool IsFormChanged
            => this.formChanged.Value;

        public ValidationHelper YearRule { get; }
        public ValidationHelper ImdbLinkRule { get; }
        public ValidationHelper PosterUrlRule { get; }

        public ReactiveCommand<Unit, Unit> AddTitle { get; }
        public ReactiveCommand<Unit, Unit> AddOriginalTitle { get; }

        public ReactiveCommand<Unit, Movie> Save { get; }
        public ReactiveCommand<Unit, Unit> Cancel { get; }
        public ReactiveCommand<Unit, bool> Close { get; }
        public ReactiveCommand<Unit, Movie?> Delete { get; }

        private void InitializeTitles(
            Func<Title, bool> predicate,
            out ReadOnlyObservableCollection<TitleFormViewModel> titles)
        {
            var canDelete = this.titlesSource.Connect()
                .Filter(predicate)
                .Count()
                .Select(count => count > 1);

            this.titlesSource.Connect()
                .Filter(predicate)
                .Sort(SortExpressionComparer<Title>.Ascending(title => title.Priority))
                .Transform(title => new TitleFormViewModel(title, canDelete, this.resourceManager))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Bind(out titles)
                .DisposeMany()
                .Subscribe();
        }

        private void InitializeValueDependencies()
        {
            this.WhenAnyValue(vm => vm.IsReleased)
                .Where(isReleased => !isReleased)
                .Subscribe(_ => this.IsWatched = false);

            this.WhenAnyValue(vm => vm.IsWatched)
                .Where(isWatched => isWatched)
                .Subscribe(_ => this.IsReleased = true);

            this.WhenAnyValue(vm => vm.Year)
                .Select(year => Int32.TryParse(year, out int value) ? (int?)value : null)
                .WhereValueNotNull()
                .Where(year => year != this.scheduler.Now.Year)
                .Subscribe(year => this.IsReleased = year < this.scheduler.Now.Year);
        }

        private IObservable<string> CreateFormTitle()
            => this.Titles.ToObservableChangeSet()
                .AutoRefresh(vm => vm.Name)
                .AutoRefresh(vm => vm.Priority)
                .ToCollection()
                .Select(vms => vms.OrderBy(vm => vm.Priority).Select(vm => vm.Name).FirstOrDefault())
                .Select(title => this.Movie.Id != default || !String.IsNullOrWhiteSpace(title)
                    ? title
                    : this.resourceManager.GetString("NewMovie") ?? String.Empty);

        private ValidationHelper CreateYearRule()
            => this.ValidationRule(
                vm => vm.Year,
                year => !String.IsNullOrWhiteSpace(year) &&
                        Int32.TryParse(year, out int value) &&
                        value >= MovieMinYear &&
                        value <= MovieMaxYear,
                year => !String.IsNullOrWhiteSpace(year)
                    ? this.resourceManager.GetString("ValidationYearEmpty")
                    : this.resourceManager.GetString("ValidationYearInvalid"));

        private ValidationHelper CreateImdbLinkRule()
            => this.ValidationRule(
                vm => vm.ImdbLink,
                link => link.IsUrl(),
                this.resourceManager.GetString("ValidationImdbLinkInvalid"));

        private ValidationHelper CreatePosterUrlRule()
            => this.ValidationRule(
                vm => vm.PosterUrl,
                url => url.IsUrl(),
                this.resourceManager.GetString("ValidationPosterUrlInvalid"));

        private void InitializeChangeTracking(BehaviorSubject<bool> canSave)
        {
            var titlesChanged = this.Titles
                .ToObservableChangeSet()
                .AutoRefreshOnObservable(vm => vm.FormChanged)
                .ToCollection()
                .Select(vms => vms.Any(vm => vm.IsFormChanged));

            var originalTitlesChanged = this.OriginalTitles
                .ToObservableChangeSet()
                .AutoRefreshOnObservable(vm => vm.FormChanged)
                .ToCollection()
                .Select(vms => vms.Any(vm => vm.IsFormChanged));

            var yearChanged = this.WhenAnyValue(vm => vm.Year)
                .Select(year => year != this.Movie.Year.ToString());

            var isWatchedChanged = this.WhenAnyValue(vm => vm.IsWatched)
                .Select(isWatched => isWatched != this.Movie.IsWatched);

            var isReleasedChanged = this.WhenAnyValue(vm => vm.IsReleased)
                .Select(isReleased => isReleased != this.Movie.IsReleased);

            var imdbLinkChanged = this.WhenAnyValue(vm => vm.ImdbLink)
                .Select(link => link != this.Movie.ImdbLink);

            var posterUrlChanged = this.WhenAnyValue(vm => vm.PosterUrl)
                .Select(url => url != this.Movie.PosterUrl);

            var falseWhenSave = this.Save.Select(_ => false);
            var falseWhenCancel = this.Cancel.Select(_ => false);

            Observable.CombineLatest(
                    titlesChanged,
                    originalTitlesChanged,
                    yearChanged,
                    isWatchedChanged,
                    isReleasedChanged,
                    imdbLinkChanged,
                    posterUrlChanged)
                .AnyTrue()
                .Merge(falseWhenSave)
                .Merge(falseWhenCancel)
                .Subscribe(this.formChanged);

            Observable.CombineLatest(
                    this.FormChanged,
                    this.YearRule.Valid(),
                    this.ImdbLinkRule.Valid(),
                    this.PosterUrlRule.Valid())
                .AllTrue()
                .Merge(falseWhenSave)
                .Merge(falseWhenCancel)
                .Subscribe(canSave);
        }

        private void CopyProperties()
        {
            this.titlesSource.Clear();
            this.titlesSource.AddRange(this.Movie.Titles);

            this.Year = this.Movie.Year.ToString();
            this.IsWatched = this.Movie.IsWatched;
            this.IsReleased = this.Movie.IsReleased;
            this.ImdbLink = this.Movie.ImdbLink;
            this.PosterUrl = this.Movie.PosterUrl;
        }
    }
}