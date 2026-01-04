using System;
using System.Collections.Generic;
using System.Linq;

namespace TopSpeed.Menu
{
    internal sealed class RadioButton : MenuItem
    {
        private readonly IReadOnlyList<string> _values;
        private readonly Func<int> _getIndex;
        private readonly Action<int> _setIndex;
        private readonly Action<int>? _onChanged;

        public RadioButton(
            string text,
            IEnumerable<string> values,
            Func<int> getIndex,
            Action<int> setIndex,
            Action<int>? onChanged = null,
            MenuAction action = MenuAction.None,
            string? nextMenuId = null,
            bool suppressPostActivateAnnouncement = false,
            string? hint = null)
            : base(text, action, nextMenuId, onActivate: null, suppressPostActivateAnnouncement, hint)
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));

            var valueList = values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (valueList.Length < 2)
                throw new ArgumentException("Radio button requires at least two values.", nameof(values));

            _values = valueList;
            _getIndex = getIndex ?? throw new ArgumentNullException(nameof(getIndex));
            _setIndex = setIndex ?? throw new ArgumentNullException(nameof(setIndex));
            _onChanged = onChanged;
        }

        public override string GetDisplayText()
        {
            return $"{GetBaseText()}; radio button {GetValueLabel(_getIndex())}";
        }

        public override string? ActivateAndGetAnnouncement()
        {
            return null;
        }

        public override bool Adjust(MenuAdjustAction action, out string? announcement)
        {
            announcement = null;
            var currentIndex = NormalizeIndex(_getIndex());
            var targetIndex = currentIndex;

            switch (action)
            {
                case MenuAdjustAction.Decrease:
                    targetIndex = currentIndex == 0 ? _values.Count - 1 : currentIndex - 1;
                    break;
                case MenuAdjustAction.Increase:
                    targetIndex = currentIndex == _values.Count - 1 ? 0 : currentIndex + 1;
                    break;
                default:
                    return false;
            }
            if (targetIndex == currentIndex)
                return true;

            _setIndex(targetIndex);
            _onChanged?.Invoke(targetIndex);
            announcement = _values[targetIndex];
            return true;
        }

        private string GetValueLabel(int index)
        {
            var normalized = NormalizeIndex(index);
            return _values[normalized];
        }

        private int NormalizeIndex(int index)
        {
            if (_values.Count == 0)
                return 0;
            if (index < 0)
                return 0;
            if (index >= _values.Count)
                return 0;
            return index;
        }
    }
}
