using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TopSpeed.Common;
using TopSpeed.Data;
using TopSpeed.Input;

namespace TopSpeed.Core
{
    internal sealed class RaceSelection
    {
        private readonly RaceSetup _setup;
        private readonly RaceSettings _settings;

        public RaceSelection(RaceSetup setup, RaceSettings settings)
        {
            _setup = setup ?? throw new ArgumentNullException(nameof(setup));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public void SelectTrack(TrackCategory category, string trackKey)
        {
            _setup.TrackCategory = category;
            _setup.TrackNameOrFile = trackKey;
        }

        public void SelectRandomTrack(TrackCategory category)
        {
            SelectRandomTrack(category, _settings.RandomCustomTracks);
        }

        public void SelectRandomTrack(TrackCategory category, bool includeCustom)
        {
            var customTracks = includeCustom ? GetCustomTrackFiles() : Array.Empty<string>();
            _setup.TrackCategory = category;
            _setup.TrackNameOrFile = TrackList.GetRandomTrackKey(category, customTracks);
        }

        public void SelectRandomTrackAny(bool includeCustom)
        {
            var customTracks = includeCustom ? GetCustomTrackFiles() : Array.Empty<string>();
            var pick = TrackList.GetRandomTrackAny(customTracks);
            _setup.TrackCategory = pick.Category;
            _setup.TrackNameOrFile = pick.Key;
        }

        public void SelectVehicle(int index)
        {
            _setup.VehicleIndex = index;
            _setup.VehicleFile = null;
        }

        public void SelectCustomVehicle(string file)
        {
            _setup.VehicleIndex = null;
            _setup.VehicleFile = file;
        }

        public void SelectRandomVehicle()
        {
            var customFiles = _settings.RandomCustomVehicles ? GetCustomVehicleFiles().ToList() : new List<string>();
            var total = VehicleCatalog.VehicleCount + customFiles.Count;
            if (total <= 0)
            {
                SelectVehicle(0);
                return;
            }

            var roll = Algorithm.RandomInt(total);
            if (roll < VehicleCatalog.VehicleCount)
            {
                SelectVehicle(roll);
                return;
            }

            var customIndex = roll - VehicleCatalog.VehicleCount;
            if (customIndex >= 0 && customIndex < customFiles.Count)
                SelectCustomVehicle(customFiles[customIndex]);
            else
                SelectVehicle(0);
        }

        public IEnumerable<string> GetCustomTrackFiles()
        {
            var root = Path.Combine(AssetPaths.Root, "Tracks");
            if (!Directory.Exists(root))
                return Array.Empty<string>();
            return Directory.EnumerateFiles(root, "*.trk", SearchOption.TopDirectoryOnly);
        }

        public IEnumerable<string> GetCustomVehicleFiles()
        {
            var root = Path.Combine(AssetPaths.Root, "Vehicles");
            if (!Directory.Exists(root))
                return Array.Empty<string>();
            return Directory.EnumerateFiles(root, "*.vhc", SearchOption.TopDirectoryOnly);
        }
    }
}
