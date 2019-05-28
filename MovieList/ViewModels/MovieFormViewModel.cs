using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

using HandyControl.Controls;

using Microsoft.Extensions.DependencyInjection;

using MovieList.Events;
using MovieList.Properties;
using MovieList.Services;
using MovieList.ViewModels.FormItems;
using MovieList.ViewModels.ListItems;
using MovieList.Views;

using MessageBox = System.Windows.MessageBox;

namespace MovieList.ViewModels
{
    public class MovieFormViewModel : ViewModelBase
    {
        private readonly App app;

        private MovieFormItem movie;
        private ObservableCollection<KindViewModel> allKinds;

        public MovieFormViewModel(App app, MovieListViewModel movieList, SidePanelViewModel sidePanel, SettingsViewModel settings)
        {
            this.app = app;

            this.MovieList = movieList;
            this.SidePanel = sidePanel;

            this.Save = new DelegateCommand(async _ => await this.SaveAsync(), _ => this.CanSaveChanges);
            this.Cancel = new DelegateCommand(_ => this.Movie.RevertChanges(), _ => this.CanCancelChanges);
            this.Delete = new DelegateCommand(async _ => await this.DeleteAsync(), _ => this.CanDelete());

            movieList.ListItemUpdated += this.OnListItemUpdated;
            settings.SettingsUpdated += this.OnSettingsUpdated;
        }

        public ICommand Save { get; }
        public ICommand Cancel { get; }
        public ICommand Delete { get; }

        public MovieFormControl MovieFormControl { get; set; }

        public MovieFormItem Movie
        {
            get => this.movie;
            set
            {
                this.movie = value;
                this.movie.PropertyChanged += this.OnMoviePropertyChanged;

                this.OnPropertyChanged();
                this.OnPropertyChanged(nameof(FormTitle));
            }
        }

        public ObservableCollection<KindViewModel> AllKinds
        {
            get => this.allKinds;
            set
            {
                this.allKinds = value;
                this.OnPropertyChanged();
            }
        }

        public MovieListViewModel MovieList { get; }
        public SidePanelViewModel SidePanel { get; }

        public string FormTitle
            => this.movie.Movie.Id != default ? this.movie.Movie.Title.Name : Messages.NewMovie;

        public bool CanSaveChanges
            => this.Movie.AreChangesPresent &&
                !this.Movie.HasErrors &&
                !this.Movie.Titles.Any(t => t.HasErrors) &&
                !this.Movie.OriginalTitles.Any(t => t.HasErrors);

        public bool CanCancelChanges
            => this.Movie.AreChangesPresent;

        public bool CanSaveOrCancelChanges
            => this.CanSaveChanges || this.CanCancelChanges;

        public async Task SaveAsync()
        {
            this.Movie.ClearEmptyTitles();

            if (!this.MovieFormControl
                .FindVisualChildren<TextBox>()
                .Select(textBox => textBox.VerifyData())
                .Aggregate(true, (a, b) => a && b))
            {
                return;
            }

            this.Movie.WriteChanges();

            using var scope = this.app.ServiceProvider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IMovieService>();

            bool shouldAddToList = this.Movie.Movie.Id == default;

            await service.SaveMovieAsync(this.Movie.Movie);

            (shouldAddToList ? this.MovieList.AddItem : this.MovieList.UpdateItem).ExecuteIfCan(this.Movie.Movie);
        }

        public async Task DeleteAsync()
        {
            var result = MessageBox.Show(Messages.DeleteMoviePrompt, Messages.Delete, MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                using var scope = this.app.ServiceProvider.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IMovieService>();

                await service.DeleteAsync(this.Movie.Movie);

                this.SidePanel.Close.ExecuteIfCan(null);
                this.MovieList.DeleteItem.ExecuteIfCan(this.Movie.Movie);
            }
        }

        public bool CanDelete()
            => this.Movie.Movie.Id != default;

        protected override void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            base.OnPropertyChanged(propertyName);

            if (propertyName != nameof(this.AllKinds))
            {
                base.OnPropertyChanged(nameof(this.CanSaveChanges));
                base.OnPropertyChanged(nameof(this.CanCancelChanges));
                base.OnPropertyChanged(nameof(this.CanSaveOrCancelChanges));
            }
        }

        private void OnListItemUpdated(object sender, ListItemUpdatedEventArgs e)
        {
            if (e.Item is MovieListItem item)
            {
                this.Movie.IsWatched = item.Movie.IsWatched;
                this.Movie.IsReleased = item.Movie.IsReleased;
            }
        }

        private void OnMoviePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            this.OnPropertyChanged(nameof(this.Movie));

            if (e.PropertyName == nameof(this.movie.Year) && Int32.TryParse(movie.Year, out int year))
            {
                if (year > DateTime.Now.Year)
                {
                    this.Movie.IsReleased = false;
                    this.Movie.IsWatched = false;
                } else if (year < DateTime.Now.Year)
                {
                    this.Movie.IsReleased = true;
                }
            }
        }

        private void OnSettingsUpdated(object sender, SettingsUpdatedEventArgs e)
        {
            this.AllKinds = new ObservableCollection<KindViewModel>(e.Kinds);
            this.Movie.Kind = this.AllKinds.First(k => k.Kind.Id == this.Movie.Kind.Kind.Id);
        }
    }
}
