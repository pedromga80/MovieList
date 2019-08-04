using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace MovieList.ViewModels.FormItems
{
    public abstract class FormItemBase : ViewModelBase, IEquatable<FormItemBase>
    {
        private bool areChangesPresent;

        public bool AreChangesPresent
        {
            get => this.areChangesPresent;
            protected set
            {
                this.areChangesPresent = value;
                this.OnPropertyChanged();
            }
        }

        protected bool IsInitialized { get; set; }

        protected abstract IEnumerable<(Func<object?> CurrentValueProvider, Func<object?> OriginalValueProvider)> Values { get; }

        public abstract void WriteChanges();
        public abstract void RevertChanges();

        public override bool Equals(object? obj)
            => obj is FormItemBase item && this.Equals(item);

        public bool Equals(FormItemBase? item)
            => item != null &&
                this.Values.Count() == item.Values.Count() &&
                this.Values
                    .Select(v => v.CurrentValueProvider())
                    .Zip(
                        item.Values.Select(v => v.CurrentValueProvider()),
                        (v1, v2) => (v1, v2))
                    .All(v =>
                        !(v.v1 == null && v.v2 != null) &&
                        !(v.v1 != null && v.v2 == null) &&
                        ((v.v1 == null && v.v2 == null) ||
                            (v.v1 is IEnumerable<object> e1 && v.v2 is IEnumerable<object> e2 && e1.SequenceEqual(e2)) ||
                            v.v1!.Equals(v.v2)));

        public override int GetHashCode()
            => Util.GetHashCode(this.Values.Select(v => v.CurrentValueProvider()).ToArray());

        protected override void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            if (this.IsInitialized)
            {
                base.OnPropertyChanged(propertyName);

                if (propertyName != nameof(AreChangesPresent))
                {
                    this.CheckIfValuesChanged();
                }
            }
        }

        private void CheckIfValuesChanged()
            => this.AreChangesPresent = this.Values.Any(v =>
                !this.AreValuesEqual(v.CurrentValueProvider(), v.OriginalValueProvider()));

        private bool AreValuesEqual(object? a, object? b)
            => !(a == null && b != null) &&
                !(a != null && b == null) &&
                ((a == null && b == null) ||
                 (a is IEnumerable<object> e1 && b is IEnumerable<object> e2 && e1.SequenceEqual(e2)) ||
                 a!.Equals(b));
    }
}
