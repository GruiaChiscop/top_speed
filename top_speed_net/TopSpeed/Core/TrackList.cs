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
            new TrackInfo("america", "Circuit of the Americas (USA)"),
            new TrackInfo("austria", "Red Bull Ring (Austria)"),
            new TrackInfo("belgium", "Spa-Francorchamps (Belgium)"),
            new TrackInfo("brazil", "Interlagos (Brazil)"),
            new TrackInfo("china", "Shanghai International Circuit (China)"),
            new TrackInfo("england", "Silverstone Circuit (England)"),
            new TrackInfo("finland", "KymiRing (Finland)"),
            new TrackInfo("france", "Circuit Paul Ricard (France)"),
            new TrackInfo("germany", "Nurburgring GP (Germany)"),
            new TrackInfo("ireland", "Mondello Park (Ireland)"),
            new TrackInfo("italy", "Monza (Italy)"),
            new TrackInfo("netherlands", "Circuit Zandvoort (Netherlands)"),
            new TrackInfo("portugal", "Algarve International Circuit (Portugal)"),
            new TrackInfo("russia", "Sochi Autodrom (Russia)"),
            new TrackInfo("spain", "Circuit de Barcelona-Catalunya (Spain)"),
            new TrackInfo("sweden", "Scandinavian Raceway (Sweden)"),
            new TrackInfo("switserland", "Zurich Street Circuit (Switzerland)")
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
            return category switch
            {
                TrackCategory.RaceTrack => RaceTracks,
                TrackCategory.StreetAdventure => AdventureTracks,
                _ => Array.Empty<TrackInfo>()
            };
        }

        public static bool TryGetDisplayName(string key, out string display)
        {
            display = string.Empty;
            if (string.IsNullOrWhiteSpace(key))
                return false;

            foreach (var track in RaceTracks)
            {
                if (string.Equals(track.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    display = track.Display;
                    return true;
                }
            }

            foreach (var track in AdventureTracks)
            {
                if (string.Equals(track.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    display = track.Display;
                    return true;
                }
            }

            return false;
        }

        public static string GetRandomTrackKey(TrackCategory category, IEnumerable<string> customTracks)
        {
            var candidates = new List<string>();
            var source = category switch
            {
                TrackCategory.RaceTrack => RaceTracks,
                TrackCategory.StreetAdventure => AdventureTracks,
                _ => Array.Empty<TrackInfo>()
            };
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
                candidates.AddRange(customTracks.Select(file => (file, TrackCategory.CustomTrack)));

            if (candidates.Count == 0)
                return (RaceTracks[0].Key, TrackCategory.RaceTrack);

            var pick = candidates[Algorithm.RandomInt(candidates.Count)];
            return pick;
        }
    }
}
