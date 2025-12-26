using System;
using TS.Audio;

namespace TopSpeed.Menu
{
    internal sealed class MenuItem
    {
        private readonly string _text;
        private readonly Func<string>? _textProvider;

        public string Text => _text;
        public MenuAction Action { get; }
        public string? NextMenuId { get; }
        public string? SoundFile { get; }
        public AudioSourceHandle? Sound { get; set; }
        public Action? OnActivate { get; }
        public bool SuppressPostActivateAnnouncement { get; }

        public MenuItem(
            string text,
            MenuAction action,
            string? soundFile,
            string? nextMenuId = null,
            Action? onActivate = null,
            bool suppressPostActivateAnnouncement = false)
        {
            _text = text;
            _textProvider = null;
            Action = action;
            SoundFile = soundFile;
            NextMenuId = nextMenuId;
            OnActivate = onActivate;
            SuppressPostActivateAnnouncement = suppressPostActivateAnnouncement;
        }

        public MenuItem(
            Func<string> textProvider,
            MenuAction action,
            string? soundFile,
            string? nextMenuId = null,
            Action? onActivate = null,
            bool suppressPostActivateAnnouncement = false)
        {
            _text = string.Empty;
            _textProvider = textProvider ?? throw new ArgumentNullException(nameof(textProvider));
            Action = action;
            SoundFile = soundFile;
            NextMenuId = nextMenuId;
            OnActivate = onActivate;
            SuppressPostActivateAnnouncement = suppressPostActivateAnnouncement;
        }

        public string GetDisplayText()
        {
            return _textProvider?.Invoke() ?? _text;
        }
    }
}
