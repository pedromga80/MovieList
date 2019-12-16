using System;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Resources;

using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace MovieList.ViewModels.Forms
{
    public abstract class SeriesComponentFormViewModelBase<TModel, TViewModel> : TitledFormViewModelBase<TModel, TViewModel>
        where TModel : class
        where TViewModel : SeriesComponentFormViewModelBase<TModel, TViewModel>
    {
        protected SeriesComponentFormViewModelBase(
            SeriesFormViewModel parent,
            IObservable<int> maxSequenceNumber,
            ResourceManager? resourceManager,
            IScheduler? scheduler = null)
            : base(resourceManager, scheduler)
        {
            this.Parent = parent;

            this.GoToSeries = ReactiveCommand.Create<Unit, SeriesFormViewModel>(_ => this.Parent, this.Valid);

            var canMoveUp = this.WhenAnyValue(vm => vm.SequenceNumber).Select(num => num != 1);

            var canMoveDown = Observable.CombineLatest(this.WhenAnyValue(vm => vm.SequenceNumber), maxSequenceNumber)
                .Select(nums => nums[0] < nums[1]);

            this.MoveUp = ReactiveCommand.Create(() => { this.SequenceNumber--; }, canMoveUp);
            this.MoveDown = ReactiveCommand.Create(() => { this.SequenceNumber++; }, canMoveDown);
        }

        public SeriesFormViewModel Parent { get; }

        [Reactive]
        public int SequenceNumber { get; set; }

        public ReactiveCommand<Unit, SeriesFormViewModel> GoToSeries { get; }

        public ReactiveCommand<Unit, Unit> MoveUp { get; }
        public ReactiveCommand<Unit, Unit> MoveDown { get; }
    }
}
