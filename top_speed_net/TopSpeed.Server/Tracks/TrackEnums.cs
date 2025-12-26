namespace TopSpeed.Server.Tracks
{
    internal enum TrackType : byte
    {
        Straight = 0,
        EasyLeft = 1,
        Left = 2,
        HardLeft = 3,
        HairpinLeft = 4,
        EasyRight = 5,
        Right = 6,
        HardRight = 7,
        HairpinRight = 8
    }

    internal enum TrackSurface : byte
    {
        Asphalt = 0,
        Gravel = 1,
        Water = 2,
        Sand = 3,
        Snow = 4
    }

    internal enum TrackNoise : byte
    {
        NoNoise = 0,
        Crowd = 1,
        Ocean = 2,
        Runway = 3,
        Clock = 4,
        Jet = 5,
        Thunder = 6,
        Pile = 7,
        Construction = 8,
        River = 9,
        Helicopter = 10,
        Owl = 11
    }

    internal enum TrackWeather : byte
    {
        Sunny = 0,
        Rain = 1,
        Wind = 2,
        Storm = 3
    }

    internal enum TrackAmbience : byte
    {
        NoAmbience = 0,
        Desert = 1,
        Airport = 2
    }
}
