using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows.Media.Imaging;

using HandyControl.Data;

using MovieList.Data.Models;
using MovieList.Properties;
using MovieList.Validation;

namespace MovieList.ViewModels.FormItems
{
    public class SpecialEpisodeFormItem : SeriesComponentFormItemBase
    {
        private int month;
        private string year;
        private bool isWatched;
        private bool isReleased;
        private string? posterUrl;

        private BitmapImage? poster;

        private readonly SpecialEpisode backup;

        public SpecialEpisodeFormItem(SpecialEpisode specialEpisode)
        {
            this.SpecialEpisode = specialEpisode;
            this.backup = new SpecialEpisode();

            this.CopySpecialEpisodeProperties();
            this.CopyProperties(this.SpecialEpisode, this.backup);

            this.IsInitialized = true;
        }

        public SpecialEpisode SpecialEpisode { get; }

        public int Month
        {
            get => this.month;
            set
            {
                this.month = value;
                this.OnPropertyChanged();
            }
        }

        [StringRange(
            Min = 1950,
            Max = 2100,
            ErrorMessageResourceName = nameof(Messages.InvalidYear),
            ErrorMessageResourceType = typeof(Messages))]
        public string Year
        {
            get => this.year;
            set
            {
                this.year = value;
                this.OnPropertyChanged();
            }
        }

        public bool IsWatched
        {
            get => this.isWatched;
            set
            {
                this.isWatched = value;
                this.OnPropertyChanged();
            }
        }

        public bool IsReleased
        {
            get => this.isReleased;
            set
            {
                this.isReleased = value;
                this.OnPropertyChanged();
            }
        }

        [Url(
            ErrorMessageResourceName = nameof(Messages.InvalidPosterUrl),
            ErrorMessageResourceType = typeof(Messages))]
        public string? PosterUrl
        {
            get => this.posterUrl;
            set
            {
                this.posterUrl = value;
                this.OnPropertyChanged();
            }
        }

        public BitmapImage? Poster
        {
            get => this.poster;
            set
            {
                this.poster = value;
                this.OnPropertyChanged();
            }
        }

        public override string Title
            => this.SpecialEpisode.Title.Name;

        public override string Years
            => this.Year;

        public Func<string, OperationResult<bool>> VerifyChannel
            => this.Verify(nameof(this.Channel));

        public Func<string, OperationResult<bool>> VerifyYear
            => this.Verify(nameof(this.Year));

        public Func<string, OperationResult<bool>> VerifyPosterUrl
            => this.Verify(nameof(this.PosterUrl));

        protected override IEnumerable<(Func<object?> CurrentValueProvider, Func<object?> OriginalValueProvider)> Values
            => new List<(Func<object?> CurrentValueProvider, Func<object?> OriginalValueProvider)>
            {
                (() => this.Titles.OrderBy(t => t.Priority).Select(t => t.Name),
                 () => this.SpecialEpisode.Titles.Where(t => !t.IsOriginal).OrderBy(t => t.Priority).Select(t => t.Name)),
                (() => this.OriginalTitles.OrderBy(t => t.Priority).Select(t => t.Name),
                 () => this.SpecialEpisode.Titles.Where(t => t.IsOriginal).OrderBy(t => t.Priority).Select(t => t.Name)),
                (() => this.Month, () => this.SpecialEpisode.Month),
                (() => this.Year, () => this.SpecialEpisode.Year),
                (() => this.IsWatched, () => this.SpecialEpisode.IsWatched),
                (() => this.IsReleased, () => this.SpecialEpisode.IsReleased),
                (() => this.Channel, () => this.SpecialEpisode.Channel),
                (() => this.PosterUrl, () => this.SpecialEpisode.PosterUrl),
            };

        public override void WriteChanges()
        {
            if (this.SpecialEpisode.Id == default)
            {
                this.SpecialEpisode.Titles.Clear();
            }

            foreach (var title in this.Titles.Union(this.OriginalTitles))
            {
                title.WriteChanges();

                if (title.Title.Id == default)
                {
                    this.SpecialEpisode.Titles.Add(title.Title);
                }
            }

            foreach (var title in this.RemovedTitles)
            {
                this.SpecialEpisode.Titles.Remove(title.Title);
            }

            if (this.PosterUrl != this.SpecialEpisode.PosterUrl)
            {
                this.SetPoster();
            }

            this.SpecialEpisode.Month = this.Month;
            this.SpecialEpisode.Year = Int32.Parse(this.Year);
            this.SpecialEpisode.IsWatched = this.IsWatched;
            this.SpecialEpisode.IsReleased = this.IsReleased;
            this.SpecialEpisode.Channel = this.Channel;
            this.SpecialEpisode.PosterUrl = String.IsNullOrEmpty(this.PosterUrl) ? null : this.PosterUrl;

            this.OnPropertyChanged(nameof(this.Title));
            this.OnPropertyChanged(nameof(this.Years));

            this.AreChangesPresent = false;
        }

        public override void RevertChanges()
        {
            this.CopySpecialEpisodeProperties();
            this.AreChangesPresent = false;
        }

        public override void FullyWriteChanges()
        {
            this.WriteChanges();
            this.CopyProperties(this.SpecialEpisode, this.backup);
        }

        public override void FullyRevertChanges()
        {
            this.CopyProperties(this.backup, this.SpecialEpisode);
            this.RevertChanges();
        }

        public override void OpenForm(SidePanelViewModel sidePanel)
            => sidePanel.OpenSeriesComponent.ExecuteIfCan(this);

        public override void WriteOrdinalNumber()
            => this.SpecialEpisode.OrdinalNumber = this.OrdinalNumber;

        private void CopySpecialEpisodeProperties()
        {
            this.CopyTitles(this.SpecialEpisode.Titles);

            this.Month = this.SpecialEpisode.Month;
            this.Year = this.SpecialEpisode.Year.ToString();
            this.IsWatched = this.SpecialEpisode.IsWatched;
            this.IsReleased = this.SpecialEpisode.IsReleased;
            this.Channel = this.SpecialEpisode.Channel;
            this.PosterUrl = this.SpecialEpisode.PosterUrl;
            this.OrdinalNumber = this.SpecialEpisode.OrdinalNumber;

            this.SetPoster();
        }

        private void SetPoster()
        {
            if (!String.IsNullOrEmpty(this.PosterUrl))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(this.PosterUrl, UriKind.Absolute);
                bitmap.EndInit();

                this.Poster = bitmap;
            } else
            {
                this.PosterUrl = null;
                this.Poster = null;
            }
        }

        private void CopyProperties(SpecialEpisode source, SpecialEpisode target)
        {
            target.Id = source.Id;
            target.Series = source.Series;
            target.IsWatched = source.IsWatched;
            target.IsReleased = source.IsReleased;
            target.Channel = source.Channel;
            target.Month = source.Month;
            target.Year = source.Year;
            target.PosterUrl = source.PosterUrl;
            target.OrdinalNumber = source.OrdinalNumber;

            target.Titles.Clear();

            foreach (var title in source.Titles)
            {
                target.Titles.Add(new Title
                {
                    Id = title.Id,
                    Name = title.Name,
                    IsOriginal = title.IsOriginal,
                    SpecialEpisode = target
                });
            }

        }
    }
}
