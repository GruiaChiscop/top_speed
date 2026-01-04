using System;
using System.Collections.Generic;

namespace TopSpeed.Vehicles
{
    internal enum PowerCurveUnit
    {
        Kilowatts,
        Horsepower,
        MetricHorsepower
    }

    internal enum TorqueProfileKind
    {
        Default,
        HighRevNa,
        TurboBroad,
        DieselLowRev,
        Motorcycle,
        Muscle,
        Economy,
        SportTurbo,
        Supercharged,
        HeavyTruck
    }

    internal readonly struct TorqueProfileParams
    {
        public readonly float RiseExponent;
        public readonly float FallExponent;
        public readonly float IdleTorqueFactor;
        public readonly float RedlineTorqueFactor;

        public TorqueProfileParams(float riseExponent, float fallExponent, float idleTorqueFactor, float redlineTorqueFactor)
        {
            RiseExponent = riseExponent;
            FallExponent = fallExponent;
            IdleTorqueFactor = idleTorqueFactor;
            RedlineTorqueFactor = redlineTorqueFactor;
        }
    }

    internal sealed class TorqueCurve
    {
        private readonly float[] _rpm;
        private readonly float[] _torque;

        public TorqueCurve(float[] rpm, float[] torque)
        {
            if (rpm == null || torque == null)
                throw new ArgumentNullException();
            if (rpm.Length != torque.Length || rpm.Length < 2)
                throw new ArgumentException("Torque curve requires at least 2 points.");

            var pairs = new List<(float rpm, float torque)>(rpm.Length);
            for (var i = 0; i < rpm.Length; i++)
            {
                var r = rpm[i];
                var t = torque[i];
                if (r <= 0f)
                    continue;
                pairs.Add((r, t));
            }
            if (pairs.Count < 2)
                throw new ArgumentException("Torque curve requires valid RPM values.");

            pairs.Sort((a, b) => a.rpm.CompareTo(b.rpm));
            _rpm = new float[pairs.Count];
            _torque = new float[pairs.Count];
            for (var i = 0; i < pairs.Count; i++)
            {
                _rpm[i] = pairs[i].rpm;
                _torque[i] = pairs[i].torque;
            }
        }

        public float Evaluate(float rpm)
        {
            if (rpm <= _rpm[0])
                return _torque[0];
            if (rpm >= _rpm[_rpm.Length - 1])
                return _torque[_torque.Length - 1];

            var idx = Array.BinarySearch(_rpm, rpm);
            if (idx >= 0)
                return _torque[idx];
            idx = ~idx;
            var i0 = Math.Max(0, idx - 1);
            var i1 = Math.Min(_rpm.Length - 1, idx);
            if (i0 == i1)
                return _torque[i0];
            var t = (rpm - _rpm[i0]) / (_rpm[i1] - _rpm[i0]);
            return _torque[i0] + ((_torque[i1] - _torque[i0]) * t);
        }

        public static TorqueCurve? TryParse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            if (!TryParsePairs(text, out var pairs))
                return null;
            return FromPairs(pairs);
        }

        public static TorqueCurve? FromPowerCurve(string? text, PowerCurveUnit unit)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            if (!TryParsePairs(text, out var pairs))
                return null;

            var rpm = new float[pairs.Count];
            var torque = new float[pairs.Count];
            for (var i = 0; i < pairs.Count; i++)
            {
                var r = pairs[i].rpm;
                var power = pairs[i].value;
                rpm[i] = r;
                torque[i] = PowerToTorque(power, r, unit);
            }
            return new TorqueCurve(rpm, torque);
        }

        private static float PowerToTorque(float power, float rpm, PowerCurveUnit unit)
        {
            if (rpm <= 0f)
                return 0f;
            switch (unit)
            {
                case PowerCurveUnit.Horsepower:
                    return (power * 7127f) / rpm;
                case PowerCurveUnit.MetricHorsepower:
                    return (power * 7023f) / rpm;
                default:
                    return (power * 9549f) / rpm;
            }
        }

        private static bool TryParsePairs(string? text, out List<(float rpm, float value)> pairs)
        {
            pairs = new List<(float rpm, float value)>();
            var raw = text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
                return false;
            var entries = raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var entry in entries)
            {
                var trimmed = entry.Trim();
                if (trimmed.Length == 0)
                    continue;
                var parts = trimmed.Split(new[] { ':', '=' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2)
                    continue;
                if (!float.TryParse(parts[0].Trim(), out var rpm))
                    continue;
                if (!float.TryParse(parts[1].Trim(), out var value))
                    continue;
                pairs.Add((rpm, value));
            }

            return pairs.Count >= 2;
        }

        private static TorqueCurve? FromPairs(List<(float rpm, float value)> pairs)
        {
            if (pairs == null || pairs.Count < 2)
                return null;
            pairs.Sort((a, b) => a.rpm.CompareTo(b.rpm));
            var rpm = new float[pairs.Count];
            var torque = new float[pairs.Count];
            for (var i = 0; i < pairs.Count; i++)
            {
                rpm[i] = pairs[i].rpm;
                torque[i] = pairs[i].value;
            }
            return new TorqueCurve(rpm, torque);
        }

        public static TorqueCurve Generate(
            float idleRpm,
            float maxRpm,
            float revLimiter,
            float peakTorqueRpm,
            float idleTorqueNm,
            float peakTorqueNm,
            float redlineTorqueNm,
            float massKg,
            float frontalAreaM2,
            TorqueProfileKind? profileOverride = null)
        {
            var idle = Math.Max(500f, idleRpm);
            var rev = Math.Max(idle + 1000f, Math.Max(maxRpm, revLimiter));
            var peakRpm = peakTorqueRpm > 0f
                ? peakTorqueRpm
                : idle + (rev - idle) * 0.60f;
            peakRpm = Clamp(peakRpm, idle + 200f, rev - 200f);

            var peakTorque = Math.Max(peakTorqueNm, Math.Max(idleTorqueNm, redlineTorqueNm));
            if (peakTorque <= 0f)
                peakTorque = 1f;

            var profile = profileOverride ?? SelectProfile(maxRpm, peakRpm, massKg, frontalAreaM2);
            var profileParams = GetProfileParams(profile);

            if (idleTorqueNm <= 0f)
                idleTorqueNm = peakTorque * profileParams.IdleTorqueFactor;
            if (redlineTorqueNm <= 0f)
                redlineTorqueNm = peakTorque * profileParams.RedlineTorqueFactor;

            idleTorqueNm = Math.Max(0f, Math.Min(idleTorqueNm, peakTorque * 0.98f));
            redlineTorqueNm = Math.Max(0f, Math.Min(redlineTorqueNm, peakTorque * 0.98f));

            var xPeak = (peakRpm - idle) / (rev - idle);
            xPeak = Clamp(xPeak, 0.10f, 0.95f);

            var samples = new List<float>(16);
            AddSample(samples, 0f);
            AddSample(samples, 0.05f);
            AddSample(samples, 0.10f);
            AddSample(samples, 0.15f);
            AddSample(samples, 0.20f);
            AddSample(samples, 0.30f);
            AddSample(samples, 0.40f);
            AddSample(samples, 0.50f);
            AddSample(samples, 0.60f);
            AddSample(samples, 0.70f);
            AddSample(samples, 0.80f);
            AddSample(samples, 0.90f);
            AddSample(samples, 0.95f);
            AddSample(samples, 1f);
            AddSample(samples, xPeak - 0.03f);
            AddSample(samples, xPeak);
            AddSample(samples, xPeak + 0.03f);
            samples.Sort();

            var rpm = new float[samples.Count];
            var torque = new float[samples.Count];
            for (var i = 0; i < samples.Count; i++)
            {
                var x = samples[i];
                rpm[i] = idle + x * (rev - idle);
                torque[i] = EvaluateGeneratedTorque(x, xPeak, idleTorqueNm, peakTorque, redlineTorqueNm,
                    profileParams.RiseExponent, profileParams.FallExponent);
            }

            return new TorqueCurve(rpm, torque);
        }

        private static float EvaluateGeneratedTorque(
            float x,
            float xPeak,
            float idleTorqueNm,
            float peakTorqueNm,
            float redlineTorqueNm,
            float riseExponent,
            float fallExponent)
        {
            if (x <= xPeak)
            {
                var t = xPeak <= 0f ? 0f : (x / xPeak);
                t = (float)Math.Pow(Clamp(t, 0f, 1f), riseExponent);
                return Lerp(idleTorqueNm, peakTorqueNm, t);
            }

            var denom = 1f - xPeak;
            var tFall = denom <= 0f ? 1f : (x - xPeak) / denom;
            tFall = (float)Math.Pow(Clamp(tFall, 0f, 1f), fallExponent);
            return Lerp(peakTorqueNm, redlineTorqueNm, tFall);
        }

        private static TorqueProfileKind SelectProfile(float maxRpm, float peakTorqueRpm, float massKg, float frontalAreaM2)
        {
            var isMotorcycle = (massKg > 0f && massKg < 450f)
                || (frontalAreaM2 > 0f && frontalAreaM2 < 1.0f)
                || maxRpm >= 11000f;
            if (isMotorcycle)
                return TorqueProfileKind.Motorcycle;

            var isDiesel = peakTorqueRpm > 0f && peakTorqueRpm <= 2200f && maxRpm <= 5200f;
            if (isDiesel)
                return massKg > 2200f ? TorqueProfileKind.HeavyTruck : TorqueProfileKind.DieselLowRev;

            var isHighRev = maxRpm >= 8000f && peakTorqueRpm >= 4500f;
            if (isHighRev)
                return TorqueProfileKind.HighRevNa;

            var isTurboBroad = peakTorqueRpm > 0f && peakTorqueRpm <= 3500f && maxRpm >= 6000f;
            if (isTurboBroad)
                return TorqueProfileKind.SportTurbo;

            var isMuscle = maxRpm <= 6200f && peakTorqueRpm <= 3500f && massKg >= 1300f;
            if (isMuscle)
                return TorqueProfileKind.Muscle;

            if (massKg > 1800f && maxRpm <= 6200f)
                return TorqueProfileKind.Economy;

            return TorqueProfileKind.Default;
        }

        private static TorqueProfileParams GetProfileParams(TorqueProfileKind profile)
        {
            switch (profile)
            {
                case TorqueProfileKind.Motorcycle:
                    return new TorqueProfileParams(1.7f, 1.15f, 0.25f, 0.65f);
                case TorqueProfileKind.HighRevNa:
                    return new TorqueProfileParams(1.45f, 1.10f, 0.28f, 0.70f);
                case TorqueProfileKind.TurboBroad:
                    return new TorqueProfileParams(0.80f, 1.60f, 0.35f, 0.60f);
                case TorqueProfileKind.DieselLowRev:
                    return new TorqueProfileParams(0.70f, 1.85f, 0.45f, 0.55f);
                case TorqueProfileKind.Muscle:
                    return new TorqueProfileParams(0.95f, 1.25f, 0.35f, 0.65f);
                case TorqueProfileKind.Economy:
                    return new TorqueProfileParams(1.05f, 1.55f, 0.30f, 0.58f);
                case TorqueProfileKind.SportTurbo:
                    return new TorqueProfileParams(0.85f, 1.45f, 0.33f, 0.62f);
                case TorqueProfileKind.Supercharged:
                    return new TorqueProfileParams(1.05f, 1.10f, 0.35f, 0.70f);
                case TorqueProfileKind.HeavyTruck:
                    return new TorqueProfileParams(0.65f, 2.10f, 0.55f, 0.50f);
                default:
                    return new TorqueProfileParams(1.10f, 1.30f, 0.32f, 0.65f);
            }
        }

        public static bool TryParseProfile(string? text, out TorqueProfileKind profile)
        {
            profile = TorqueProfileKind.Default;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            var value = (text ?? string.Empty).Trim().ToLowerInvariant();
            switch (value)
            {
                case "default":
                case "auto":
                    profile = TorqueProfileKind.Default;
                    return true;
                case "highrev":
                case "highrevna":
                case "na":
                case "sportna":
                    profile = TorqueProfileKind.HighRevNa;
                    return true;
                case "turbo":
                case "turbobroad":
                case "sportturbo":
                    profile = TorqueProfileKind.SportTurbo;
                    return true;
                case "diesel":
                case "diesellowrev":
                    profile = TorqueProfileKind.DieselLowRev;
                    return true;
                case "truck":
                case "heavytruck":
                    profile = TorqueProfileKind.HeavyTruck;
                    return true;
                case "motorcycle":
                case "bike":
                    profile = TorqueProfileKind.Motorcycle;
                    return true;
                case "muscle":
                    profile = TorqueProfileKind.Muscle;
                    return true;
                case "economy":
                    profile = TorqueProfileKind.Economy;
                    return true;
                case "supercharged":
                case "sc":
                    profile = TorqueProfileKind.Supercharged;
                    return true;
            }
            return false;
        }

        private static void AddSample(List<float> samples, float value)
        {
            if (value < 0f || value > 1f)
                return;
            const float epsilon = 0.004f;
            for (var i = 0; i < samples.Count; i++)
            {
                if (Math.Abs(samples[i] - value) <= epsilon)
                    return;
            }
            samples.Add(value);
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }
    }
}
