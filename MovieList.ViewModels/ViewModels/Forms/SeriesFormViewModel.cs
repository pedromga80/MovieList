using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Resources;
using System.Threading.Tasks;

using DynamicData;
using DynamicData.Binding;

using MovieList.Data.Models;
using MovieList.Data.Services;
using MovieList.DialogModels;

using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using ReactiveUI.Validation.Extensions;
using ReactiveUI.Validation.Helpers;

using Splat;

namespace MovieList.ViewModels.Forms
{
    public sealed class SeriesFormViewModel : TitledFormViewModelBase<Series, SeriesFormViewModel>
    {
        private readonly IEntityService<Series> seriesService;

        private readonly SourceList<Season> seasonsSource;
        private readonly ReadOnlyObservableCollection<SeasonFormViewModel> seasons;

        public SeriesFormViewModel(
            Series series,
            ReadOnlyObservableCollection<Kind> kinds,
            string fileName,
            ResourceManager? resourceManager = null,
            IScheduler? scheduler = null,
            IEntityService<Series>? seriesService = null)
            : base(resourceManager, scheduler)
        {
            this.Series = series;
            this.Kinds = kinds;

            this.seriesService = seriesService ?? Locator.Current.GetService<IEntityService<Series>>(fileName);

            this.CopyProperties();

            this.seasonsSource = new SourceList<Season>();

            this.seasonsSource.Connect()
                .Sort(SortExpressionComparer<Season>.Ascending(season => season.SequenceNumber))
                .Transform(this.CreateSeasonForm)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Bind(out this.seasons)
                .DisposeMany()
                .Subscribe();

            this.ImdbLinkRule = this.ValidationRule(vm => vm.ImdbLink, link => link.IsUrl(), "ImdbLinkInvalid");
            this.PosterUrlRule = this.ValidationRule(vm => vm.PosterUrl, url => url.IsUrl(), "PosterUrlInvalid");

            this.CanDeleteWhenNotNew();

            this.Close = ReactiveCommand.Create(() => { });

            this.AddSeason = ReactiveCommand.Create(this.OnAddSeason);

            this.EnableChangeTracking();
        }

        public Series Series { get; }

        public ReadOnlyObservableCollection<Kind> Kinds { get; }

        [Reactive]
        public Kind Kind { get; set; } = null!;

        [Reactive]
        public bool IsAnthology { get; set; }

        [Reactive]
        public SeriesWatchStatus WatchStatus { get; set; }

        [Reactive]
        public SeriesReleaseStatus ReleaseStatus { get; set; }

        public ReadOnlyObservableCollection<SeasonFormViewModel> Seasons
            => this.seasons;

        [Reactive]
        public string ImdbLink { get; set; } = String.Empty;

        [Reactive]
        public string PosterUrl { get; set; } = String.Empty;

        public ValidationHelper ImdbLinkRule { get; }
        public ValidationHelper PosterUrlRule { get; }

        public ReactiveCommand<Unit, Unit> Close { get; }

        public ReactiveCommand<Unit, Unit> AddSeason { get; }

        public override bool IsNew
            => this.Series.Id == default;

        protected override SeriesFormViewModel Self
            => this;

        protected override IEnumerable<Title> ItemTitles
            => this.Series.Titles;

        protected override string NewItemKey
            => "NewSeries";

        protected override void EnableChangeTracking()
        {
            this.TrackChanges(vm => vm.WatchStatus, vm => vm.Series.WatchStatus);
            this.TrackChanges(vm => vm.ReleaseStatus, vm => vm.Series.ReleaseStatus);
            this.TrackChanges(vm => vm.Kind, vm => vm.Series.Kind);
            this.TrackChanges(vm => vm.IsAnthology, vm => vm.Series.IsAnthology);
            this.TrackChanges(vm => vm.ImdbLink, vm => vm.Series.ImdbLink.EmptyIfNull());
            this.TrackChanges(vm => vm.PosterUrl, vm => vm.Series.PosterUrl.EmptyIfNull());
            this.TrackChanges(this.IsCollectionChanged(vm => vm.Seasons, vm => vm.Series.Seasons));

            this.TrackValidation(this.IsCollectionValid<SeasonFormViewModel, Season>(this.Seasons));

            base.EnableChangeTracking();
        }

        protected override async Task<Series> OnSaveAsync()
        {
            foreach (var title in this.Titles.Union(this.OriginalTitles))
            {
                await title.Save.Execute();
            }

            this.Series.Titles.Add(this.TitlesSource.Items.Except(this.Series.Titles).ToList());
            this.Series.Titles.Remove(this.Series.Titles.Except(this.TitlesSource.Items).ToList());

            this.Series.IsAnthology = this.IsAnthology;
            this.Series.WatchStatus = this.WatchStatus;
            this.Series.ReleaseStatus = this.ReleaseStatus;
            this.Series.Kind = this.Kind;
            this.Series.ImdbLink = this.ImdbLink.NullIfEmpty();
            this.Series.PosterUrl = this.PosterUrl.NullIfEmpty();

            await this.seriesService.SaveAsync(this.Series);

            return this.Series;
        }

        protected override async Task<Series?> OnDeleteAsync()
        {
            bool shouldDelete = await Dialog.Confirm.Handle(new ConfirmationModel("DeleteSeries"));

            if (shouldDelete)
            {
                await this.seriesService.DeleteAsync(this.Series);
                return this.Series;
            }

            return null;
        }

        protected override void CopyProperties()
        {
            this.TitlesSource.Clear();
            this.TitlesSource.AddRange(this.Series.Titles);

            this.IsAnthology = this.Series.IsAnthology;
            this.Kind = this.Series.Kind;
            this.WatchStatus = this.Series.WatchStatus;
            this.ReleaseStatus = this.Series.ReleaseStatus;
            this.ImdbLink = this.Series.ImdbLink.EmptyIfNull();
            this.PosterUrl = this.Series.PosterUrl.EmptyIfNull();
        }

        protected override void AttachTitle(Title title)
            => title.Series = this.Series;

        private void OnAddSeason()
        {
            var period = new Period
            {
                StartMonth = 1,
                StartYear = 2000,
                EndMonth = 1,
                EndYear = 2000
            };

            var season = new Season
            {
                Titles = new List<Title>
                {
                    new Title { Priority = 1, IsOriginal = false },
                    new Title { Priority = 1, IsOriginal = true }
                },
                Series = this.Series,
                Periods = new List<Period> { period },
                SequenceNumber = this.Seasons.Count + 1
            };

            period.Season = season;

            this.seasonsSource.Add(season);
        }

        private SeasonFormViewModel CreateSeasonForm(Season season)
            => new SeasonFormViewModel(season, this.ResourceManager, this.Scheduler);
    }
}
