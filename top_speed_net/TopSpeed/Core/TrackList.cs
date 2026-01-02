using System;
using System.Collections.Generic;
using System.Linq;
using TopSpeed.Common;

namespace TopSpeed.Core
{
    internal readonly struct TrackInfo
    {
        public TrackInfo(string key, string display)
        {
            Key = key;
            Display = display;
        }

        public string Key { get; }
        public string Display { get; }
    }

    internal static class TrackList
    {
        public static readonly TrackInfo[] RaceTracks =
        {
            new TrackInfo("america", "America"),
            new TrackInfo("austria", "Austria"),
            new TrackInfo("belgium", "Belgium"),
            new TrackInfo("brazil", "Brazil"),
            new TrackInfo("china", "China"),
            new TrackInfo("england", "England"),
            new TrackInfo("finland", "Finland"),
            new TrackInfo("france", "France"),
            new TrackInfo("germany", "Germany"),
            new TrackInfo("ireland", "Ireland"),
            new TrackInfo("italy", "Italy"),
            new TrackInfo("netherlands", "Netherlands"),
            new TrackInfo("portugal", "Portugal"),
            new TrackInfo("russia", "Russia"),
            new TrackInfo("spain", "Spain"),
            new TrackInfo("sweden", "Sweden"),
            new TrackInfo("switserland", "Switserland")
        };

        public static readonly TrackInfo[] AdventureTracks =
        {
            new TrackInfo("advHills", "Rally hills"),
            new TrackInfo("advCoast", "French coast"),
            new TrackInfo("advCountry", "English country"),
            new TrackInfo("advAirport", "Ride airport"),
            new TrackInfo("advDesert", "Rally desert"),
            new TrackInfo("advRush", "Rush hour"),
            new TrackInfo("advEscape", "Polar escape")
        };

        public static IReadOnlyList<TrackInfo> GetTracks(TrackCategory category)
        {
            return category == TrackCategory.RaceTrack ? RaceTracks : AdventureTracks;
        }

        public static string GetRandomTrackKey(TrackCategory category, IEnumerable<string> customTracks)
        {
            var candidates = new List<string>();
            var source = category == TrackCategory.RaceTrack ? RaceTracks : AdventureTracks;
            candidates.AddRange(source.Select(t => t.Key));

            if (customTracks != null)
                candidates.AddRange(customTracks);

            if (candidates.Count == 0)
                return RaceTracks[0].Key;

            var index = Algorithm.RandomInt(candidates.Count);
            return candidates[index];
        }

        public static (string Key, TrackCategory Category) GetRandomTrackAny(IEnumerable<string> customTracks)
        {
            var candidates = new List<(string Key, TrackCategory Category)>();
            candidates.AddRange(RaceTracks.Select(track => (track.Key, TrackCategory.RaceTrack)));
            candidates.AddRange(AdventureTracks.Select(track => (track.Key, TrackCategory.StreetAdventure)));
            if (customTracks != null)
                candidates.AddRange(customTracks.Select(file => (file, TrackCategory.RaceTrack)));

            if (candidates.Count == 0)
                return (RaceTracks[0].Key, TrackCategory.RaceTrack);

            var pick = candidates[Algorithm.RandomInt(candidates.Count)];
            return pick;
        }
    }
}
