using System;
using System.Collections.Generic;
using System.Linq;

namespace TopSpeed.Menu
{
    internal sealed class Slider : MenuItem
    {
        private readonly IReadOnlyList<int> _steps;
        private readonly Func<int> _getValue;
        private readonly Action<int> _setValue;
        private readonly Action<int>? _onChanged;

        public Slider(
            string text,
            string rangeOrSteps,
            Func<int> getValue,
            Action<int> setValue,
            Action<int>? onChanged = null,
            MenuAction action = MenuAction.None,
            string? nextMenuId = null,
            bool suppressPostActivateAnnouncement = false,
            string? hint = null)
            : this(text, ParseSteps(rangeOrSteps), getValue, setValue, onChanged, action, nextMenuId, suppressPostActivateAnnouncement, hint)
        {
        }

        public Slider(
            string text,
            int minValue,
            int maxValue,
            Func<int> getValue,
            Action<int> setValue,
            Action<int>? onChanged = null,
            MenuAction action = MenuAction.None,
            string? nextMenuId = null,
            bool suppressPostActivateAnnouncement = false,
            string? hint = null)
            : this(text, BuildRange(minValue, maxValue), getValue, setValue, onChanged, action, nextMenuId, suppressPostActivateAnnouncement, hint)
        {
        }

        public Slider(
            string text,
            IEnumerable<int> steps,
            Func<int> getValue,
            Action<int> setValue,
            Action<int>? onChanged = null,
            MenuAction action = MenuAction.None,
            string? nextMenuId = null,
            bool suppressPostActivateAnnouncement = false,
            string? hint = null)
            : base(text, action, nextMenuId, onActivate: null, suppressPostActivateAnnouncement, hint)
        {
            if (steps == null)
                throw new ArgumentNullException(nameof(steps));
            _steps = NormalizeSteps(steps);
            if (_steps.Count == 0)
                throw new ArgumentException("Slider requires at least one step.", nameof(steps));
            _getValue = getValue ?? throw new ArgumentNullException(nameof(getValue));
            _setValue = setValue ?? throw new ArgumentNullException(nameof(setValue));
            _onChanged = onChanged;
        }

        public override string GetDisplayText()
        {
            return $"{GetBaseText()}; slider {GetValueLabel(_getValue())}";
        }

        public override string? ActivateAndGetAnnouncement()
        {
            return null;
        }

        public override bool Adjust(MenuAdjustAction action, out string? announcement)
        {
            announcement = null;
            var currentValue = _getValue();
            var currentIndex = GetClosestIndex(currentValue);
            var targetIndex = currentIndex;

            switch (action)
            {
                case MenuAdjustAction.Decrease:
                    targetIndex = currentIndex - 1;
                    break;
                case MenuAdjustAction.Increase:
                    targetIndex = currentIndex + 1;
                    break;
                case MenuAdjustAction.PageDecrease:
                    targetIndex = currentIndex - 10;
                    break;
                case MenuAdjustAction.PageIncrease:
                    targetIndex = currentIndex + 10;
                    break;
                case MenuAdjustAction.ToMinimum:
                    targetIndex = 0;
                    break;
                case MenuAdjustAction.ToMaximum:
                    targetIndex = _steps.Count - 1;
                    break;
            }

            targetIndex = Math.Max(0, Math.Min(_steps.Count - 1, targetIndex));
            var newValue = _steps[targetIndex];
            if (newValue == currentValue)
                return true;

            _setValue(newValue);
            _onChanged?.Invoke(newValue);
            announcement = GetValueLabel(newValue);
            return true;
        }

        private static IReadOnlyList<int> ParseSteps(string rangeOrSteps)
        {
            if (string.IsNullOrWhiteSpace(rangeOrSteps))
                throw new ArgumentException("Slider requires a range or steps list.", nameof(rangeOrSteps));

            var trimmed = rangeOrSteps.Trim();
            if (trimmed.Contains("-"))
            {
                var parts = trimmed.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 &&
                    int.TryParse(parts[0].Trim(), out var min) &&
                    int.TryParse(parts[1].Trim(), out var max))
                {
                    return BuildRange(min, max);
                }
            }

            if (trimmed.Contains(","))
            {
                var values = new List<int>();
                foreach (var part in trimmed.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (int.TryParse(part.Trim(), out var value))
                        values.Add(value);
                }
                return NormalizeSteps(values);
            }

            if (int.TryParse(trimmed, out var single))
                return new[] { single };

            throw new ArgumentException("Slider range or step list is invalid.", nameof(rangeOrSteps));
        }

        private static IReadOnlyList<int> BuildRange(int minValue, int maxValue)
        {
            var min = Math.Min(minValue, maxValue);
            var max = Math.Max(minValue, maxValue);
            var values = new List<int>(max - min + 1);
            for (var i = min; i <= max; i++)
                values.Add(i);
            return values;
        }

        private static IReadOnlyList<int> NormalizeSteps(IEnumerable<int> steps)
        {
            return steps
                .Distinct()
                .OrderBy(value => value)
                .ToArray();
        }

        private string GetValueLabel(int value)
        {
            return value.ToString();
        }

        private int GetClosestIndex(int value)
        {
            var bestIndex = 0;
            var bestDistance = int.MaxValue;
            for (var i = 0; i < _steps.Count; i++)
            {
                var distance = Math.Abs(_steps[i] - value);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }
            return bestIndex;
        }
    }
}
