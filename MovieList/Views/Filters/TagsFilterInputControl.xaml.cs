using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows.Controls;

using DynamicData.Aggregation;
using DynamicData.Binding;

using MovieList.Core;
using MovieList.Core.ViewModels.Filters;
using MovieList.Core.ViewModels.Forms.Preferences;

using ReactiveUI;

namespace MovieList.Views.Filters
{
    public abstract class TagsFilterInputControlBase : ReactiveUserControl<TagsFilterInputViewModel> { }

    public partial class TagsFilterInputControl : TagsFilterInputControlBase
    {
        public TagsFilterInputControl()
        {
            this.InitializeComponent();

            this.WhenActivated(disposables =>
            {
                this.WhenAnyValue(v => v.ViewModel)
                    .BindTo(this, v => v.DataContext)
                    .DisposeWith(disposables);

                this.OneWayBind(this.ViewModel, vm => vm.Tags, v => v.Tags.ItemsSource)
                    .DisposeWith(disposables);

                this.OneWayBind(this.ViewModel, vm => vm.AddableTags, v => v.AddableTagsComboBox.ItemsSource)
                    .DisposeWith(disposables);

                this.AddableTagsComboBox.Events()
                    .SelectionChanged
                    .Select(e => e.AddedItems.OfType<AddableTagViewModel>().FirstOrDefault())
                    .WhereNotNull()
                    .Select(vm => vm.Tag)
                    .InvokeCommand(this.ViewModel!.AddTag)
                    .DisposeWith(disposables);

                this.ViewModel!.AddableTags
                    .ToObservableChangeSet()
                    .Count()
                    .StartWith(this.ViewModel.AddableTags.Count)
                    .Select(count => count > 0)
                    .BindTo(this, v => v.AddableTagsComboBox.IsEnabled)
                    .DisposeWith(disposables);
            });
        }
    }
}
