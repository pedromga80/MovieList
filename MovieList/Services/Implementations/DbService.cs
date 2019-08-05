using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using MovieList.Config;
using MovieList.Data;
using MovieList.Data.Models;
using MovieList.ViewModels;
using MovieList.ViewModels.ListItems;

namespace MovieList.Services.Implementations
{
    public class DbService : IDbService
    {
        private readonly IServiceProvider serviceProvider;
        private readonly IOptions<Configuration> config;

        public DbService(IServiceProvider serviceProvider, IOptions<Configuration> config)
        {
            this.serviceProvider = serviceProvider;
            this.config = config;
        }

        public async Task<List<ListItem>> LoadListAsync()
        {
            using var context = this.serviceProvider.GetRequiredService<MovieContext>();

            var movies = await context.Movies
                .Include(context.GetIncludePaths(typeof(Movie)))
                .ToListAsync();

            var series = await context.Series
                .Include(context.GetIncludePaths(typeof(Series)))
                .Include(series => series.Seasons)
                    .ThenInclude(season => season.Titles)
                .Include(series => series.Seasons)
                    .ThenInclude(season => season.Periods)
                .Include(series => series.SpecialEpisodes)
                    .ThenInclude(episode => episode.Titles)
                .ToListAsync();

            var movieSeries = await context.MovieSeries
                .Include(ms => ms.Entries)
                    .ThenInclude(e => e.Movie)
                .Include(ms => ms.Entries)
                    .ThenInclude(e => e.Series)
                .Include(ms => ms.Titles)
                .ToListAsync();

            return movies
                .Select(movie => new MovieListItem(movie, this.config.Value))
                .Cast<ListItem>()
                .Union(series.Select(series => new SeriesListItem(series, this.config.Value)))
                .Union(movieSeries
                    .Where(series => series.Title != null)
                    .Select(series => new MovieSeriesListItem(series, this.config.Value)))
                .OrderBy(item => item)
                .ToList();
        }

        public async Task<ObservableCollection<KindViewModel>> LoadAllKindsAsync()
        {
            using var context = serviceProvider.GetRequiredService<MovieContext>();

            return new ObservableCollection<KindViewModel>(
                await context.Kinds
                    .Include(context.GetIncludePaths(typeof(Kind)))
                    .OrderBy(k => k.Id)
                    .Select(k => new KindViewModel(k))
                    .ToListAsync());
        }

        public async Task SaveMovieAsync(Movie movie, IEnumerable<Title> titlesToDelete)
        {
            using var context = this.serviceProvider.GetRequiredService<MovieContext>();

            if (movie.Id == default)
            {
                context.ChangeTracker.TrackGraph(movie, node =>
                    node.Entry.State = !node.Entry.IsKeySet ? EntityState.Added : EntityState.Unchanged);
            } else
            {
                context.Entry(movie).State = EntityState.Modified;

                foreach (var title in movie.Titles)
                {
                    this.AddOrUpdate(context, title);
                }
            }

            foreach (var title in titlesToDelete)
            {
                context.Attach(title).State = EntityState.Deleted;
            }

            await context.SaveChangesAsync();
        }

        public async Task SaveSeriesAsync(
            Series series,
            IEnumerable<Season> seasonsToDelete,
            IEnumerable<SpecialEpisode> episodesToDelete,
            IEnumerable<Title> titlesToDelete)
        {
            using var context = this.serviceProvider.GetRequiredService<MovieContext>();

            if (series.Id == default)
            {
                context.ChangeTracker.TrackGraph(series, node =>
                    node.Entry.State = !node.Entry.IsKeySet ? EntityState.Added : EntityState.Unchanged);
            } else
            {
                context.Entry(series).State = EntityState.Modified;

                foreach (var title in series.Titles)
                {
                    this.AddOrUpdate(context, title);
                }

                foreach (var season in series.Seasons)
                {
                    this.AddOrUpdate(context, season);

                    foreach (var period in season.Periods)
                    {
                        this.AddOrUpdate(context, period);
                    }

                    foreach (var title in season.Titles)
                    {
                        this.AddOrUpdate(context, title);
                    }
                }

                foreach (var episode in series.SpecialEpisodes)
                {
                    this.AddOrUpdate(context, episode);

                    foreach (var title in episode.Titles)
                    {
                        this.AddOrUpdate(context, title);
                    }
                }
            }

            foreach (var season in seasonsToDelete)
            {
                context.Attach(season).State = EntityState.Deleted;

                foreach (var period in season.Periods)
                {
                    context.Entry(period).State = EntityState.Deleted;
                }

                foreach (var title in season.Titles)
                {
                    context.Entry(title).State = EntityState.Deleted;
                }
            }

            foreach (var episode in episodesToDelete)
            {
                context.Attach(episode).State = EntityState.Deleted;

                foreach (var title in episode.Titles)
                {
                    context.Entry(title).State = EntityState.Deleted;
                }
            }

            foreach (var title in titlesToDelete)
            {
                context.Attach(title).State = EntityState.Deleted;
            }

            await context.SaveChangesAsync();
        }

        public async Task SaveMovieSeriesAsync(
            MovieSeries movieSeries,
            IEnumerable<Title> titlesToDelete,
            IEnumerable<MovieSeriesEntry> entriesToDelete,
            IEnumerable<MovieSeries> partsToDetach)
        {
            using var context = this.serviceProvider.GetRequiredService<MovieContext>();

            if (movieSeries.Id == default)
            {
                context.Entry(movieSeries).State = EntityState.Added;
                foreach (var entry in movieSeries.Entries)
                {
                    context.Entry(entry).State = EntityState.Added;
                }
            } else
            {
                context.Entry(movieSeries).State = EntityState.Modified;

                foreach (var title in movieSeries.Titles)
                {
                    this.AddOrUpdate(context, title);
                }

                foreach (var entry in movieSeries.Entries)
                {
                    this.AddOrUpdate(context, entry);
                }

                foreach (var part in movieSeries.Parts)
                {
                    this.AddOrUpdate(context, part);
                }
            }

            foreach (var title in titlesToDelete)
            {
                context.Attach(title).State = EntityState.Deleted;
            }

            foreach (var entry in entriesToDelete)
            {
                context.Attach(entry).State = EntityState.Deleted;
                this.MoveOrdinalNumbersUp(movieSeries, entry.OrdinalNumber);
            }

            foreach (var part in partsToDetach)
            {
                part.ParentSeries = null;
                part.OrdinalNumber = null;
                part.DisplayNumber = null;
                context.Attach(part).State = EntityState.Modified;
                movieSeries.Parts.Remove(part);

                this.MoveOrdinalNumbersUp(movieSeries, part.OrdinalNumber ?? 0);
            }

            await context.SaveChangesAsync();
        }

        public async Task SaveKindsAsync(IEnumerable<KindViewModel> kinds)
        {
            if (kinds.Any(k => k.HasErrors))
            {
                throw new ArgumentException("Cannot save invalid kinds.", nameof(kinds));
            }

            using var context = this.serviceProvider.GetRequiredService<MovieContext>();

            var dbKinds = await context.Kinds
                .Include(context.GetIncludePaths(typeof(Kind)))
                .AsNoTracking()
                .ToListAsync();

            var kindsToSave = kinds.Select(k => k.Kind).ToList();

            foreach (var kind in kindsToSave)
            {
                if (await context.Kinds.ContainsAsync(kind))
                {
                    context.Attach(kind).State = EntityState.Modified;
                } else
                {
                    context.Kinds.Add(kind);
                }
            }

            foreach (var kind in dbKinds.Except(kindsToSave, IdEqualityComparer<Kind>.Instance))
            {
                context.Attach(kind).State = EntityState.Deleted;
            }

            await context.SaveChangesAsync();
        }

        public async Task ToggleWatchedAsync(ListItem item)
        {
            using var context = this.serviceProvider.GetRequiredService<MovieContext>();

            switch (item)
            {
                case MovieListItem movieItem:
                    movieItem.Movie.IsWatched = !movieItem.Movie.IsWatched;
                    context.Attach(movieItem.Movie).State = EntityState.Modified;
                    await context.SaveChangesAsync();
                    break;
                case SeriesListItem seriesItem:
                    seriesItem.Series.IsWatched = !seriesItem.Series.IsWatched;
                    context.Attach(seriesItem.Series).State = EntityState.Modified;
                    await context.SaveChangesAsync();
                    break;
            }
        }

        public async Task ToggleReleasedAsync(MovieListItem item)
        {
            using var context = this.serviceProvider.GetRequiredService<MovieContext>();

            item.Movie.IsReleased = !item.Movie.IsReleased;
            context.Attach(item.Movie).State = EntityState.Modified;
            await context.SaveChangesAsync();
        }

        public async Task DeleteAsync(Movie movie)
        {
            using var context = this.serviceProvider.GetRequiredService<MovieContext>();

            context.Entry(movie).State = EntityState.Deleted;

            if (movie.Entry != null)
            {
                context.Entry(movie.Entry).State = EntityState.Deleted;

                movie.Entry.MovieSeries.Entries.Remove(movie.Entry);

                if (movie.Entry.MovieSeries.Entries.Count == 0)
                {
                    context.Entry(movie.Entry.MovieSeries).State = EntityState.Deleted;
                }
            }

            foreach (var title in movie.Titles)
            {
                context.Entry(title).State = EntityState.Deleted;
            }

            if (movie.Entry != null)
            {
                context.Entry(movie.Entry).State = EntityState.Deleted;
                this.MoveOrdinalNumbersUp(movie.Entry.MovieSeries, movie.Entry.OrdinalNumber);
            }

            await context.SaveChangesAsync();
        }

        public async Task DeleteAsync(Series series)
        {
            using var context = this.serviceProvider.GetRequiredService<MovieContext>();

            context.Entry(series).State = EntityState.Deleted;

            if (series.Entry != null)
            {
                context.Entry(series.Entry).State = EntityState.Deleted;

                series.Entry.MovieSeries.Entries.Remove(series.Entry);

                if (series.Entry.MovieSeries.Entries.Count == 0)
                {
                    context.Entry(series.Entry.MovieSeries).State = EntityState.Deleted;
                }
            }

            foreach (var title in series.Titles)
            {
                context.Entry(title).State = EntityState.Deleted;
            }

            foreach (var season in series.Seasons)
            {
                context.Entry(season).State = EntityState.Deleted;

                foreach (var period in season.Periods)
                {
                    context.Entry(period).State = EntityState.Deleted;
                }

                foreach (var title in season.Titles)
                {
                    context.Entry(title).State = EntityState.Deleted;
                }
            }

            foreach (var episode in series.SpecialEpisodes)
            {
                context.Attach(episode).State = EntityState.Deleted;

                foreach (var title in episode.Titles)
                {
                    context.Entry(title).State = EntityState.Deleted;
                }
            }

            if (series.Entry != null)
            {
                context.Entry(series.Entry).State = EntityState.Deleted;
                this.MoveOrdinalNumbersUp(series.Entry.MovieSeries, series.Entry.OrdinalNumber);
            }

            await context.SaveChangesAsync();
        }

        public async Task DeleteAsync<TEntity>(TEntity entity)
            where TEntity : EntityBase
        {
            using var context = this.serviceProvider.GetRequiredService<MovieContext>();

            context.Attach(entity).State = EntityState.Deleted;
            await context.SaveChangesAsync();
        }

        public async Task DeleteAsync<TEntity>(IEnumerable<TEntity> entities)
            where TEntity : EntityBase
        {
            using var context = this.serviceProvider.GetRequiredService<MovieContext>();

            foreach (var entity in entities)
            {
                context.Attach(entity).State = EntityState.Deleted;
            }

            await context.SaveChangesAsync();
        }

        public async Task<bool> CanDeleteKindAsync(KindViewModel kind)
        {
            using var context = this.serviceProvider.GetRequiredService<MovieContext>();

            return (await context.Movies.Where(m => m.KindId == kind.Kind.Id).CountAsync()) == 0 &&
                (await context.Series.Where(s => s.KindId == kind.Kind.Id).CountAsync()) == 0;
        }

        private void AddOrUpdate(DbContext context, EntityBase entity)
        {
            if (entity.Id == default)
            {
                context.Add(entity);
            } else
            {
                context.Entry(entity).State = EntityState.Modified;
            }
        }

        private void MoveOrdinalNumbersUp(MovieSeries movieSeries, int ordinalNumber)
        {
            foreach (var entry in movieSeries.Entries.Where(entry => entry.OrdinalNumber > ordinalNumber))
            {
                entry.OrdinalNumber--;
                if (entry.DisplayNumber != null)
                {
                    entry.DisplayNumber--;
                }
            }

            foreach (var part in movieSeries.Parts.Where(part => part.OrdinalNumber > ordinalNumber))
            {
                part.OrdinalNumber--;
                if (part.DisplayNumber != null)
                {
                    part.DisplayNumber--;
                }
            }
        }
    }
}
